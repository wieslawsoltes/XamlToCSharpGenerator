---
title: "x:Bind"
---

# x:Bind

AXSG supports WinUI-style `x:Bind` for Avalonia as a source-generated, typed binding
surface. It is not treated as a string-path alias for normal Avalonia bindings; the
compiler parses the expression, resolves the source statically, validates members and
methods at build time, and emits generated runtime helpers.

## What x:Bind gives you

- direct binding to root members in `x:Class`-backed views
- typed template-item binding with `x:DataType`
- method calls, indexers, static members, and pathless binds
- `TwoWay` and explicit `BindBack`
- `ElementName`, `RelativeSource`, and `Source` options
- generated lifecycle helpers through `Bindings.Initialize()`, `Bindings.Update()`, and
  `Bindings.StopTracking()`

## Requirements

- `x:Bind` requires an `x:Class` root.
- Typed template scopes should use `x:DataType`.
- Source selection is resolved by the compiler, not guessed dynamically at runtime.

## Supported surface

Examples:

```xaml
<TextBlock Text="{x:Bind Title}" />
<TextBlock Text="{x:Bind FormatTitle(Title)}" />
<TextBlock Text="{x:Bind Text, ElementName=Editor}" />
<TextBlock Text="{x:Bind Value, Source={x:Static local:BindingSources.Current}, DataType=local:StaticSource}" />
<TextBox Text="{x:Bind Alias, Mode=TwoWay}" />
<Button Click="{x:Bind HandlePrimaryClick}" />
```

Supported options:

- `Mode`
- `BindBack`
- `ElementName`
- `RelativeSource`
- `Source`
- `DataType`
- `Converter`
- `ConverterCulture`
- `ConverterLanguage`
- `ConverterParameter`
- `StringFormat`
- `FallbackValue`
- `TargetNullValue`
- `Delay`
- `Priority`
- `UpdateSourceTrigger`

## Semantics

The compiler resolves `x:Bind` source context from:

1. explicit source options
2. typed template context
3. class-backed root context

This makes `x:Bind` closer to WinUI/UWP/Uno semantics than ordinary Avalonia
`{Binding}`.

## Editor and runtime support

`x:Bind` participates in completion, hover, definition, references, rename, and signature
help. Runtime materialization is generated and reflection-free.

`UpdateSourceTrigger=Explicit` is supported through generated lifecycle update calls.

## Current intentional differences

- integrated with Avalonia `MultiBinding` / `InstancedBinding` rather than WinUI runtime
  binding managers
- no WinUI `x:Load` integration
- POCO-target `x:Bind` outside Avalonia binding application is not a validated surface

## Where to look next

- [Guides: x:Bind](../guides/xbind/)
- [Compiled Bindings](compiled-bindings/)
- [Binding and Expression Model](../concepts/binding-and-expression-model/)
- full implementation/spec document: [`plan/127-xbind-implementation-spec-and-comparison-2026-03-21.md`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/plan/127-xbind-implementation-spec-and-comparison-2026-03-21.md)
