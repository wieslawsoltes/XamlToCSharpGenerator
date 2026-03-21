# x:Bind in XAML

## Purpose

AXSG supports WinUI-style `x:Bind` for Avalonia through source generation, typed semantic
binding, generated runtime helpers, and language-service support.

This surface is intended for cases where you want:

- compile-time member validation
- direct access to root members, methods, static members, and typed template items
- stronger editor tooling than plain string-path bindings
- `TwoWay`/`BindBack` behavior without falling back to reflection

This is an Avalonia-adapted `x:Bind` implementation. It follows the WinUI/UWP/Uno mental
model closely, but it integrates with Avalonia's binding engine and target model.

## Requirements

- The root must be `x:Class`-backed.
- The binding scope must have a known type.
- Template scopes should declare `x:DataType`.
- Generated lifecycle helpers are only emitted for class-backed roots that actually use
  `x:Bind`.

## Full syntax

```xaml
{x:Bind path,
         Mode=OneWay|TwoWay|OneTime,
         BindBack=MethodOrSetterCompatibleExpression,
         ElementName=SomeControl,
         RelativeSource={RelativeSource Self|TemplatedParent|FindAncestor,...},
         Source={x:Static ...}|{x:Reference ...}|SomeExpression,
         DataType=local:SomeType,
         Converter={StaticResource SomeConverter},
         ConverterCulture='en-US',
         ConverterLanguage='en-US',
         ConverterParameter=SomeValue,
         StringFormat='Value: {0}',
         FallbackValue='...',
         TargetNullValue='...',
         Delay=250,
         Priority=LocalValue,
         UpdateSourceTrigger=PropertyChanged|LostFocus|Explicit}
```

`ConverterLanguage` is accepted as an alias of `ConverterCulture`.

## Supported forms

### Root members

```xaml
<TextBlock Text="{x:Bind Title}" />
<TextBlock Text="{x:Bind FormatTitle(Title)}" />
```

### Template-item members

```xaml
<DataTemplate x:DataType="vm:ItemViewModel">
  <TextBlock Text="{x:Bind Name}" />
</DataTemplate>
```

### Named elements

```xaml
<TextBox x:Name="Editor" />
<TextBlock Text="{x:Bind Text, ElementName=Editor}" />
```

### Static members and explicit sources

```xaml
<TextBlock Text="{x:Bind Value, Source={x:Static local:BindingSources.Current}, DataType=local:StaticSource}" />
<TextBlock Text="{x:Bind helpers:UiHelpers.Prefix}" />
```

### Pathless bind

```xaml
<ContentPresenter Content="{x:Bind}" />
```

Pathless `x:Bind` means "bind to the current source object".

### Method calls, indexers, and null-aware expressions

```xaml
<TextBlock Text="{x:Bind FormatItem(Items[SelectedIndex])}" />
<TextBlock Text="{x:Bind SelectedItem?.DisplayName}" />
```

### Event handlers

```xaml
<Button Click="{x:Bind HandlePrimaryClick}" />
<Button Click="{x:Bind CaptureEditorText(Editor.Text)}" />
<Button Click="{x:Bind Perform(), ElementName=ActionButton}" />
```

## Source-resolution model

The source is not chosen by Avalonia runtime binding-path rules. AXSG resolves it up front.

Resolution order is:

1. explicit `ElementName=`, `RelativeSource=`, or `Source=`
2. typed template source when inside a typed template
3. root object when outside a template

Supported source modes:

- root object
- template/data-context source
- target object via `RelativeSource Self`
- templated parent via `RelativeSource TemplatedParent`
- ancestor lookup via `RelativeSource FindAncestor`
- named element via `ElementName=...` or `Source={x:Reference ...}`
- explicit source expression via `Source=...`

`DataType=` can override the source type used for semantic analysis when the runtime source
is known but its type cannot be inferred cleanly.

## TwoWay, BindBack, and explicit update

`x:Bind` supports `OneTime`, `OneWay`, and `TwoWay`.

Simple `TwoWay`:

```xaml
<TextBox Text="{x:Bind Alias, Mode=TwoWay}" />
```

Explicit bind-back target:

```xaml
<TextBox Text="{x:Bind SearchDraft, Mode=TwoWay, BindBack=ApplySearchDraft}" />
```

`Delay` and `UpdateSourceTrigger` are Avalonia-oriented extensions on top of the WinUI-like
surface. `UpdateSourceTrigger=Explicit` stores pending values until you flush them:

```csharp
Bindings.Update();
```

or:

```csharp
SourceGenMarkupExtensionRuntime.UpdateXBind(this);
```

## Generated lifecycle surface

When a class-backed root uses `x:Bind`, AXSG emits:

```csharp
Bindings.Initialize();
Bindings.Update();
Bindings.StopTracking();
```

Intended usage:

- `Initialize()` reattaches generated x:Bind tracking
- `Update()` flushes pending explicit bind-back values and refreshes active x:Bind bindings
- `StopTracking()` detaches generated x:Bind subscriptions

## Converters and formatting

`x:Bind` supports:

- `Converter`
- `ConverterCulture`
- `ConverterLanguage`
- `ConverterParameter`
- `StringFormat`
- `FallbackValue`
- `TargetNullValue`

These are emitted into the generated Avalonia runtime binding descriptors, not handled by
reflection.

## Diagnostics

Representative x:Bind diagnostics include:

- missing `x:Class` root when `x:Bind` requires class-backed generation
- unresolved source configuration
- unsupported `Mode`
- invalid event expression for the target delegate
- unresolved members, methods, or type tokens

## Language-service support

`x:Bind` participates in:

- completion
- hover
- go to definition
- find references
- rename propagation
- signature help

The editor understands `ElementName`, `RelativeSource`, `Source`, `DataType`, converter
options, and the active source scope.

## Runtime and hot reload

`x:Bind` is compiled into generated evaluators and runtime descriptors. It does not fall back
to reflection binding.

Hot reload uses the generated object-graph path and resets/rebuilds generated x:Bind state
when the root graph is repopulated.

## Differences from Avalonia `{Binding}` and `{CompiledBinding}`

- `x:Bind` is expression-oriented, not just path-oriented.
- `x:Bind` can call methods and use static members directly.
- `x:Bind` default source is the root or typed template item, not ordinary Avalonia
  `DataContext` lookup everywhere.
- `TwoWay` reverse flow uses generated bind-back plumbing, not only standard Avalonia setter
  semantics.

## Current intentional gaps

Not currently part of the validated surface:

- WinUI `x:Load` integration
- Uno-style POCO-target `x:Bind` outside Avalonia binding application

## Related docs

- [`site/articles/xaml/xbind.md`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/site/articles/xaml/xbind.md)
- [`site/articles/guides/xbind.md`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/site/articles/guides/xbind.md)
- Detailed implementation/spec notes are also kept in the repository `plan/` folder for engineering work.
