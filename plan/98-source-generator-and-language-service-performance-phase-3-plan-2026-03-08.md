# Source Generator and Language Service Performance Plan - Phase 3 (2026-03-08)

## Goal
Drive the next measurable latency reduction in the language-service cross-file reference path without changing semantics, then capture the remaining compiler and language-service hotspots in a method-level optimization matrix.

## Phase 3 Scope
1. Reduce repeated cross-file XAML source enumeration overhead in the language service reference pipeline.
2. Preserve the same correctness and freshness contract already enforced by `SourceValidationCacheTtl`.
3. Add one regression test for workspace-root directory resolution, because the optimized path now relies on direct directory-to-project discovery without LINQ sorting.
4. Add a microbenchmark for warm project-source scans so future changes do not regress the optimized path.

## Root-Cause Summary
The heavy cross-file reference pipeline in `XamlReferenceService` still paid repeated warm-path costs on every request:

- `EnumerateProjectXamlSources(...)`
  - rebuilt a `HashSet<string>` and path array for every request
  - re-walked the cached project file path list on every request
  - reloaded each candidate through `TryLoadCachedSourceFile(...)`, even when the source cache itself was already warm
- `ResolveProjectPath(...)`
  - used `OrderBy(...).FirstOrDefault()` over top-level project-file enumeration on every request when the workspace root was a directory
- per-source URI projection
  - repeatedly recomputed `UriPathHelper.ToDocumentUri(source.FilePath)` inside each reference collector loop

These were not cold-start problems. They were warm-path costs that still executed in normal editor bursts such as repeated references, hover, declaration, and rename-driven scans.

## Implementation Plan
- [x] Add a cached validated project source snapshot keyed by project path.
- [x] Keep snapshot freshness aligned with `SourceValidationCacheTtl` so behavior remains no worse than the pre-existing source cache freshness window.
- [x] Carry precomputed document URIs in cached source entries and consume them in reference collectors.
- [x] Replace LINQ sorting in `ResolveProjectPath(...)` with direct loop-based `TryFindFirstProjectFile(...)`.
- [x] Add a regression test for workspace-root directory project resolution.
- [x] Add a microbenchmark for the warm project-source scan path.
- [x] Run the full test suite after the optimization.

## Optimization Matrix
| Area | File | Method / Path | Previous Cost Shape | Optimization | Status | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| LS cross-file references | `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `EnumerateProjectXamlSources(...)` | Re-enumerated project file paths and reloaded cached source entries on every request | Introduced `CachedProjectSourceSnapshot` and reused warm `XamlProjectSourceFile` entries | Implemented | New microbenchmark + full suite |
| LS project path resolution | `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `ResolveProjectPath(...)` | `OrderBy(...).FirstOrDefault()` over top-level `.csproj` enumeration | Loop-based `TryFindFirstProjectFile(...)` with ordinal-ignore-case comparison | Implemented | New regression test |
| LS per-source URI creation | `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | All `Collect*References(...)` loops | Recomputed `ToDocumentUri` per source hit | Stored `DocumentUri` in cached source snapshot entries | Implemented | Full suite |
| Compiler emitter arrays | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `BuildParentStackExpression(...)`, hot reload descriptor builders | Repeated `string.Join` and intermediate list materialization | Candidate for phase 4 | Pending | Benchmark not added yet |
| LS project include expansion | `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `ExpandProjectIncludePattern(...)` / glob regex build | Regex rebuild on project file-list refresh | Candidate for phase 4 if refresh cost shows in traces | Pending | Not benchmarked yet |
| Emitter event binding invocation | `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs` | `TryBuildEventBindingMethodInvocationExpression(...)` | `List<string>` + `string.Join` for each invocation expression | Candidate for phase 4 | Pending | Not benchmarked yet |

## Benchmarks
Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

### Phase 3 measured result
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Language-service warm project-source scan | `113.65 ms` | `65.21 ms` | `42.6%` faster |
| Language-service warm project-source scan allocations | `16,288,040 B` | `2,400,040 B` | `85.3%` lower |

### Existing carried-forward results
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Parser object-node attribute scan | `36.63 ms` | `21.67 ms` | `40.8%` faster |
| Compiler-host diagnostic filter | `77.63 ms` | `47.72 ms` | `38.5%` faster |
| LS URI invalidation | `253.03 ms` | `0.64 ms` | `99.7%` faster |
| Avalonia feature enricher | `179.28 ms` | `110.95 ms` | `38.1%` faster |
| LS CLR member resolver | `144.49 ms` | `101.44 ms` | `29.8%` faster |

Note: the CLR member resolver benchmark already landed in phase 2. The canonical recorded improvement remains the earlier best-of measurement (`144.49 ms -> 101.44 ms`, `29.8%` faster, allocations `162,800,040 B -> 109,200,040 B`).

## Test Coverage Added
- `tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs`
  - `References_ForElementType_ResolveProjectFromWorkspaceDirectory`
- `tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
  - `LanguageService_ProjectSourceSnapshot_WarmScan_Outperforms_Baseline`

## Full Validation
Planned command after implementation:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:

- `1136` passed
- `7` skipped
- `0` failed
- duration: `4 m 15 s`

## Exit Criteria
- Warm project-source scan benchmark remains at least `35%` faster than the baseline.
- Full test suite remains green.
- No behavioral change in reference results for directory-based workspace roots.

## Next Phase Candidates
1. `AvaloniaCodeEmitter` string materialization reduction in parent-stack and hot reload descriptor builders.
2. Reference-service project include glob refresh cost if it still shows up in traces after the snapshot cache.
3. `CSharpToXamlNavigationService` cross-language file scans if LS cross-language navigation becomes the next dominant path.
