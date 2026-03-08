# Source Generator and Language Service Performance Plan - Phase 4 (2026-03-08)

## Goal
Reduce emitter-side string materialization overhead in the source-generated Avalonia backend without changing emitted semantics, then lock the gains behind microbenchmarks and existing generator/runtime regression coverage.

## Phase 4 Scope
1. Remove `List<string>` + `string.Join` churn from emitter helper paths that run for every generated view or hot reload descriptor manifest.
2. Replace event-binding member-path `Split(...)` parsing with a direct segment scanner.
3. Preserve exact emitted output for all optimized helpers.
4. Add microbenchmarks that compare old and new helper algorithms with representative generator workloads.

## Root-Cause Summary
The Avalonia emitter still had several hot string-building paths that were allocation-heavy even after earlier compiler and language-service improvements:

- `AvaloniaCodeEmitter.Emit(...)`
  - module initializer known-type registration used `string.Join(... Select(...))`
- `BuildParentStackExpression(...)`
  - built object arrays through `string.Join` every time nested markup context was emitted
- `TryBuildEventBindingMethodInvocationExpression(...)`
  - allocated `List<string>` for every compiled event-binding method call
- `IsSimpleEventBindingMemberPath(...)`
  - split and trimmed the entire path on every check
- hot reload descriptor manifest builders
  - `BuildHotReloadCollectionCleanupDescriptorArrayExpression(...)`
  - `BuildHotReloadClrPropertyCleanupDescriptorArrayExpression(...)`
  - `BuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(...)`
  - `BuildHotReloadEventCleanupDescriptorArrayExpression(...)`
  - all materialized temporary lists and then joined them

These are not cold-start-only costs. They execute in normal generator paths for large XAML files, hot reload metadata manifests, and event-binding heavy views.

## Implementation Plan
- [x] Replace known-type registration argument materialization with loop-based `StringBuilder` assembly.
- [x] Replace parent-stack object-array `string.Join` with direct append logic.
- [x] Replace event-binding invocation `List<string>` + `string.Join` with direct `StringBuilder` assembly.
- [x] Replace event-binding member-path `Split(...)` with segment scanning.
- [x] Replace hot reload cleanup descriptor builders with direct append logic and a shared separator helper.
- [x] Add one generator regression check for an emitter-adjacent binding output surface already covered by existing tests.
- [x] Add dedicated emitter microbenchmarks and validate them under `AXSG_RUN_PERF_TESTS=true`.
- [x] Run the full test suite after the optimization.

## Optimization Matrix
| Area | File | Method / Path | Previous Cost Shape | Optimization | Status | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| Emitter known-type registration | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | module initializer registration block | `string.Join(... Select(...))` over known type names | loop-based `BuildTypeofArgumentListExpression(...)` | Implemented | emitter benchmark + full suite |
| Emitter parent stack | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `BuildParentStackExpression(...)` | `string.Join` over nested parent references | direct append with pre-sized `StringBuilder` | Implemented | emitter benchmark + full suite |
| Event binding invocation | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `TryBuildEventBindingMethodInvocationExpression(...)` | `List<string>` + `string.Join` per invocation | single-pass `StringBuilder` assembly | Implemented | emitter benchmark + generator/event tests |
| Event binding path validation | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `IsSimpleEventBindingMemberPath(...)` | `Split` + trim array allocation | direct segment scanner | Implemented | emitter benchmark |
| Hot reload cleanup manifests | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `BuildHotReload*CleanupDescriptorArrayExpression(...)` | temporary descriptor lists + `string.Join` | direct append into final array expression | Implemented | emitter benchmark + existing hot reload generator tests |
| LS project-source scan | `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `EnumerateProjectXamlSources(...)` | repeated warm scan and URI rebuild | cached project snapshot | Already implemented in phase 3 | benchmark + full suite |
| Compiler feature extraction | `src/XamlToCSharpGenerator.Avalonia/Parsing/AvaloniaDocumentFeatureEnricher.cs` | full-document feature collection | repeated tree and attribute scans | single-pass enrichment | Already implemented in phase 2 | benchmark + full suite |

## Benchmarks
Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

### Phase 4 measured result
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Emitter string assembly hot paths | `75.34 ms` | `57.98 ms` | `23.0%` faster |
| Emitter string assembly allocations | `641,280,040 B` | `457,680,040 B` | `28.6%` lower |
| Emitter event-binding builders | `20.52 ms` | `12.31 ms` | `40.0%` faster |
| Emitter event-binding allocations | `101,440,040 B` | `54,560,040 B` | `46.2%` lower |

### Carried-forward results
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Parser object-node attribute scan | `39.22 ms` | `23.99 ms` | `38.8%` faster |
| Compiler-host diagnostic filter | `88.83 ms` | `56.64 ms` | `36.2%` faster |
| LS URI invalidation | `287.95 ms` | `0.94 ms` | `99.7%` faster |
| Avalonia feature enricher | `201.57 ms` | `138.21 ms` | `31.4%` faster |
| LS CLR member resolver | `206.00 ms` | `137.99 ms` | `33.0%` faster |
| LS warm project-source scan | `156.38 ms` | `101.48 ms` | `35.1%` faster |

## Test Coverage Added / Relied On
- `tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
  - `Emitter_StringAssembly_HotPaths_Outperform_Baseline`
  - `Emitter_EventBinding_Builders_Outperform_Baseline`
- Existing generator/runtime tests already cover the affected semantics:
  - `Generates_Runtime_Call_For_Explicit_CSharp_Expression_Binding`
  - `HotReload_Emits_Named_Field_Members_In_Clr_Reset_Manifest`
  - `HotReload_Emits_Root_Event_Subscription_Manifest_For_Reconciliation`
  - runtime parent-stack and event-binding suites

## Full Validation
Command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `1136` passed
- `9` skipped
- `0` failed
- duration: `4 m 35 s`

## Exit Criteria
- Emitter string assembly benchmark remains at least `15%` faster than the baseline.
- Emitter event-binding benchmark remains at least `25%` faster than the baseline.
- Full test suite remains green.
- No change in emitted semantics for existing generator and runtime coverage.

## Next Phase Candidates
1. `XamlReferenceService` project include glob refresh and regex compilation reuse.
2. `XamlLanguageServiceEngine` repeated cross-request immutable-map materialization.
3. `XamlSourceGeneratorCompilerHost` semantic-model cache reuse across document batches.
