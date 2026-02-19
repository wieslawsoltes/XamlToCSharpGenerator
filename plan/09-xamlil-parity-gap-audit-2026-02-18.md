# Avalonia XamlIl Parity Gap Audit (2026-02-18)

## Scope
This audit compares Avalonia's current XamlIl transform pipeline with the current C# source-generator backend in this repository.

Primary upstream references:
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/*.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/GroupTransformers/*.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets`

## Current backend status snapshot
Implemented/expanded in this iteration:
1. Compiled binding accessor generation now supports indexer segments (for example `People[0].Name`).
2. Binding markup parsing now captures `Mode`, `ElementName`, and `RelativeSource` options and emits richer `Binding` initializers.
3. Markup extension conversion added for `x:Null`, `x:Type`, `x:Static`, `StaticResource`, `DynamicResource`, and `TemplateBinding` (context-sensitive).
4. Setter property token resolution improved: style target type context can resolve `Setter Property="Text"` to `TextBlock.TextProperty`.
5. Child attachment expanded with `DirectAdd` mode for style/setter-like object graphs.
6. Generated name-scope registration path added (when Avalonia `NameScope` types are available).
7. Generated static-resource resolver helper emitted on-demand when static-resource expressions are present.

## Transformer parity table
Legend:
- `Done`: implemented with generated runtime behavior.
- `Partial`: implemented for subset; still missing parity edges.
- `Missing`: no equivalent implementation yet.

| Avalonia XamlIl transformer/group | SourceGen status | Notes |
|---|---|---|
| `XNameTransformer` | Partial | `x:Name` fields + optional `NameScope` registration emitted; nested template scope parity incomplete. |
| `IgnoredDirectivesTransformer` | Done | `mc:Ignorable` namespaces are filtered in parser. |
| `AvaloniaXamlIlDesignPropertiesTransformer` | Done | Design namespace attributes ignored. |
| `AvaloniaBindingExtensionTransformer` | Partial | Binding/CompiledBinding parsed and emitted; converter/source/name-scope propagation parity not complete. |
| `AvaloniaXamlIlResolveClassesPropertiesTransformer` | Partial | Basic CLR property resolution done; advanced classes/property resolution/reorder behavior missing. |
| `AvaloniaXamlIlTransformInstanceAttachedProperties` | Partial | Attached property set works; advanced instance-attached semantics incomplete. |
| `AvaloniaXamlIlTransformSyntheticCompiledBindingMembers` | Missing | No synthetic member expansion pass yet. |
| `AvaloniaXamlIlAvaloniaPropertyResolver` | Partial | Avalonia property field lookup implemented; full intrinsic/type-converter parity still missing. |
| `AvaloniaXamlIlReorderClassesPropertiesTransformer` | Missing | No explicit ordering pass equivalent. |
| `AvaloniaXamlIlClassesTransformer` | Partial | `x:Class`, class modifier, root typing done; full class metadata handling incomplete. |
| `AvaloniaXamlIlControlThemeTransformer` | Partial | ControlTheme metadata and setter diagnostics exist; full runtime materialization parity incomplete. |
| `AvaloniaXamlIlSelectorTransformer` | Partial | selector token extraction/validation exists; full selector AST parity missing. |
| `AvaloniaXamlIlQueryTransformer` | Missing | `#name`, `$parent`, query nodes not transformed to runtime equivalents. |
| `AvaloniaXamlIlDuplicateSettersChecker` | Done | duplicate setter diagnostics for style/control-theme implemented. |
| `AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer` | Partial | target-type validation metadata implemented; template runtime shaping incomplete. |
| `AvaloniaXamlIlBindingPathParser` | Partial | simple paths + indexers implemented; advanced grammar (casts/methods/plugins) missing. |
| `AvaloniaXamlIlSetterTargetTypeMetadataTransformer` | Partial | setter target resolution partly implemented through style context; full metadata parity missing. |
| `AvaloniaXamlIlSetterTransformer` | Partial | setter object graph now attaches via `Add`; full setter value transform semantics missing. |
| `AvaloniaXamlIlStyleValidatorTransformer` | Partial | key diagnostics available; full validator parity missing. |
| `AvaloniaXamlIlConstructorServiceProviderTransformer` | Missing | No service-provider aware constructor pipeline. |
| `AvaloniaXamlIlTransitionsTypeMetadataTransformer` | Missing | no equivalent pass yet. |
| `AvaloniaXamlIlResolveByNameMarkupExtensionReplacer` | Missing | resolve-by-name replacement path not implemented. |
| `AvaloniaXamlIlThemeVariantProviderTransformer` | Missing | no theme-variant provider transform equivalent. |
| `AvaloniaXamlIlDataTemplateWarningsTransformer` | Partial | data-template `x:DataType` warning exists. |
| `AvaloniaXamlIlOptionMarkupExtensionTransformer` | Missing | no explicit option markup transform pass. |
| `XDataTypeTransformer` | Partial | scoped `x:DataType` consumed for compiled bindings; full propagation semantics incomplete. |
| `AddNameScopeRegistration` | Partial | root namescope registration path added; template-specific namescope parity missing. |
| `AvaloniaXamlIlControlTemplatePartsChecker` | Missing | no checker pass yet. |
| `AvaloniaXamlIlDataContextTypeTransformer` | Missing | no equivalent pass yet. |
| `AvaloniaXamlIlBindingPathTransformer` | Missing | compiled-binding plugin chain not implemented. |
| `AvaloniaXamlIlCompiledBindingsMetadataRemover` | Missing | no cleanup pass required yet; parity not modeled. |
| `AvaloniaXamlResourceTransformer` | Partial | keyed dictionary adds and resource metadata registry exist; full resource transformation parity missing. |
| `AvaloniaXamlIlTransformRoutedEvent` | Partial | CLR event handler hookup works; full routed-event semantics and metadata parity incomplete. |
| `AvaloniaXamlIlControlTemplatePriorityTransformer` | Missing | no priority transformation pass. |
| `AvaloniaXamlIlMetadataRemover` | Missing | no explicit final metadata cleanup pass. |
| `AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer` | Missing | no capacity preallocation pass. |
| `AvaloniaXamlIlRootObjectScope` | Missing | no dedicated root-scope runtime object model equivalent. |
| `AvaloniaXamlIlAddSourceInfoTransformer` | Partial | source-info registry exists; full runtime source-info graph parity missing. |
| `XamlMergeResourceGroupTransformer` (group) | Partial | include metadata captured; merge materialization parity missing. |
| `AvaloniaXamlIncludeTransformer` (group) | Partial | include discovery/validation implemented; runtime include object graph still partial. |

## Build/runtime integration findings
1. Backend switching seam is in place (`AvaloniaXamlCompilerBackend=SourceGen`) and disables `EnableAvaloniaXamlCompilation` for sourcegen builds.
2. AdditionalFiles injection for `@(AvaloniaXaml)` is working.
3. Generated `InitializeComponent` wiring and runtime registry bootstrap are functioning.
4. Critical gap remains: full style/template/resource/include materialization still trails XamlIl.

## High-priority blockers to full parity
1. No explicit pass framework mirroring XamlIl ordering and pass contracts.
2. Selector and query language transforms are incomplete.
3. Full compiled-binding path/plugin parity is incomplete.
4. Service-provider-sensitive transforms (static resource, constructor provider, deferred content) are only partial.
5. ControlTemplate/Theme runtime semantics and priority behaviors are incomplete.
6. Group-transform include/merge materialization is incomplete.

## Recommended short-term parity target
1. Close style/setter/control-theme runtime parity first.
2. Implement query + resolve-by-name transforms.
3. Implement pass-engine scaffolding to lock deterministic ordering and diagnostics equivalence.
4. Expand compiled-binding grammar and plugin parity.
