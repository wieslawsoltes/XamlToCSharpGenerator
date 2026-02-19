# Final Parity Gap Closure Plan and Execution

Date: 2026-02-19

## Objective
Close the last known gaps between current SourceGen backend behavior and a shippable full-parity posture for XamlIl/Avalonia integration.

## Scope
1. Eliminate `dotnet watch` duplicate AXAML source warnings.
2. Expand differential parity harness from baseline build success to feature-tagged coverage.
3. Promote performance harness from opt-in to dedicated enforced CI lane.
4. Produce parity sign-off evidence and update parity matrix bookkeeping.
5. Keep release hardening visible (warnings/dependency risk tracking).

## Workstream Plan

### WS1: Watch Warning Elimination
1. Reproduce duplicate AXAML warnings under `dotnet watch --verbose`.
2. Ensure SourceGen-mode Avalonia compilation disable switches are applied in `.props` (early evaluation), not only `.targets`.
3. Harden AXAML-to-`AdditionalFiles` projection to remove path-variant duplicates.
4. Add/extend integration tests to guard duplicate-free SourceGen projection behavior.
5. Validate with watch session and `--list`.

Exit criteria:
1. No duplicate AXAML source warning from watch startup/session processing.

### WS2: Differential Feature Corpus Expansion
1. Add feature-tagged fixture set:
   - bindings,
   - styles/selectors,
   - templates,
   - includes/resources.
2. Build each fixture under both backends (`XamlIl` and `SourceGen`).
3. Normalize and compare diagnostics buckets by feature fixture.
4. Assert SourceGen generated output invariants (`.XamlSourceGen.g.cs` presence).
5. Emit feature-tagged differential summary in test output.

Exit criteria:
1. Differential tests report per-feature pass/fail and fail on mismatched diagnostics/build outcomes.

### WS3: Perf Lane Enforcement
1. Convert perf harness from static skip to environment-gated execution.
2. Introduce threshold settings (full build, incremental edit, include edit, ratio guardrail).
3. Add CI workflow with dedicated perf job enabling perf gates.

Exit criteria:
1. Perf tests run and enforce thresholds in dedicated CI job.

### WS4: Parity Sign-Off Bookkeeping
1. Update parity matrix notes with current coverage and evidence references.
2. Add evidence dashboard mapping feature rows -> tests/docs.
3. Mark remaining non-semantic hardening tasks separately from semantic parity rows.

Exit criteria:
1. Matrix status and evidence are auditable from docs without ambiguity.

### WS5: Release Hardening Follow-Up
1. Track known warnings/risk debt:
   - NU1903 transitive vulnerability in sample graph,
   - doc/analyzer warning debt (CS1591/RS2008).
2. Add explicit release checklist notes for accepted vs blocking warnings.

Exit criteria:
1. Release checklist has explicit warning policy and current status.

## Execution Log
1. [x] Plan file created.
2. [x] WS1 implementation.
   - Moved `EnableAvaloniaXamlCompilation=false` to SourceGen `.props` for early evaluation.
   - Hardened AXAML `AdditionalFiles` projection cleanup (`SourceItemGroup=AvaloniaXaml`, relative/full-path removes) in build targets.
   - Added watch-mode integration regression test:
     - `SourceGen_Backend_WatchMode_Removes_AvaloniaXaml_And_Leaves_Deduplicated_AdditionalFiles`.
3. [x] WS2 implementation.
   - Added feature-tagged differential corpus test:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Build/DifferentialFeatureCorpusTests.cs`
   - Feature tags covered:
     - `bindings`, `styles`, `templates`, `resources`.
4. [x] WS3 implementation.
   - Added `PerfFact` env-gated attribute and threshold-based perf assertions:
     - `AXSG_PERF_MAX_FULL_BUILD_MS`
     - `AXSG_PERF_MAX_SINGLE_EDIT_MS`
     - `AXSG_PERF_MAX_INCLUDE_EDIT_MS`
     - `AXSG_PERF_MAX_INCREMENTAL_TO_FULL_RATIO`
   - Added dedicated CI perf lane:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/.github/workflows/ci.yml` (`perf-sourcegen` job).
5. [x] WS4 implementation.
   - Added parity evidence dashboard:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/35-parity-evidence-dashboard.md`
   - Updated matrix/checklist references:
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/04-parity-matrix.md`
     - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/32-ws73-packaging-migration-and-release-checklist.md`
6. [x] WS5 implementation.
   - Added release warning policy section and tracked current warning debt in checklist docs.

## Validation Commands
1. `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.sln --nologo -m:1 /nodeReuse:false --disable-build-servers`
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers`
3. `dotnet watch --verbose --project /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj`

## Validation Results
1. Build integration tests:
   - `Passed: 4, Failed: 0`.
2. Differential feature corpus tests:
   - `Passed: 4, Failed: 0`.
3. Full test suite:
   - `Passed: 137, Skipped: 1, Failed: 0`.
4. Full solution build:
   - succeeded.
5. Watch verification:
   - duplicate AXAML source-file warnings no longer observed.

## Remaining (Non-Parity Semantics)
1. `NU1903` warning from sample dependency graph (`SkiaSharp` transitive path).
2. Existing documentation/analyzer warning debt (`CS1591`, `RS2008`, `RS1036`) requiring separate quality pass.
