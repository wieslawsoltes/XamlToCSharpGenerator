# Phase 16 - Compiler Host Include Source Normalization

## Goal
Reduce compiler-host setup overhead in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` by removing substring-heavy include markup normalization from the include graph path.

## Hotspot
`NormalizeIncludeSource(...)` still used:
- `Trim()` on the whole string
- `Substring(...).Trim()` for the markup body
- `IndexOfAny(new[] { ' ', ',' })` with per-call array allocation
- more `Substring(...).Trim()` for key/value extraction
- string-based unquoting

This path runs for every include in the global graph analysis, including `ResourceInclude` and `StyleInclude` authored as `{x:Uri ...}` markup.

## Optimization Matrix
| Area | Method | Baseline cost | Change | Expected win | Test gate |
| --- | --- | --- | --- | --- | --- |
| Compiler host setup | `NormalizeIncludeSource(...)` | repeated trim/substring/index allocations | single-pass span parser | lower allocations, faster include graph setup | direct unit tests + benchmark |
| Compiler host setup | `UnquoteIncludeSource(...)` | substring allocation | span slice to string | lower allocations | unit tests |
| Compiler host setup | token split | `IndexOfAny(new[] { ... })` | manual span scan | lower allocations | benchmark |

## Implementation
Accepted:
- made `NormalizeIncludeSource(string)` internal for direct tests and benchmarking
- rewrote markup normalization to use `ReadOnlySpan<char>` throughout
- added `IndexOfWhitespaceOrComma(ReadOnlySpan<char>)`
- added `SliceToString(ReadOnlySpan<char>, string)` to reuse the original string when trimming is a no-op
- rewrote `UnquoteIncludeSource(...)` to accept spans and avoid intermediate substrings
- preserved authored semantics for:
  - plain include strings
  - `{x:Uri /Path}`
  - `{x:Uri Uri='...'}` / `{Uri Value="..."}`
  - non-URI markup extensions, which remain unchanged apart from outer trim

## Tests
Added to `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostGlobalGraphTests.cs`:
- `NormalizeIncludeSource_Extracts_Unquoted_Uri_From_XUri_Markup`
- `NormalizeIncludeSource_Extracts_Quoted_Uri_Argument`
- `NormalizeIncludeSource_Preserves_NonUri_Markup_Extension_Text`

Extended `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`:
- `CompilerHost_IncludeSourceNormalization_Outperforms_Baseline`

## Benchmark result
Command:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_IncludeSourceNormalization_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

Measured best-of-5:
- baseline: `203.07 ms`, `1,492,800,040 B`
- optimized: `88.13 ms`, `254,400,040 B`
- elapsed: `56.6%` faster
- allocations: `83.0%` lower

## Validation
Focused correctness:
```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~NormalizeIncludeSource_" --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- `3` passed

Decision: keep. The speedup is well above noise and the parser semantics are directly covered.
