# AXSG Package Surface

Load this file first when the correct AXSG artifact is unclear. It covers the full public install surface shipped by this repository.

## Public artifacts

| Artifact | Kind | Use when | Install |
| --- | --- | --- | --- |
| `XamlToCSharpGenerator` | NuGet package | Adding AXSG to a normal Avalonia app with the recommended defaults | `dotnet add package XamlToCSharpGenerator` |
| `XamlToCSharpGenerator.Build` | NuGet package | Needing build-transitive props/targets without the umbrella package | `dotnet add package XamlToCSharpGenerator.Build` |
| `XamlToCSharpGenerator.Runtime` | NuGet package | Wanting the composed runtime package for generated output, runtime loader support, and hot reload | `dotnet add package XamlToCSharpGenerator.Runtime` |
| `XamlToCSharpGenerator.Runtime.Core` | NuGet package | Reusing runtime registries, URI mapping, and hot-reload contracts without Avalonia-specific runtime code | `dotnet add package XamlToCSharpGenerator.Runtime.Core` |
| `XamlToCSharpGenerator.Runtime.Avalonia` | NuGet package | Integrating directly with Avalonia runtime loader, helpers, hot reload, or hot design | `dotnet add package XamlToCSharpGenerator.Runtime.Avalonia` |
| `XamlToCSharpGenerator.LanguageService` | NuGet package | Embedding AXSG semantic analysis, completion, hover, rename, or inline C# support in-process | `dotnet add package XamlToCSharpGenerator.LanguageService` |
| `XamlToCSharpGenerator.Editor.Avalonia` | NuGet package | Embedding an AvaloniaEdit-based AXAML editor inside an Avalonia app | `dotnet add package XamlToCSharpGenerator.Editor.Avalonia` |
| `XamlToCSharpGenerator.LanguageServer.Tool` | .NET tool package | Hosting the AXSG language server outside VS Code or launching `axsg-lsp` directly | `dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool` |
| `xamltocsharpgenerator.axsg-language-server` | VS Code extension | Using the packaged VS Code AXAML experience | `code --install-extension ./axsg-language-server-x.y.z.vsix` |
| `XamlToCSharpGenerator.Generator` | NuGet package | Taking the standalone Roslyn generator backend instead of the umbrella package | `dotnet add package XamlToCSharpGenerator.Generator` |
| `XamlToCSharpGenerator.Compiler` | NuGet package | Working on project discovery, include graphs, transform rules, or compiler host orchestration | `dotnet add package XamlToCSharpGenerator.Compiler` |
| `XamlToCSharpGenerator.Core` | NuGet package | Needing the immutable parser model, diagnostics, configuration model, or semantic contracts | `dotnet add package XamlToCSharpGenerator.Core` |
| `XamlToCSharpGenerator.Framework.Abstractions` | NuGet package | Building or extending framework profiles | `dotnet add package XamlToCSharpGenerator.Framework.Abstractions` |
| `XamlToCSharpGenerator.Avalonia` | NuGet package | Taking the Avalonia binder/emitter profile layer directly | `dotnet add package XamlToCSharpGenerator.Avalonia` |
| `XamlToCSharpGenerator.ExpressionSemantics` | NuGet package | Reusing the Roslyn-backed C# expression analysis layer | `dotnet add package XamlToCSharpGenerator.ExpressionSemantics` |
| `XamlToCSharpGenerator.MiniLanguageParsing` | NuGet package | Reusing low-allocation parsers for selectors, bindings, and markup fragments | `dotnet add package XamlToCSharpGenerator.MiniLanguageParsing` |
| `XamlToCSharpGenerator.NoUi` | NuGet package | Studying or prototyping a framework-neutral profile outside Avalonia | `dotnet add package XamlToCSharpGenerator.NoUi` |

## Selection shortcuts

- Standard Avalonia app: use `XamlToCSharpGenerator`.
- Explicit build import control: use `XamlToCSharpGenerator.Build`.
- Runtime-only or hot reload/runtime investigation: use `XamlToCSharpGenerator.Runtime`, or split into `Runtime.Core` plus `Runtime.Avalonia`.
- Custom IDE/editor integration: use `LanguageService`, `Editor.Avalonia`, `LanguageServer.Tool`, or the VS Code extension depending on host shape.
- Custom compiler/profile work: use `Compiler`, `Core`, `Framework.Abstractions`, and a concrete profile such as `Avalonia` or `NoUi`.

## Do not recommend

- `XamlToCSharpGenerator.DotNetWatch.Proxy` is an internal support component, not a public consumer package.
- Internal repo projects are acceptable in repo-local development guidance, but public setup instructions should use the shipped package/tool names above.

## Maintenance note

If the host repository includes AXSG documentation, re-check the package catalog and package-selection guidance before editing this skill so the shipped artifact list stays current.
