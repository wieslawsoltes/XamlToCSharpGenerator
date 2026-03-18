---
title: "Editing and Navigation"
---

# Editing and Navigation

The VS Code extension is more than a thin LSP wrapper. It combines AXSG-native XAML semantics with a few editor-side integrations where reusing VS Code providers improves the result.

## XAML editing features

The packaged extension covers:

- completion
- hover
- definition and declaration
- references
- rename
- semantic tokens
- inline binding type hints
- cross-language navigation between XAML and C#

That includes AXSG-specific surfaces such as:

- owner-qualified property tokens
- property elements
- compiled bindings and runtime bindings
- inline C# and projected C# interop
- markup extensions, selectors, resources, and template-related navigation

## Rename workflow

The extension contributes:

- `AXSG: Rename Symbol Across C# and XAML`
- `F2` bindings for `xaml`, `axaml`, and `csharp`

Use AXSG rename when you want coordinated XAML and C# updates instead of a plain single-language rename.

## Preview command

You can open the preview from:

- the command palette with `AXSG: Open Avalonia Preview`
- the XAML editor context menu
- the XAML editor title actions

## Inspector commands

The inspector contributes:

- `AXSG Inspector: Show Inspector Panel`
- `AXSG Inspector: Refresh`
- `AXSG Inspector: Undo`
- `AXSG Inspector: Redo`

These commands operate on the current preview-backed design session.

## Status bar and output

The `AXSG` status bar item shows language-server state and opens:

- `AXSG: Show Language Server Info`

Use that command when you want to inspect:

- effective launch mode
- requested mode
- command and args
- workspace root
- last server error if one exists

The main diagnostic log surface is the `AXSG Language Server` output channel.

## Inline C# boundary

AXSG remains the semantic owner for the XAML document. For some inline-C# interactions the extension can reuse C# editor providers where that improves completion, hover, references, or navigation. Semantic coloring still stays AXSG-owned in the XAML document.

That split is deliberate:

- XAML semantics remain compiler-aligned
- projected C# interop is additive, not authoritative

## Related docs

- [Configuration](configuration/)
- [Preview and Inspector](preview-and-inspector/)
- [VS Code and Language Service](../guides/vscode-language-service/)
- [Navigation and Refactorings](../guides/navigation-and-refactorings/)
