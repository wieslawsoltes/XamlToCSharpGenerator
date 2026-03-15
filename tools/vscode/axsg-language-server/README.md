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
The preview command launches Avalonia's official designer host through a bundled AXSG bridge so unsaved XAML edits can be pushed directly to the preview session without relying on file polling.

Semantic highlighting is enabled for `.xaml` and `.axaml` with a Visual Studio-style XAML palette:
- blue delimiters/attribute values/keywords
- red element and attribute names
- teal namespace prefixes
- green comments
- cyan markup-extension class identifiers

## Avalonia Preview

Preview sessions require a previewable Avalonia executable project in the workspace.
If the current XAML file belongs to a library, set `axsg.preview.hostProject` to the Avalonia app project that should host the preview.

Relevant settings:

- `axsg.preview.dotNetCommand`
- `axsg.preview.targetFramework`
- `axsg.preview.hostProject`
- `axsg.preview.buildBeforeLaunch`
- `axsg.preview.autoUpdateDelayMs`

## Development

```bash
npm install
npm run prepare:server
npm test
npx @vscode/vsce package
```

Install generated VSIX via VS Code command palette: `Extensions: Install from VSIX...`.
