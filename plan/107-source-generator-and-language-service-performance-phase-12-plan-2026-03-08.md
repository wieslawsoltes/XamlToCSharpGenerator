# Source Generator and Language Service Performance Plan - Phase 12 (2026-03-08)

## Scope

This phase targets the remaining ordered-LINQ and string-key churn in compiler-host transform configuration aggregation inside:

- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs`

The hot path is startup/configuration work that runs before semantic binding:

1. parse raw transform documents from unified configuration
2. merge legacy transform rules with unified configuration
3. sort the resulting type/property alias arrays deterministically

The previous implementation still used:

- `OrderBy(...).ToImmutableArray()` over `ImmutableDictionary` entries
- `OrderBy(...).Select(...).ToImmutableArray()` over alias dictionaries
- repeated string concatenation in key builders

That cost is small per project, but it compounds across generator runs and sits on the compiler-host critical path.

## Guard rails

- Preserve deterministic ordering by alias key and raw transform document key.
- Preserve override diagnostics:
  - legacy rule files overridden by unified configuration still emit `AXSG0903`.
- Preserve public behavior and keep all changes `netstandard2.0`-compatible.
- Add direct unit tests before relying on benchmark wins.

## Optimization matrix

| Area | File | Method / Path | Previous Cost Shape | Optimization | Validation |
| --- | --- | --- | --- | --- | --- |
| Raw transform document ordering | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | `ParseConfigurationTransformRuleInputs(...)` | LINQ `OrderBy` over dictionary + builder materialization | copy dictionary entries to array, `Array.Sort`, fill result array directly | direct ordering test + microbenchmark |
| Transform alias merge output | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | `MergeTransformConfigurations(...)` | LINQ `OrderBy`/`Select`/`ToImmutableArray` for type/property aliases | copy dictionary entries to arrays, `Array.Sort`, project aliases in loops | direct merge/diagnostic test + microbenchmark |
| Alias key creation | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Compiler/XamlSourceGeneratorCompilerHost.cs` | `BuildTypeAliasKey(...)`, `BuildPropertyAliasKey(...)` | repeated `+` concatenation | `string.Concat` | covered by merge tests + microbenchmark |
| Perf harness coverage | `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/CompilerMicrobenchmarkTests.cs` | transform aggregation benchmark | no direct benchmark | added parse+merge benchmark | perf harness |

## Test additions

- Add `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/Configuration/CompilerHostTransformConfigurationTests.cs`
  - raw transform document ordering
  - unified configuration override semantics
  - deterministic alias ordering

## Benchmark plan

Add:

- `CompilerHost_TransformConfigurationAggregation_Outperforms_Baseline`

State:

- large raw transform document dictionary with mixed-case keys
- large base and overlay transform configurations with duplicate and non-duplicate aliases
- compare baseline LINQ implementation against optimized compiler-host path

Exit criteria:

- direct compiler-host transform tests pass
- focused benchmark is positive
- full perf slice passes
- full suite passes

## Measured results

Focused benchmark:

- baseline: `1203.81 ms`
- optimized: `840.11 ms`
- `30.2%` faster
- allocations: `1,291,352,368 B -> 1,123,802,440 B`
- `13.0%` lower

## Validation

Focused compiler-host tests:

- `21 passed`

Focused benchmark:

- `CompilerHost_TransformConfigurationAggregation_Outperforms_Baseline` passed

Full perf slice:

- `18 passed`

Full suite:

- `1160 passed`
- `19 skipped`
- `0 failed`
- duration `4 m 23 s`
