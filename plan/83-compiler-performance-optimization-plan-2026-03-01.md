# Compiler Performance Optimization Plan (2026-03-01)

## 1. Goal

Reduce end-to-end compile latency and allocation pressure across:

1. Roslyn source generator pipeline host.
2. Avalonia semantic binder and semantic-contract symbol lookup.
3. Avalonia code emitter.

Scope is generator-time performance only; runtime behavior and generated semantics must remain unchanged.

## 2. Baseline Findings

## 2.1 Source generator host (`XamlSourceGeneratorCompilerHost`)

1. Global control-theme suppression keys were recomputed per document by scanning all parsed documents repeatedly.
2. Per-document diagnostic filtering reused full parsed snapshots but did repeated global-set construction, creating avoidable O(N) work per file.

## 2.2 Binder + semantic contract path (`SemanticContractMap` + `CompilationTypeSymbolCatalog`)

1. `SemanticContractMap.TypeContracts` re-ordered and re-materialized immutable arrays on each access.
2. Catalog cache key was rebuilt as a full string for every catalog lookup call.

## 2.3 Emitter + framework profile (`AvaloniaCodeEmitter`, `AvaloniaFrameworkProfile`)

1. Generated source `StringBuilder` started with default capacity, forcing repeated growth/realloc for large files.
2. Framework profile created fresh wrapper+binder/emitter objects repeatedly even though implementations are stateless.

## 3. Optimization Plan

## Phase A - Source generator pipeline optimization

1. Precompute global control-theme key set once per parsed-document snapshot.
2. Feed precomputed immutable set into per-document diagnostic filtering.
3. Keep suppression semantics unchanged.

## Phase B - Binder/config contract optimization

1. Persist ordered type-contract array once in `SemanticContractMap`.
2. Precompute stable `CatalogCacheKey` once in `SemanticContractMap`.
3. Switch `CompilationTypeSymbolCatalog` cache lookup to precomputed key.

## Phase C - Emitter/profile optimization

1. Add deterministic generated-source capacity estimation for emitter `StringBuilder`.
2. Reuse singleton framework binder/emitter wrappers in `AvaloniaFrameworkProfile`.

## Phase D - Validation

1. Run focused generator tests.
2. Run perf harness when enabled (`AXSG_RUN_PERF_TESTS=true`) and capture behavior.
3. Harden perf harness execution determinism:
   - configurable process timeout (`AXSG_PERF_PROCESS_TIMEOUT_MS`),
   - configurable total timeout (`AXSG_PERF_TOTAL_TIMEOUT_MS`),
   - optional JSON result artifact output (`AXSG_PERF_RESULTS_PATH`).
4. Ensure no semantic output regressions.

## Phase E - Harness execution-path stabilization

1. Remove indefinite stream-drain waits in perf harness subprocess execution.
2. Add bounded stream-drain timeout and bounded kill-wait timeout controls:
   - `AXSG_PERF_STREAM_DRAIN_TIMEOUT_MS`
   - `AXSG_PERF_PROCESS_KILL_WAIT_MS`
3. Return deterministic failure messages with partial captured output on timeout.

## Phase F - Test-host timeout guard

1. Add perf-test host timeout guard in `PerfFactAttribute` so enabled perf tests are bounded even if subprocess handling regresses.
2. Make timeout configurable by environment variable:
   - `AXSG_PERF_TEST_TIMEOUT_MS`
3. Keep skip behavior unchanged when `AXSG_RUN_PERF_TESTS` is disabled.
4. Ensure perf tests using `PerfFactAttribute` are async (`Task`) to satisfy xUnit timeout support.

## Phase G - Perf regression guard tests

1. Add deterministic guard tests for `SemanticContractMap`:
   - ordered `TypeContracts` stability,
   - `CatalogCacheKey` determinism for equivalent maps,
   - `CatalogCacheKey` sensitivity to metadata fallback-order changes.
2. Add catalog cache guard test ensuring equivalent map instances reuse the same `CompilationTypeSymbolCatalog` cache entry.
3. Run focused configuration test suite to validate perf-contract behavior remains stable.

## Phase H - Framework profile singleton guard tests

1. Add guard tests proving `AvaloniaFrameworkProfile.CreateSemanticBinder()` and `CreateEmitter()` reuse shared singleton instances.
2. Add guard test proving `CreateDocumentEnrichers()` reuses shared immutable-array storage.
3. Run focused framework-profile tests to lock the perf optimization contract.

## Phase I - Global xmlns prefix parsing optimization

1. Replace split-based `GlobalXmlnsPrefixes` parsing with span-based tokenization to reduce intermediate allocations.
2. Cache parsed `GlobalXmlnsPrefixes` maps by normalized raw property value.
3. Add framework-profile tests for:
   - mixed-delimiter parsing correctness,
   - invalid-entry rejection,
   - explicit-prefix override behavior against implicit standard prefixes.

## 4. Implementation Status

- [x] Phase A implemented.
- [x] Phase B implemented.
- [x] Phase C implemented.
- [x] Phase D implemented (focused tests passed and perf harness determinism/reporting hardened).
- [x] Phase E implemented (bounded stream-drain/kill-wait logic removes internal indefinite wait path).
- [x] Phase F implemented (host-level timeout guard for enabled perf tests).
- [x] Phase G implemented (perf-critical contract/cache guard tests added and passing).
- [x] Phase H implemented (framework profile singleton/document-enricher reuse guard tests added and passing).
- [x] Phase I implemented (span-based global-prefix parsing + cache and guard tests added and passing).

## 5. Files Changed for This Plan

1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Configuration/SemanticContractMap.cs`
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Configuration/CompilationTypeSymbolCatalog.cs`
4. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaFrameworkProfile.cs`
5. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
6. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/SemanticContractMapTests.cs`
7. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilationTypeSymbolCatalogTests.cs`
8. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaFrameworkProfileTests.cs`
9. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaFrameworkProfile.cs`

## 6. Acceptance Criteria

1. No behavioral regressions in generator/binder/emitter tests.
2. Hot path allocations reduced in profile traces for large XAML files (fewer `StringBuilder` growth reallocations).
3. Global control-theme suppression path scales with one global-set build per snapshot instead of per-document rebuild.
4. Semantic contract/catalog lookups avoid repeated map sorting and cache-key reconstruction.
