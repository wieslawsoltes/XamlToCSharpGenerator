# Final Remaining Closure Plan

Date: 2026-02-19

## Current Completion Snapshot
1. `WS7.1` baseline dual-backend differential harness: done.
2. `WS7.2` baseline incremental perf harness scaffolding: done (opt-in test lane).
3. `WS7.3` packaging and migration baseline closure: done.

## Remaining High-Value Work

### 1) Dotnet Watch Duplicate AXAML Warning Elimination
Status: `Open`

Work items:
1. Reproduce warning under isolated fixture with minimal project graph.
2. Capture item graph used by watch (including design-time builds) and locate duplicate producer.
3. Add build-target normalization/removal step for duplicate source surfaces specific to watch evaluation.
4. Add automated regression test for warning-free watch startup (or scripted verification target if watch automation is unstable in CI).

Exit:
1. `dotnet watch` startup no longer emits duplicate AXAML source-file warnings.

### 2) Differential Harness Expansion to Feature Tags
Status: `Open`

Work items:
1. Add fixture categories:
   - bindings,
   - styles/selectors,
   - templates,
   - include/resources.
2. Add expected-result normalization and comparison layer:
   - build diagnostics buckets,
   - generated source presence invariants,
   - runtime smoke checks where deterministic.
3. Emit feature-tagged differential summary for CI logs.

Exit:
1. Differential report shows per-feature pass/fail with explicit failing fixture IDs.

### 3) Perf Harness Promotion to Enforced CI Lane
Status: `Open`

Work items:
1. Unskip performance harness in dedicated CI job only.
2. Define baseline thresholds for:
   - full clean build,
   - single-file AXAML incremental build,
   - include-graph edit incremental build.
3. Fail CI on threshold regressions.

Exit:
1. Perf lane runs automatically and enforces agreed thresholds.

## Suggested Execution Order
1. Watch warning elimination.
2. Differential feature-tag expansion.
3. Perf lane threshold enforcement.

## Status Update (2026-02-19)
1. Watch warning elimination: `Completed`.
   - SourceGen build contract now applies early Avalonia compile disable in props and hardened AXAML projection cleanup.
   - Regression coverage: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/BuildIntegrationTests.cs`.
2. Differential feature-tag expansion: `Completed`.
   - Feature corpus coverage implemented in `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`.
3. Perf lane threshold enforcement: `Completed`.
   - Env-gated threshold harness + dedicated workflow job added:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerformanceHarnessTests.cs`
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/PerfFactAttribute.cs`
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/.github/workflows/ci.yml`.
