---
title: "Configuration"
---

# Configuration

This page collects the current VS Code extension settings in one place and explains when to change them.

## Language-server settings

| Setting | Default | Use it for |
| --- | --- | --- |
| `axsg.languageServer.mode` | `bundled` | Choose between the packaged managed server and a custom command. |
| `axsg.languageServer.command` | `axsg-lsp` | Custom server executable or script when `mode = custom`. |
| `axsg.languageServer.args` | `[]` | Extra command-line arguments for a custom server launch. |
| `axsg.languageServer.trace` | `off` | Protocol tracing for debugging client/server traffic. |

### Recommended usage

- Leave `mode = bundled` for normal product use.
- Switch to `custom` only when you are debugging a custom server build, external tool wrapper, or transport issue.
- Use `trace = messages` or `verbose` only for short debugging sessions because it increases log noise.

## Inlay-hint settings

| Setting | Default | Use it for |
| --- | --- | --- |
| `axsg.inlayHints.bindingTypeHints.enabled` | `true` | Show inferred binding types inline in XAML. |
| `axsg.inlayHints.typeDisplayStyle` | `short` | Choose short names vs fully qualified CLR names. |

## Preview settings

| Setting | Default | Use it for |
| --- | --- | --- |
| `axsg.preview.dotNetCommand` | `dotnet` | Override the `.NET` command used for build/launch. |
| `axsg.preview.compilerMode` | `auto` | Select preview startup strategy. |
| `axsg.preview.targetFramework` | `""` | Force a target framework when the project has multiple desktop TFMs. |
| `axsg.preview.hostProject` | `""` | Point library XAML files at an executable preview host project. |
| `axsg.preview.buildBeforeLaunch` | `true` | Allow preview orchestration to build missing or stale outputs. |
| `axsg.preview.autoUpdateDelayMs` | `300` | Debounce unsaved XAML pushes to the preview host. |

## Preview compiler modes

### `auto`

Recommended default.

- probes source-generated preview first
- falls back to Avalonia/XamlX when AXSG runtime output is unavailable
- avoids treating a normal fallback path as a user-facing error

### `sourceGenerated`

Use this only when you want to force the AXSG preview path.

- uses the AXSG source-generated runtime/designer host
- gives the best parity for AXSG-specific features when the runtime output is present
- fails fast if the required AXSG runtime output is missing

### `avalonia`

Use this when you explicitly want the Avalonia/XamlX previewer path.

- bypasses AXSG source-generated startup selection
- useful for comparison/debugging or non-AXSG preview workflows

## Editor defaults applied by the extension

For both `xaml` and `axaml`, the extension also contributes defaults for:

- semantic highlighting enabled
- string quick suggestions enabled
- inlay hints enabled with `onUnlessPressed`

Those are configuration defaults contributed by the extension, not hard-coded editor behavior.

## Example configurations

### Recommended general setup

```json
{
  "axsg.preview.compilerMode": "auto",
  "axsg.preview.buildBeforeLaunch": true,
  "axsg.inlayHints.bindingTypeHints.enabled": true
}
```

### Force source-generated preview

```json
{
  "axsg.preview.compilerMode": "sourceGenerated"
}
```

### Library XAML with explicit preview host

```json
{
  "axsg.preview.hostProject": "samples/ControlCatalog/ControlCatalog.Desktop/ControlCatalog.Desktop.csproj",
  "axsg.preview.targetFramework": "net10.0"
}
```

### Custom language server

```json
{
  "axsg.languageServer.mode": "custom",
  "axsg.languageServer.command": "/path/to/axsg-lsp",
  "axsg.languageServer.args": [
    "--stdio"
  ]
}
```

## Related docs

- [Installation and Setup](installation-and-setup/)
- [Preview and Inspector](preview-and-inspector/)
- [Troubleshooting](troubleshooting/)
