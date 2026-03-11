---
title: "Package: XamlToCSharpGenerator.LanguageService"
---

# XamlToCSharpGenerator.LanguageService

## Role

Shared semantic engine used by the LSP server and in-app editors. It owns completion, hover, navigation, references, semantic tokens, refactorings, and inline C# projections.

## Related namespaces

- <xref:XamlToCSharpGenerator.LanguageService>
- <xref:XamlToCSharpGenerator.LanguageService.Analysis>
- <xref:XamlToCSharpGenerator.LanguageService.Completion>
- <xref:XamlToCSharpGenerator.LanguageService.Definitions>
- <xref:XamlToCSharpGenerator.LanguageService.Hover>
- <xref:XamlToCSharpGenerator.LanguageService.InlayHints>
- <xref:XamlToCSharpGenerator.LanguageService.InlineCode>
- <xref:XamlToCSharpGenerator.LanguageService.Refactorings>
- <xref:XamlToCSharpGenerator.LanguageService.SemanticTokens>
- <xref:XamlToCSharpGenerator.LanguageService.Symbols>
- <xref:XamlToCSharpGenerator.LanguageService.Workspace>

## Primary responsibilities

This package is the center of AXSG editor tooling. It handles:

- semantic analysis over XAML documents and project compilations
- completion, hover, definitions, declarations, and references
- rename/refactoring propagation between C# and XAML
- inlay hints and semantic token generation
- inline C# projection and cross-language editor interoperability

It is the semantic center of the tooling stack. The standalone server, the VS Code extension, and in-app editor surfaces all depend on this package behaving like the compiler rather than like a disconnected heuristic parser.

## Typical hosts

- `XamlToCSharpGenerator.LanguageServer.Tool`
- the bundled VS Code extension
- `XamlToCSharpGenerator.Editor.Avalonia`

## Use this package directly when

- you are building an editor integration that should not shell out to the standalone server
- you want the semantic engine in-process
- you need the shared model behind navigation, completion, references, and inline C# projections

## What it is not

This package is not the LSP transport layer and it is not the editor client integration layer. Protocol shaping belongs in `XamlToCSharpGenerator.LanguageServer.Tool`, while editor-specific UX and middleware belong in the extension or in-process editor host.

## Performance expectations

Because this package sits on hot editor request paths, it carries explicit work around deferred compilation loading, cache reuse, low-allocation symbol/reference resolution, and inline-C# projection reuse.

## Related docs

- [Tooling Surface](../concepts/tooling-surface/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
