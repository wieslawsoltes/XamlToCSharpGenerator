# Parity Matrix: Avalonia XamlIl vs SourceGen C# Backend

## 1. Status Legend
- `Implemented`: end-to-end behavior implemented and validated in generated C#.
- `Partial`: implemented only for subset of scenarios or metadata-only.
- `Missing`: no equivalent behavior yet.

Detailed transform analysis: `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/07-avalonia-xamlil-transform-analysis.md`.

## 2. Current Matrix

| Capability | Status | Notes |
|---|---|---|
| Backend switch and build-task bypass | Implemented | `AvaloniaXamlCompilerBackend=SourceGen` disables Avalonia IL compilation path. |
| Watch/design-time AXAML source-surface dedupe | Implemented | SourceGen-mode early disable of Avalonia compile path + deterministic AXAML projection eliminates duplicate source-file warnings (`dotnet watch --list` regression covered). |
| `InitializeComponent` + populate/build generation | Implemented | Generated partials and module initializer registration emitted. |
| Base object graph (Content/Children/Items/DirectAdd) | Implemented | Includes inherited collection `Add` and direct child `Add(...)` attachment mode. |
| Property-element object assignment | Implemented | CLR property set + collection add emission for object-valued property elements. |
| Keyed dictionary child add (`x:Key`) | Partial | `Add(key, value)` path implemented; full resource transform parity missing. |
| Attached Avalonia property assignment | Implemented | `SetValue(Owner.Property, value)` emission supported. |
| Routed + CLR event hookup | Implemented | CLR events now validate delegate compatibility; routed-event field (`FooEvent`) assignments emit `RemoveHandler`/`AddHandler` wiring with typed delegate casts and `AXSG0600` compatibility diagnostics. |
| Reflection binding runtime assignment | Partial | Binding markup now supports core options (`Mode`, `ElementName`, `RelativeSource`) for emitted runtime bindings. |
| Compiled binding path/accessor metadata | Partial | Accessors now support indexer segments; advanced grammar/plugins still missing. |
| DataTemplate inline content path | Partial | Inline template content emitted via `FuncDataTemplate`; broader template/deferred parity missing. |
| Markup extension value conversion | Partial | Added `x:Null`, `x:Type`, `x:Static`, `StaticResource`, `DynamicResource`, `TemplateBinding` conversions; static resource resolution now includes source-generated transitive include fallback, full provider semantics still pending. |
| Style selector + setter semantics | Partial | Setter property resolution improved with style target-type context; full selector transform/runtime equivalence still missing. |
| ControlTheme semantics | Partial | Added runtime materialization path (`XamlControlThemeRegistry.TryMaterialize`) with generated factory registration, `BasedOn` chain resolution, and theme-variant fallback; broader differential parity vs XamlIl fixtures is still pending. |
| Include/merge group transform behavior | Partial | Includes now have global cross-file diagnostics (`AXSG0403/AXSG0404`), runtime include-edge graph registration, deterministic registration ordering, and last-include precedence for static-resource fallback; full merge/resource materialization parity is still missing. |
| Deferred content / TemplateContent behavior | Partial | Deferred template factory emission exists for template families; full checker/runtime differential parity remains pending. |
| Query / resolve-by-name markup extension transforms | Partial | `x:Reference`/`ResolveByName` helper conversion and binding-source normalization are implemented; broader query-transform parity coverage remains. |
| Root object scope + namescope emitter parity | Partial | Root and deferred-template scopes now emit NameScope registration for named nodes; full differential parity for nested edge cases remains pending. |
| Source info parity (`XamlSourceInfo`) | Partial | Granular source-info emission now includes object/property/event/property-element and style/control-theme setter identities with deterministic registry query APIs; full baseline differential parity remains open. |

## 3. Blocking Items for Full Parity
1. Ordered transform-pass framework equivalent to Avalonia XamlIl.
2. Deferred content and template transformation parity.
3. Full selector/query/binding path grammar parity.
4. Resource/include group transform materialization parity.
5. Root scope/name scope/source info runtime-equivalent behavior.

## 4. Acceptance Rule
Each row can move to `Implemented` only when:
1. Positive scenario integration test passes.
2. Negative/diagnostic scenario test passes.
3. Output snapshot is deterministic.
4. Runtime behavior matches Avalonia reference fixture for that feature.

Evidence index:
1. `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/35-parity-evidence-dashboard.md`.
