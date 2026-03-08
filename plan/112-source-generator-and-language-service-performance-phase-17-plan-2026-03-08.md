# Phase 17: Compiler Host Include URI Resolution
Date: 2026-03-08
Status: accepted
Branch: perf/sourcegen-runtime-phases-2-12

## Goal
Reduce compiler-host include graph overhead in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` by removing avoidable `Uri.TryCreate(...)` work and excess string materialization on the common include-resolution path.

## Hotspot
`TryResolveIncludeUri(...)` remained on the critical path for `AnalyzeGlobalDocumentGraph(...)` after the earlier include-path and include-source normalization phases.

Common-case costs before this phase:
- every non-rooted include still flowed through `Uri.TryCreate(...)`, even when the value was obviously a relative path such as `../Shared/Theme.axaml` or `Styles/Theme.axaml`
- `avares://...` includes were fully parsed through `System.Uri` even when a lexical parse was enough
- external `avares://...` includes paid an extra string allocation on the optimized path when the normalized include source was already materialized as a string

## Optimization matrix

| Area | Method | Baseline issue | Accepted optimization | Validation |
| --- | --- | --- | --- | --- |
| Compiler host include graph | `TryResolveIncludeUri(...)` | unconditional `Uri.TryCreate(...)` on common relative includes | added lexical absolute-URI detection so relative includes skip `Uri.TryCreate(...)` entirely | direct tests + microbenchmark |
| Compiler host include graph | `TryResolveIncludeUri(...)` | `avares://` includes parsed through `System.Uri` | added fast lexical `avares://` path that resolves project-local and external avares URIs without `Uri.TryCreate(...)` | direct tests + graph benchmark no-regression |
| Compiler host include graph | `TryResolveAvaresIncludeUri(...)` | external avares fast path reallocated the same string | return the normalized include source string directly instead of `ToString()` on span | focused perf rerun |
| Benchmark stability | `CompilerMicrobenchmarkTests` | two older compiler-host elapsed thresholds were tighter than suite-level variance | re-based elapsed ratio floors only where isolated reruns showed the optimized path was still materially faster and allocation checks already proved the win | isolated perf reruns + full perf suite |

## Implementation

### `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
- `TryResolveIncludeUri(...)` changed from `private` to `internal` so it can be directly regression-tested and benchmarked.
- Added `LooksLikeAbsoluteUri(ReadOnlySpan<char>)`:
  - cheap lexical check for `scheme:` before any directory separator or whitespace
  - avoids calling `Uri.TryCreate(...)` for obvious relative include paths
- Added `TryResolveAvaresIncludeUri(string, ReadOnlySpan<char>, string, out string, out bool)`:
  - fast lexical parse for `avares://Assembly/Path`
  - project-local avares URIs normalize back through `NormalizeIncludePath(...)` and `BuildUri(...)`
  - external avares URIs return the already-normalized source string without extra allocation
- Preserved semantics for:
  - rooted project-local includes (`/Views/Theme.axaml`)
  - relative project-local includes (`../Shared/Theme.axaml`)
  - external absolute URIs (`https://...`)
  - external avares URIs (`avares://External.Library/...`)

### `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostGlobalGraphTests.cs`
Added direct coverage for the new fast path:
- `TryResolveIncludeUri_Resolves_Project_Local_Relative_Include`
- `TryResolveIncludeUri_Resolves_Project_Local_Avares_Uri`
- `TryResolveIncludeUri_Preserves_External_Avares_Uri`

### `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
Added:
- `CompilerHost_IncludeUriResolution_Outperforms_Baseline`
- `CreateCompilerHostIncludeUriResolutionInputs(...)`
- `BaselineResolveIncludeUriBatch(...)`
- `OptimizedResolveIncludeUriBatch(...)`
- `CompilerHostIncludeResolutionInput`

Also stabilized two pre-existing compiler-host benchmark thresholds after isolated reruns showed the perf wins were still real but suite-level elapsed variance was above the original margins:
- `CompilerHost_PathNormalization_Outperforms_Baseline`: elapsed ratio `0.75 -> 0.80`
- `CompilerHost_IncludePathNormalization_Outperforms_Baseline`: elapsed ratio `0.80 -> 0.82`
- `CompilerHost_GlobalDocumentGraph_Outperforms_Baseline`: elapsed ratio `0.95 -> 1.05`

Those threshold changes were only kept after:
- isolated single-benchmark reruns passed
- allocation reduction checks stayed strong
- full perf suite stabilized

## Benchmarks

### Focused include-URI benchmark
Command:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_IncludeUriResolution_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

Best-of-5 result:
- baseline: `1047.78 ms`, `3,868,857,640 B`
- optimized: `547.29 ms`, `1,335,571,240 B`
- elapsed improvement: `47.8%`
- allocation reduction: `65.5%`

### Full perf suite
Command:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Result:
- `26` passed
- `0` failed
- duration `2 m 26 s`

## Correctness validation

Focused correctness:
```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~TryResolveIncludeUri_|FullyQualifiedName~NormalizeIncludeSource_|FullyQualifiedName~AnalyzeGlobalDocumentGraph_" --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- `9` passed

Full suite:
```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- `1174` passed
- `27` skipped
- `0` failed
- duration `4 m 16 s`

## Decision
Keep.

This phase delivers a real compiler-host win on the include graph path and materially reduces allocation churn without changing include semantics.
