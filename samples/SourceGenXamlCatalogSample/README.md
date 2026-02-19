# SourceGen XAML Catalog Sample

`SourceGenXamlCatalogSample` is a tabbed Avalonia app that demonstrates supported SourceGen XAML features.

## Tabs

- `Basics`: object graph creation, property assignment, attached properties, content/children, DataTemplate + ItemsPanelTemplate.
- `Bindings`: compiled bindings with nested paths and indexer segments.
- `Relative Source`: `$self` and `$parent[...]` query paths.
- `Markup Extensions`: `x:Static`, custom `MarkupExtension`, `OnPlatform`, `OnFormFactor`, and extended `x:` primitives.
- `Styles + Selectors`: class selectors, combinators, and property predicate selectors.
- `Control Themes`: `ControlTheme`, `BasedOn`, and theme assignment on framework/custom controls.
- `Templates`: `DataTemplate`, inline deferred templates, and `ControlTemplate` + `TemplateBinding`.
- `Resources + Dictionaries`: merged dictionary precedence, `StaticResource` and `DynamicResource`.
- `Construction Grammar`: `x:Arguments`, `x:FactoryMethod`, `x:TypeArguments`, and `x:Array`.

## Run

```bash
dotnet run --project samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj
```
