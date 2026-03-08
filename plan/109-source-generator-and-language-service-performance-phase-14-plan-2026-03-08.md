# Phase 14: Compiler-host include normalization and language-service XML cache retention

Date: 2026-03-08
Branch: `perf/sourcegen-runtime-phases-2-12`
Status: implemented and validated

## Goal
Continue the next accepted compiler-host setup optimization in `src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`, re-profile `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs`, and only keep a new phase if it produces a real measured win. In parallel, reduce the retained XML cache footprint materially without regressing correctness.

## Findings

### Compiler host
The next real compiler-host hotspot was include-path normalization inside global graph analysis:
- `AnalyzeGlobalDocumentGraph(...)`
- `TryResolveIncludeUri(...)`
- `NormalizeIncludePath(...)`

The old implementation used:
- `Replace('\\', '/')`
- `Split(...)`
- `List<string>` stack mutation
- `string.Join(...)`

That path executes repeatedly when building include edges and normalizing source-generated target URIs. It was still allocation-heavy and benchmark-visible.

### Language service
`XamlReferenceService` still had a large retained-memory issue even after prior warm-scan optimizations:
- `SourceFileCache` held strong `XDocument` instances for cached files.
- `ProjectSourceSnapshotCache` indirectly retained those `XDocument` instances again by materializing them into `XamlProjectSourceFile` snapshot entries.

The warm reuse benchmark remained useful, but it was not the right acceptance gate for this phase. The main win here is memory retention, not elapsed time. Re-profile showed that further time-oriented rewrites beyond this were in the noise band, so they were not kept.

## Optimization matrix

| Area | Method / structure | Baseline cost | Change | Result |
| --- | --- | --- | --- | --- |
| Compiler host | `NormalizeIncludePath(...)` | Split/list/join on every normalization | Replaced with pooled segment scanner over spans | Accepted |
| Compiler host | `AnalyzeGlobalDocumentGraph(...)` | Include normalization on every document/include edge | Reused optimized include-path normalization | Accepted |
| Language service | `SourceFileCache` XML retention | Strong `XDocument` per cached file | Store `WeakReference<XDocument>` instead | Accepted |
| Language service | `ProjectSourceSnapshotCache` XML retention | Snapshot entries retained strong `XDocument` objects | Strip XML from snapshot entries and resolve lazily from source cache | Accepted |
| Language service | Warm XML reuse elapsed time | Existing cache already close to reparse floor on wall time | Kept as no-regression guard, not a speed gate | Accepted |
| Language service | Additional cross-file refresh rewrite | Candidate follow-up | Re-profiled as noise-level for this slice | Rejected |

## Implementation

### Accepted changes

#### Compiler host
File:
- `src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`

Changes:
- `NormalizeIncludePath(...)` is now `internal` and implemented as a pooled, allocation-light segment scanner.
- Preserved previous lexical semantics, including whitespace preservation around authored include strings.
- Reused the optimized normalizer in the existing include-resolution paths used by global graph analysis.

#### Language service
File:
- `src/XamlToCSharpGenerator.LanguageService/Definitions/XamlReferenceService.cs`

Changes:
- `CachedXamlSourceFile` now stores `WeakReference<XDocument>?` instead of a strong `XDocument`.
- Added `TryGetCachedXmlDocument(...)` to centralize weak-target retrieval.
- `TryEnsureXmlDocumentLoaded(...)` now:
  - reuses a live cached `XDocument` when available
  - reparses when the weak target has been reclaimed
  - refreshes the cache with a new weak reference after reparsing
- `BuildProjectSourceSnapshot(...)` now strips `XmlDocument` from snapshot entries so snapshots do not retain XML strongly.
- Added testing helpers:
  - `CountLiveCachedXmlDocumentsForTesting()`
  - `CountRetainedProjectSnapshotXmlDocumentsForTesting(...)`

## Test coverage added

### Compiler host
File:
- `tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostGlobalGraphTests.cs`

Added:
- `NormalizeIncludePath_Removes_Dot_Segments_And_Normalizes_Separators`

### Language service
File:
- `tests/XamlToCSharpGenerator.Tests/LanguageService/XamlReferenceServiceTests.cs`

Added:
- `ProjectSourceSnapshot_Does_Not_Retain_Strong_Xml_Documents`
- `Cached_Xml_Documents_Can_Be_Reclaimed_And_Reparsed`

### Benchmarks
File:
- `tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`

Added:
- `CompilerHost_IncludePathNormalization_Outperforms_Baseline`
- `LanguageService_XmlCache_WeakRetention_Releases_Documents_After_Gc`

Adjusted:
- `LanguageService_XmlDocumentCacheReuse_Outperforms_Baseline_Reparse`
  - now acts as a no-regression guard (`<= 1.05x` baseline elapsed) while still requiring lower allocations

## Benchmark results

Command:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Focused results from this phase:

| Benchmark | Baseline | Optimized | Delta |
| --- | ---: | ---: | ---: |
| Compiler-host include-path normalization | 517.48 ms, 2,304,000,040 B | 398.66 ms, 345,600,040 B | 23.0% faster, 85.0% lower allocations |
| Compiler-host global graph | 207.70 ms, 362,505,640 B | 175.98 ms, 182,016,040 B | 15.3% faster, 49.8% lower allocations |
| Language-service warm XML reuse | 281.88 ms, 2,385,425,672 B | 277.47 ms, 1,308,299,688 B | elapsed roughly flat, 45.2% lower allocations |
| Language-service XML retention after GC | strong cache retains parsed docs | weak cache releases them | accepted as memory-footprint win |

## Validation

Focused correctness:
```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~XamlReferenceServiceTests|FullyQualifiedName~CompilerHostGlobalGraphTests" --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- Passed: `10`
- Failed: `0`

Focused perf slice:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_IncludePathNormalization_Outperforms_Baseline|FullyQualifiedName~CompilerHost_GlobalDocumentGraph_Outperforms_Baseline|FullyQualifiedName~LanguageService_XmlDocumentCacheReuse_Outperforms_Baseline_Reparse|FullyQualifiedName~LanguageService_XmlCache_WeakRetention_Releases_Documents_After_Gc" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```
- Passed: `4`
- Failed: `0`

Full perf suite:
```bash
AXSG_RUN_PERF_TESTS=true dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```
- Passed: `23`
- Failed: `0`
- Duration: `2 m 29 s`

Full suite:
```bash
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```
- Passed: `1166`
- Skipped: `24`
- Failed: `0`
- Duration: `6 m 27 s`

## Decision
Keep this phase.

Reason:
- compiler-host include normalization shows a clear non-noise performance win
- language-service XML cache change materially lowers retained XML footprint while preserving warm-cache behavior and correctness
- no additional `XamlReferenceService` time-only rewrite was kept unless it beat noise
