---
title: "Workspace MCP Language Tools"
---

# Workspace MCP Language Tools

Use this guide when you want AXSG language-service operations over MCP from the standalone workspace host.

This guide is about:

- `axsg-mcp`
- workspace-shaped queries
- language-service operations that are useful outside an editor

It is not about the live runtime host inside a running Avalonia process.

## Use this guide when

- you need preview project resolution outside VS Code
- you want metadata-as-source text from an MCP client
- you need inline C# projections for a custom editor or analysis client
- you want cross-language references or declarations between C# and XAML
- you need rename planning or rename propagation over MCP

## Start the host

Install:

```bash
dotnet tool install --global XamlToCSharpGenerator.McpServer.Tool --version x.y.z
```

Run:

```bash
axsg-mcp --workspace /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator
```

For repo development:

```bash
dotnet run --project src/XamlToCSharpGenerator.McpServer -- --workspace /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator
```

## Query model

The workspace host is query-oriented.

Use this rule:

- `tools/call` or `resources/read`
- poll after workspace or editor changes
- do not expect live subscriptions into a running app

The host does expose the same runtime-shaped snapshot resources as the shared catalog, but on the standalone workspace tool those are only snapshots of the tool process itself. They are not a live bridge into a separate Avalonia runtime.

## Workspace MCP tools

### Preview planning

- `axsg.preview.projectContext`

### Metadata and projections

- `axsg.workspace.metadataDocument`
- `axsg.workspace.inlineCSharpProjections`

### Cross-language navigation

- `axsg.workspace.csharpReferences`
- `axsg.workspace.csharpDeclarations`

### Rename planning and edits

- `axsg.workspace.renamePropagation`
- `axsg.workspace.prepareRename`
- `axsg.workspace.rename`

## Preview project context

Use `axsg.preview.projectContext` when a client needs to know which project and project-relative path the preview system should use.

Example:

```json
{
  "jsonrpc": "2.0",
  "id": 10,
  "method": "tools/call",
  "params": {
    "name": "axsg.preview.projectContext",
    "arguments": {
      "uri": "file:///Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog/Pages/ListBoxPage.xaml",
      "workspaceRoot": "/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator"
    }
  }
}
```

Typical response fields include:

- `projectPath`
- `targetPath`
- `projectRelativeXamlPath`
- `targetFramework`

Use this before you start a preview session from a custom client.

## Metadata-as-source

Use `axsg.workspace.metadataDocument` when you already have either:

- an AXSG metadata document id
- an AXSG metadata URI

Example:

```json
{
  "jsonrpc": "2.0",
  "id": 11,
  "method": "tools/call",
  "params": {
    "name": "axsg.workspace.metadataDocument",
    "arguments": {
      "metadataUri": "axsg-metadata://document?id=ExternalControl"
    }
  }
}
```

Response shape:

```json
{
  "text": "namespace ExternalLibrary { public partial class ExternalControl : UserControl { ... } }"
}
```

## Inline C# projections

Use `axsg.workspace.inlineCSharpProjections` when a custom editor or tool needs the synthetic C# documents derived from inline AXSG code regions.

Example:

```json
{
  "jsonrpc": "2.0",
  "id": 12,
  "method": "tools/call",
  "params": {
    "name": "axsg.workspace.inlineCSharpProjections",
    "arguments": {
      "uri": "file:///Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Pages/InlineCodeCDataPage.axaml",
      "workspaceRoot": "/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator",
      "documentText": "<UserControl ... />",
      "version": 7
    }
  }
}
```

Each projection item includes:

- `id`
- `kind`
- `xamlRange`
- `projectedCodeRange`
- `projectedText`

Use `documentText` when the client has unsaved in-memory edits and wants projection results against that text instead of the file on disk.

## C# to XAML references and declarations

### References

Use `axsg.workspace.csharpReferences` to map a C# symbol position back to XAML references.

```json
{
  "jsonrpc": "2.0",
  "id": 13,
  "method": "tools/call",
  "params": {
    "name": "axsg.workspace.csharpReferences",
    "arguments": {
      "uri": "file:///Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog/ViewModels/ListBoxPageViewModel.cs",
      "line": 12,
      "character": 18,
      "workspaceRoot": "/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator"
    }
  }
}
```

Each result includes:

- `uri`
- `range`
- `isDeclaration`

### Declarations

Use `axsg.workspace.csharpDeclarations` when you want the XAML declaration side instead.

The arguments match the references tool:

- `uri`
- `line`
- `character`
- optional `workspaceRoot`
- optional `documentText`

## Rename planning and rename edits

### `axsg.workspace.prepareRename`

Use this first when your client needs to know whether rename is valid at a XAML position.

Response:

- `range`
- `placeholder`

### `axsg.workspace.renamePropagation`

Use this when rename started from C# and you only need the XAML-side workspace edit.

### `axsg.workspace.rename`

Use this when rename starts from a XAML position and you want the full AXSG rename edit payload.

Example:

```json
{
  "jsonrpc": "2.0",
  "id": 14,
  "method": "tools/call",
  "params": {
    "name": "axsg.workspace.rename",
    "arguments": {
      "uri": "file:///Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/ControlCatalog/Pages/ListBoxPage.xaml",
      "line": 2,
      "character": 16,
      "newName": "ItemsPanelView",
      "workspaceRoot": "/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator"
    }
  }
}
```

The tool returns a workspace-edit payload with URI-keyed text changes. That result is designed for AI tools and custom editors that want the rename plan without speaking LSP directly.

## Suggested client workflows

### Preview launch planning

1. call `axsg.preview.projectContext`
2. start preview using the resolved project and relative XAML path

### Custom editor inline-code analysis

1. call `axsg.workspace.inlineCSharpProjections` with the in-memory `documentText`
2. render diagnostics or semantic overlays against the projection mapping

### Cross-language rename from C#

1. call `axsg.workspace.renamePropagation`
2. apply only the returned XAML edit set

### XAML rename workflow

1. call `axsg.workspace.prepareRename`
2. if valid, call `axsg.workspace.rename`
3. apply the resulting workspace edit

## Polling guidance

Because the workspace host is not a live runtime host, use explicit polling:

- after file saves
- after editor buffer changes that matter to your query
- after project/solution reload

Use the runtime host instead when the question is about:

- live hot reload state
- live hot design workspace state
- studio session state

## Related docs

- [MCP Servers and Live Tooling](mcp-servers-and-live-tooling/)
- [VS Code and Language Service](vscode-language-service/)
- [Unified Remote API and MCP](../architecture/unified-remote-api-and-mcp/)
- [Package: XamlToCSharpGenerator.McpServer.Tool](../reference/mcp-server-tool/)
- [Package: XamlToCSharpGenerator.LanguageService](../reference/language-service/)
