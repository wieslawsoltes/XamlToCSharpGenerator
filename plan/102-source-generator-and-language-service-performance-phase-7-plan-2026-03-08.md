# Source Generator and Language Service Performance Plan - Phase 7 (2026-03-08)

## Goal
Reduce compiler-host discovery overhead in incremental-generator setup by removing redundant pre-sort work from configuration, XAML, and transform-rule input snapshot normalization while preserving the existing winner-selection semantics exactly.

## Phase 7 Scope
1. Replace the current sort-then-dedupe-then-sort-again snapshot builders in `XamlSourceGeneratorCompilerHost` with single-pass dedupe builders and one final deterministic sort.
2. Make the winner-selection rules explicit instead of letting sort order imply them.
3. Add regression tests for duplicate normalized-path handling across:
   - configuration files
   - XAML inputs
   - transform rule inputs
4. Add a microbenchmark for the dominant snapshot path: XAML input normalization.

## Root-Cause Summary
`XamlSourceGeneratorCompilerHost` still paid unnecessary work during generator initialization:

- configuration snapshot:
  - sorted the full input set
  - overwrote duplicates in a dictionary
  - sorted the deduped values again
- XAML input snapshot:
  - sorted the full input set by `FilePath` and `TargetPath`
  - deduped by normalized source path
  - sorted the deduped values again
- transform rule snapshot:
  - sorted the full input set
  - overwrote duplicates in a dictionary
  - sorted the deduped values again

The second sort is required for deterministic output. The first sort is not, as long as the duplicate winner rules are implemented directly and tested.

## Existing Semantics To Preserve
### Configuration files
- duplicate key: normalized source path
- winner: lexicographically later `Path` under `OrdinalIgnoreCase`
- final output order: ascending `Path`

### Transform rule inputs
- duplicate key: normalized source path
- winner: lexicographically later `FilePath` under `OrdinalIgnoreCase`
- final output order: ascending `FilePath`

### XAML inputs
- duplicate key: normalized source path
- winner:
  1. better `TargetPath` preference
  2. if target-path preference ties, lexicographically smaller `FilePath`
- final output order:
  1. ascending `FilePath`
  2. ascending `TargetPath`

## Implementation Plan
- [x] Introduce explicit snapshot-builder helpers in `XamlSourceGeneratorCompilerHost`.
- [x] Switch the incremental pipeline to those helpers.
- [x] Add regression tests for the three duplicate-path cases.
- [x] Add the XAML snapshot microbenchmark.
- [x] Record measured results.
- [x] Run focused tests.
- [x] Run perf slice.
- [x] Run full suite.

## Optimization Matrix
| Area | File | Method / Path | Previous Cost Shape | Optimization | Status | Validation |
| --- | --- | --- | --- | --- | --- | --- |
| Configuration snapshot | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | configuration file collection | full sort + dedupe + final sort | single-pass dedupe + final sort | Implemented | regression tests |
| XAML input snapshot | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | XAML discovery collection | full sort + dedupe + final sort | single-pass dedupe + explicit winner rules + final sort | Implemented | regression tests + microbenchmark |
| Transform rule snapshot | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | transform rule collection | full sort + dedupe + final sort | single-pass dedupe + final sort | Implemented | regression tests |
| Snapshot semantics regression | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostSnapshotNormalizationTests.cs` | duplicate-path semantics | no direct guard | explicit winner tests | Implemented | focused tests |
| Compiler-host perf harness | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs` | XAML snapshot normalization | no direct benchmark | added baseline-vs-optimized benchmark | Implemented | perf harness |

## Exit Criteria
- Compiler-host snapshot normalization preserves existing winner-selection behavior.
- XAML snapshot normalization benchmark shows measurable elapsed and allocation improvement.
- Focused and full test suites stay green.

## Benchmarks
Command used:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHost_XamlInputSnapshot_Normalization_Outperforms_Baseline" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=detailed"
```

| Scenario | Baseline | Optimized | Improvement |
| --- | ---: | ---: | ---: |
| Compiler-host XAML snapshot normalization | `2055.76 ms` | `1061.52 ms` | `48.4%` faster |
| Compiler-host XAML snapshot allocations | `1,862,696,432 B` | `1,680,262,688 B` | `9.8%` lower |

Notes:
- The dominant win in this phase is elapsed time. Allocation reduction is moderate because `NormalizeDedupePath(...)` still does the same path normalization work per input and remains the next likely hotspot inside this slice.
- Configuration and transform-rule snapshots now use the same single-pass strategy and are covered by regression tests, but the measured benchmark focuses on the XAML path because it dominates real generator input volume.

## Focused Validation
Command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerHostSnapshotNormalizationTests|FullyQualifiedName~Deduplicates_AdditionalFiles_With_Same_Path_To_Avoid_HintName_Collisions|FullyQualifiedName~Duplicate_Path_Representations_Are_Deduplicated_To_Avoid_HintName_Collisions" --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Result:
- `6` passed
- `0` failed

## Full Validation
Perf command:

```bash
AXSG_RUN_PERF_TESTS=true dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --filter "FullyQualifiedName~CompilerMicrobenchmarkTests" --nologo -m:1 /nodeReuse:false --disable-build-servers --logger "console;verbosity=minimal"
```

Perf result:
- `13` passed
- `0` failed

Suite command:

```bash
dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

Suite result:
- `1142` passed
- `14` skipped
- `0` failed
- duration: `4 m 35 s`
