# Phase 8: Compiler Host Path Normalization Hot Path
Date: 2026-03-08
Status: Implemented and validated

## Goal
Reduce allocation and CPU cost in compiler-host path normalization used by snapshot dedupe and cache-key generation, while preserving exact duplicate-resolution semantics.

## Scope
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostSnapshotNormalizationTests.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs`

## Root Cause
`NormalizeDedupePath(...)` and `NormalizePathSegments(...)` were still using a split/list/join pipeline:
- unconditional separator replacement before and after `Path.GetFullPath(...)`
- `string.Split(...)` per path
- per-segment `List<string>` materialization
- `string.Join(...)` for final assembly

This code sits on the hot path for:
- configuration snapshot dedupe
- XAML input snapshot dedupe
- transform rule snapshot dedupe
- cache-key normalization for compiler inputs

## Optimization Matrix
| Area | Method | Baseline issue | Optimization | Risk | Test coverage |
|---|---|---|---|---|---|
| Compiler host | `NormalizeDedupePath(string)` | Extra `Replace(...)` allocations before/after `GetFullPath(...)` | Defer separator normalization to segment scan; keep `GetFullPath(...)` lexical fallback | Low | Existing snapshot dedupe tests + new direct normalization tests |
| Compiler host | `NormalizePathSegments(string)` | `Split` + `List<string>` + `Join` allocate per segment/path | Manual char scan with pooled segment metadata and pooled char buffer | Medium | New direct lexical normalization tests |
| Compiler host | `NormalizeCacheKeyFilePath(string, string?)` | Repeated `Replace(...)` allocation on every path | Route through optimized segment normalizer directly | Low | Existing compiler-host integration coverage |
| Compiler host | `NormalizeCacheKeyTargetPath(string)` | Redundant normalization string allocation | Route through optimized segment normalizer directly | Low | Existing cache-key behavior coverage indirectly |
| Tests | `CompilerHostSnapshotNormalizationTests` | Only indirect duplicate behavior was covered | Add direct `.` / `..` / separator / UNC-like cases | Low | Added |
| Perf | `CompilerMicrobenchmarkTests` | No direct benchmark for path normalization | Add dedicated microbenchmark | Low | Added |

## Implementation Notes
- Keep semantics deterministic and platform-portable.
- Do not introduce reflection or platform-specific path guessing.
- Preserve current rooted behavior:
  - rooted paths do not accumulate leading `..`
  - relative paths preserve leading `..`
  - UNC-like and unix-root lexical prefixes stay intact
- Use only APIs compatible with `netstandard2.0`.

## Validation Plan
1. Add direct tests for lexical normalization semantics.
2. Add focused microbenchmark for path normalization.
3. Re-run existing snapshot-normalization benchmark to verify composition win.
4. Run focused compiler-host tests.
5. Run full perf suite.
6. Run full test suite.

## Measured Result
### Direct path normalization benchmark
- baseline: `624.06 ms`
- optimized: `452.37 ms`
- improvement: `27.5%`
- allocations: `2,633,673,640 B -> 418,934,440 B`
- allocation reduction: `84.1%`

### Snapshot normalization benchmark after path normalization
- baseline: `2045.53 ms`
- optimized: `824.23 ms`
- improvement: `59.7%`
- allocations: `1,862,694,864 B -> 489,863,008 B`
- allocation reduction: `73.7%`

## Validation Result
- Focused normalization tests: `10 passed`
- Perf slice (`CompilerMicrobenchmarkTests`): `14 passed`
- Full suite: `1146 passed, 15 skipped, 0 failed` in `4 m 27 s`
