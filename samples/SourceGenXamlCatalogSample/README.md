# SourceGen XAML Catalog Sample

`SourceGenXamlCatalogSample` is a tabbed Avalonia app that demonstrates supported SourceGen XAML features.

## Tabs

- `Basics`: object graph creation, property assignment, attached properties, content/children, DataTemplate + ItemsPanelTemplate.
- `Bindings`: compiled bindings with nested paths and indexer segments.
- `Relative Source`: `$self` and `$parent[...]` query paths.
- `Markup Extensions`: `x:Static`, custom `MarkupExtension`, `OnPlatform`, `OnFormFactor`, and extended `x:` primitives.
- `Event Bindings`: first-class `{EventBinding ...}` command/method dispatch with `Parameter`, `PassEventArgs`, and `Source` mode examples.
- `Global XMLNS`: global prefix imports (`vm`, `catalog`) and implicit default namespace support. The page omits local `vm/catalog` declarations and resolves them from project-level SourceGen settings.
- `Conditional XAML`: conditional namespace aliases (`?ApiInformation.*`) that gate elements, properties, styles, and contract-specific branches.
- `Runtime Loader`: runtime URI and inline-string loading via the SourceGen parser/binder/emitter pipeline.
- `Styles + Selectors`: class selectors, combinators, and property predicate selectors.
- `Control Themes`: `ControlTheme`, `BasedOn`, and theme assignment on framework/custom controls.
- `Templates`: `DataTemplate`, inline deferred templates, and `ControlTemplate` + `TemplateBinding`.
- `Resources + Dictionaries`: merged dictionary precedence, `StaticResource` and `DynamicResource`.
- `Construction Grammar`: `x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`, and `x:Array`.

## Run

```bash
dotnet run --project samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj
```
