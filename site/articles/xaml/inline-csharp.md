---
title: "Inline C#"
---

# Inline C#

AXSG supports inline C# inside valid XAML through:

- `{CSharp Code=...}`
- `<CSharp Code="..." />`
- `<CSharp><![CDATA[ ... ]]></CSharp>`

Inline code participates in language-service features and can use the current XAML binding/event context.

See:

- [Guides: Inline C# Code](../guides/inline-csharp-code)
- API: <xref:XamlToCSharpGenerator.Runtime.Markup.CSharp>
