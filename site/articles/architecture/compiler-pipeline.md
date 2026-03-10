---
title: "Compiler Pipeline"
---

# Compiler Pipeline

Major stages:

1. XAML parsing and semantic feature enrichment
2. framework-specific binding and markup binding
3. generated view model / object graph model construction
4. C# emission and generated artifact registration
5. runtime coordination for reload and design-time services

Key assemblies:

- `XamlToCSharpGenerator.Core`
- `XamlToCSharpGenerator.Compiler`
- `XamlToCSharpGenerator.Avalonia`
