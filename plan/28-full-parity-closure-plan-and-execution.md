# Full Parity Closure Plan and Execution (Wave 6-8)

Date: 2026-02-19

## Objective
Close all remaining parity blockers identified in the latest review:
1. `WS3.3` ControlTheme runtime materialization and validation parity tail.
2. `WS5` include graph + merge/resource precedence parity.
3. `WS6` namescope/source-info/runtime-service hardening.
4. `WS7` differential validation and release hardening baseline.

## Execution Strategy
Implement one workstream slice at a time with immediate tests and build validation after each slice.

## Work Breakdown

### Slice A (WS5.1 + WS6.3): Include Graph + Runtime URI Hardening
1. Add global generator analysis over all parsed AXAML documents.
2. Implement deterministic local include URI resolution:
   - `avares://<CurrentAssembly>/...`
   - rooted includes (`/X.axaml`)
   - relative includes (`../X.axaml`, `./X.axaml`, `X.axaml`)
3. Add diagnostics:
   - `AXSG0403` include target not found in local compile set.
   - `AXSG0404` include cycle detected.
   - `AXSG0601` duplicate generated URI registration target.
4. Extend include semantic model with resolved source URI metadata.
5. Emit include-edge registration to runtime graph registry.
6. Add runtime include graph registry with direct/transitive deterministic traversal.
7. Harden URI loader registry conflict/missing handling events.
8. Add generator/runtime regression tests for diagnostics and runtime graph behavior.

Status: `Completed`

### Slice B (WS3.3): ControlTheme Validation/Materialization Tail
1. Add ControlTheme `BasedOn` reference resolution for local keys.
2. Add `BasedOn` missing-key diagnostics parity branch.
3. Add `BasedOn` cycle diagnostics parity branch.
4. Normalize and validate `ThemeVariant` conversion edge cases.
5. Add regression tests for `BasedOn` chains and cycle/missing behavior.

Status: `Completed`

### Slice C (WS6.1 + WS6.2): NameScope + SourceInfo Completion
1. Expand namescope registration parity for nested/template identity nodes.
2. Add stable source-info node identity emission for setters/property nodes.
3. Add tests for source-info identity stability and nested namescope behavior.

Status: `Completed`

### Slice D (WS7.1 + WS7.2 + WS7.3): Differential/Perf/Packaging Baseline
1. Add fixture scaffolding for dual backend differential comparison (`XamlIl`/`SourceGen`).
2. Add deterministic-output regression checks over repeated runs.
3. Add baseline perf harness shell (single-file edit + cross-file include edit).
4. Update migration/compat docs and release checklist.

Status: `Partially Completed`

## Acceptance Gates
1. New diagnostics are mapped, test-covered, and location-accurate.
2. Existing suite remains green.
3. `dotnet test` and `dotnet build` pass after each implemented slice.
4. Execution tracker update includes command outputs and remaining open items.

## Execution Log
1. Created this plan file.
2. Completed Slice A implementation:
   - Added global include graph analysis diagnostics (`AXSG0403`, `AXSG0404`) and duplicate URI target diagnostics (`AXSG0601`).
   - Added resolved include URI metadata to semantic model and include-edge runtime registration emission.
   - Added runtime include graph registry and runtime source-gen registry conflict/missing events.
   - Added generator and runtime tests for all new Slice A behaviors.
3. Completed Slice B validation branch:
   - Added ControlTheme `BasedOn` missing-key diagnostics (`AXSG0305`).
   - Added ControlTheme `BasedOn` cycle diagnostics (`AXSG0306`).
   - Added generator tests for missing and cyclic `BasedOn` chains.
4. Implemented WS5.2 static-resource parity slice:
   - Added runtime include-aware static resource resolver (`SourceGenStaticResourceResolver`).
   - Updated emitted static-resource helper to call runtime resolver with document URI context.
   - Added runtime tests for transitive include static-resource resolution.
5. Completed Slice C namescope/source-info parity slice:
   - Added resolved object node source coordinates (`Line`, `Column`) and binder propagation.
   - Decoupled generated NameScope registration from root-field assignment so template-local names are registered even when no backing field is emitted.
   - Expanded source-info generation to recursive object/property/property-element plus style-setter/control-theme-setter identities with deterministic structural keys.
   - Extended runtime `XamlSourceInfoRegistry` with deterministic retrieval order, kind filter/query methods, and explicit `Clear()` support.
   - Added generator/runtime tests for template NameScope registration and source-info ordering/query behavior.
6. Completed WS5.2 precedence hardening slice:
   - Extended runtime include edge descriptor with deterministic registration order (`Order`).
   - Updated include graph direct traversal to preserve registration order.
   - Updated static resource include fallback traversal to evaluate transitive merged dictionaries in reverse order (last include wins).
   - Added runtime tests for include registration ordering and merged dictionary duplicate-key precedence.
7. Completed Slice B materialization branch:
   - Extended `XamlControlThemeRegistry` with factory-backed materialization APIs, deterministic entry ordering, `BasedOn` chain resolution, and target-type/theme-variant selection with default fallback.
   - Extended resolved setter model with Avalonia-property owner/field metadata for emitted style/control-theme setter materialization.
   - Updated generated emission to register control-theme factories and emit per-theme generated materializer methods.
   - Added runtime tests for key-based `BasedOn` chain materialization, target-type + variant fallback behavior, and metadata-only registration fallback.
   - Added generator test asserting factory registration and emitted control-theme builder method shape.
8. Validation commands:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
     - Passed: `122`, Failed: `0`.
   - `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
     - Build succeeded.
9. Started Slice D deterministic baseline:
   - Added generator-level determinism regression tests for repeated-run and additional-file-order invariance.
   - Extended generator test harness helper to capture `GeneratorDriverRunResult` and compare generated source content by hint name.
10. Validation commands:
   - `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -m:1 /nodeReuse:false --disable-build-servers`
     - Passed: `124`, Failed: `0`.
   - `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/XamlToCSharpGenerator.slnx -m:1 /nodeReuse:false --disable-build-servers`
     - Build succeeded.
