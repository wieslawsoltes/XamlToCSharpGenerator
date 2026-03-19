---
title: "VS Code"
---

# VS Code

This section documents the packaged AXSG VS Code extension as a product surface, not just as a bundle artifact.

Use it when you need to:

- install or update the extension
- configure the language server or preview behavior
- use the preview tab together with the dedicated AXSG Inspector side panel
- understand editor features such as rename, navigation, inline C# interop, semantic tokens, and inlay hints
- troubleshoot preview startup, host selection, or inspector synchronization

## What the extension includes

The VS Code package bundles four cooperating surfaces:

1. the AXSG language client and editor middleware
2. the managed AXSG language server
3. the preview host and source-generated designer host
4. the AXSG Inspector activity-bar container with `Documents`, `Toolbox`, `Logical Tree`, `Visual Tree`, and `Properties`

The preview remains an editor tab. The inspector is a separate left-side sidebar surface with its own rail button.

## Read by task

### Install or wire the extension into a workspace

- [Installation and Setup](installation-and-setup/)

### Configure the extension and preview behavior

- [Configuration](configuration/)

### Use editing, rename, hover, references, and inline C# features

- [Editing and Navigation](editing-and-navigation/)

### Use the preview tab, design overlay, and inspector panels

- [Preview and Inspector](preview-and-inspector/)

### Debug startup, host-selection, and preview issues

- [Troubleshooting](troubleshooting/)

## Related docs

- [VS Code and Language Service](../guides/vscode-language-service/)
- [Language Service and VS Code](../architecture/language-service-and-vscode/)
- [Preview MCP Host and Live Preview](../guides/preview-mcp-host-and-live-preview/)
- [Artifact: VS Code Extension](../reference/vscode-extension/)
