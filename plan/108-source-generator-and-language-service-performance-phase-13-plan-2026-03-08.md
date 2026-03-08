# Phase 13: Compiler-Host Setup Reuse and Cross-File XML Cache Churn

Date: 2026-03-08
Branch: `perf/sourcegen-runtime-phases-2-12`
Status: Implemented and validated

## Goal

Continue the remaining compiler-host batch/setup work in `XamlSourceGeneratorCompilerHost` and revisit the remaining cross-file discovery churn in `XamlReferenceService` without weakening semantics or adding unstable complexity.

This phase explicitly keeps only optimizations that survive microbenchmark validation. Two candidate rewrites were evaluated and rejected because they either regressed or produced only noise-level wins relative to the added complexity.

## Scope

Files changed in the accepted slice:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/LanguageService/XamlReferenceServiceTests.cs`

## Optimization Matrix

| Area | Method / Path | Baseline issue | Accepted change | Result | Status |
|---|---|---|---|---|---|
| Compiler host | `Initialize(...)` per-document output path | Recreated framework services through profile factory calls inside the file output lambda | Capture binder/emitter once and reuse the same instances for the batch | Large allocation drop, clear latency win | Accepted |
| Language service | `TryEnsureXmlDocumentLoaded(...)` cached-XML hit path | Cache hit still rewrote `SourceFileCache` freshness on every reuse | Refresh cache timestamp only when it is meaningfully stale | Small latency win, large allocation reduction on stale-source XML reuse benchmark | Accepted |
| Language service | `TryLoadCachedSourceFile(...)` unchanged-file hit path | Cache hit rewrote validated timestamp eagerly | Apply the same throttled timestamp refresh policy | Reduces repeated cache-churn writes in cross-file queries | Accepted |
| Compiler host | Legacy transform-rule parsing over `ImmutableArray<XamlFrameworkTransformRuleInput>` | Suspected LINQ materialization overhead | Replaced `Select(...).ToImmutableArray()` with a direct array loop | Slower and more alloc-heavy than baseline | Rejected |
| Language service | Expired project-source snapshot refresh | Suspected rebuild churn after snapshot TTL | Attempted refresh from cached entries instead of rebuild | Improvement was noise-level and not worth extra complexity | Rejected |

## Accepted implementation details

### 1. Compiler-host framework service reuse

The compiler host already uses a single framework profile per generator initialization. Reacquiring the semantic binder and emitter inside the per-document source-output path was unnecessary setup churn.

Accepted implementation:
- capture once near initialization:
  - `var semanticBinder = frameworkProfile.CreateSemanticBinder();`
  - `var emitter = frameworkProfile.CreateEmitter();`
- reuse those captured instances in the per-document generation path.

Why this is safe:
- the framework profile already acts as the composition root for the generator instance
- binder/emitter behavior is deterministic for the same profile
- this does not change semantics, diagnostics, or ordering

### 2. Cross-file XML cache churn reduction

The important remaining churn in `XamlReferenceService` was not the parse itself. It was repeated cache-entry rewrites on hot cache hits.

Accepted implementation:
- add `SourceValidationRefreshThreshold = 500ms`
- in `TryEnsureXmlDocumentLoaded(...)`, when the XML is already parsed and cached, only rewrite `ValidatedAtUtc` if the cached entry is meaningfully stale
- in `TryLoadCachedSourceFile(...)`, when the file is unchanged and the cached text is still valid, apply the same throttled timestamp refresh policy

Why this is safe:
- file freshness still revalidates via `LastWriteTimeUtc` and file length once the cache ages out
- hot repeated cross-file queries stop churning the concurrent cache for every XML reuse
- diagnostics and XML reuse semantics stay unchanged

## Rejected experiments

### A. Legacy transform-rule parsing rewrite

Candidate:
- replace
  - `inputs.Select(transformProvider.ParseTransformRule).ToImmutableArray()`
- with a manual array loop

Measured result:
- baseline best: `12.93 ms`, `38,740,040 B`
- rewrite best: `19.84 ms`, `44,920,040 B`

Decision:
- reverted completely
- no test or benchmark for that path remains in the tree

### B. Expired project-source snapshot refresh

Candidate:
- refresh expired project snapshots from warm file cache instead of rebuilding the snapshot

Measured result after correcting the benchmark shape:
- best baseline: `1967.31 ms`, `685,957,832 B`
- best rewrite: `1964.21 ms`, `683,759,208 B`

Decision:
- rejected as too small to justify the extra code path
- reverted completely

## Benchmarks retained for this phase

Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_FrameworkServiceReuse_Outperforms_Baseline|FullyQualifiedName~LanguageService_XmlDocumentCacheReuse_Outperforms_Baseline_Reparse|FullyQualifiedName~LanguageService_ProjectPathResolution_Cache_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

Measured results:

| Benchmark | Baseline | Optimized | Delta |
|---|---:|---:|---:|
| `CompilerHost_FrameworkServiceReuse_Outperforms_Baseline` | `0.40 ms`, `1,920,040 B` | `0.18 ms`, `40 B` | `55.0%` faster, `~100%` lower allocations |
| `LanguageService_XmlDocumentCacheReuse_Outperforms_Baseline_Reparse` | `288.70 ms`, `2,385,408,040 B` | `269.07 ms`, `1,311,465,880 B` | `6.8%` faster, `45.0%` lower allocations |
| `LanguageService_ProjectPathResolution_Cache_Outperforms_Baseline` | `4294.38 ms`, `337,920,040 B` | `12.73 ms`, `15,360,040 B` | retained prior gain; still passing after this phase |

## Test coverage

Focused correctness run:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHostTransformConfigurationTests|FullyQualifiedName~XamlReferenceServiceTests" --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `6 passed`
- `0 failed`

Full microbenchmark suite:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Result:
- `21 passed`
- `0 failed`

Full suite:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `1163 passed`
- `22 skipped`
- `0 failed`
- duration: `4 m 27 s`

## Follow-up targets

Highest-value remaining candidates after this phase:
1. `XamlSourceGeneratorCompilerHost` remaining setup/batch overhead outside the now-accepted service reuse path
2. `XamlReferenceService` cross-file enumeration churn beyond XML cache reuse, but only if a benchmark shows a real win
3. broader compiler-host graph preparation only where microbenchmarks show more than noise-level improvement
