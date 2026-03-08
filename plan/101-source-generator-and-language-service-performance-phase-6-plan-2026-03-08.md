# Source Generator and Language Service Performance Plan - Phase 6 (2026-03-08)

## Goal
Reduce `XamlLanguageServiceEngine` request-path allocations and avoid rebuilding immutable collections when the requested result is already identical to the cached/shared source data.

## Phase 6 Scope
1. Cache shared analysis-option profiles by workspace root instead of cloning `XamlLanguageServiceOptions` for every analysis request.
2. Replace eager diagnostic filtering with a lazy builder that returns the original immutable array when no entry is excluded.
3. Replace eager inlay-hint range filtering with a lazy builder that returns the original immutable array when the requested range already covers the cached hint set.
4. Add behavioral tests for inlay-range filtering so the no-rebuild fast path stays functionally correct.
5. Add microbenchmarks for diagnostic filtering, inlay range filtering, and shared analysis-option reuse.

## Root-Cause Summary
`XamlLanguageServiceEngine` sat on a hot request boundary for completions, hover, references, and inlay hints. Three small but repeated costs remained:

- `CreateSharedAnalysisOptions(...)`
  - always allocated a new `XamlLanguageServiceOptions` via `with` even when the same workspace-root profile repeated across requests.
- `FilterDiagnostics(...)`
  - always created a new immutable builder and array once any filtering mode was enabled, even when no diagnostic matched the excluded source classes.
- `FilterInlayHints(...)`
  - always rebuilt the result array, including the common case where the requested range already covered the full cached hint set.

These were not semantic bottlenecks. They were request-pipeline allocation leaks that compounded under editor traffic.

## Implementation Plan
- [x] Add `SharedAnalysisOptionsCache` keyed by `WorkspaceRoot`.
- [x] Replace `CreateSharedAnalysisOptions(...)` with cached `GetSharedAnalysisOptions(...)`.
- [x] Rewrite `FilterDiagnostics(...)` to use lazy builder materialization.
- [x] Rewrite `FilterInlayHints(...)` to use lazy builder materialization.
- [x] Add a behavioral guard for inlay-range filtering.
- [x] Add microbenchmarks for diagnostic filter, inlay filter, and shared analysis options.
- [x] Run focused language-service tests.
- [x] Run the perf benchmark slice.
- [x] Run the full test suite.

## Optimization Matrix
| Area | File | Method / Path | Previous Cost Shape | Optimization | Status | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| Shared analysis options | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlLanguageServiceEngine.cs` | `CreateSharedAnalysisOptions(...)` | per-request record clone | workspace-root keyed cache | Implemented | microbenchmark |
| Diagnostic filtering | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlLanguageServiceEngine.cs` | `FilterDiagnostics(...)` | always rebuilt immutable array when filtering enabled | lazy builder + original-array fast path | Implemented | microbenchmark |
| Inlay range filtering | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlLanguageServiceEngine.cs` | `FilterInlayHints(...)` | always rebuilt immutable array | lazy builder + original-array fast path | Implemented | behavioral test + microbenchmark |
| Inlay range behavior | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs` | inlay range regression | no direct guard for subset filtering | explicit range-filter test | Implemented | focused LS tests |
| Request-path perf harness | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs` | LS engine filter/option benchmarks | no direct coverage for these engine helpers | added microbenchmarks | Implemented | perf harness |

## Exit Criteria
- Shared analysis-option reuse shows materially lower allocations than the old clone path.
- Diagnostic filtering shows lower allocations than the eager-builder baseline.
- Inlay range filtering shows lower allocations than the eager-builder baseline.
- Focused and full test suites stay green.

## Benchmarks
Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~LanguageService_DiagnosticFilter_NoSuppression_Avoids_Rebuild|FullyQualifiedName~LanguageService_InlayHintRangeFilter_FullCoverage_Avoids_Rebuild|FullyQualifiedName~LanguageService_SharedAnalysisOptionsCache_Reduces_Allocations" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Shared analysis options | `0.53 ms` | `0.78 ms` | slower on raw elapsed, allocation-focused |
| Shared analysis options allocations | `1,600,040 B` | `40 B` | `99.99%` lower |
| Diagnostic filter no-suppression path | `133.50 ms` | `56.81 ms` | `57.4%` faster |
| Diagnostic filter allocations | `640,288,072 B` | `40 B` | `99.99%` lower |
| Inlay full-range filter | `373.40 ms` | `50.28 ms` | `86.5%` faster |
| Inlay full-range allocations | `1,369,865,984 B` | `40 B` | `99.99%` lower |

Notes:
- Shared analysis options are now validated primarily by allocation reduction. The cache trades a small amount of raw lookup overhead for eliminating per-request option cloning.
- Diagnostic and inlay filtering now preserve the original immutable arrays when nothing needs to be removed.

## Focused Validation
Command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~InlayHints_FilterToRequestedRange_ReturnOnlyHintsInsideRange|FullyQualifiedName~XamlLanguageServiceEngineTests" --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `87` passed
- `0` failed

## Full Validation
Perf command:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Perf result:
- `12` passed
- `0` failed

Suite command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Suite result:
- `1138` passed
- `13` skipped
- `0` failed
- duration: `4 m 38 s`

## Next Phase Candidates
1. `XamlSourceGeneratorCompilerHost` discovery snapshot normalization and dedupe sorting.
2. Broader compiler-host semantic-model reuse across document batches.
3. Language-service request cache-key materialization if profiler data still points there.
