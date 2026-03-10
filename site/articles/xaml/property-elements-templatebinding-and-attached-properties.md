---
title: "Property Elements, TemplateBinding, and Attached Properties"
---

# Property Elements, TemplateBinding, and Attached Properties

AXSG treats property-element syntax as a first-class surface in both the compiler and the language service.

## Owner-qualified property elements

Property elements such as `<Window.IsVisible>` or `<Grid.RowDefinitions>` are resolved as two linked symbols:

- the owner type (`Window`, `Grid`)
- the property (`IsVisible`, `RowDefinitions`)

This matters for:

- completion after `.`
- hover and definition on either side of the qualified name
- references and rename for the owner type or the property

## TemplateBinding

`TemplateBinding` is understood as a property reference against the templated control type.

```xaml
<Border BorderBrush="{TemplateBinding BorderBrush}" />
```

Navigation and references on `BorderBrush` resolve to the actual Avalonia property rather than treating the token as plain text.

## Attached properties

Attached property usage participates in the same owner-qualified resolution model.

```xaml
<Border Grid.Row="1" />
<Grid.RowDefinitions>
  <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```

Both attribute and property-element forms map back to the same underlying symbol.

## Related docs

- [Styles, Templates, and Themes](styles-templates-and-themes)
- [Navigation and Refactorings](../guides/navigation-and-refactorings)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload)
