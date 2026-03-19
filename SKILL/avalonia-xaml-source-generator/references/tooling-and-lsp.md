# Tooling And LSP

Load this file when the task is about editor features, language-service hosting, in-app AXAML editing, or VS Code integration.

## Choose the right tooling layer

| Need | Recommended artifact | Why |
| --- | --- | --- |
| Packaged VS Code authoring experience | `wieslawsoltes.axsg-language-server` | Delivers the full extension experience with completion, diagnostics, navigation, rename propagation, semantic highlighting, and inline C# support |
| Standalone LSP host | `XamlToCSharpGenerator.LanguageServer.Tool` | Ships `axsg-lsp` for editors or workflows that want an external language server process |
| In-process semantic engine | `XamlToCSharpGenerator.LanguageService` | Exposes the shared analysis engine used by the tool and extension |
| In-app AXAML editor surface | `XamlToCSharpGenerator.Editor.Avalonia` | Adds an AvaloniaEdit-based editor control on top of `LanguageService` |

## Install shapes

VS Code extension:

```bash
code --install-extension ./axsg-language-server-x.y.z.vsix
```

CLI language server:

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool
axsg-lsp
```

In-process packages:

```bash
dotnet add package XamlToCSharpGenerator.LanguageService
dotnet add package XamlToCSharpGenerator.Editor.Avalonia
```

## Recommended combinations

- VS Code only: use the extension; add the standalone tool only when the editor should launch a custom LSP host.
- Custom editor host outside VS Code: start with `XamlToCSharpGenerator.LanguageServer.Tool` if out-of-process hosting is acceptable.
- Avalonia desktop tool with embedded editing: use `XamlToCSharpGenerator.Editor.Avalonia`; it already layers on `LanguageService`.
- Product code that needs semantic analysis but not an editor control: use `XamlToCSharpGenerator.LanguageService` directly.

## What each tooling package owns

- `LanguageService`: semantic analysis, completion, hover, definitions, references, rename, inlay hints, semantic tokens, and inline C# projections.
- `LanguageServer.Tool`: LSP transport and `axsg-lsp` host process.
- `Editor.Avalonia`: AvaloniaEdit-based in-app editor surface with AXSG language-service integration.
- VS Code extension: packaged client experience over the shared language-service pipeline.

## Maintenance note

If the host repository includes AXSG tooling docs, re-check the language-service, language-server, editor-control, and VS Code guidance before editing this skill.
