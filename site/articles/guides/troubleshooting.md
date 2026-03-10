---
title: "Troubleshooting"
---

# Troubleshooting

Use this guide when AXSG behavior is wrong, slow, or inconsistent across build, runtime, hot reload, or editor tooling.

## Quick triage checklist

1. Confirm you are on a build that actually contains the change you expect.
2. Rebuild the affected sample or app in `Release` once before blaming the language service or hot reload.
3. If the issue is in VS Code, reload the window so the extension and server are rebuilt and restarted.
4. If the issue is in `dotnet watch`, reproduce with `AXSG_HOTRELOAD_TRACE=1`.
5. If the issue is in docs, run `bash ./check-docs.sh` or `pwsh ./check-docs.ps1` serially, not in parallel shells against the same output tree.

## Compiler and binding diagnostics

### `Compiled binding ... requires x:DataType in scope`

Likely causes:

- there is no ambient `x:DataType`
- the binding source is not explicitly typed
- shorthand or relative-source lowering did not resolve to a known source type

Check:

- `x:DataType` on the nearest binding scope
- typed `$parent[...]`, `$self`, `#name`, or `RelativeSource` source expressions
- whether the binding is inside a style, control theme, template, or resource dictionary where scope differs from the root view

Relevant docs:

- [Compiled Bindings](../xaml/compiled-bindings)
- [Binding and Expression Model](../concepts/binding-and-expression-model)

### Control-theme `BasedOn` cycle diagnostics

If a theme uses the same key as a base theme and `BasedOn` points to the same key, AXSG now treats that as the normal override pattern unless there is an actual earlier local cycle.

If you still see a cycle:

- check for multiple local themes with the same key in the same document chain
- inspect merged dictionary/include order
- verify the reference does not truly resolve back into the same local node set

## Hot reload and `dotnet watch`

### Roslyn `EmitDifference` duplicate-key or duplicate-row crashes

These usually mean generated helper identities are unstable between edits.

Typical surfaces that can destabilize EnC:

- inline event wrapper methods
- expression helper lambdas
- generated local functions or closure-heavy emission

Checklist:

- reproduce on the latest branch head
- edit only the target XAML file, not generated outputs
- check for deterministic helper naming in the generated file under `obj`

Relevant docs:

- [Hot Reload and Hot Design](hot-reload-and-hot-design)
- [Runtime and Hot Reload](../architecture/runtime-and-hot-reload)

### `dotnet watch` metadata-reference or named-pipe issues

Check:

- local analyzer/generator restore completed
- `Runtime.Avalonia` is not accidentally pulling incompatible compiler-only dependencies
- mobile/remote runs have the correct transport configuration

For iOS-specific workflows, use [iOS Hot Reload](hot-reload-ios).

## VS Code and language service

### Extension startup is slow

Check:

- whether the workspace actually contains XAML files
- whether AXSG is being activated by stale extension state from an older install
- whether `MSBuildWorkspace` initialization is happening eagerly because the server has already serviced a compilation-backed request

Useful actions:

- reload the VS Code window
- restart the AXSG language server
- check whether the issue only happens on the first compilation-backed request

### Completion or references are slow in inline C#

AXSG answers inline code directly first and only falls back to projected C# providers when needed. If inline requests are still slow:

- confirm the window was reloaded after updating the extension
- verify the position is actually inside an inline `CSharp` region and not plain XAML text
- compare AXSG-first behavior with projected-provider fallback behavior

### Ctrl/Cmd-click goes to the wrong token

AXSG differentiates several split-token surfaces:

- `pages:TypeName` -> prefix vs type token
- `Window.IsVisible` -> owner type vs property token
- `ToggleButton#ThemeToggle:checked` -> type vs `#name` vs pseudoclass token

If navigation chooses the wrong side, reproduce with the exact cursor position because token-side behavior is deliberate and separately tested.

## Docs and Lunet

### The docs site renders but API pages are broken or raw assets are missing

Check:

- `site/config.scriban` bundle configuration
- `site/.lunet/includes/_builtins/bundle.sbn-html`
- committed `site/.lunet/css/template-main.css`

### Generated docs serve raw `.md` links

Use:

- `bash ./check-docs.sh`
- `pwsh ./check-docs.ps1`

Both scripts fail when generated output still contains raw `.md` article links.

### AvaloniaEdit API links do not resolve

`AvaloniaEdit.*` links are expected to resolve through the external Avalonia API site:

- `https://api-docs.avaloniaui.net/docs/AvaloniaEdit.TextEditor/`
- `https://api-docs.avaloniaui.net/docs/AvaloniaEdit.Document.TextDocument/`

If editor-package links regress:

- verify `site/config.scriban` still maps the `AvaloniaEdit` assembly under `external_apis`
- rebuild the site with `./build-docs.sh` or `./build-docs.ps1`
- check the target URL directly before changing the Lunet API graph

Do not add a docs-only project that merely references the package. Lunet documents the output assembly of the configured project, not arbitrary referenced package assemblies, so that approach does not generate `AvaloniaEdit` API pages.

## Build and package selection

### I do not know which package to install

Start with:

- [Package Selection and Integration](package-selection-and-integration)
- [Package Catalog](../reference/package-catalog)
- [Package Guides](../reference/packages/readme)

### I need runtime support but not the full app package

Start with:

- [Runtime Loader and Fallback](runtime-loader-and-fallback)
- [Package: XamlToCSharpGenerator.Runtime](../reference/packages/runtime)
- [Package: XamlToCSharpGenerator.Runtime.Avalonia](../reference/packages/runtime-avalonia)

## When to open the API docs

Go to the generated API when you already know the subsystem and want member-level details.

Start with:

- [API Coverage Index](../reference/api-coverage-index)
- [Assembly Catalog](../reference/assembly-catalog)
- the relevant namespace summary page in [Reference](../reference/readme)

## Related

- [Glossary](../concepts/glossary)
- [VS Code and Language Service](vscode-language-service)
- [Hot Reload and Hot Design](hot-reload-and-hot-design)
- [Package Selection and Integration](package-selection-and-integration)
