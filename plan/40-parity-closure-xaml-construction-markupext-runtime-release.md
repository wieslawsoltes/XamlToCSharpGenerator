# Parity Closure Plan: XAML Construction + Markup Extensions + Differential Runtime + Release Hardening (2026-02-19)

## Scope
Close the remaining feature gap in four linked areas:
1. XAML construction grammar parity (`x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`, `x:Array`).
2. Broader markup-extension parity with robust `ProvideValue` context propagation.
3. Differential corpus expansion (build + runtime) focused on Avalonia BasicTests-style behavior.
4. Release hardening: warning policy and dependency warning cleanup.

## Workstreams

### WS1: Construction Grammar Parity
- Add parser/model representation for construction directives:
  - `x:FactoryMethod`, `x:TypeArguments`, `x:Arguments`, `x:Array` item type metadata.
- Extend semantic binder:
  - Construct generic type instances from `x:TypeArguments`.
  - Build constructor/factory-call expressions from `x:Arguments` and `x:FactoryMethod`.
  - Materialize `x:Array` into strongly typed C# array expressions.
  - Add diagnostics for invalid construction/factory/array paths.
- Keep emitter stable by using resolved `FactoryExpression` without introducing runtime reflection.

Acceptance
- Generator emits valid C# for canonical construction grammar scenarios.
- Existing feature tests stay green.
- New construction tests cover success + diagnostic paths.

### WS2: Markup Extension Family + ProvideValue Context
- Expand binder conversion:
  - Add primitive `x:` markup-extension forms (`x:True`, `x:False`, `x:String`, numeric forms).
  - Add generic markup-extension fallback for known `MarkupExtension` types (constructor + named args).
- Extend runtime helper:
  - Add single runtime entrypoint that invokes `ProvideValue` using full sourcegen context (`IProvideValueTarget`, `IRootObjectProvider`, `IUriContext`, parent stack).
- Ensure no compiler-path reflection.

Acceptance
- Runtime tests verify context access across generic/fallback markup-extension paths.
- Existing special-case extensions (`StaticResource`, `DynamicResource`, `Reference`, bindings, etc.) remain unchanged.

### WS3: Differential Corpus Expansion
- Add build differential fixtures for:
  - construction grammar (`x:Arguments`/`x:FactoryMethod`/`x:Array`/`x:TypeArguments`),
  - broader markup-extension usage.
- Add runtime differential fixtures for:
  - construction behavior output equivalence,
  - ProvideValue-context-dependent extension behavior equivalence.
- Add targeted include/resource precedence checks for cross-assembly URI edges.

Acceptance
- Differential suites pass with zero AXSG errors in SourceGen backend.
- Runtime result markers match between SourceGen and XamlIl for new fixtures.

### WS4: Release Hardening
- Warning policy tightening:
  - Keep local/dev permissive by default.
  - Add CI/release strict mode switch for warnings-as-errors policy.
- Dependency warning cleanup:
  - Upgrade vulnerable transitive graph (currently seen as `NU1903`) by moving sample/test package baselines to non-vulnerable versions.
- Verify `dotnet restore/build/test` warning surface after upgrade.

Acceptance
- No `NU1903` warning in sample/test restore/build flows.
- Strict mode builds fail on warnings when enabled.

## Execution Order
1. WS1 implementation + unit/generator tests.
2. WS2 runtime/binder implementation + runtime tests.
3. WS3 differential fixture expansion + verification.
4. WS4 warning/dependency hardening + full test sweep.

## Exit Criteria
- Construction grammar and expanded markup-extension features compile and run with deterministic generated output.
- Differential build/runtime suites pass for old + new fixtures.
- Full test suite passes.
- Remaining gaps documented only if blocked by upstream Avalonia/XamlX internals that cannot be represented without non-goal changes.

## Implementation Status (2026-02-19)
- `WS1` implemented:
  - Parser/model/binder/emitter support for `x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`, `x:Array`.
  - Added `AXSG0106`, `AXSG0107`, `AXSG0108` diagnostics.
  - Added generator/parser tests for construction directives and array materialization.
- `WS2` implemented:
  - Expanded primitive `x:` markup-extension conversions: `Byte`, `SByte`, `Char`, `UInt16/32/64`, `Decimal`, `DateTime`, `TimeSpan`, `Uri` (plus existing primitives).
  - Generic markup-extension fallback emits runtime `ProvideValue` call with full service context.
  - Added generator/runtime tests validating emitted/runtime context contracts.
- `WS3` expanded:
  - Added differential runtime fixture for nested deferred-template resource materialization.
  - Extended runtime markup-extension context differential fixture with `TargetProperty` + parent-stack provider checks.
  - Added additional construction differential fixture where compatible with both backends.
  - Kept sourcegen-only coverage for XamlIl-incompatible construction permutations in unit tests.
- `WS4` hardened:
  - Strict warning mode retained (`XamlToCSharpGeneratorStrictBuild` + `NU1901-NU1904` as errors).
  - Dependency baseline aligned to Avalonia `11.3.12`.
  - Analyzer/release warning cleanup:
    - enabled `EnforceExtendedAnalyzerRules` for generator project.
    - removed banned environment-variable API usage from generator path.
    - standardized warning suppression policy (`CS1591`, `RS2008`) and default doc-file generation off for local builds.

## Notes on Cross-Backend Differential Limits
- Avalonia/XamlIl currently fails on some construction combinations (`x:FactoryMethod` + certain generic directive shapes, and some `x:Array` directive forms) with internal/emitter errors.
- Those cases remain covered by sourcegen parser/generator/runtime tests; cross-backend differential corpus only includes scenarios that compile on both backends.
