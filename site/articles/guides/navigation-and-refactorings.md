---
title: "Navigation and Refactorings"
---

# Navigation and Refactorings

AXSG extends navigation beyond plain element names.

## Supported definition/reference surfaces

Definitions, declarations, references, and rename work across:

- element types and qualified prefixes
- binding paths and compiled bindings
- shorthand expressions and inline C# code
- event handlers and event lambdas
- style classes, pseudoclasses, and selector `#name` tokens
- `TemplateBinding` properties
- resource keys and include URIs
- owner-qualified property elements such as `<Window.IsVisible>`

## Cross-language behavior

AXSG can propagate navigation and rename flows between C# and XAML when the symbol has XAML usages.

Examples:

- renaming a view-model property updates binding usages in XAML
- finding references on a selector `#ThemeToggle` includes the `x:Name` declaration
- definition on `pages:` navigates to the `xmlns:pages` declaration while definition on `ContentControlRowDetailsSwapPage` navigates to the CLR type

## Inline C#

Inline C# in attribute form, object-element form, and CDATA form participates in:

- definition/declaration
- references
- completion
- hover
- semantic highlighting

## Related docs

- [VS Code and Language Service](vscode-language-service.md)
- [Inline C# Code](inline-csharp-code.md)
- [C# Expressions](csharp-expressions.md)
