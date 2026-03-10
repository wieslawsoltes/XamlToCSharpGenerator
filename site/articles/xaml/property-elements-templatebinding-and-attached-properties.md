---
title: "Property Elements, TemplateBinding, and Attached Properties"
---

# Property Elements, TemplateBinding, and Attached Properties

AXSG treats property-element syntax as a first-class surface in both the compiler and the language service.

## Owner-qualified property elements

Property elements such as `<Window.IsVisible>` or `<Grid.RowDefinitions>` are resolved as two linked semantic targets:

- the owner type (`Window`, `Grid`)
- the property (`IsVisible`, `RowDefinitions`)

That matters for:

- completion after `.`
- hover and definition on either side of the qualified name
- references and rename for the owner type or the property

## Attached properties

Attached property usage participates in the same owner-qualified resolution model.

```xaml
<Border Grid.Row="1" />
<Grid.RowDefinitions>
  <RowDefinition Height="Auto" />
</Grid.RowDefinitions>
```

Both attribute and property-element forms map back to the same underlying symbol. This is why references and rename can flow between the attached-property attribute form and the owner-qualified property-element form.

## `TemplateBinding`

`TemplateBinding` is treated as a property reference against the templated control type rather than as opaque text.

```xaml
<Border BorderBrush="{TemplateBinding BorderBrush}" />
```

Navigation and references on `BorderBrush` resolve to the actual Avalonia property declaration, including inside control themes and templates.

## Why this matters for tooling

These surfaces are easy to get wrong if the editor only tokenizes XML text. AXSG resolves them semantically, so it can distinguish:

- owner token vs property token
- attached property usage vs ordinary attribute text
- template-bound property reference vs plain string content

## Common workflows

- use completion after `<Window.` or `<Grid.` to insert the correct property element name without duplicating the owner token
- use references on an attached property to gather both attribute and property-element usages
- use definition on `TemplateBinding` property names to inspect the real Avalonia property being referenced

## Related docs

- [Styles, Templates, and Themes](styles-templates-and-themes/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
