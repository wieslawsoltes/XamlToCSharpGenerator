# XamlToCSharpGenerator

`XamlToCSharpGenerator` is a source-generated XAML compiler stack for Avalonia, with optional runtime services, hot reload/hot design support, a reusable language-service core, an Avalonia editor control, a CLI language-server tool, and a VS Code extension.

The repository ships both a recommended end-user install surface and the lower-level packages used to compose custom tooling, editors, and framework adapters.

## What Ships

| Artifact | Kind | Package | Downloads | Audience | Install | Purpose |
| --- | --- | --- | --- | --- | --- | --- |
| `XamlToCSharpGenerator` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator/) | Application authors | `dotnet add package XamlToCSharpGenerator` | Recommended umbrella package. Installs build integration, generator assets, and runtime bootstrap pieces needed by Avalonia apps. |
| `XamlToCSharpGenerator.Build` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Build?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Build/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Build?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Build/) | Application authors, SDK integrators | `dotnet add package XamlToCSharpGenerator.Build` | MSBuild-only integration package. Use when you want SourceGen build integration without the umbrella package. |
| `XamlToCSharpGenerator.Runtime` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime/) | Runtime/hot-reload consumers | `dotnet add package XamlToCSharpGenerator.Runtime` | Compatibility runtime package that composes the framework-neutral and Avalonia runtime layers. |
| `XamlToCSharpGenerator.Runtime.Core` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime.Core?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Core/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime.Core?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Core/) | Tooling/runtime authors | `dotnet add package XamlToCSharpGenerator.Runtime.Core` | Framework-neutral runtime registries, URI mapping, and hot-reload contracts. |
| `XamlToCSharpGenerator.Runtime.Avalonia` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Runtime.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Runtime.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Runtime.Avalonia/) | Avalonia runtime authors | `dotnet add package XamlToCSharpGenerator.Runtime.Avalonia` | Avalonia-specific runtime loader, markup helpers, bootstrap extensions, and hot-reload integration. |
| `XamlToCSharpGenerator.Editor.Avalonia` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Editor.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Editor.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Editor.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Editor.Avalonia/) | Editor/tool authors | `dotnet add package XamlToCSharpGenerator.Editor.Avalonia` | AvaloniaEdit-based AXAML editor control backed by the AXSG language-service core. |
| `XamlToCSharpGenerator.LanguageService` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.LanguageService?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageService/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.LanguageService?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageService/) | Tooling authors | `dotnet add package XamlToCSharpGenerator.LanguageService` | Shared semantic language-service layer used by LSP and in-app editors. |
| `XamlToCSharpGenerator.LanguageServer.Tool` | .NET tool package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.LanguageServer.Tool?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageServer.Tool/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.LanguageServer.Tool?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.LanguageServer.Tool/) | CLI and editor integration | `dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool` | Packs the `axsg-lsp` command for LSP hosting outside VS Code. |
| `AXSG XAML Language Service` | VS Code extension (`.vsix`) | [![Marketplace](https://img.shields.io/visual-studio-marketplace/v/xamltocsharpgenerator.axsg-language-server?label=Marketplace)](https://marketplace.visualstudio.com/items?itemName=xamltocsharpgenerator.axsg-language-server) | [![Downloads](https://img.shields.io/visual-studio-marketplace/d/xamltocsharpgenerator.axsg-language-server?label=Downloads)](https://marketplace.visualstudio.com/items?itemName=xamltocsharpgenerator.axsg-language-server) | VS Code users | `code --install-extension ./axsg-language-server-x.y.z.vsix` | XAML/AXAML completion, diagnostics, navigation, rename propagation, inlay hints, hover, semantic highlighting, and inline C# editor support. |
| `XamlToCSharpGenerator.Generator` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Generator?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Generator/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Generator?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Generator/) | Advanced compiler integrators | `dotnet add package XamlToCSharpGenerator.Generator` | Standalone Roslyn generator backend. Use when you need the generator without the umbrella package. |
| `XamlToCSharpGenerator.Core` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Core?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Core/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Core?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Core/) | Advanced compiler integrators | `dotnet add package XamlToCSharpGenerator.Core` | Immutable parser model, diagnostics, configuration contracts, and shared semantic core. |
| `XamlToCSharpGenerator.Compiler` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Compiler?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Compiler/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Compiler?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Compiler/) | Advanced compiler integrators | `dotnet add package XamlToCSharpGenerator.Compiler` | Incremental host orchestration and generator pipeline entry points. |
| `XamlToCSharpGenerator.Framework.Abstractions` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Framework.Abstractions?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Framework.Abstractions/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Framework.Abstractions?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Framework.Abstractions/) | Framework adapter authors | `dotnet add package XamlToCSharpGenerator.Framework.Abstractions` | Framework profile abstractions for non-Avalonia reuse. |
| `XamlToCSharpGenerator.ExpressionSemantics` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.ExpressionSemantics?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.ExpressionSemantics/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.ExpressionSemantics?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.ExpressionSemantics/) | Binding/expression tooling authors | `dotnet add package XamlToCSharpGenerator.ExpressionSemantics` | Roslyn-based expression rewriting and dependency analysis shared across compiler and tooling paths. |
| `XamlToCSharpGenerator.MiniLanguageParsing` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.MiniLanguageParsing?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.MiniLanguageParsing/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.MiniLanguageParsing?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.MiniLanguageParsing/) | Parser/tooling authors | `dotnet add package XamlToCSharpGenerator.MiniLanguageParsing` | Shared low-allocation parsers for selectors, bindings, and markup fragments. |
| `XamlToCSharpGenerator.Avalonia` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.Avalonia?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.Avalonia/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.Avalonia?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.Avalonia/) | Avalonia compiler integrators | `dotnet add package XamlToCSharpGenerator.Avalonia` | Avalonia semantic binder and emitter passes over the framework-neutral compiler core. |
| `XamlToCSharpGenerator.NoUi` | NuGet package | [![NuGet](https://img.shields.io/nuget/v/XamlToCSharpGenerator.NoUi?label=NuGet)](https://www.nuget.org/packages/XamlToCSharpGenerator.NoUi/) | [![Downloads](https://img.shields.io/nuget/dt/XamlToCSharpGenerator.NoUi?label=Downloads)](https://www.nuget.org/packages/XamlToCSharpGenerator.NoUi/) | Framework experiments | `dotnet add package XamlToCSharpGenerator.NoUi` | NoUI framework profile used to validate framework-neutral host reuse. |

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

### CLI language server tool

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool
axsg-lsp
```

Use the tool when you want editor integration outside VS Code or when the VS Code extension is configured to launch a custom server.

## Package Overview And Usage

### `XamlToCSharpGenerator`

Use this unless you have a specific reason not to. It is the application-facing distribution for Avalonia projects and carries the standard build integration plus runtime bootstrap assemblies.

### `XamlToCSharpGenerator.Build`

Use this when you want build-transitive props/targets only and intend to manage generator/runtime package composition yourself.

### `XamlToCSharpGenerator.Runtime`, `Runtime.Core`, `Runtime.Avalonia`

These packages cover runtime loading, URI registries, hot reload, hot design, and Avalonia-specific runtime services.

Use:
- `Runtime` when you want the composed runtime package.
- `Runtime.Core` when you are building framework-neutral runtime infrastructure.
- `Runtime.Avalonia` when you are integrating directly with Avalonia runtime services.

### `XamlToCSharpGenerator.LanguageService`, `Editor.Avalonia`, `LanguageServer.Tool`, and the VS Code extension

These are the tooling-facing artifacts.

Use:
- `LanguageService` for custom IDE or editor integrations.
- `Editor.Avalonia` for an in-app AXAML editor surface.
- `LanguageServer.Tool` when you need a CLI/LSP host.
- the VS Code extension when you want the packaged editor experience, including inline C# completion, hover, references, definitions, inlay hints, and semantic highlighting inside attribute expressions, object-element code, and `<![CDATA[ ... ]]>` blocks.

### Compiler building blocks

The remaining NuGet packages exist for advanced composition:

- `XamlToCSharpGenerator.Generator`: Roslyn generator entrypoint.
- `XamlToCSharpGenerator.Core`: parser model, diagnostics, configuration, semantic contracts.
- `XamlToCSharpGenerator.Compiler`: incremental host orchestration.
- `XamlToCSharpGenerator.Framework.Abstractions`: framework adapter contracts.
- `XamlToCSharpGenerator.ExpressionSemantics`: Roslyn-backed expression analysis.
- `XamlToCSharpGenerator.MiniLanguageParsing`: shared mini-language parsers.
- `XamlToCSharpGenerator.Avalonia`: Avalonia binder and emitter layer.
- `XamlToCSharpGenerator.NoUi`: framework-neutral pilot profile.

## Core Capabilities

- Source-generated Avalonia XAML backend selected with `AvaloniaXamlCompilerBackend=SourceGen`
- Compiled-binding-first workflow with semantic type analysis
- C# expression bindings with explicit, implicit, shorthand, interpolation, and formatting forms
- Inline C# code via `{CSharp Code=...}`, `<CSharp>...</CSharp>`, and `<![CDATA[ ... ]]>` content blocks
- Inline event handlers, including lambda expressions and multi-line statement bodies
- Event bindings for commands, methods, and inline code
- Global XML namespace imports and implicit namespace conventions
- Conditional XAML pruning
- Runtime loading for URI and inline XAML scenarios
- Hot reload, iOS hot reload transport support, and hot design tooling
- Shared XAML language-service core with references/definitions/hover/inlay hints/rename
- Inline C# language-service support with semantic highlighting, completion, references, and declarations in both attribute and element-content forms
- VS Code extension and Avalonia editor control built on the same semantic engine

For feature-specific details:

- configuration model: [`site/articles/reference/configuration-model.md`](site/articles/reference/configuration-model.md)
- configuration migration: [`site/articles/reference/configuration-migration.md`](site/articles/reference/configuration-migration.md)
- C# expressions: [`site/articles/xaml/csharp-expressions.md`](site/articles/xaml/csharp-expressions.md)
- inline C# code blocks: [`site/articles/guides/inline-csharp-code.md`](site/articles/guides/inline-csharp-code.md)
- iOS hot reload: [`site/articles/guides/hot-reload-ios.md`](site/articles/guides/hot-reload-ios.md)

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
dotnet pack src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj -c Release --nologo -m:1 /nodeReuse:false --disable-build-servers
```

The release workflow packs every shippable project under `src/` that is marked packable.

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
- all shippable NuGet packages and the `.NET` tool package are packed
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

### Build-host and configuration alias properties

| Property | Default | Purpose |
| --- | --- | --- |
| `XamlSourceGenBackend` | mirrors `AvaloniaXamlCompilerBackend` | Backward-compatible backend alias. |
| `XamlSourceGenEnabled` | mirrors `AvaloniaSourceGenCompilerEnabled` | Backward-compatible enable switch alias. |
| `XamlSourceGenInputItemGroup` | `AvaloniaXaml` | Item group used as XAML input for the generator host. |
| `XamlSourceGenAdditionalFilesSourceItemGroup` | `AvaloniaXaml` | AdditionalFiles source item group for XAML inputs. |
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
