# Full Parity Closure Plan (XAML Standard + XamlX + Avalonia Integration)

Date: 2026-02-19

## 1) Current baseline (implemented in this pass)

### Completed now
- Fixed differential parity blockers:
  - `style-selector` fixture compile parity.
  - `include-resource` fixture compile parity.
  - `deferred-template-resource-basic` fixture compile parity.
- Fixed style/control graph materialization gaps:
  - `StyleBase` nodes now prefer direct `Add(...)` child attachment semantics (setter/style children) instead of `Children` collection misrouting.
  - Avalonia default type resolution now includes `Avalonia.Markup.Xaml.Styling.*` (`ResourceInclude`, `StyleInclude`, `MergeResourceInclude`).
  - Settable property-element assignment precedence now emits direct assignment (for example `Application.Resources = ...`) before collection-add fallbacks.
- Added parity features:
  - `Classes="..."` literal now materializes as collection-add operations on `StyledElement.Classes`.
  - `Setter.Value` conversion now infers value type from resolved `Setter.Property` Avalonia property metadata.
- Fixed sample integration warning source:
  - Updated sample AXAML item declaration to metadata-only `Update` so default Avalonia globs are not duplicated.

### Validation
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`:
  - Passed: `173`
  - Skipped: `1`
  - Failed: `0`
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample/SourceGenCrudSample.csproj`:
  - No AXAML duplicate-source warning.

## 2) What is still missing for true full parity

Even with green tests, full parity with Avalonia XamlX + broader XAML language is not closed yet. Remaining gaps:

1. XAML language surface not yet complete:
   - `x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`, `x:Array`, richer object-construction grammar.
   - More complete directive/property-element interaction parity.

2. Markup-extension family breadth:
   - Missing/partial support for extension families beyond current implemented paths (for example richer binding composites and uncommon extension argument forms).
   - More complete `ProvideValue` call-path parity for extension-specific edge cases.

3. Runtime differential breadth:
   - Existing differential runtime tests cover selected cases; broader Avalonia `BasicTests` behavioral corpus still needs direct sourcegen-vs-xamlil runtime comparison.

4. Template/deferred edge behavior:
   - Additional nested deferred template + resource/include + name-scope edge permutations still need parity fixtures.

5. Include/resource advanced behavior:
   - Cross-assembly include graph and advanced merge precedence behaviors need explicit parity fixtures and hardening.

6. IDE/watch hardening:
   - Stress validation under sustained watch edits with transient invalid AXAML + recovery across multiple files.

7. Non-semantic release hardening:
   - Public API documentation/analyzer warning debt (`CS1591`, `RS1036`, related analyzer quality rules).
   - Dependency hygiene warning cleanup (`NU1903` in sample graph).

## 3) Execution waves to finish remaining parity

## Wave 1 (done in this pass)
- Materialization/order/type-resolution blockers:
  - style/setter child routing
  - include type resolution
  - settable resource property-element ordering
  - classes literal conversion
  - setter-value typed conversion

Exit: Achieved (differential blockers removed, tests green).

## Wave 2 (next): XAML construction grammar parity
Scope:
- Add parser/binder support for:
  - `x:Arguments`
  - `x:FactoryMethod`
  - `x:TypeArguments`
  - `x:Array`
- Add deterministic emission for constructor/factory argument graphs.

Tests:
- Parser unit tests for each directive.
- Generator snapshot tests for emitted construction code.
- Differential build fixtures for each feature.

Exit:
- No AXSG errors for valid fixture set.
- Matching build success between `SourceGen` and `XamlIl`.

## Wave 3 (next): Markup-extension breadth + service-context closure
Scope:
- Expand extension conversion matrix and argument parsing.
- Ensure service-context parity across all extension paths (`IProvideValueTarget`, `IRootObjectProvider`, `IUriContext`, parent stack).

Tests:
- Runtime unit tests on `SourceGenMarkupExtensionRuntime`.
- Differential runtime fixtures for extension-heavy pages.

Exit:
- Runtime outcomes match between backends on selected extension corpus.

## Wave 4 (next): Deferred/template/runtime differential expansion
Scope:
- Add nested template/deferred parity fixtures:
  - template-in-template
  - deferred resources + includes + theme lookups
  - name-scope and parent-stack edge propagation

Tests:
- Extend `DifferentialRuntimeBehaviorTests` fixture corpus.
- Add headless assertions for instantiated tree semantics.

Exit:
- Runtime differential suite passes without behavior deltas.

## Wave 5 (next): Include/resource advanced parity
Scope:
- Cross-assembly include URI resolution and merge ordering.
- Theme/control-theme resource lookup precedence hardening.

Tests:
- Build + runtime differential fixtures with multiple assemblies and chained includes.

Exit:
- Include graph/materialization behavior matches expected XamlX/Avalonia semantics.

## Wave 6 (next): Release hardening
Scope:
- Resolve/accept analyzer warning policy (`CS1591`, `RS1036`, rule configuration).
- Address sample dependency vulnerability warnings (package updates where possible).
- Final packaging + migration verification on sample apps.

Exit:
- CI green with enforced warning policy.
- Migration/sample docs reflect final behavior and switches.

## 4) Tracking and acceptance

Definition of done for full parity closure:
- Differential build parity: pass on complete fixture corpus.
- Differential runtime parity: pass on expanded lifecycle/deferred/markup corpus.
- No known unimplemented XAML language directives in documented supported surface.
- SourceGen sample builds/runs/watch-edit without duplicate-item integration warnings.
- Release hardening gates (warnings + dependency + packaging) closed or explicitly waived with documented rationale.
