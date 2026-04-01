# XamlToCSharpGenerator

`XamlToCSharpGenerator` is a source-generated XAML compiler stack for Avalonia, with optional runtime services, hot reload/hot design support, a reusable language-service core, an Avalonia editor control, a CLI language-server tool, a workspace MCP tool, and a VS Code extension.

The repository ships both a recommended end-user install surface and the lower-level packages used to compose custom tooling, editors, and framework adapters.

<img width="3494" height="1750" alt="image" src="https://github.com/user-attachments/assets/74951ae9-4866-4911-be11-be890f99dc31" />

## What Ships

### VSIX Extension

| Package Name | Badge | Downloads | Short Description |
| --- | --- | --- | --- |
| `AXSG XAML Language Service` | [![Marketplace](https://img.shields.io/visual-studio-marketplace/v/wieslawsoltes.axsg-language-server?label=Marketplace)](https://marketplace.visualstudio.com/items?itemName=wieslawsoltes.axsg-language-server) | [![Downloads](https://img.shields.io/visual-studio-marketplace/d/wieslawsoltes.axsg-language-server?label=Downloads)](https://marketplace.visualstudio.com/items?itemName=wieslawsoltes.axsg-language-server) | VS Code extension for XAML and AXAML completion, diagnostics, preview, navigation, rename, semantic highlighting, and inline C# editor support. |

### NuGet Packages

| Package Name | Badge | Downloads | Short Description |
| --- | --- | --- | --- |
| `XamlToCSharpGenerator` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator/) | Recommended umbrella package for Avalonia apps using AXSG. |
| `XamlToCSharpGenerator.Build` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Build?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Build/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Build?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Build/) | MSBuild-only integration package for SourceGen build wiring. |
| `XamlToCSharpGenerator.Runtime` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime/) | Compatibility runtime package that composes the AXSG runtime layers. |
| `XamlToCSharpGenerator.Runtime.Core` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime.Core?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Core/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime.Core?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Core/) | Framework-neutral runtime registries, URI mapping, and hot-reload contracts. |
| `XamlToCSharpGenerator.Runtime.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Avalonia/) | Avalonia-specific runtime loader, bootstrap extensions, and hot-reload integration. |
| `XamlToCSharpGenerator.RemoteProtocol` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.RemoteProtocol?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.RemoteProtocol/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.RemoteProtocol?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.RemoteProtocol/) | Shared remote protocol contracts for MCP, preview, and studio hosts. |
| `XamlToCSharpGenerator.Editor.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Editor.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Editor.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Editor.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Editor.Avalonia/) | AvaloniaEdit-based AXAML editor control backed by the shared language-service layer. |
| `XamlToCSharpGenerator.LanguageService` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.LanguageService?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageService/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.LanguageService?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageService/) | Shared semantic language-service layer for IDEs, editors, and tooling hosts. |
| `XamlToCSharpGenerator.LanguageServer.Tool` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.LanguageServer.Tool?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageServer.Tool/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.LanguageServer.Tool?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageServer.Tool/) | .NET tool package that ships the `axsg-lsp` command. |
| `XamlToCSharpGenerator.McpServer.Tool` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.McpServer.Tool?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.McpServer.Tool/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.McpServer.Tool?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.McpServer.Tool/) | .NET tool package that ships the `axsg-mcp` workspace MCP host. |
| `XamlToCSharpGenerator.Generator` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Generator?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Generator/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Generator?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Generator/) | Standalone Roslyn generator backend for advanced compiler composition. |
| `XamlToCSharpGenerator.Core` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Core?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Core/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Core?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Core/) | Immutable parser model, diagnostics, configuration contracts, and shared semantic core. |
| `XamlToCSharpGenerator.Compiler` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Compiler?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Compiler/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Compiler?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Compiler/) | Incremental host orchestration and generator pipeline entry points. |
| `XamlToCSharpGenerator.Framework.Abstractions` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Framework.Abstractions?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Framework.Abstractions/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Framework.Abstractions?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Framework.Abstractions/) | Framework adapter contracts for non-Avalonia reuse. |
| `XamlToCSharpGenerator.ExpressionSemantics` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.ExpressionSemantics?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.ExpressionSemantics/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.ExpressionSemantics?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.ExpressionSemantics/) | Roslyn-based expression rewriting and dependency analysis. |
| `XamlToCSharpGenerator.MiniLanguageParsing` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.MiniLanguageParsing?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.MiniLanguageParsing/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.MiniLanguageParsing?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.MiniLanguageParsing/) | Shared low-allocation parsers for selectors, bindings, and markup fragments. |
| `XamlToCSharpGenerator.Avalonia` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Avalonia/) | Avalonia semantic binder and emitter layer over the framework-neutral compiler core. |
| `XamlToCSharpGenerator.NoUi` | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.NoUi?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.NoUi/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.NoUi?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.NoUi/) | NoUI framework profile used to validate framework-neutral host reuse. |

## Recommended Install Paths

### Avalonia app using the SourceGen backend

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
</ItemGroup>

<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

Minimal bootstrap:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

### VS Code extension

The release pipeline produces a `.vsix` asset. Install it with:

```bash
code --install-extension ./axsg-language-server-x.y.z.vsix
```

The extension runs the bundled managed language server by default. You only need the CLI tool separately when you want to host the server yourself.
It also includes Avalonia preview support for `.xaml` and `.axaml` files through `AXSG: Open Avalonia Preview`.
Preview sessions can run either Avalonia's XamlX previewer or the AXSG source-generated loader. The default mode is now `sourceGenerated`, which keeps live unsaved XAML edits in sync in the preview while still rebuilding on save to realign generated output. `auto` also prefers source-generated preview when AXSG runtime output is available and falls back to Avalonia/XamlX otherwise.

### CLI language server tool

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool
axsg-lsp
```

Use the tool when you want editor integration outside VS Code or when the VS Code extension is configured to launch a custom server.

## Package Overview And Usage

### `AXSG XAML Language Service`

For VS Code users. Install it with `code --install-extension ./axsg-language-server-x.y.z.vsix`. Use it when you want the packaged AXSG editor experience with completion, diagnostics, navigation, preview, semantic highlighting, and inline C# tooling.

### `XamlToCSharpGenerator`

For Avalonia application authors. Install it with `dotnet add package XamlToCSharpGenerator`. Use it when you want the recommended umbrella package that brings in AXSG build integration and the runtime bootstrap surface.

### `XamlToCSharpGenerator.Build`

For application authors and SDK integrators who want only the MSBuild side. Install it with `dotnet add package XamlToCSharpGenerator.Build`. Use it when you want SourceGen props and targets without taking the umbrella package.

### `XamlToCSharpGenerator.Runtime`

For runtime and hot-reload consumers. Install it with `dotnet add package XamlToCSharpGenerator.Runtime`. Use it when you want the composed AXSG runtime package rather than referencing the lower-level runtime packages directly.

### `XamlToCSharpGenerator.Runtime.Core`

For tooling and runtime authors building framework-neutral services. Install it with `dotnet add package XamlToCSharpGenerator.Runtime.Core`. Use it for URI registries, hot-reload contracts, and other runtime primitives that are not tied to Avalonia.

### `XamlToCSharpGenerator.Runtime.Avalonia`

For Avalonia-specific runtime integrations. Install it with `dotnet add package XamlToCSharpGenerator.Runtime.Avalonia`. Use it when you need the Avalonia runtime loader, markup helpers, bootstrap extensions, or AXSG hot-reload integration.

### `XamlToCSharpGenerator.RemoteProtocol`

For tooling and host authors. Install it with `dotnet add package XamlToCSharpGenerator.RemoteProtocol`. Use it when you need the shared JSON-RPC, preview, studio, or MCP contract types used across AXSG hosts.

### `XamlToCSharpGenerator.Editor.Avalonia`

For editor and tooling authors building in-app editing surfaces. Install it with `dotnet add package XamlToCSharpGenerator.Editor.Avalonia`. Use it when you want an AvaloniaEdit-based AXAML editor control backed by AXSG semantics.

### `XamlToCSharpGenerator.LanguageService`

For custom IDE and editor integrations. Install it with `dotnet add package XamlToCSharpGenerator.LanguageService`. Use it when you need the shared semantic language-service layer without the packaged VS Code experience.

### `XamlToCSharpGenerator.LanguageServer.Tool`

For CLI and editor integration outside the bundled extension. Install it with `dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool`. Use it when you want to host `axsg-lsp` yourself.

### `XamlToCSharpGenerator.McpServer.Tool`

For AI, automation, and remote-tool integration. Install it with `dotnet tool install --global XamlToCSharpGenerator.McpServer.Tool`. Use it when you want the `axsg-mcp` workspace MCP host as a standalone tool.

### `XamlToCSharpGenerator.Generator`

For advanced compiler integrators. Install it with `dotnet add package XamlToCSharpGenerator.Generator`. Use it when you need the Roslyn generator backend without the umbrella distribution.

### `XamlToCSharpGenerator.Core`

For advanced compiler integrators working with the semantic core directly. Install it with `dotnet add package XamlToCSharpGenerator.Core`. Use it for parser models, diagnostics, configuration contracts, and shared semantic abstractions.

### `XamlToCSharpGenerator.Compiler`

For advanced compiler integrators building custom orchestration around AXSG. Install it with `dotnet add package XamlToCSharpGenerator.Compiler`. Use it for incremental host orchestration and generator pipeline entry points.

### `XamlToCSharpGenerator.Framework.Abstractions`

For framework adapter authors. Install it with `dotnet add package XamlToCSharpGenerator.Framework.Abstractions`. Use it when you are building non-Avalonia adapters on top of the shared compiler stack.

### `XamlToCSharpGenerator.ExpressionSemantics`

For binding and expression tooling authors. Install it with `dotnet add package XamlToCSharpGenerator.ExpressionSemantics`. Use it when you need Roslyn-based expression rewriting and dependency analysis outside the full compiler package set.

### `XamlToCSharpGenerator.MiniLanguageParsing`

For parser and tooling authors. Install it with `dotnet add package XamlToCSharpGenerator.MiniLanguageParsing`. Use it for the low-allocation parsers shared by selectors, bindings, and markup fragments.

### `XamlToCSharpGenerator.Avalonia`

For Avalonia compiler integrators. Install it with `dotnet add package XamlToCSharpGenerator.Avalonia`. Use it when you need the Avalonia semantic binder and emitter layer over the framework-neutral compiler core.

### `XamlToCSharpGenerator.NoUi`

For framework experiments and neutral-host validation. Install it with `dotnet add package XamlToCSharpGenerator.NoUi`. Use it when you want the NoUI framework profile for framework-neutral host reuse work.

## Core Capabilities

- Source-generated Avalonia XAML backend selected with `AvaloniaXamlCompilerBackend=SourceGen`
- Compiled-binding-first workflow with semantic type analysis
- Full `x:Bind` support with typed source resolution, generated event handlers, bind-back, and lifecycle helpers
- C# expression bindings with explicit, implicit, shorthand, interpolation, and formatting forms
- Inline C# code via `{CSharp Code=...}`, `<CSharp>...</CSharp>`, and `<![CDATA[ ... ]]>` content blocks
- Inline event handlers, including lambda expressions and multi-line statement bodies
- Event bindings for commands, methods, and inline code
- Global XML namespace imports and implicit namespace conventions
- Conditional XAML pruning
- Runtime loading for URI and inline XAML scenarios
- Build-time IL weaving for legacy `AvaloniaXamlLoader.Load(...)` migration, including service-provider overload support and AXSG-initializer rewrites
- Hot reload, iOS hot reload transport support, and hot design tooling
- Shared XAML language-service core with references/definitions/hover/inlay hints/rename
- Inline C# language-service support with semantic highlighting, completion, references, and declarations in both attribute and element-content forms
- VS Code extension and Avalonia editor control built on the same semantic engine
- Unified MCP surfaces for workspace queries, runtime hot reload and hot design state, and preview lifecycle orchestration

For feature-specific details:

- configuration model: [`site/articles/reference/configuration-model.md`](site/articles/reference/configuration-model.md)
- configuration migration: [`site/articles/reference/configuration-migration.md`](site/articles/reference/configuration-migration.md)
- x:Bind: [`site/articles/xaml/xbind.md`](site/articles/xaml/xbind.md)
- x:Bind guide/spec: [`site/articles/guides/xbind.md`](site/articles/guides/xbind.md)
- C# expressions: [`site/articles/xaml/csharp-expressions.md`](site/articles/xaml/csharp-expressions.md)
- inline C# code blocks: [`site/articles/guides/inline-csharp-code.md`](site/articles/guides/inline-csharp-code.md)
- Avalonia loader migration and IL weaving: [`site/articles/guides/avalonia-loader-il-weaving.md`](site/articles/guides/avalonia-loader-il-weaving.md)
- iOS hot reload: [`site/articles/guides/hot-reload-ios.md`](site/articles/guides/hot-reload-ios.md)
- MCP hosts and live tooling: [`site/articles/guides/mcp-servers-and-live-tooling.md`](site/articles/guides/mcp-servers-and-live-tooling.md)
- workspace MCP language tools: [`site/articles/guides/workspace-mcp-language-tools.md`](site/articles/guides/workspace-mcp-language-tools.md)
- runtime MCP hot design control: [`site/articles/guides/runtime-mcp-hot-design-control.md`](site/articles/guides/runtime-mcp-hot-design-control.md)
- runtime MCP studio control: [`site/articles/guides/runtime-mcp-studio-control.md`](site/articles/guides/runtime-mcp-studio-control.md)

## MCP Hosts

AXSG now ships three MCP-oriented host modes:

- workspace host: `axsg-mcp --workspace /path/to/repo`
- runtime host: embed `XamlSourceGenRuntimeMcpServer` into the running Avalonia app
- preview host: `dotnet run --project src/XamlToCSharpGenerator.PreviewerHost -- --mcp`

Use the workspace host for project and query tooling.
Use the runtime host for live `dotnet watch`, hot reload, hot design, and studio state.
Use the preview host when a custom client needs explicit preview start, in-process preview hot reload, update, stop, and lifecycle resources.

The full operational guide is here:

- [`site/articles/guides/mcp-servers-and-live-tooling.md`](site/articles/guides/mcp-servers-and-live-tooling.md)
- [`site/articles/guides/workspace-mcp-language-tools.md`](site/articles/guides/workspace-mcp-language-tools.md)
- [`site/articles/guides/runtime-mcp-hot-design-control.md`](site/articles/guides/runtime-mcp-hot-design-control.md)
- [`site/articles/guides/runtime-mcp-studio-control.md`](site/articles/guides/runtime-mcp-studio-control.md)
- [`site/articles/guides/preview-mcp-host-and-live-preview.md`](site/articles/guides/preview-mcp-host-and-live-preview.md)
- [`site/articles/architecture/unified-remote-api-and-mcp.md`](site/articles/architecture/unified-remote-api-and-mcp.md)

## AXSG vs XamlX

For Avalonia, `XamlX` is the compiler foundation behind the default `XamlIl` backend. `XamlToCSharpGenerator` is the source-generator alternative in this repository. The goal is standard Avalonia XAML parity first, then SourceGen-specific tooling and live-edit capabilities on top of that baseline.

| Area | AXSG (`XamlToCSharpGenerator`) | XamlX / Avalonia `XamlIl` | Notes |
| --- | --- | --- | --- |
| Primary build artifact | Generates C# into the normal Roslyn/MSBuild graph | Compiles XAML through the XamlX/XamlIl pipeline into generated IL/helpers | This is the main architectural difference. |
| Avalonia backend selection | Opt-in with `AvaloniaXamlCompilerBackend=SourceGen` | Avalonia default backend | AXSG is intentionally explicit so projects can switch per app/repo. |
| Standard Avalonia XAML surface | Implemented with ongoing parity work and guard tests | Mature baseline used by Avalonia itself | AXSG uses XamlX/XamlIl behavior as the parity reference for standard semantics. |
| Compiled bindings | Yes | Yes | Both stacks support typed binding flows for Avalonia. |
| Runtime loading | Shipped as AXSG runtime packages with source-generated registries | Shipped in Avalonia via `AvaloniaRuntimeXamlLoader` / `AvaloniaXamlIlRuntimeCompiler` | Both support runtime loading, but through different runtime contracts. |
| AOT / trimming posture | Explicit project rule: no reflection in emitted/runtime execution paths | Compile-time path is mature; Avalonia runtime loader paths are marked `RequiresUnreferencedCode` | AXSG is stricter here because NativeAOT/trimming is a first-class contract in this repo. |
| C# expression bindings (`{= ...}`, shorthand, interpolation) | Yes | No equivalent compiler feature in Avalonia `XamlIl` baseline | AXSG-specific extension. |
| Inline C# code blocks (`{CSharp ...}`, `<CSharp>`, `<![CDATA[ ... ]]>`) | Yes | No equivalent compiler feature in Avalonia `XamlIl` baseline | AXSG-specific extension. |
| Event bindings | Yes | No equivalent AXSG-style event-binding feature in Avalonia `XamlIl` baseline | AXSG-specific extension. |
| Conditional XAML pruning | Yes | Not part of the default Avalonia `XamlIl` compiler surface | AXSG-specific extension. |
| Global xmlns and transform-rule configuration | Yes | Not exposed as the same unified configuration model | AXSG-specific configuration surface. |
| Hot reload / hot design hooks | Built into the AXSG runtime and build flow in this repo | Not shipped as part of XamlX itself | This table compares compiler stacks, not every external IDE feature around Avalonia. |
| Language service and editor tooling | Shared semantic language-service core, VS Code extension, Avalonia editor control, rename propagation | Not shipped as part of the XamlX compiler stack | AXSG treats compiler semantics and editor tooling as one product surface. |
| Best fit | Projects that want generated C#, strong tooling integration, and SourceGen-specific live-edit features | Projects staying on Avalonia's default production compiler path | Both can coexist because backend selection is explicit. |

## Build Instructions

### Prerequisites

- .NET 10 SDK
- Node.js 20+ for the VS Code extension
- Xcode 26.2 for the iOS sample and iOS hot-reload validation
- Android SDK for the Android sample

### Fast CI-equivalent build

Use the solution filter when you do not need mobile workloads:

```bash
dotnet restore XamlToCSharpGenerator.CI.slnf --nologo
dotnet build XamlToCSharpGenerator.CI.slnf --nologo -m:1 /nodeReuse:false --disable-build-servers
dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --nologo -m:1 /nodeReuse:false --disable-build-servers --no-build
```

### Full repository build

Use the full solution when mobile prerequisites are installed:

```bash
dotnet restore XamlToCSharpGenerator.slnx --nologo
dotnet build XamlToCSharpGenerator.slnx --nologo -m:1 /nodeReuse:false --disable-build-servers
```

### Package the NuGet and tool artifacts locally

Pack a specific artifact:

```bash
dotnet pack src/XamlToCSharpGenerator/XamlToCSharpGenerator.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
dotnet pack src/XamlToCSharpGenerator.RemoteProtocol/XamlToCSharpGenerator.RemoteProtocol.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
dotnet pack src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
dotnet pack src/XamlToCSharpGenerator.McpServer/XamlToCSharpGenerator.McpServer.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

The release workflow packs a curated public artifact list from `src/`, including the published `RemoteProtocol`, `LanguageServer.Tool`, and `McpServer.Tool` packages.

To mirror the workflow artifact packaging locally:

```bash
bash eng/release/package-artifacts.sh 0.1.0-local
```

```powershell
pwsh eng/release/package-artifacts.ps1 -Version 0.1.0-local
```

Lower-level helpers are also available when you want to pack only part of the release surface:

```bash
bash eng/release/pack-nuget-artifacts.sh 0.1.0-local
bash eng/release/package-vscode-extension.sh 0.1.0-local
```

```powershell
pwsh eng/release/pack-nuget-artifacts.ps1 -Version 0.1.0-local
pwsh eng/release/package-vscode-extension.ps1 -Version 0.1.0-local
```

### Build the VS Code extension locally

```bash
cd tools/vscode/axsg-language-server
npm ci
npm run prepare:server
npx @vscode/vsce package
```

## Release Pipeline

The repository ships release artifacts through [`.github/workflows/release.yml`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/.github/workflows/release.yml).

Behavior:

- tag `v*` pushes create a release build
- all published NuGet packages and both published `.NET` tool packages are packed
- the VS Code extension is packaged as a `.vsix`
- artifacts are uploaded to the workflow run
- GitHub Releases are created automatically for tag builds
- NuGet publishing runs automatically when `NUGET_API_KEY` is configured
- VS Code Marketplace publishing runs automatically when `VSCE_PAT` is configured

Release assets include:

- `*.nupkg` for every shipped package/tool
- `axsg-language-server-x.y.z.vsix`

## Development Flags

### Core compiler MSBuild properties

These properties are exported through `XamlToCSharpGenerator.Build.props` and are the canonical switches for SourceGen-enabled Avalonia projects.

| Property | Default | Purpose |
| --- | --- | --- |
| `AvaloniaXamlCompilerBackend` | `XamlIl` | Selects the active XAML backend. Set to `SourceGen` to enable AXSG. |
| `AvaloniaSourceGenCompilerEnabled` | `false` | Explicit master enable switch for the SourceGen compiler path. |
| `AvaloniaSourceGenUseCompiledBindingsByDefault` | `false` | Makes bindings compiled by default when binding scopes support it. |
| `AvaloniaSourceGenCSharpExpressionsEnabled` | `true` | Enables explicit C# expression bindings (`{= ...}`). |
| `AvaloniaSourceGenImplicitCSharpExpressionsEnabled` | `true` | Enables implicit expression detection for `{ ... }` payloads. |
| `AvaloniaSourceGenCreateSourceInfo` | `false` | Emits `#line` and source mapping metadata into generated C#. |
| `AvaloniaSourceGenStrictMode` | `false` | Enables stricter semantic validation and warning behavior. |
| `AvaloniaSourceGenHotReloadEnabled` | `true` | Enables SourceGen hot reload integration. |
| `AvaloniaSourceGenHotReloadErrorResilienceEnabled` | `true` | Keeps last-known-good output during transient invalid edits. |
| `AvaloniaSourceGenIdeHotReloadEnabled` | `true` | Enables IDE-triggered hot reload behavior. |
| `AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled` | `false` | Re-enables SDK `dotnet watch` XAML build triggers when AXSG IDE hot reload is active. |
| `AvaloniaSourceGenHotDesignEnabled` | `false` | Enables hot design tooling support. |
| `AvaloniaSourceGenTracePasses` | `false` | Traces compiler pass execution for diagnostics/perf investigation. |
| `AvaloniaSourceGenMetricsEnabled` | `false` | Enables compiler metrics emission. |
| `AvaloniaSourceGenMetricsDetailed` | `false` | Enables detailed compiler metrics output. |
| `AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled` | `false` | Opt-in compatibility fallback for legacy invalid markup-argument behavior. |
| `AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled` | `false` | Opt-in compatibility fallback for legacy type-resolution behavior. |
| `AvaloniaSourceGenAllowImplicitXmlnsDeclaration` | `false` | Allows implicit default XAML namespace behavior. |
| `AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled` | `true` | Pre-seeds `x`, `d`, and `mc` namespace prefixes. |
| `AvaloniaSourceGenImplicitDefaultXmlns` | `https://github.com/avaloniaui` | Default XML namespace used when implicit xmlns mode is enabled. |
| `AvaloniaSourceGenInferClassFromPath` | `false` | Infers `x:Class` from root namespace and target path when possible. |
| `AvaloniaSourceGenImplicitProjectNamespacesEnabled` | `false` | Lets project-local namespaces participate in default type resolution. |
| `AvaloniaSourceGenGlobalXmlnsPrefixes` | empty | Declares global namespace prefix mappings. |
| `AvaloniaSourceGenTransformRules` | empty | Adds transform rules to the unified configuration model. |

When `DotNetWatchBuild=true` and AXSG IDE hot reload is active, AXSG suppresses XAML entries from the SDK watch/build-trigger inputs by default so theme and resource-dictionary edits flow through AXSG runtime reload instead of Roslyn EnC rebuilds. Set `AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled=true` only when you explicitly want the SDK `dotnet watch` XAML trigger behavior back.

### IL weaving MSBuild properties

These switches control AXSG's build-time compatibility pass that rewrites supported `AvaloniaXamlLoader.Load(...)` call sites to generated AXSG initializer helpers.

| Property | Default | Purpose |
| --- | --- | --- |
| `XamlSourceGenIlWeavingEnabled` | `true` | Canonical switch for build-time rewriting of supported legacy loader calls. |
| `AvaloniaSourceGenIlWeavingEnabled` | mirrors `XamlSourceGenIlWeavingEnabled` | Compatibility alias for the canonical switch. |
| `XamlSourceGenIlWeavingStrict` | `true` | Fails the build when a matched loader call on a source-generated type cannot be rewritten to a generated initializer. |
| `AvaloniaSourceGenIlWeavingStrict` | mirrors `XamlSourceGenIlWeavingStrict` | Compatibility alias. |
| `XamlSourceGenIlWeavingVerbose` | `false` | Logs inspection, match, and rewrite counts for the IL-weaving pass. |
| `AvaloniaSourceGenIlWeavingVerbose` | mirrors `XamlSourceGenIlWeavingVerbose` | Compatibility alias. |
| `XamlSourceGenIlWeavingBackend` | `Metadata` | Selects the weave scan backend. `Metadata` uses `System.Reflection.Metadata` for the fast scan path. `Cecil` keeps the legacy all-Cecil scan path. |
| `AvaloniaSourceGenIlWeavingBackend` | mirrors `XamlSourceGenIlWeavingBackend` | Compatibility alias. |

These are MSBuild-only build integration properties. They do not map to `xaml-sourcegen.config.json`. Use them when migrating legacy `App.axaml.cs` and class-backed view code that still calls `AvaloniaXamlLoader.Load(this)` or `AvaloniaXamlLoader.Load(serviceProvider, this)`.

### Build-host and configuration alias properties

| Property | Default | Purpose |
| --- | --- | --- |
| `XamlSourceGenBackend` | mirrors `AvaloniaXamlCompilerBackend` | Backward-compatible backend alias. |
| `XamlSourceGenEnabled` | mirrors `AvaloniaSourceGenCompilerEnabled` | Backward-compatible enable switch alias. |
| `XamlSourceGenInputItemGroup` | `AvaloniaXaml` | Item group used as XAML input for the generator host. This is the supported way to switch AXSG to a custom project item group. |
| `XamlSourceGenAdditionalFilesSourceItemGroup` | `AvaloniaXaml` | Reserved for Avalonia package integration. AXSG always projects Avalonia XAML into Roslyn `AdditionalFiles` as `AvaloniaXaml`; custom values are ignored with a build warning. |
| `XamlSourceGenTransformRules` | empty | Backward-compatible transform-rule alias. |
| `XamlSourceGenTransformRuleItemGroup` | `AvaloniaSourceGenTransformRule` | Item group used to contribute transform rules. |
| `XamlSourceGenConfigurationPrecedence` | empty | Overrides configuration source precedence. |
| `AvaloniaSourceGenConfigurationPrecedence` | empty | Canonical configuration precedence override. |
| `XamlSourceGenLocalAnalyzerProject` | none | Development-only item used to point sample apps at locally built generator projects and watch graph inputs. |

### iOS and remote hot-reload MSBuild properties

| Property | Default | Purpose |
| --- | --- | --- |
| `AvaloniaSourceGenIosHotReloadEnabled` | `true` for Debug iOS, otherwise `false` | Enables iOS-specific hot-reload wiring. |
| `AvaloniaSourceGenIosHotReloadUseInterpreter` | `true` when iOS hot reload is enabled | Turns on interpreter mode needed by the iOS edit-and-continue path. |
| `AvaloniaSourceGenIosHotReloadEnableStartupHookSupport` | `false` | Enables startup-hook forwarding support on iOS. |
| `AvaloniaSourceGenIosHotReloadForwardWatchEnvironment` | `true` | Forwards `dotnet watch` environment variables into the iOS launch flow. |
| `AvaloniaSourceGenIosHotReloadForwardStartupHooks` | `false` | Forwards startup hook variables into the iOS launch flow. |
| `AvaloniaSourceGenIosHotReloadForwardModifiableAssemblies` | `false` | Forwards modifiable-assemblies settings into the iOS launch flow. |
| `AvaloniaSourceGenIosHotReloadStartupBannerEnabled` | `true` | Prints the iOS hot-reload startup banner. |
| `AvaloniaSourceGenIosHotReloadTransportMode` | `Auto` | Chooses `Auto`, `MetadataOnly`, or `RemoteOnly` transport selection. |
| `AvaloniaSourceGenIosHotReloadHandshakeTimeoutMs` | `3000` | Transport handshake timeout. |
| `AvaloniaSourceGenHotReloadRemoteEndpoint` | empty | Explicit remote host endpoint for device/simulator transport. |
| `AvaloniaSourceGenHotReloadRemotePort` | `45820` | Default remote transport port when endpoint auto-resolution is used. |
| `AvaloniaSourceGenHotReloadRemoteAutoSimulatorEndpointEnabled` | `false` | Allows simulator endpoint auto-selection. |
| `AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint` | `true` | Requires an explicit device endpoint instead of guessing. |
| `AvaloniaSourceGenIosDotNetWatchXamlBuildTriggersEnabled` | `false` | Enables iOS-specific `dotnet watch` XAML build trigger plumbing. |
| `AvaloniaSourceGenIosDotNetWatchProxyProjectPath` | empty | Points to the dotnet-watch proxy project used by iOS debugging flows. |
| `AvaloniaSourceGenIosDotNetWatchProxyPath` | empty | Points to the published proxy assembly used by iOS debugging flows. |

### Runtime and test environment variables

| Variable | Default | Purpose |
| --- | --- | --- |
| `AXSG_HOTRELOAD_TRACE` | off | Enables runtime hot-reload trace logging. |
| `AXSG_HOTRELOAD_TRANSPORT_MODE` | unset | Overrides transport selection at runtime. |
| `AXSG_HOTRELOAD_HANDSHAKE_TIMEOUT_MS` | unset | Overrides hot-reload handshake timeout. |
| `AXSG_HOTRELOAD_REMOTE_ENDPOINT` | unset | Supplies the remote endpoint directly from the environment. |
| `AXSG_RUN_PERF_TESTS` | unset | Enables the performance harness tests. |
| `AXSG_PERF_TEST_TIMEOUT_MS` | harness default | Overrides perf-test timeout. |
| `AXSG_PERF_MAX_FULL_BUILD_MS` | harness default | Max acceptable full build duration for perf validation. |
| `AXSG_PERF_MAX_SINGLE_EDIT_MS` | harness default | Max acceptable single-edit incremental duration. |
| `AXSG_PERF_MAX_INCLUDE_EDIT_MS` | harness default | Max acceptable include-edit incremental duration. |
| `AXSG_PERF_MAX_INCREMENTAL_TO_FULL_RATIO` | harness default | Max acceptable incremental/full build ratio. |

### VS Code extension settings

| Setting | Default | Purpose |
| --- | --- | --- |
| `axsg.languageServer.mode` | `bundled` | Chooses bundled vs custom language-server launch mode. |
| `axsg.languageServer.command` | `axsg-lsp` | Command used when `mode=custom`. |
| `axsg.languageServer.args` | `[]` | Additional arguments passed to the custom server. |
| `axsg.languageServer.trace` | `off` | LSP trace level. |
| `axsg.inlayHints.bindingTypeHints.enabled` | `true` | Enables semantic binding type hints. |
| `axsg.inlayHints.typeDisplayStyle` | `short` | Shows short or fully qualified type names in hints. |
| `axsg.preview.dotNetCommand` | `dotnet` | Dotnet executable used for preview build and launch steps. |
| `axsg.preview.compilerMode` | `sourceGenerated` | Chooses `auto`, `sourceGenerated`, or `avalonia` preview compilation. |
| `axsg.preview.targetFramework` | `""` | Optional target framework override for preview host/source evaluation. |
| `axsg.preview.hostProject` | `""` | Optional Avalonia executable project used when the current XAML file lives in a library. |
| `axsg.preview.buildBeforeLaunch` | `true` | Builds preview projects only when fresh outputs are needed. Source-generated save refresh rebuilds just the source project when the host output can be reused. |
| `axsg.preview.autoUpdateDelayMs` | `300` | Debounce interval before unsaved XAML edits are pushed to the active preview session. |

## Repository Layout

- `src`: packages, tool, generator, runtime, and language-service projects
- `tools/vscode/axsg-language-server`: VS Code extension source
- `tests`: compiler, runtime, and language-service test suite
- `samples`: sample apps used to validate compiler/runtime/tooling behavior
- `docs`: focused documentation for configuration and platform-specific flows
- `site`: Lunet documentation site content, navigation, and API-doc generation

Build docs locally:

```bash
./build-docs.sh
./serve-docs.sh
```

```powershell
./build-docs.ps1
./serve-docs.ps1
```

Generated docs output is written to `site/.lunet/build/www`.
- `.github/workflows`: CI and release pipelines

## License

MIT. See [`LICENSE`](https://github.com/wieslawsoltes/XamlToCSharpGenerator/blob/main/LICENSE).
