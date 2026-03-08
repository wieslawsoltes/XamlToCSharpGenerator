# Phase 11 - Language Service Reference Result Sorting

Date: 2026-03-08
Status: in progress

## Scope

This phase targets deterministic reference-result sorting in `XamlReferenceService`.

Current implementation repeatedly uses:

- `OrderBy(item => item.Uri, StringComparer.Ordinal)`
- `.ThenBy(item => item.Range.Start.Line)`
- `.ThenBy(item => item.Range.Start.Character)`
- `.ToImmutableArray()`

across multiple reference collection paths.

## Root-cause analysis

The current sort shape allocates and re-materializes multiple iterator layers for every request path. This is especially expensive in reference-heavy LS features where results can span many files.

The code already deduplicates references before sorting, so a single shared array-sort pass can preserve deterministic order with lower allocation pressure.

## Optimization matrix

| Area | Current method | Problem | Planned change | Test gate | Benchmark |
| --- | --- | --- | --- | --- | --- |
| LS reference merging | `MergeReferences` | LINQ sort chain allocates iterator pipeline | shared array-sort helper | direct ordering test | reference-sort benchmark |
| Style/class/pseudo refs | `CollectStyleClassReferences`, `CollectPseudoClassReferences` | repeated LINQ sort | shared helper | full LS tests | reference-sort benchmark |
| Type/property/expression refs | `CollectTypeReferences`, `CollectTypeAttributeValueReferences`, `CollectPropertyReferences`, `CollectExpressionSymbolReferences` | repeated LINQ sort | shared helper | full LS tests | reference-sort benchmark |

## Behavioral contract

Reference order must remain deterministic. We will sort by:
1. `Uri` ordinal
2. `Range.Start.Line`
3. `Range.Start.Character`
4. `Range.End.Line`
5. `Range.End.Character`
6. declarations before usages when all coordinates match

This gives a total ordering and removes dependence on sort stability.

## Testing plan

1. Add direct ordering tests for the helper.
2. Keep existing LS reference tests as regression coverage.

## Benchmark plan

Add `LanguageService_ReferenceSort_ArraySort_Outperforms_Baseline`.

State:
- generate a large immutable array of reference locations with mixed URIs and positions
- compare baseline LINQ chain vs optimized shared helper
- assert equal checksum and lower allocations

## Exit criteria

- direct helper tests pass
- benchmark is positive
- full perf slice passes
- full suite passes

## Measured results

Focused benchmark:
- baseline: `3592.26 ms`
- optimized: `2463.61 ms`
- `31.4%` faster
- allocations: `1,083,058,248 B -> 576,940,848 B`
- `46.7%` lower

## Validation

Focused LS tests:
- `88 passed`

Focused benchmark:
- `LanguageService_ReferenceSort_ArraySort_Outperforms_Baseline` passed

Full perf slice:
- `18 passed`

Full suite:
- `1160 passed`
- `19 skipped`
- `0 failed`
- duration `4 m 23 s`
