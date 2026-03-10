---
title: "Styles, Templates, and Themes"
---

# Styles, Templates, and Themes

AXSG resolves selectors, `TemplateBinding`, theme resource usage, and control-theme include graphs at compile time where possible.

This part of the surface matters for:

- selector/property validation
- theme override diagnostics
- navigation and references in the editor
- hot reload for control themes and includes

Relevant APIs live mainly in:

- <xref:XamlToCSharpGenerator.Avalonia.Parsing>
- <xref:XamlToCSharpGenerator.Avalonia.Binding>
- <xref:XamlToCSharpGenerator.Runtime>
