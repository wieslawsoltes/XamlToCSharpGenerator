# Advanced Composition

Load this file when the task is about compiler internals, custom package composition, framework profiles, or reuse outside the default Avalonia app surface.

## Compiler stack layers

| Layer | Packages | Owns |
| --- | --- | --- |
| Roslyn generator bridge | `XamlToCSharpGenerator.Generator` | Source-generator entry point and packaged analyzer payload |
| Host and orchestration | `XamlToCSharpGenerator.Compiler` | Project discovery, configuration precedence, include graphs, transform-rule handling |
| Shared semantic contracts | `XamlToCSharpGenerator.Core` | Immutable models, diagnostics, configuration contracts, semantic model types |
| Framework seams | `XamlToCSharpGenerator.Framework.Abstractions` | Framework profile interfaces and compiler extension points |
| Avalonia implementation | `XamlToCSharpGenerator.Avalonia` | Avalonia binder, emitter, profile implementation, framework-specific lowering |
| Parser and expression helpers | `XamlToCSharpGenerator.ExpressionSemantics`, `XamlToCSharpGenerator.MiniLanguageParsing` | Roslyn-backed expression analysis plus low-allocation parsers |
| Minimal reference profile | `XamlToCSharpGenerator.NoUi` | Framework-neutral end-to-end sample profile |

## Choose by scenario

- Custom compiler embedding: use `Compiler`, `Core`, `Framework.Abstractions`, and one concrete profile package.
- Avalonia compiler behavior change: include `Avalonia` plus its companion shared packages.
- New framework profile exploration: start from `Framework.Abstractions` and `NoUi` before touching `Avalonia`.
- Expression or inline-C# analysis reuse: include `ExpressionSemantics`.
- Selector/binding/markup mini-language reuse: include `MiniLanguageParsing`.
- Manual generator integration: use `Generator` only when the request explicitly avoids the umbrella package.

## Runtime split for advanced hosts

- `XamlToCSharpGenerator.Runtime` is the composed runtime entry point.
- `XamlToCSharpGenerator.Runtime.Core` owns neutral runtime registries, URI mapping, source info, and hot-reload contracts.
- `XamlToCSharpGenerator.Runtime.Avalonia` owns Avalonia-specific runtime loader behavior, resource/include resolution, inline-code helpers, hot reload, and hot design integration.

Use the runtime split only when the host needs direct control over those layers.

## Decision rules

- Do not start from `Compiler` or `Generator` for simple app setup requests.
- Do not recommend `NoUi` for normal Avalonia apps; use it as the reference profile when explaining framework-neutral reuse.
- When the request is about "which package do I add to my app", redirect back to `references/avalonia-app-setup.md`.

## Maintenance note

If the host repository includes compiler-stack documentation, re-check the package layering there before editing this skill so the composition guidance stays aligned with the actual AXSG stack.
