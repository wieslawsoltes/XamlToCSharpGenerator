---
title: "Feature Coverage Matrix"
---

# Feature Coverage Matrix

This page maps the major AXSG feature areas to the narrative docs, the primary packages, and the generated API entry points.

| Feature area | Narrative docs | Primary packages | Primary API/namespace entry points |
| --- | --- | --- | --- |
| Compiled bindings | [Compiled Bindings](../xaml/compiled-bindings), [C# Expressions](../guides/csharp-expressions) | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.ExpressionSemantics` | <xref:XamlToCSharpGenerator.Avalonia.Binding>, <xref:XamlToCSharpGenerator.ExpressionSemantics> |
| Event bindings and inline lambdas | [Event Bindings](../xaml/event-bindings), [Inline C# Code](../guides/inline-csharp-code) | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.Runtime.Avalonia` | <xref:XamlToCSharpGenerator.Avalonia.Binding>, <xref:XamlToCSharpGenerator.Runtime.Markup> |
| Inline C# and CDATA | [Inline C#](../xaml/inline-csharp), [Inline C# Code](../guides/inline-csharp-code) | `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.LanguageService` | <xref:XamlToCSharpGenerator.ExpressionSemantics>, <xref:XamlToCSharpGenerator.Runtime.Markup>, <xref:XamlToCSharpGenerator.LanguageService.InlineCode> |
| Property elements, `TemplateBinding`, attached properties | [Property Elements and TemplateBinding](../xaml/property-elements-templatebinding-and-attached-properties) | `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.LanguageService` | <xref:XamlToCSharpGenerator.Core.Parsing>, <xref:XamlToCSharpGenerator.LanguageService.Definitions> |
| Resources, includes, and URIs | [Resources, Includes, and URIs](../xaml/resources-includes-and-uris) | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.LanguageService` | <xref:XamlToCSharpGenerator.Avalonia.Binding>, <xref:XamlToCSharpGenerator.Runtime>, <xref:XamlToCSharpGenerator.LanguageService.Definitions> |
| Selectors, styles, templates, control themes | [Styles, Templates, and Themes](../xaml/styles-templates-and-themes) | `XamlToCSharpGenerator.Avalonia`, `XamlToCSharpGenerator.MiniLanguageParsing` | <xref:XamlToCSharpGenerator.Avalonia.Parsing>, <xref:XamlToCSharpGenerator.MiniLanguageParsing.Selectors> |
| Global `xmlns` and configuration | [Global XML Namespaces and Project Configuration](../xaml/global-xmlns-and-project-configuration), [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules) | `XamlToCSharpGenerator.Compiler`, `XamlToCSharpGenerator.Core`, `XamlToCSharpGenerator.Build` | <xref:XamlToCSharpGenerator.Compiler>, <xref:XamlToCSharpGenerator.Core.Configuration> |
| Language service and VS Code | [VS Code and Language Service](../guides/vscode-language-service), [Navigation and Refactorings](../guides/navigation-and-refactorings) | `XamlToCSharpGenerator.LanguageService`, `XamlToCSharpGenerator.LanguageServer.Tool`, VS Code extension | <xref:XamlToCSharpGenerator.LanguageService>, <xref:XamlToCSharpGenerator.LanguageService.Completion>, <xref:XamlToCSharpGenerator.LanguageService.Definitions> |
| Runtime loader and fallback | [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback), [Runtime and Hot Reload](../architecture/runtime-and-hot-reload) | `XamlToCSharpGenerator.Runtime`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Runtime.Avalonia` | <xref:XamlToCSharpGenerator.Runtime>, <xref:XamlToCSharpGenerator.Runtime.Markup> |
| Hot reload and hot design | [Hot Reload and Hot Design](../guides/hot-reload-and-hot-design), [iOS Hot Reload](../guides/hot-reload-ios), [Hot Reload and Hot Design Internals](../advanced/hot-reload-and-hot-design) | `XamlToCSharpGenerator.Runtime.Avalonia`, `XamlToCSharpGenerator.Runtime.Core`, `XamlToCSharpGenerator.Build` | <xref:XamlToCSharpGenerator.Runtime>, <xref:XamlToCSharpGenerator.Runtime.Markup> |
| Embedded/editor hosting | [VS Code and Language Service](../guides/vscode-language-service) | `XamlToCSharpGenerator.Editor.Avalonia` | <xref:XamlToCSharpGenerator.Editor.Avalonia> |
| Packaging and release | [Packaging and Release](../guides/packaging-and-release), [Docs and Release Infrastructure](../advanced/docs-and-release-infrastructure) | `XamlToCSharpGenerator`, `XamlToCSharpGenerator.Build`, `XamlToCSharpGenerator.LanguageServer.Tool`, VS Code extension | [Package and Assembly](package-and-assembly), [Artifact Matrix](artifact-matrix) |

## How to use this page

- Start here when you know the feature but not the package.
- Use [Package Catalog](package-catalog) when you know the artifact you want to install.
- Use [API Coverage Index](api-coverage-index) when you want to jump directly into generated namespaces.
