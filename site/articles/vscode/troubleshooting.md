---
title: "Troubleshooting"
---

# Troubleshooting

This page focuses on the packaged VS Code extension workflow.

## The AXSG Inspector views still appear under Explorer

Usually this means VS Code persisted the old view location from a previous build.

Try:

1. reload the VS Code window
2. run `View: Reset View Locations`
3. reopen `AXSG Inspector: Show Inspector Panel`

The intended layout is:

- preview in the editor area
- inspector views in a dedicated left activity-bar container

## The AXSG Inspector rail button is missing

Check:

- the updated extension build is actually installed
- VS Code was reloaded after installation
- the workspace contains `.xaml` or `.axaml` so AXSG activates

If you just rebuilt the VSIX locally, reinstall that exact package and reload the window.

## Preview startup fails immediately

Start with the `AXSG Language Server` output channel.

Common causes:

- the host project does not build
- the active XAML file is in a library and `axsg.preview.hostProject` is not set
- the chosen target framework is not the desktop TFM you intended
- the installed extension bundle is stale

Recommended settings while diagnosing:

```json
{
  "axsg.preview.compilerMode": "auto",
  "axsg.preview.buildBeforeLaunch": true
}
```

## Source-generated preview is unavailable

In `auto` mode this should fall back cleanly to Avalonia/XamlX.

If you forced:

```json
{
  "axsg.preview.compilerMode": "sourceGenerated"
}
```

then the preview will fail until the AXSG runtime output is present in the build output.

## Preview works for executables but not for library XAML

Set:

```json
{
  "axsg.preview.hostProject": "/path/to/your/app.csproj"
}
```

Optionally also set:

```json
{
  "axsg.preview.targetFramework": "net10.0"
}
```

if the project multi-targets and the chosen desktop TFM is not the one you want.

## Preview is visible but interaction is wrong

Check the workspace mode in the preview toolbar:

- use `Interactive` for normal app input
- use `Design` for hit testing and overlay selection

If clicks are selecting elements instead of pressing buttons, you are in `Design`.

If keyboard input is not reaching the previewed app, confirm you are not in `Design`.

## Selection sync looks stale

Try:

1. `AXSG Inspector: Refresh`
2. reopening the preview tab
3. reselecting the document from `Documents`

Also verify that the active editor is the same XAML document that belongs to the active preview session.

## Property edits or toolbox insertions do not land where expected

The runtime mutation pipeline operates against the current active design document and selected element.

Before editing:

- confirm the right document is active in `Documents`
- confirm the right element is selected in preview or tree
- use the `Logical Tree` or `Visual Tree` to verify the current selection

## Native preview dependencies

The packaged extension bundles designer-host native assets for:

- macOS
- Windows
- Linux

If preview startup still reports missing native rendering libraries, the installed VSIX is usually stale or incomplete. Reinstall the freshly packaged extension and reload VS Code.

## Related docs

- [Installation and Setup](installation-and-setup/)
- [Configuration](configuration/)
- [Preview and Inspector](preview-and-inspector/)
- [Packaging and Release](../guides/packaging-and-release/)
