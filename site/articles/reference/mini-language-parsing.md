---
title: "Package: XamlToCSharpGenerator.MiniLanguageParsing"
---

# XamlToCSharpGenerator.MiniLanguageParsing

## Role

Low-allocation parsers and tokenizers for selectors, binding paths, markup fragments, and helper text parsing.

## Install

```xml
<PackageReference Include="XamlToCSharpGenerator.MiniLanguageParsing" Version="0.1.0-alpha.3" />
```

## Related namespaces

- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Bindings>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors>
- <xref:XamlToCSharpGenerator.MiniLanguageParsing.Text>

## Use it when

- you need selector or binding parsing outside the compiler host
- you want standalone mini-language utilities in tests or other tools

## What lives here

This package contains the low-level tokenizers and semantic helpers behind:

- selector parsing
- binding path parsing
- markup fragment parsing
- small text-diff and character-reader helpers used by hot paths

Use it when you want these parsers without taking the full compiler host.

It exists separately so selector, binding-fragment, and markup parsing can be optimized and tested independently from the larger compiler host.

## Related docs

- [Binding and Expression Model](../concepts/binding-and-expression-model/)
- [Expression, Parsing, and Framework Namespaces](../namespace-expression-and-framework/)
- [Styles, Templates, and Themes](../../xaml/styles-templates-and-themes/)
