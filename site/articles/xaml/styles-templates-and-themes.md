---
title: "Styles, Templates, and Themes"
---

# Styles, Templates, and Themes

AXSG understands style selectors, control themes, setters, templates, and theme resource relationships as compiler features rather than as opaque strings.

## Covered areas

- style selectors and selector mini-language parsing
- style classes and pseudoclasses
- named-element selectors
- control themes and `BasedOn` relationships
- `TemplateBinding`
- theme resource and include navigation

## Control themes

AXSG handles control themes as first-class semantic objects. That includes:

- `TargetType` resolution
- `BasedOn` chain analysis
- cycle detection that distinguishes real local cycles from valid override patterns
- property and resource navigation inside theme content

## Selectors

Selectors are parsed and semantically indexed so tooling can support:

- completion
- hover
- definition/declaration
- references
- rename

That includes mixed selector forms such as type + `#name` + pseudoclass combinations.

## Template binding and property elements

Property values such as `TemplateBinding BorderBrush` and property elements like `<Window.IsVisible>` are resolved semantically, not treated as plain text.

## Runtime and hot reload implications

Theme and style edits matter for hot reload because they affect visual state broadly. AXSG keeps runtime/theme metadata separate enough that style edits can be reapplied without rebuilding unrelated compiler semantics.

## Related docs

- [Property Elements, TemplateBinding, and Attached Properties](property-elements-templatebinding-and-attached-properties/)
- [Resources, Includes, and URIs](resources-includes-and-uris/)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload/)
- [Expression, Parsing, and Framework Namespaces](../reference/namespace-expression-and-framework/)
