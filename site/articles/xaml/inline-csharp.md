---
title: "Inline C#"
---

# Inline C#

AXSG supports inline C# in valid XAML forms for scenarios where an expression or event block is clearer than introducing dedicated code-behind handlers or trivial view-model glue.

## Supported forms

- `{CSharp Code=...}`
- `<CSharp Code="..." />`
- `<CSharp><![CDATA[ ... ]]></CSharp>`

These forms remain valid XAML and are understood by the compiler, runtime, and language service.

## Supported contexts

Inline C# can be used for:

- property values that need expression-level or statement-capable computation
- event handlers expressed as inline lambdas or statement blocks
- multiline CDATA blocks where readability matters

Use inline C# when it improves local XAML readability. Do not use it as a substitute for domain logic or reusable behavior that should live in a view model or service.

## Context available to inline code

Inline code is analyzed against the current XAML scope. Depending on location, that can include:

- the current data source
- the root object
- the current target element
- event delegate parameters such as sender/event args
- target property type information

The same source-context model is reused by the language service, so compiler semantics and editor semantics stay aligned.

## Forms and when to use them

### Compact attribute form

Use `{CSharp Code=...}` for short expressions or compact lambdas.

### Object-element `Code` form

Use `<CSharp Code=\"...\" />` when you want object-element readability without introducing a multiline code block.

### CDATA form

Use `<CSharp><![CDATA[ ... ]]></CSharp>` when:

- the logic spans multiple lines
- interpolation or quoting becomes awkward in an attribute
- an inline event handler is clearer as a statement block

## Editor experience

Inline code participates in:

- completion and hover
- definition, declaration, and references
- rename where the symbol model supports it
- semantic highlighting and inlay hints
- projected C# interop in VS Code for selected provider-backed features

This includes navigation and references for context-aware members such as `source`, `root`, and event parameters when the underlying scope can be resolved.

## Runtime and hot reload implications

Inline code is not a late string-eval escape hatch. AXSG lowers it into generated helpers or runtime-capable descriptors depending on the scenario. That is why helper identity, generated method shape, and context binding all matter for hot reload and Edit-and-Continue stability.

## When not to use inline C#

Prefer view-model or service code when:

- the logic is shared
- the logic is domain-facing rather than view-facing
- the snippet becomes large enough to obscure the surrounding XAML
- the behavior needs stronger unit-test boundaries than a view-local snippet provides

## Related docs

- [Guides: Inline C# Code](../guides/inline-csharp-code/)
- [Event Bindings](event-bindings/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- API: <xref:XamlToCSharpGenerator.Runtime.Markup.CSharp>
