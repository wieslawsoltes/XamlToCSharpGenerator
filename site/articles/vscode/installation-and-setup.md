---
title: "Installation and Setup"
---

# Installation and Setup

## What you need

For the full AXSG VS Code workflow you need:

- VS Code 1.95 or newer
- a workspace that contains `.xaml` or `.axaml` files
- a working `.NET` SDK on `PATH`
- a buildable Avalonia executable for preview

If your XAML lives in a library project, the preview still needs a runnable Avalonia host application. Configure that with `axsg.preview.hostProject`.

## Install the extension

The repository packages the extension from:

- `tools/vscode/axsg-language-server`

Typical local workflow:

1. build/package the VSIX from that folder
2. install the generated `.vsix` into VS Code
3. reload the window
4. open a XAML or AXAML file to activate AXSG

## First-run checks

After opening a XAML file, verify:

- the `AXSG` status bar item appears
- `AXSG: Show Language Server Info` reports the expected launch mode
- `AXSG: Open Avalonia Preview` opens a preview editor tab
- the `AXSG Inspector` rail button is visible on the left activity bar

If the inspector views still appear under Explorer after updating from an older build, run `View: Reset View Locations` once and reload the window.

## Preview setup

The recommended preview setup is now:

```json
{
  "axsg.preview.compilerMode": "auto"
}
```

`auto` tries the AXSG source-generated preview first and falls back to the Avalonia/XamlX previewer when needed. That is the smoothest default because it keeps source-generated parity when available without forcing the workspace into a broken state when the AXSG runtime output is not ready yet.

## Host-project selection

When the active XAML file belongs to a library, the extension may need help picking the executable preview host. Use:

```json
{
  "axsg.preview.hostProject": "samples/ControlCatalog/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj"
}
```

You can point this at either:

- a `.csproj`
- a folder that contains the desired `.csproj`

The extension also remembers successful host-project choices per source project in workspace state.

## Output surfaces

The extension uses:

- the `AXSG Language Server` output channel for language-server and preview orchestration logs
- preview notifications for startup/update failures
- the status bar item for quick server-state inspection

When setup looks wrong, start with the output channel before changing compiler or runtime settings.

## Related docs

- [Configuration](configuration/)
- [Preview and Inspector](preview-and-inspector/)
- [Troubleshooting](troubleshooting/)
