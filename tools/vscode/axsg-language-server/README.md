# AXSG Language Server (VS Code)

VS Code extension for AXSG XAML/AXAML language service.

By default, the extension runs a bundled managed language server:

- `dotnet <extension>/server/XamlToCSharpGenerator.LanguageServer.dll`

You can switch to a custom executable with:

- `axsg.languageServer.mode = "custom"`
- `axsg.languageServer.command = "..."`
- `axsg.languageServer.args = [...]`

The extension also adds a status bar item (`$(info) AXSG`) that shows language-server status.
Click it to view runtime info and open the output channel.
When metadata symbols include SourceLink debug information, navigation opens a virtual source document (`axsg-sourcelink://`) fetched from the mapped URL.
Avalonia preview is available from `AXSG: Open Avalonia Preview` in the command palette, editor title, or XAML editor context menu.
The preview command launches Avalonia's designer host through a bundled AXSG bridge. In `auto` mode the extension prefers AXSG source-generated preview when the project output contains `XamlToCSharpGenerator.Runtime.Avalonia`, and falls back to Avalonia's XamlX previewer otherwise.

Semantic highlighting is enabled for `.xaml` and `.axaml` with a Visual Studio-style XAML palette:
- blue delimiters/attribute values/keywords
- red element and attribute names
- teal namespace prefixes
- green comments
- cyan markup-extension class identifiers

## Avalonia Preview

Preview sessions require a previewable Avalonia executable project in the workspace.
If the current XAML file belongs to a library, set `axsg.preview.hostProject` to the Avalonia app project that should host the preview.

Preview compiler modes:

- `auto`: prefer AXSG source-generated preview when available, otherwise use Avalonia/XamlX
- `sourceGenerated`: force AXSG source-generated preview; this is the default mode, it keeps live unsaved XAML edits in sync in the preview, and save/build refresh keeps the generated baseline aligned when `axsg.preview.buildBeforeLaunch` is enabled
- `avalonia`: force Avalonia's official XamlX previewer and keep live unsaved XAML updates

Build behavior is optimized for preview latency:

- preview startup reuses existing host/source outputs when they are already usable
- source-generated save refresh rebuilds only the source project when the host app output can be reused
- preview builds use `--no-restore` when the project is already restored and fall back to a normal build only if restore is actually required

Relevant settings:

- `axsg.preview.dotNetCommand`
- `axsg.preview.compilerMode`
- `axsg.preview.targetFramework`
- `axsg.preview.hostProject`
- `axsg.preview.buildBeforeLaunch`
- `axsg.preview.autoUpdateDelayMs`

`axsg.preview.compilerMode = auto` is the default. It prefers source-generated preview first and falls back to Avalonia/XamlX when needed without treating the fallback path as an error.

## MCP and Preview Hosts

The extension does not require you to start an MCP server for normal editing or preview usage.

Current behavior:

- the extension owns the bundled language-server startup path
- the preview UI uses the bundled preview helper transport for the product workflow
- the same preview helper also supports `--mcp` for custom clients, tests, and future remote integrations

Separate AXSG MCP surfaces are available when you need them outside the extension:

- workspace MCP host: `axsg-mcp --workspace /path/to/workspace`
- runtime MCP host: embed `XamlSourceGenRuntimeMcpServer` into the running app
- preview MCP host: `dotnet <preview-host>/XamlToCSharpGenerator.PreviewerHost.dll --mcp`

The preview MCP host now exposes:

- `axsg.preview.start`
- `axsg.preview.hotReload`
- `axsg.preview.update`
- `axsg.preview.stop`

Use `axsg.preview.hotReload` when a custom client needs the in-process live preview result instead of dispatch-only update acceptance.

For the operational host matrix and `dotnet watch` guidance, see:

- [site/articles/guides/mcp-servers-and-live-tooling.md](../../../site/articles/guides/mcp-servers-and-live-tooling.md)
- [site/articles/guides/workspace-mcp-language-tools.md](../../../site/articles/guides/workspace-mcp-language-tools.md)
- [site/articles/guides/runtime-mcp-hot-design-control.md](../../../site/articles/guides/runtime-mcp-hot-design-control.md)
- [site/articles/guides/runtime-mcp-studio-control.md](../../../site/articles/guides/runtime-mcp-studio-control.md)
- [site/articles/guides/preview-mcp-host-and-live-preview.md](../../../site/articles/guides/preview-mcp-host-and-live-preview.md)
- [site/articles/architecture/unified-remote-api-and-mcp.md](../../../site/articles/architecture/unified-remote-api-and-mcp.md)

## Development

```bash
npm install
npm run prepare:server
npm test
npx @vscode/vsce package
```

Install generated VSIX via VS Code command palette: `Extensions: Install from VSIX...`.
