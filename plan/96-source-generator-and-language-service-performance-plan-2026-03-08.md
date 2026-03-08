# Source Generator and Language Service Performance Plan (2026-03-08)

## Goal
Reach real-time interactive performance for the source-generator compiler and XAML language service without changing emitted semantics, diagnostics, or editor behavior.

## Scope
- In scope:
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService`
  - targeted regression tests and opt-in microbenchmarks
- Out of scope for this plan:
  - runtime behavior changes
  - protocol feature additions
  - broad binder/emitter redesign that is not backed by a measured hotspot

## Performance Target
1. Reduce parser microbenchmark wall-clock time by at least 20% on representative medium/large XAML inputs.
2. Reduce compiler-host diagnostic filtering overhead by at least 20% in the zero-suppression common path.
3. Reduce language-service document-update invalidation overhead from O(cache-entries) scans to O(1) generation bumps.
4. Keep the full `XamlToCSharpGenerator.Tests` suite green.

## Baseline Hotspots

### Pipeline inventory
| Stage | Area | Current symptom | Primary cost shape | Risk |
| --- | --- | --- | --- | --- |
| Parse/normalize | `SimpleXamlDocumentParser` | repeated attribute rescans per element | repeated `element.Attributes()` enumeration + LINQ allocations | Low |
| Global graph + diagnostics | `XamlSourceGeneratorCompilerHost` | avoidable allocations in parity filtering and duplicate ordering work | extra `ImmutableArray` builder creation, second sort pass | Low |
| IDE caches | `XamlLanguageServiceEngine` | every open/update/close scans all cache keys | O(total cache entries) invalidation | Medium |
| Perf validation | tests | only macro harness exists today | hard to attribute wins to a single change | Low |

## Optimization Matrix

### Parser and normalization
| File | Method | Current issue | Proposed optimization | Test coverage required | Benchmark scenario |
| --- | --- | --- | --- | --- | --- |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs` | `ParseObjectNode` | rescans directive/name/type attributes multiple times before the main attribute walk | collapse directive extraction into the existing single attribute pass and reuse captured values | existing parser tests + new directive/array/name regression if needed | parse representative view 1,000x |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs` | `TryGetInlineTextContent` | `OfType/Select/Where/ToArray/string.Join` allocates on every inline text node | replace with single-pass text aggregation using `StringBuilder` only when needed | inline text tests | multi-fragment text content parse |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Parsing/SimpleXamlDocumentParser.cs` | `CollectIgnoredNamespaces` | LINQ `FirstOrDefault` over attributes | use a single loop and early exit | existing ignorable-namespace coverage | parse same document 1,000x |

### Compiler host and global graph
| File | Method | Current issue | Proposed optimization | Test coverage required | Benchmark scenario |
| --- | --- | --- | --- | --- | --- |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | `ApplyGlobalParityDiagnosticFilters` | allocates builder even when nothing is suppressed | delay builder allocation until first suppression and backfill prior diagnostics only then | generator regression tests | 10k diagnostics, zero suppression |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | `AnalyzeGlobalDocumentGraph` | sorts documents, then sorts dictionary values again | preserve ordered entry list from first sorted traversal and reuse it for edge building | existing global graph tests | 1k documents with includes |

### Language service
| File | Method | Current issue | Proposed optimization | Test coverage required | Benchmark scenario |
| --- | --- | --- | --- | --- | --- |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlLanguageServiceEngine.cs` | `InvalidateUriCaches` | scans every cache key on each edit/open/close | add per-URI generation stamp and include it in cache keys so invalidation is O(1) | reopen-same-uri-same-version cache regression tests | repeated update/definition loop over 500 cached entries |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/XamlLanguageServiceEngine.cs` | `BuildAnalysisCacheKey` + request cache-key builders | cache keys do not isolate URI lifecycle beyond version | include generation/session stamp from document lifecycle | LS engine tests around reopen/update/close | same benchmark as above |

### Test and benchmark infrastructure
| File | Method/Area | Current issue | Proposed optimization | Output |
| --- | --- | --- | --- | --- |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build` | perf coverage | only macro build harness exists | add opt-in microbenchmarks for parser, compiler host filter, and LS invalidation | attributable perf data per phase |
| `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/LanguageService` | cache safety | no direct test for reopen same URI/same version | add regression tests before changing invalidation model | stale-cache prevention |

## Implementation Phases

### Phase A - Guard rails and microbenchmark scaffolding
1. Add missing regression tests for language-service URI lifecycle invalidation behavior.
2. Add opt-in microbenchmark tests that compare old algorithm copies against current implementations.
3. Keep benchmarks disabled by default behind `AXSG_RUN_PERF_TESTS=true`.

### Phase B - Parser single-pass extraction
1. Rewrite `ParseObjectNode` to capture directive/name/type values inside the main attribute loop.
2. Replace LINQ in `TryGetInlineTextContent` with a single-pass implementation.
3. Remove the dedicated `TryGetName`, `TryGetDirectiveValue`, `TryGetBoolDirectiveValue`, `TryGetFieldModifier`, and `TryGetArrayItemType` rescans from the hot object-node path.
4. Run parser regression tests and parser microbenchmark.

### Phase C - Compiler-host allocation cleanup
1. Fix delayed builder allocation in `ApplyGlobalParityDiagnosticFilters`.
2. Reuse first-pass ordered entries in `AnalyzeGlobalDocumentGraph`.
3. Add focused guard tests if existing coverage is not sufficient.
4. Run compiler-host microbenchmark.

### Phase D - O(1) language-service invalidation
1. Add per-URI generation tracking to `XamlLanguageServiceEngine`.
2. Include generation in analysis/inflight/definition/reference/inlay/semantic-token cache keys.
3. Replace cache-key scans with generation bumps in `InvalidateUriCaches`.
4. Validate reopen/update/close semantics and run LS microbenchmark.

### Phase E - Validation and perf summary
1. Run focused test suites for parser, compiler host, and language service.
2. Run opt-in microbenchmarks and capture before/after ratios against baseline implementations.
3. Run the full `XamlToCSharpGenerator.Tests` suite.
4. Record measured results back into this plan file if the target is met.

## Exit Criteria
1. All new and existing regression tests pass.
2. Parser, compiler-host, and LS microbenchmarks show measurable wins over preserved baseline implementations.
3. No diagnostics/emission/navigation behavior changes in covered tests.
4. Full test suite remains green.

## Implementation Status
- [x] Phase A - Guard rails and microbenchmark scaffolding
- [x] Phase B - Parser single-pass extraction
- [x] Phase C - Compiler-host allocation cleanup
- [x] Phase D - O(1) language-service invalidation
- [x] Phase E - Validation and perf summary

## Implemented Changes
1. `SimpleXamlDocumentParser.ParseObjectNode` now captures directive/name/type data during the main attribute walk instead of rescanning attributes repeatedly.
2. `SimpleXamlDocumentParser.TryGetInlineTextContent` now uses a single-pass text aggregation path instead of LINQ materialization.
3. `SimpleXamlDocumentParser.CollectIgnoredNamespaces` now uses a single attribute scan with early exit.
4. `XamlSourceGeneratorCompilerHost.ApplyGlobalParityDiagnosticFilters` now delays `ImmutableArray` builder allocation until the first actual suppression.
5. `XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph` now reuses the first ordered document traversal instead of sorting dictionary values again.
6. `XamlLanguageServiceEngine` now uses per-URI generation stamps in analysis/definition/reference/inlay/semantic-token cache keys so invalidation is O(1).
7. Added parser regression tests, LS reopen/same-version cache regression tests, and opt-in compiler microbenchmarks.

## Validation Results

### Release microbenchmarks
Command:
`AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"`

| Scenario | Baseline | Optimized | Improvement |
| --- | --- | --- | --- |
| Parser object-node attribute scan | 51.54 ms | 23.32 ms | 54.8% faster |
| Parser object-node allocations | 22,560,040 B | 11,360,040 B | 49.6% lower |
| Compiler-host diagnostic filter (best-of) | 79.59 ms | 50.93 ms | 36.0% faster |
| Compiler-host diagnostic filter allocations | 320,224,040 B | 40 B | effectively eliminated |
| Language-service URI invalidation | 213.82 ms | 0.95 ms | 99.6% faster |
| Language-service URI invalidation allocations | 40 B | 40 B | unchanged |

### Regression validation
1. Focused parser + language-service tests:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~SimpleXamlDocumentParserTests|FullyQualifiedName~XamlLanguageServiceEngineTests|FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Result: `99` passed, `3` skipped, `0` failed
2. Full suite:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Result: `1133` passed, `4` skipped, `0` failed, duration `4 m 39 s`
