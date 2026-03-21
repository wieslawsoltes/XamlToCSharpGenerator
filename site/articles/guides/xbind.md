---
title: "x:Bind"
---

# x:Bind in XAML

## Purpose

AXSG supports WinUI-style `x:Bind` for Avalonia through source generation, typed semantic
binding, generated runtime helpers, and language-service support.

Use `x:Bind` when you want:

- compile-time validation of members, methods, and source scopes
- direct access to root members and typed template items
- source-aware binding to named elements, ancestors, self, templated parent, or explicit
  source expressions
- stronger editor tooling than plain runtime path bindings
- generated `TwoWay`/`BindBack` behavior without reflection

This is an Avalonia-adapted `x:Bind` model. It follows the WinUI/UWP/Uno concept closely,
but the runtime integration is implemented on top of Avalonia bindings and generated
helpers.

## Requirements

- The root must be `x:Class`-backed.
- Template scopes should declare `x:DataType`.
- The compiler must be able to resolve the source type, either from scope or from explicit
  `DataType=`.

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

### Root members and methods

```xaml
<TextBlock Text="{x:Bind Title}" />
<TextBlock Text="{x:Bind FormatTitle(Title)}" />
<TextBlock Text="{x:Bind BuildSummary(FirstName, LastName, Count)}" />
```

### Template-item binding

```xaml
<ItemsControl ItemsSource="{x:Bind Items}">
  <ItemsControl.ItemTemplate>
    <DataTemplate x:DataType="vm:ItemViewModel">
      <TextBlock Text="{x:Bind Name}" />
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

### Named elements

```xaml
<TextBox x:Name="Editor" />
<TextBlock Text="{x:Bind Text, ElementName=Editor}" />
```

### Relative sources

```xaml
<TextBlock Tag="{x:Bind Tag, RelativeSource={RelativeSource Self}}" />
<TextBlock Text="{x:Bind Title, RelativeSource={RelativeSource TemplatedParent}, DataType=local:ParentView}" />
<TextBlock Text="{x:Bind DataContext.Title, RelativeSource={RelativeSource FindAncestor, AncestorType=local:ShellView}}" />
```

### Explicit `Source=`

```xaml
<TextBlock Text="{x:Bind Value, Source={x:Static local:BindingSources.Current}, DataType=local:StaticSource}" />
<TextBlock Text="{x:Bind Text, Source={x:Reference Editor}}" />
```

### Static members, indexers, pathless bind

```xaml
<TextBlock Text="{x:Bind helpers:UiHelpers.Prefix}" />
<TextBlock Text="{x:Bind Items[SelectedIndex].Name}" />
<ContentPresenter Content="{x:Bind}" />
```

### Event handlers

```xaml
<Button Click="{x:Bind HandlePrimaryClick}" />
<Button Click="{x:Bind CaptureEditorText(Editor.Text)}" />
<Button Click="{x:Bind Perform(), ElementName=ActionButton}" />
```

## Source-resolution model

`x:Bind` source selection is expression-based and compiler-resolved.

Resolution order is:

1. explicit `ElementName`, `RelativeSource`, or `Source`
2. typed template source when inside a typed template
3. root object when outside a typed template

Supported source kinds in the current implementation:

- root object
- typed template/data-context source
- named element
- target object (`RelativeSource Self`)
- templated parent (`RelativeSource TemplatedParent`)
- ancestor lookup (`RelativeSource FindAncestor`)
- explicit source expression (`Source=...`)

`DataType=` can override the semantic source type when the runtime source is known but the
compiler cannot infer a useful CLR type from the source expression alone.

## TwoWay and bind-back semantics

Simple `TwoWay`:

```xaml
<TextBox Text="{x:Bind Alias, Mode=TwoWay}" />
```

Explicit bind-back method:

```xaml
<TextBox Text="{x:Bind SearchDraft, Mode=TwoWay, BindBack=ApplySearchDraft}" />
```

Key behavior:

- the forward path is generated from the evaluated `x:Bind` expression
- the reverse path is generated separately through the bind-back contract
- this is not just a plain Avalonia runtime setter shortcut

## Delay and update source trigger

AXSG supports Avalonia-oriented `Delay` and `UpdateSourceTrigger` options on `x:Bind`.

Example:

```xaml
<TextBox Text="{x:Bind SearchDraft,
                        Mode=TwoWay,
                        BindBack=ApplySearchDraft,
                        Delay=250,
                        UpdateSourceTrigger=Explicit}" />
```

`UpdateSourceTrigger=Explicit` stores pending bind-back values until you flush them:

```csharp
Bindings.Update();
```

or:

```csharp
SourceGenMarkupExtensionRuntime.UpdateXBind(this);
```

## Generated lifecycle surface

For class-backed roots that use `x:Bind`, AXSG emits:

```csharp
Bindings.Initialize();
Bindings.Update();
Bindings.StopTracking();
```

Intended usage:

- `Initialize()` reattaches generated x:Bind tracking
- `Update()` flushes pending explicit values and refreshes active x:Bind bindings
- `StopTracking()` detaches generated x:Bind subscriptions

## Converters and formatting

Supported conversion and formatting options:

- `Converter`
- `ConverterCulture`
- `ConverterLanguage`
- `ConverterParameter`
- `StringFormat`
- `FallbackValue`
- `TargetNullValue`

These are part of generated runtime descriptors and are applied before final target
assignment/coercion.

## Diagnostics

Representative x:Bind diagnostics include:

- missing `x:Class` root for generated `x:Bind`
- unresolved named elements or source expressions
- unsupported mode values
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

The language service understands:

- root vs template source scope
- `ElementName`, `RelativeSource`, and `Source`
- inline `DataType=`
- converter and formatting options

## Runtime and hot reload

`x:Bind` is compiled into generated evaluators and reflection-free runtime descriptors.

Hot reload uses the generated object-graph path and resets generated x:Bind state when the
root graph is repopulated.

## How x:Bind compares to Avalonia bindings

Compared with normal Avalonia `{Binding}` / `{CompiledBinding}`:

- `x:Bind` is expression-oriented, not just path-oriented
- `x:Bind` can call methods and use static members directly
- default source semantics are root/template-item oriented instead of ordinary
  data-context-only lookup
- `TwoWay` reverse flow uses generated bind-back plumbing

Compared with inline C#:

- `x:Bind` is the better fit for binding-like expressions and event call expressions
- inline C# is better when you need multi-statement logic or explicit code blocks

## Current intentional gaps

Not currently part of the validated surface:

- WinUI `x:Load` integration
- Uno-style POCO-target `x:Bind` outside Avalonia binding application

## Related docs

- [x:Bind feature overview](../xaml/xbind/)
- [Compiled Bindings](../xaml/compiled-bindings/)
- [Inline C# Code](inline-csharp-code/)
- [Event Bindings](../xaml/event-bindings/)
- implementation/spec reference: [`plan/127-xbind-implementation-spec-and-comparison-2026-03-21.md`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/plan/127-xbind-implementation-spec-and-comparison-2026-03-21.md)
