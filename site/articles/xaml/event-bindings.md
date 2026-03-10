---
title: "Event Bindings"
---

# Event Bindings

AXSG supports event bindings as compile-time features instead of treating event hookup as a purely runtime, string-based concern.

## Supported forms

### Handler-name binding

Use a CLR method name when you want a stable named handler on the root or typed source context:

```xaml
<Button Click="OnSaveClicked" />
<Button Click="{Binding SaveClicked}" />
```

This mode is useful when the handler already exists as a method and should participate in normal symbol navigation.

### Inline lambda form

Use an inline lambda when the behavior is short and local to the XAML site:

```xaml
<Button Content="{$'{ClickCount} clicks'}"
        Click="{(sender, e) => ClickCount++}" />
```

### Inline statement-block form

Use `CSharp` when the event logic is multi-line or needs local sequencing:

```xaml
<Button.Click>
  <CSharp><![CDATA[
source.ClickCount++;
source.LastAction = $"Clicked by {sender}";
  ]]></CSharp>
</Button.Click>
```

## Semantic rules

- Event handlers are resolved against the event delegate type.
- Inline lambdas are compiled against the real event signature.
- AXSG rejects async lambdas in the current event-binding surface.
- Generated handler methods use stable identities so hot reload and Edit-and-Continue remain deterministic.

## Tooling support

The language service understands event bindings for:

- completion of handler names and lambda bodies
- hover over event parameters and referenced members
- go to definition and find references for event methods, properties, and context variables
- semantic highlighting and inline C# projections in the VS Code extension

## Related docs

- [Inline C#](inline-csharp)
- [C# Expressions](csharp-expressions)
- [Navigation and Refactorings](../guides/navigation-and-refactorings)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback)
