# String-Type Contract and Configuration Model Plan (2026-02-26)

## 1. Goal

Replace broad string-literal type/config usage across source generator, semantic binder, and emitter with a deterministic, extensible configuration model that is:

1. Strongly typed at API boundaries.
2. Framework-profile driven (Avalonia/NoUI and future frameworks).
3. Fully configurable from:
   - MSBuild properties/items,
   - configuration files (AdditionalFiles),
   - code declarations (compile-time discoverable).
4. Backward compatible with current properties and transform rule files.

## 2. Why This Is Needed

Current behavior works but is fragmented:

1. Metadata names are hardcoded in many binder/emitter/services paths.
2. Configuration is split between:
   - `GeneratorOptions` (`build_property.*` keys),
   - transform rule JSON parsing in `AvaloniaFrameworkProfile`,
   - local ad-hoc constants in binder/services.
3. There is no single contract object representing framework semantic requirements.
4. Extending semantics currently requires editing many files.

This increases regression risk and makes feature evolution slower.

## 3. Current-State Inventory (Hotspots)

### 3.1 String-based metadata-name resolution hotspots

`GetTypeByMetadataName(...)` distribution (current snapshot):

| File | Count | Notes |
|---|---:|---|
| `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs` | 31 | Largest concentration of framework/runtime type strings. |
| `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.NodeTypeResolution.cs` | 7 | Template/style/control-theme type resolution. |
| `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs` | 6 | Alias-resolution + attribute mapping strings. |
| `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TypeResolution.cs` | 5 | General type resolution fallback paths. |
| `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs` | 5 | Style/template feature probes. |
| Other files/services | 1-2 each | NameScope/HotDesign/classification helpers. |

### 3.2 Build/config ingestion hotspots

1. `src/XamlToCSharpGenerator.Core/Models/GeneratorOptions.cs`
   - Flat list of `build_property.*` string keys.
2. `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.props`
   - Property defaults + compiler-visible properties.
3. `src/XamlToCSharpGenerator.Build/buildTransitive/XamlToCSharpGenerator.Build.targets`
   - AdditionalFiles projection for XAML and transform rules.
4. `src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaFrameworkProfile.cs`
   - Transform rule JSON parse/merge (`typeAliases`, `propertyAliases`) and parser namespace behavior.

### 3.3 String roles that should remain (by design)

Not all strings are “bad”; some are required data:

1. `Resolved*` models used by emitter to generate C# source (`TypeName`, `TargetTypeName`, etc.).
2. Runtime registries carrying serialized XAML metadata (URIs, keys, raw XAML).
3. External/interchange format keys in config files.

The target is to remove semantic string lookups from execution logic, not to eliminate all string payloads.

## 4. Reference Pattern from Avalonia/XamlX (11.3.12)

Observed integration shape:

1. Central compiler configuration object:
   - `TransformerConfiguration` (XamlX),
   - specialized `AvaloniaXamlIlCompilerConfiguration : TransformerConfiguration`.
2. Central language/type map:
   - `AvaloniaXamlIlLanguage.Configure(...)` builds `XamlLanguageTypeMappings` + emit mappings.
3. Extensibility via typed “extras”:
   - `TransformerConfiguration.AddExtra/GetExtra/GetOrCreateExtra`.
4. Build configuration flow:
   - MSBuild `.props/.targets` define defaults and pass explicit task parameters.

Implication for this repo:

1. We should centralize semantic contract in one typed object and inject it into binder/emitter/services.
2. We should support extension sections (typed extras) for framework/plugin growth.
3. We should keep build/task boundaries deterministic and strongly validated.

## 5. Target Architecture

## 5.1 New configuration root

Introduce immutable root contract:

`XamlSourceGenConfiguration`

Proposed sections:

1. `BuildOptions` (feature flags, performance/debug/strict switches).
2. `ParserOptions` (implicit xmlns behavior, namespace defaults/prefixes).
3. `SemanticContract` (framework type/property/event contract IDs -> metadata names).
4. `BindingOptions` (compiled/reflection behavior, expression policies, fallback policies).
5. `EmitterOptions` (generated code features and runtime contract hooks).
6. `TransformOptions` (type/property aliases and transform-rule payloads).
7. `DiagnosticsOptions` (severity/compatibility policy maps).
8. `FrameworkExtras` (typed extension bag, XamlX-style extras pattern).

## 5.2 Semantic contract model (critical)

Replace direct string probes with typed contract IDs.

Introduce:

1. `TypeContractId` (enum-like stable IDs, e.g. `StyledElement`, `Binding`, `TemplateBinding`, `ControlTheme`, `RoutedEvent`, etc.).
2. `PropertyContractId` / `EventContractId` where needed.
3. `SemanticContractMap`:
   - metadata name value,
   - optional fallback names,
   - required/optional marker,
   - feature tag and diagnostics metadata.
4. `ITypeSymbolCatalog`:
   - resolves/caches symbols once per compilation/config,
   - exposes typed accessors (`TryGet(TypeContractId, out INamedTypeSymbol)`),
   - emits structured diagnostics for missing required contracts.

Binder/services/emitter should consume catalog IDs, not raw metadata-name literals.

## 5.3 Configuration source model

Introduce `IConfigurationSource` pipeline with deterministic merge:

1. `BuiltInDefaultsSource` (framework-provided defaults).
2. `FileConfigurationSource` (AdditionalFiles config docs).
3. `MsBuildConfigurationSource` (`AnalyzerConfigOptions`).
4. `CodeConfigurationSource` (assembly/source declarations discoverable via Roslyn symbols).

Default precedence:

`BuiltInDefaults < File < MSBuild < Code`

Support explicit precedence override for advanced scenarios via one property:

`XamlSourceGenConfigurationPrecedence`.

## 5.4 File-based configuration

Add canonical file:

`xaml-sourcegen.config.json`

Additional supported files:

1. Existing transform-rule files (compat mode).
2. Optional future YAML/TOML adapters (not required in first slice).

Schema characteristics:

1. Versioned (`schemaVersion`).
2. Strict validation with diagnostics (`AXSG09xx` extension).
3. Full config sections, not only transform aliases.
4. Optional include/import support with cycle detection.

## 5.5 Code-based configuration

Add compile-time declarative model (no reflection, no runtime execution dependency):

1. Assembly-level attributes for targeted overrides.
2. Optional source-declared configuration class marker attribute with static data contract that generator reads via Roslyn symbols.
3. Reuse/extend existing alias attributes where possible.

## 5.6 MSBuild configuration

Keep existing properties; add grouped entry points:

1. `XamlSourceGenConfigurationFile`
2. `XamlSourceGenConfigurationFiles`
3. `XamlSourceGenConfigurationPrecedence`
4. `XamlSourceGenSemanticContractFile` (optional split file)

Existing `AvaloniaSourceGen*` properties remain supported and mapped into the new model.

## 6. Implementation Plan (Phased)

## Phase A - Inventory and guard rails

1. Build inventory test that fails if new direct `GetTypeByMetadataName("...")` appears outside allowed configuration files/services.
2. Classify existing literals into:
   - semantic-contract literals (to migrate),
   - data payload literals (to keep).
3. Add baseline metrics report for literal hotspots.

Deliverables:

1. `StringLiteralSemanticInventory` tests.
2. Analyzer-style guard test for metadata-name direct calls.

## Phase B - Core config model

1. Add `XamlSourceGenConfiguration` and section records in `Core`.
2. Add `ConfigurationBuilder` + merge semantics.
3. Add source abstraction interfaces.
4. Add validation engine and diagnostics.

Deliverables:

1. New configuration root and section types.
2. Merge/validation unit tests.

## Phase C - Semantic contract + symbol catalog

1. Add `TypeContractId`/`SemanticContractMap`.
2. Add `CompilationTypeSymbolCatalog`.
3. Add contract definitions for Avalonia and NoUI profiles.
4. Refactor binder/services to query catalog IDs.

Deliverables:

1. No raw metadata-name probes in binder business logic.
2. Catalog cache tests + required/optional contract diagnostics tests.

## Phase D - Config sources integration

1. Implement `MsBuildConfigurationSource` mapping current keys to new sections.
2. Implement `FileConfigurationSource` for `xaml-sourcegen.config.json`.
3. Implement `CodeConfigurationSource` via attributes/symbol reading.
4. Integrate into `XamlSourceGeneratorCompilerHost.Initialize(...)` pipeline.

Deliverables:

1. End-to-end config snapshot generated from combined sources.
2. Precedence and conflict tests.

## Phase E - Transform-rule convergence

1. Fold existing transform-rule model into unified config section while preserving existing rule files.
2. Keep `AvaloniaSourceGenTransformRules` behavior as compatibility adapter.
3. Emit deterministic diagnostics when both old/new configs overlap.

Deliverables:

1. Backward compatibility tests.
2. Unified transform configuration object consumed by binder.

## Phase F - Emitter/runtime contract cleanup

1. Replace emitter semantic feature probes with typed contract queries.
2. Ensure runtime registries still receive payload strings only where required.
3. Add schema-driven serialization for runtime registries where useful.

Deliverables:

1. Emitter de-stringification for semantic decisions.
2. No behavior regression in generated output snapshots.

## Phase G - Documentation and migration

1. Document full configuration schema and precedence.
2. Provide migration guide:
   - existing MSBuild properties,
   - transform rules,
   - new config file and code attributes.
3. Add examples for:
   - MSBuild-only,
   - file-only,
   - code-only,
   - mixed mode with precedence override.

Deliverables:

1. Docs + sample projects + integration tests.

## 7. Acceptance Criteria

1. Semantic logic path has zero ad-hoc metadata-name strings outside:
   - framework contract definition tables,
   - compatibility adapters,
   - payload serialization models.
2. Full configuration can be expressed through any one source:
   - MSBuild-only,
   - file-only,
   - code-only.
3. Mixed-source merges are deterministic and diagnosable.
4. Existing `AvaloniaSourceGen*` and transform-rule workflows remain functional.
5. No reflection introduced in generator/runtime semantic path.
6. Existing behavioral tests pass; new config tests added for precedence and contract resolution.

## 8. Risk and Mitigation

1. Risk: breaking subtle binder behavior when replacing direct probes.
   - Mitigation: contract-by-contract migration with guard tests per feature cluster.
2. Risk: configuration ambiguity across sources.
   - Mitigation: explicit precedence policy + conflict diagnostics.
3. Risk: over-abstracting and slowing binder.
   - Mitigation: symbol catalog caching, immutable snapshots, benchmark checks.

## 9. Concrete Next Slices (recommended order)

1. Slice 1: Add `SemanticContractMap` + `CompilationTypeSymbolCatalog` and migrate `NodeTypeResolution` + `StylesTemplates`.
2. Slice 2: Migrate `BindingSemantics` contract probes to catalog.
3. Slice 3: Introduce unified config root + MSBuild source adapter.
4. Slice 4: Add JSON config file parser and precedence engine.
5. Slice 5: Add code-based config attributes and merge tests.
6. Slice 6: Converge transform rules into unified config with compatibility shim.

## 10. Proposed New Files (initial)

1. `src/XamlToCSharpGenerator.Core/Configuration/XamlSourceGenConfiguration.cs`
2. `src/XamlToCSharpGenerator.Core/Configuration/XamlSourceGenConfigurationBuilder.cs`
3. `src/XamlToCSharpGenerator.Core/Configuration/SemanticContractMap.cs`
4. `src/XamlToCSharpGenerator.Core/Configuration/TypeContractId.cs`
5. `src/XamlToCSharpGenerator.Core/Configuration/IConfigurationSource.cs`
6. `src/XamlToCSharpGenerator.Core/Configuration/Sources/MsBuildConfigurationSource.cs`
7. `src/XamlToCSharpGenerator.Core/Configuration/Sources/FileConfigurationSource.cs`
8. `src/XamlToCSharpGenerator.Core/Configuration/Sources/CodeConfigurationSource.cs`
9. `src/XamlToCSharpGenerator.Avalonia/Binding/Services/CompilationTypeSymbolCatalog.cs`
10. `tests/XamlToCSharpGenerator.Tests/Configuration/*`

## 11. Non-goals (this plan)

1. Rewriting runtime registry payload formats unless required for semantic correctness.
2. Removing all string fields from all model records (many are valid serialized outputs).
3. Introducing runtime reflection-based configuration execution.

