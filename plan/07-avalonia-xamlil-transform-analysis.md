# Avalonia XamlIl Transform Analysis (C# SourceGen Backend)

## 1. Scope
This document analyzes Avalonia's current XamlIl pipeline and maps it to the current `XamlToCSharpGenerator` implementation.

Primary upstream references:
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/*.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/GroupTransformers/*.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs`
- `/Users/wieslawsoltes/GitHub/Avalonia/packages/Avalonia/AvaloniaBuildTasks.targets`

## 2. Avalonia XamlIl Pipeline (Ordered)
From `AvaloniaXamlIlCompiler`:

1. `XNameTransformer`
2. `IgnoredDirectivesTransformer`
3. `AvaloniaXamlIlDesignPropertiesTransformer`
4. `AvaloniaBindingExtensionTransformer`
5. `AvaloniaXamlIlResolveClassesPropertiesTransformer`
6. `AvaloniaXamlIlTransformInstanceAttachedProperties`
7. `AvaloniaXamlIlTransformSyntheticCompiledBindingMembers`
8. `AvaloniaXamlIlAvaloniaPropertyResolver`
9. `AvaloniaXamlIlReorderClassesPropertiesTransformer`
10. `AvaloniaXamlIlClassesTransformer`
11. `AvaloniaXamlIlControlThemeTransformer`
12. `AvaloniaXamlIlSelectorTransformer`
13. `AvaloniaXamlIlQueryTransformer`
14. `AvaloniaXamlIlDuplicateSettersChecker`
15. `AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer`
16. `AvaloniaXamlIlBindingPathParser`
17. `AvaloniaXamlIlSetterTargetTypeMetadataTransformer`
18. `AvaloniaXamlIlSetterTransformer`
19. `AvaloniaXamlIlStyleValidatorTransformer`
20. `AvaloniaXamlIlConstructorServiceProviderTransformer`
21. `AvaloniaXamlIlTransitionsTypeMetadataTransformer`
22. `AvaloniaXamlIlResolveByNameMarkupExtensionReplacer`
23. `AvaloniaXamlIlThemeVariantProviderTransformer`
24. `AvaloniaXamlIlDataTemplateWarningsTransformer`
25. `AvaloniaXamlIlOptionMarkupExtensionTransformer`
26. `XDataTypeTransformer`
27. `AddNameScopeRegistration`
28. `AvaloniaXamlIlControlTemplatePartsChecker`
29. `AvaloniaXamlIlDataContextTypeTransformer`
30. `AvaloniaXamlIlBindingPathTransformer`
31. `AvaloniaXamlIlCompiledBindingsMetadataRemover`
32. `AvaloniaXamlResourceTransformer`
33. `AvaloniaXamlIlTransformRoutedEvent`
34. `AvaloniaXamlIlControlTemplatePriorityTransformer`
35. `AvaloniaXamlIlMetadataRemover`
36. `AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer`
37. `AvaloniaXamlIlRootObjectScope`
38. `AvaloniaXamlIlAddSourceInfoTransformer`

Emitters:
- `AvaloniaNameScopeRegistrationXamlIlNodeEmitter`
- `AvaloniaXamlIlRootObjectScope.Emitter`

Group transformers:
- `XamlMergeResourceGroupTransformer`
- `AvaloniaXamlIncludeTransformer`

## 3. Current SourceGen Capability vs XamlIl
Status legend:
- `Implemented`: working C# emission with runtime behavior.
- `Partial`: diagnostics/metadata present, behavior incomplete.
- `Missing`: not implemented.

| XamlIl Area | SourceGen Status | Notes |
|---|---|---|
| x:Class discovery and generated partial | Implemented | `InitializeComponent`, populate/build, module registration generated. |
| `x:Name` field generation | Partial | Fields assigned; full namescope registration semantics are not equivalent to XamlIl. |
| Ignorable/design namespaces | Implemented | Parser ignores `mc:Ignorable` namespaces. |
| Object graph creation (children/content/items) | Implemented | Includes inherited `Add` support for collections. |
| Generic property-element object assignment | Implemented | CLR set and collection `.Add(...)` emission added. |
| Dictionary child add with `x:Key` | Partial | Emits `Add(key, value)` for keyed dictionary nodes; no full resource transform parity yet. |
| Literal conversion intrinsics | Partial | Primitive/enum/Uri only; no full intrinsic/type-converter matrix. |
| Attached Avalonia property resolution | Implemented | Generates `SetValue(Owner.Property, value)` where possible. |
| Routed event hookup | Partial | CLR event handler wiring implemented; not full routed-event transform parity. |
| Compiled binding metadata emission | Partial | Accessor registry emitted; full binding-path feature parity missing. |
| Runtime binding object emission | Partial | Emits `Binding` for Avalonia-property bindings; not full compiled-binding runtime behavior. |
| DataTemplate object graph use | Partial | Emits `FuncDataTemplate` for inline template content; broader template semantics missing. |
| Style selector semantics | Partial | Selector validation metadata exists; no full selector AST/runtime application parity. |
| Setter transforms/metadata | Partial | Setter diagnostics/metadata present; full transformed runtime behavior missing. |
| ControlTheme semantics | Partial | Metadata generated; full application/priority behavior missing. |
| Resource include/merge group transforms | Partial | Includes/merge captured in registries, not fully materialized as transformed object graph. |
| Deferred content / `TemplateContent` shaping | Missing | No equivalent of XamlIl deferred-content transform model. |
| Constructor service-provider semantics | Missing | No service-provider-sensitive construction pipeline. |
| Query transformer / `$parent`, `#name` replacement parity | Missing | No equivalent transform stack. |
| Root-object scope emitter parity | Missing | No equivalent runtime scope object/emitter. |
| Source info parity | Partial | Registry metadata emitted; no full `XamlSourceInfo` parity in runtime graph. |
| Metadata remover / final AST cleanup passes | Missing | No equivalent pass layer yet. |

## 4. Build/Runtime Integration Findings

### 4.1 Build seams
- Avalonia default: `CompileAvaloniaXamlTask` in `/packages/Avalonia/AvaloniaBuildTasks.targets`.
- SourceGen backend already bypasses this by setting:
  - `EnableAvaloniaXamlCompilation=false`
  - `AvaloniaXamlCompilerBackend=SourceGen`
  - AdditionalFiles injection from `@(AvaloniaXaml)`.

### 4.2 Runtime seams
- Current runtime uses generated module-initializer registration + `XamlSourceGenRegistry`.
- Missing parity with `CompiledAvaloniaXaml.!XamlLoader` service-provider-rich dispatch and transformed deferred content.

## 5. Major Parity Gaps Blocking "Full XamlX Feature" Equivalence
1. No full transform pipeline equivalent (ordered, composable passes mirroring Avalonia XamlIl).
2. No full XAML language intrinsic/type-converter layer.
3. No deferred-content/template-content transform model.
4. No full selector/query parser and setter target metadata transform parity.
5. No full compiled-binding path parser/transformer parity.
6. No full group-transform materialization for includes/merged dictionaries.
7. No root scope/name scope runtime-equivalent emitter model.

## 6. Implemented in this iteration
1. Property-element object assignment and collection-add emission.
2. `x:Key` capture and keyed dictionary child add emission path.
3. Runtime binding assignment fallback generation for Avalonia-property binding markup.
4. DataTemplate inline content handling via generated `FuncDataTemplate` path.
5. Expanded default Avalonia type resolution candidates beyond `Avalonia.Controls.*`.

