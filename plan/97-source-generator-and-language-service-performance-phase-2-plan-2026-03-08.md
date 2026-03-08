# Source Generator and Language Service Performance Plan - Phase 2 (2026-03-08)

## Goal
Continue the real-time performance push after Phase 1 by removing high-frequency XML traversal and CLR symbol resolution overhead from the compiler and language service while preserving emitted code, diagnostics, and editor behavior.

## Scope
- In scope:
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Parsing`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Symbols`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions`
  - focused regression tests and opt-in microbenchmarks
- Out of scope:
  - protocol changes
  - runtime behavior changes
  - broad emitter redesign without a measured hotspot

## Target
1. Reduce feature-enrichment wall-clock time by at least 25% on style/control-theme heavy XAML documents.
2. Reduce CLR member-resolution wall-clock time by at least 25% on repeated binding/navigation/completion symbol lookups.
3. Keep functional behavior unchanged in parser/binder/language-service tests.
4. Keep the full `XamlToCSharpGenerator.Tests` suite green.

## Hotspot Inventory

### Optimization matrix
| Stage | File | Method / area | Current issue | Proposed optimization | Required regression coverage | Benchmark scenario |
| --- | --- | --- | --- | --- | --- | --- |
| Compiler feature extraction | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Parsing/AvaloniaDocumentFeatureEnricher.cs` | `Enrich` + `CollectResources` + `CollectTemplates` + `CollectStyles` + `CollectControlThemes` + `CollectIncludes` | walks `root.DescendantsAndSelf()` up to five separate times and re-enumerates attributes with LINQ for each feature class | replace multi-pass collection with a single tree walk that classifies each element once and populates all feature builders in order | direct feature-enricher tests for resources, templates, styles, setters, control themes, and includes | enrich large style/theme-heavy document 1,000x |
| Compiler feature extraction | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Parsing/AvaloniaDocumentFeatureEnricher.cs` | `AddSetterDefinition` | repeated `Attributes().FirstOrDefault()` and `Elements().FirstOrDefault()` allocations per setter | switch to single-pass attribute/child scan helpers and keep exact fallback semantics | setter regression tests for attribute and property-element values | included in enricher benchmark |
| Language-service symbol resolution | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Symbols/XamlClrMemberSymbolResolver.cs` | `ResolveInstanceProperty` | exact and fallback resolution use LINQ and full member re-enumeration on each type/base type hop | use direct loops over `GetMembers(name)` for exact match and a single non-LINQ fallback pass only when needed | binding completion/navigation/reference tests | repeated property resolution across inheritance chain |
| Language-service symbol resolution | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Symbols/XamlClrMemberSymbolResolver.cs` | `ResolveParameterlessMethod` | same LINQ-heavy duplicate logic as property resolution | convert to direct loops with exact-first/fallback-second search | expression binding completion/navigation/reference tests | repeated method resolution across inheritance chain |
| Language-service symbol resolution | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Symbols/XamlClrMemberSymbolResolver.cs` | `ResolveIndexedElementType` | LINQ over all members/interfaces for indexer/list detection | use direct loops and exact interface scan | binding/indexer completion tests | indexed-element-type resolution loop |
| Language-service callers | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlBindingCompletionService.cs` | duplicated local member-resolution helpers | completion duplicates the same LINQ-heavy algorithms instead of using the shared resolver | replace private local methods with shared resolver calls to keep one optimized implementation | binding completion tests | included in member-resolution benchmark via completion path |

## Functional Risk Summary
| Area | Risk | Mitigation |
| --- | --- | --- |
| Feature collection order | single-pass traversal may reorder collected resources/styles/themes/includes | preserve `DescendantsAndSelf()` document order and append to builders in encounter order |
| Setter value extraction | property-element fallback could change when replacing LINQ helpers | add explicit tests for attribute `Value`, nested `<Setter.Value>`, and raw child element fallback |
| Completion/navigation parity | shared resolver refactor could alter case-insensitive fallback behavior | preserve exact-first / case-insensitive-second search contract and validate with existing LS tests |

## Implementation Phases

### Phase A - Guard rails
1. Add focused feature-enricher tests that assert resource/template/style/control-theme/include extraction shape.
2. Add microbenchmarks for feature enrichment and CLR member resolution against preserved baseline implementations.
3. Keep microbenchmarks opt-in behind `AXSG_RUN_PERF_TESTS=true`.

### Phase B - Single-pass feature enrichment
1. Replace the current five full-tree traversals with one `DescendantsAndSelf()` walk.
2. Introduce single-pass attribute helpers for commonly queried attributes/directives.
3. Preserve ordering and exact `RawXaml` / line/column semantics.

### Phase C - Shared member-resolution cleanup
1. Rewrite `XamlClrMemberSymbolResolver` with loop-based exact/fallback search.
2. Use the shared resolver from `XamlBindingCompletionService` instead of duplicated local implementations.
3. Keep method/property/indexer behavior identical.

### Phase D - Validation and summary
1. Run focused parser/language-service tests covering the touched paths.
2. Run the opt-in microbenchmarks and capture before/after results.
3. Run the full `XamlToCSharpGenerator.Tests` suite.
4. Record results back into this plan.

## Exit Criteria
1. All new and existing relevant regression tests pass.
2. Enricher and member-resolution microbenchmarks show measurable wins over preserved baselines.
3. No behavior regressions in full test-suite validation.

## Implementation Status
- [x] Phase A - Guard rails
- [x] Phase B - Single-pass feature enrichment
- [x] Phase C - Shared member-resolution cleanup
- [x] Phase D - Validation and summary

## Implemented Changes
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Parsing/AvaloniaDocumentFeatureEnricher.cs`
   - collapsed five full-tree traversals into one `DescendantsAndSelf()` pass
   - replaced repeated `Attributes().FirstOrDefault(...)` scans with a single attribute walk per element
   - replaced repeated setter attribute/child LINQ scans with direct loops while preserving `Setter.Value` fallback behavior
2. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Symbols/XamlClrMemberSymbolResolver.cs`
   - replaced LINQ-heavy exact/fallback member lookup with direct loops for properties, methods, and indexed element type resolution
3. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Completion/XamlBindingCompletionService.cs`
   - removed duplicated local member-resolution helpers and routed completion through the shared optimized resolver
4. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/SimpleXamlDocumentParserTests.cs`
   - added direct feature-enricher regression coverage for resources/templates/styles/control-themes/includes and `Setter.Value` property-element preservation
5. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
   - added opt-in microbenchmarks for the feature-enricher hot path and CLR member-resolution hot path

## Validation Results

### Release microbenchmarks
Command:
`AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"`

| Scenario | Baseline | Optimized | Improvement |
| --- | --- | --- | --- |
| Avalonia feature enricher hot path | 168.57 ms | 106.08 ms | 37.1% faster |
| Avalonia feature enricher allocations | 373,272,040 B | 317,904,040 B | 14.8% lower |
| Language-service CLR member resolver (best-of) | 144.49 ms | 101.44 ms | 29.8% faster |
| Language-service CLR member resolver allocations | 162,800,040 B | 109,200,040 B | 32.9% lower |

### Regression validation
1. Focused parser + language-service tests:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~SimpleXamlDocumentParserTests|FullyQualifiedName~XamlLanguageServiceEngineTests" --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Result: `101` passed, `0` failed
2. Full suite:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers`
   - Result: `1135` passed, `6` skipped, `0` failed, duration `4 m 58 s`
