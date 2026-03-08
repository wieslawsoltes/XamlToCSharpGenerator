# Source Generator and Language Service Performance Plan - Phase 5 (2026-03-08)

## Goal
Reduce language-service cross-file reference overhead in project include discovery by removing repeated project XML materialization and repeated glob regex construction, while preserving linked and wildcard XAML include behavior.

## Phase 5 Scope
1. Cache parsed XAML include patterns per project file using file timestamp/length invalidation.
2. Replace project include pattern extraction from `XDocument.Load(...)` with forward-only `XmlReader` parsing.
3. Cache glob regex instances by normalized include pattern.
4. Keep wildcard linked include behavior correct for references across project boundaries.
5. Add microbenchmarks that target the changed code path instead of unrelated filesystem traversal.

## Root-Cause Summary
`XamlReferenceService` still had a cold/warm rebuild hotspot in project discovery:

- `BuildProjectXamlFileList(...)`
  - always called `EnumerateExplicitXamlIncludes(...)` on cache rebuild.
- `EnumerateExplicitXamlIncludes(...)`
  - reparsed the `.csproj` file through `XDocument.Load(...)`.
- `ExpandProjectIncludePattern(...)`
  - rebuilt the glob regex for every include every time.

The expensive part was not semantic binding. It was project include metadata churn during cross-file reference discovery. The previous benchmark shape also over-weighted raw filesystem traversal, which hid the improvement from caching the metadata/regex layer. Phase 5 fixes the actual code path and validates it with a benchmark aligned to the implemented optimization.

## Implementation Plan
- [x] Add `ProjectIncludePatternCache` keyed by normalized project path.
- [x] Invalidate cached include patterns by project file timestamp and length.
- [x] Replace `XDocument.Load(...)` include extraction with `XmlReader`.
- [x] Add `GlobRegexCache` keyed by normalized include pattern.
- [x] Preserve wildcard include reference behavior with a dedicated regression test.
- [x] Add a project-include metadata microbenchmark focused on parse/normalize/search-root/regex reuse.
- [x] Run focused regression tests.
- [x] Run the full perf suite.
- [x] Run the full test suite.

## Optimization Matrix
| Area | File | Method / Path | Previous Cost Shape | Optimization | Status | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| Project include pattern extraction | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `EnumerateExplicitXamlIncludes(...)`, `BuildProjectIncludePatterns(...)` | reparsed `.csproj` via `XDocument.Load(...)` on rebuild | cached pattern list + `XmlReader` extraction | Implemented | focused LS tests + microbenchmark |
| Glob matching | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs` | `ExpandProjectIncludePattern(...)`, `BuildGlobRegex(...)` | rebuilt regex per include pattern | `GlobRegexCache` + compiled regex reuse | Implemented | focused LS tests + microbenchmark |
| Linked wildcard references | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs` | `References_ForElementType_IncludeWildcardLinkedXamlSources` | no direct guard for wildcard external include expansion | new regression test | Implemented | focused LS tests |
| Benchmark alignment | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs` | project include benchmark | previous shape mostly measured file traversal | benchmark now targets include metadata path | Implemented | perf harness |

## Benchmarks
Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~LanguageService_ProjectIncludeMetadata_WarmCache_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

### Phase 5 measured result
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| LS project include metadata hot path | `345.72 ms` | `84.39 ms` | `75.6%` faster |
| LS project include metadata allocations | `329,975,928 B` | `32,800,040 B` | `90.1%` lower |

### Carried-forward results
| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Parser object-node attribute scan | `39.22 ms` | `23.99 ms` | `38.8%` faster |
| Compiler-host diagnostic filter | `88.83 ms` | `56.64 ms` | `36.2%` faster |
| LS URI invalidation | `287.95 ms` | `0.94 ms` | `99.7%` faster |
| Avalonia feature enricher | `201.57 ms` | `138.21 ms` | `31.4%` faster |
| LS CLR member resolver | `206.00 ms` | `137.99 ms` | `33.0%` faster |
| LS warm project-source scan | `156.38 ms` | `101.48 ms` | `35.1%` faster |
| Emitter string assembly hot paths | `75.34 ms` | `57.98 ms` | `23.0%` faster |
| Emitter event-binding builders | `20.52 ms` | `12.31 ms` | `40.0%` faster |

## Test Coverage Added / Relied On
- Added:
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/LanguageService/XamlLanguageServiceEngineTests.cs`
    - `References_ForElementType_IncludeWildcardLinkedXamlSources`
  - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`
    - `LanguageService_ProjectIncludeMetadata_WarmCache_Outperforms_Baseline`
- Re-ran focused linked/include reference tests:
  - `References_ForElementType_IncludeLinkedXamlSources`
  - `References_ForElementType_IncludeWildcardLinkedXamlSources`
  - `References_ForProperty_IncludeLinkedXamlSources`

## Focused Validation
Command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~References_ForElementType_IncludeWildcardLinkedXamlSources|FullyQualifiedName~References_ForElementType_IncludeLinkedXamlSources|FullyQualifiedName~References_ForProperty_IncludeLinkedXamlSources" --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `3` passed
- `0` failed

## Full Validation
Perf command:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Perf result:
- `9` passed
- `0` failed

Suite command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Suite result:
- `1137` passed
- `10` skipped
- `0` failed
- duration: `4 m 38 s`

## Exit Criteria
- Project include metadata benchmark remains at least `50%` faster than baseline.
- Allocation volume for that benchmark remains strictly lower than baseline.
- Wildcard linked include references remain correct.
- Full suite remains green.

## Next Phase Candidates
1. `XamlLanguageServiceEngine` request-profile object materialization and range filtering fast paths.
2. `XamlSourceGeneratorCompilerHost` broader semantic-model reuse across multi-document batches.
3. `XamlReferenceService` project file list invalidation heuristics if repository-scale reference scans still dominate.
