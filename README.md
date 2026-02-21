# XamlToCSharpGenerator

Standalone Avalonia source-generator compiler backend as an alternative to XamlX.

## Install

Add one package to your Avalonia app:

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator" Version="1.0.0" />
</ItemGroup>
```

## Enable Source-Generator Backend

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

`AvaloniaSourceGenCompilerEnabled` is set automatically when the backend is `SourceGen`.

Optional backend knobs:

```xml
<PropertyGroup>
  <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>
  <AvaloniaSourceGenCSharpExpressionsEnabled>true</AvaloniaSourceGenCSharpExpressionsEnabled>
  <AvaloniaSourceGenImplicitCSharpExpressionsEnabled>true</AvaloniaSourceGenImplicitCSharpExpressionsEnabled>
  <AvaloniaSourceGenCreateSourceInfo>true</AvaloniaSourceGenCreateSourceInfo>
  <AvaloniaSourceGenStrictMode>true</AvaloniaSourceGenStrictMode>
  <AvaloniaSourceGenHotReloadEnabled>true</AvaloniaSourceGenHotReloadEnabled>
  <AvaloniaSourceGenHotReloadErrorResilienceEnabled>true</AvaloniaSourceGenHotReloadErrorResilienceEnabled>
  <AvaloniaSourceGenIdeHotReloadEnabled>true</AvaloniaSourceGenIdeHotReloadEnabled>
  <AvaloniaSourceGenHotDesignEnabled>true</AvaloniaSourceGenHotDesignEnabled>
  <AvaloniaSourceGenMetricsEnabled>true</AvaloniaSourceGenMetricsEnabled>
  <AvaloniaSourceGenMetricsDetailed>true</AvaloniaSourceGenMetricsDetailed>
  <AvaloniaSourceGenAllowImplicitXmlnsDeclaration>true</AvaloniaSourceGenAllowImplicitXmlnsDeclaration>
  <AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled>true</AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled>
  <AvaloniaSourceGenInferClassFromPath>true</AvaloniaSourceGenInferClassFromPath>
  <AvaloniaSourceGenImplicitProjectNamespacesEnabled>true</AvaloniaSourceGenImplicitProjectNamespacesEnabled>
</PropertyGroup>
```

When `AvaloniaSourceGenCreateSourceInfo=true`, generated C# also emits AXAML `#line` mappings (`// AXSG:XAML line:column` + `#line`) to improve debugger stepping and stack-trace source correlation.

## C# XAML Expressions

SourceGen supports C# expression markup for Avalonia bindings:

```xml
<TextBlock Text="{= FirstName + ' ' + LastName}" />
<TextBlock Text="{FirstName + '!'}" />
<TextBlock IsVisible="{Count GT 0}" />
```

Behavior:
- Explicit expressions use `{= ...}`.
- Implicit expressions use `{ ... }` when the payload is detected as C# (and not a markup extension).
- Expressions are compiled against the current `x:DataType` scope and emitted as typed runtime expression bindings.
- Style and `ControlTheme` setters also support expression bindings when their scope defines `x:DataType`.
- `x:DataType` is required for expression bindings (`AXSG0110` when missing).
- `AvaloniaSourceGenCSharpExpressionsEnabled=false` disables expression parsing entirely.
- `AvaloniaSourceGenImplicitCSharpExpressionsEnabled=false` keeps explicit `{= ...}` support while disabling implicit `{ ... }` C# detection.

## Event Bindings

SourceGen supports first-class event bindings in AXAML:

```xml
<Button Click="{EventBinding SaveCommand}" />
<Button Click="{EventBinding Command=SaveCommand, Parameter={Binding SelectedItem}}" />
<Button Click="{EventBinding Method=SaveWithArgs, PassEventArgs=True}" />
<Button Click="{EventBinding Method=HandleRootAction, Source=Root}" />
```

EventBinding arguments:
- `Command` or `Path`: command member path on the event source.
- `Method`: method path on the event source.
- `Parameter` / `CommandParameter`: optional parameter value/path.
- `PassEventArgs`: when `true`, event args are passed when no explicit parameter is provided.
- `Source`: `DataContext`, `Root`, or `DataContextThenRoot` (default).

Notes:
- Existing handler syntax (`Click="OnClick"`) continues to work.
- EventBinding can target commands or methods without manual event-hook code-behind wiring.

## Global XMLNS Imports

SourceGen can pre-seed XML namespace prefixes globally so individual AXAML files don’t need repeated `xmlns:*` declarations.

### MSBuild-based global prefixes

```xml
<PropertyGroup>
  <AvaloniaSourceGenGlobalXmlnsPrefixes>
    x=http://schemas.microsoft.com/winfx/2006/xaml,
    vm=using:MyApp.ViewModels,
    catalog=using:MyApp.Catalog
  </AvaloniaSourceGenGlobalXmlnsPrefixes>
</PropertyGroup>
```

`AvaloniaSourceGenGlobalXmlnsPrefixes` accepts comma/semicolon/newline separators. Comma-separated entries are recommended in MSBuild properties.

### Assembly-attribute global prefixes

```csharp
using Avalonia.Metadata;
using XamlToCSharpGenerator.Runtime;

[assembly: XmlnsPrefix("using:MyApp.ViewModels", "vm")]
[assembly: SourceGenGlobalXmlnsPrefix("catalog", "using:MyApp.Catalog")]
```

### Optional implicit default namespace

```xml
<PropertyGroup>
  <AvaloniaSourceGenAllowImplicitXmlnsDeclaration>true</AvaloniaSourceGenAllowImplicitXmlnsDeclaration>
  <AvaloniaSourceGenImplicitDefaultXmlns>https://github.com/avaloniaui</AvaloniaSourceGenImplicitDefaultXmlns>
</PropertyGroup>
```

With implicit mode enabled, AXAML can omit the default `xmlns="https://github.com/avaloniaui"` declaration.

When `AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled=true`, SourceGen also pre-seeds:

- `x -> http://schemas.microsoft.com/winfx/2006/xaml`
- `d -> http://schemas.microsoft.com/expression/blend/2008`
- `mc -> http://schemas.openxmlformats.org/markup-compatibility/2006`

Notes:
- File-local `xmlns:*` declarations still win over global mappings.
- `using:` namespace URIs are supported in resolver paths.
- Generic XML URI -> CLR namespace resolution now honors `XmlnsDefinition` attributes beyond Avalonia default URI.
- Under default Avalonia build integration, AXAML still needs to remain XML-valid for Avalonia resource preprocessing (for example prefixed element names may still require a declaration even when SourceGen globals are configured).

## Convention-Based Class And Type Resolution

Optional conventions inspired by Avalonia issue `#11906`:

```xml
<PropertyGroup>
  <AvaloniaSourceGenInferClassFromPath>true</AvaloniaSourceGenInferClassFromPath>
  <AvaloniaSourceGenImplicitProjectNamespacesEnabled>true</AvaloniaSourceGenImplicitProjectNamespacesEnabled>
</PropertyGroup>
```

Behavior:

- `AvaloniaSourceGenInferClassFromPath=true` infers `x:Class` from `RootNamespace + TargetPath` when `x:Class` is omitted, and applies it when the partial class already exists in compilation.
- `AvaloniaSourceGenImplicitProjectNamespacesEnabled=true` lets unprefixed controls resolve from project namespaces (scoped by `RootNamespace`) under the default Avalonia XML namespace.

## Conditional XAML

SourceGen supports conditional namespace aliases in AXAML:

```xml
<UserControl
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:cx="https://github.com/avaloniaui?ApiInformation.IsTypePresent('Avalonia.Controls.TextBlock')">
  <cx:TextBlock cx:Text="Only emitted when TextBlock type is present." />
</UserControl>
```

Supported conditional methods:
- `IsTypePresent` / `IsTypeNotPresent`
- `IsPropertyPresent` / `IsPropertyNotPresent`
- `IsMethodPresent` / `IsMethodNotPresent`
- `IsEventPresent` / `IsEventNotPresent`
- `IsEnumNamedValuePresent` / `IsEnumNamedValueNotPresent`
- `IsApiContractPresent` / `IsApiContractNotPresent`

Behavior:
- Conditional namespace URIs are normalized to the base namespace for normal type/property resolution.
- Condition-false branches are pruned before semantic binding, so unreachable markup does not produce unknown-type/property diagnostics.
- Invalid conditional expressions produce `AXSG0120`.

## Bootstrap Runtime

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

Enable runtime compilation fallback for dynamic URI/string loads:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml()
        .UseAvaloniaSourceGeneratedRuntimeXamlCompilation(enable: true, configure: options =>
        {
            options.TraceDiagnostics = true;
        });
```

Runtime load APIs:

```csharp
var fromUri = AvaloniaXamlLoader.Load(new Uri("avares://MyApp/Assets/RuntimeCard.xml"));
var fromInline = AvaloniaSourceGeneratedXamlLoader.Load(
    "<TextBlock xmlns='https://github.com/avaloniaui' Text='Runtime SourceGen' />",
    localAssembly: typeof(App).Assembly);
```

Optional Rider/IDE fallback poller (only needed when native metadata callback is unreliable in a specific IDE session):

```csharp
public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml()
        .UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(enable: true, pollingIntervalMs: 1000);
```

Advanced hot reload pipeline hooks (phased extensibility):

```csharp
using XamlToCSharpGenerator.Runtime;

public sealed class MyHotReloadHandler : ISourceGenHotReloadHandler
{
    public void ReloadCompleted(SourceGenHotReloadUpdateContext context)
    {
        // custom refresh/reporting
    }
}

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml()
        .UseAvaloniaSourceGeneratedXamlHotReloadHandler(new MyHotReloadHandler());
```

Assembly-level handler registration is also supported:

```csharp
[assembly: SourceGenHotReloadHandler(typeof(MyHotReloadHandler))]
```

## Hot Design Mode (Runtime Tool API)

Enable runtime hot-design orchestration (opt-in):

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml()
        .UseAvaloniaSourceGeneratedXamlHotDesign(configure: options =>
        {
            options.PersistChangesToSource = true;
            options.WaitForHotReload = true;
            options.HotReloadWaitTimeout = TimeSpan.FromSeconds(10);
            options.FallbackToRuntimeApplyOnTimeout = false;
        });
```

Runtime tool facade (invocable from debug/dev tooling code paths):

```csharp
using XamlToCSharpGenerator.Runtime;

XamlSourceGenHotDesignTool.Enable();
var status = XamlSourceGenHotDesignTool.GetStatus();
var docs = XamlSourceGenHotDesignTool.ListDocuments();

var result = await XamlSourceGenHotDesignTool.ApplyUpdateByUriAsync(
    "avares://MyApp/MainWindow.axaml",
    "<Window xmlns=\"https://github.com/avaloniaui\" />");
```

Core tool-panel API surface (toolbar/elements/toolbox/canvas/properties):

```csharp
using XamlToCSharpGenerator.Runtime;

var snapshot = XamlSourceGenHotDesignTool.GetWorkspaceSnapshot();
XamlSourceGenHotDesignTool.SetWorkspaceMode(SourceGenHotDesignWorkspaceMode.Design);
XamlSourceGenHotDesignTool.SetPropertyFilterMode(SourceGenHotDesignPropertyFilterMode.Smart);
XamlSourceGenHotDesignTool.SetCanvasZoom(1.15);
XamlSourceGenHotDesignTool.SelectElement(snapshot.ActiveBuildUri, "0/0");

await XamlSourceGenHotDesignTool.ApplyPropertyUpdateAsync(new SourceGenHotDesignPropertyUpdateRequest
{
    BuildUri = snapshot.ActiveBuildUri,
    ElementId = "0/0",
    PropertyName = "Margin",
    PropertyValue = "16",
    PersistChangesToSource = true,
    WaitForHotReload = false
});

await XamlSourceGenHotDesignTool.InsertElementAsync(new SourceGenHotDesignElementInsertRequest
{
    BuildUri = snapshot.ActiveBuildUri,
    ParentElementId = "0/0",
    ElementName = "Button",
    PersistChangesToSource = true,
    WaitForHotReload = false
});

await XamlSourceGenHotDesignTool.UndoAsync(snapshot.ActiveBuildUri);
await XamlSourceGenHotDesignTool.RedoAsync(snapshot.ActiveBuildUri);
```

The hot-design manager is extensible via `ISourceGenHotDesignUpdateApplier` for custom source propagation or update policies.

Policy-style handler helpers are available for app-owned side effects (for example manual style/resource/event wiring that must be explicitly cleaned/reapplied during reload):

```csharp
using XamlToCSharpGenerator.Runtime;

var cleanupPolicy = SourceGenHotReloadPolicies.Create<MyView, string[]>(
    priority: 50,
    captureState: static (_, view) => view.Classes.ToArray(),
    beforeElementReload: static (_, view, _) => view.Classes.Clear(),
    afterElementReload: static (_, view, previous) =>
    {
        if (previous is null)
        {
            return;
        }

        foreach (var cls in previous)
        {
            if (cls.StartsWith("manual-", StringComparison.Ordinal))
            {
                view.Classes.Add(cls);
            }
        }
    });
```

## IDE Hot Reload (Visual Studio and Rider)

When `AvaloniaXamlCompilerBackend=SourceGen`:

1. AXAML files are projected into Roslyn `AdditionalFiles` for source generation.
2. AXAML files are also injected into `CustomAdditionalCompileInputs` and `UpToDateCheckInput` so IDE fast up-to-date and compile invalidation detect AXAML edits.
3. Generated runtime refresh is driven by `.NET` metadata update callbacks (`MetadataUpdateHandler`).
4. Hot reload error resilience is enabled in `dotnet watch` and IDE build sessions by default (`AvaloniaSourceGenIdeHotReloadEnabled=true`).
5. Runtime hot reload pipeline maps replacement types to original types and serializes reentrant updates.
6. Runtime emits `HotReloadRudeEditDetected` when CLR/metadata shape changes are not patchable via Edit-and-Continue and require rebuild/restart.

## Language Service (LSP)

SourceGen now includes a standalone LSP server:

- Project:
  `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageServer`
- Tool command:
  `axsg-lsp --workspace <workspace-root>`

Supported LSP features:
- `initialize`, `shutdown`, `exit`
- `textDocument/didOpen`, `didChange`, `didSave`, `didClose`
- `textDocument/publishDiagnostics`
- `textDocument/completion`
- `textDocument/hover`
- `textDocument/definition` (Go To Definition from XAML element/property tokens)

Diagnostics reuse SourceGen compiler semantics (`SimpleXamlDocumentParser` + `AvaloniaSemanticBinder`) and publish existing `AXSG####` codes directly in editor diagnostics.

### Package and Install `axsg-lsp`

Pack locally:

```bash
dotnet pack /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj -c Release -o /tmp/axsg-pack
```

Install/update globally from local package output:

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool --add-source /tmp/axsg-pack
dotnet tool update --global XamlToCSharpGenerator.LanguageServer.Tool --add-source /tmp/axsg-pack
```

Publishable package id:
- `XamlToCSharpGenerator.LanguageServer.Tool`

### VS Code Local Install

VS Code language-client wrapper project:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tools/vscode/axsg-language-server`

Build a local VSIX:

```bash
cd /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tools/vscode/axsg-language-server
npm install
npx @vscode/vsce package
```

Then install the generated VSIX via:
- Extensions panel
- `...` menu
- `Install from VSIX...`

Client wiring requirements (VS Code and other IDEs):
- Start server process over stdio with the command above (or a custom configured command path).
- Register language IDs/extensions for `*.axaml` and `*.xaml`.
- Use full document sync (`textDocumentSync.change = 1`).
- Pass workspace root (`--workspace`) so project resolution can locate nearest `*.csproj`.

Example Neovim `lspconfig` wiring:

```lua
require('lspconfig').axsg = {
  cmd = {
    'dotnet',
    'run',
    '--project',
    '/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.LanguageServer/XamlToCSharpGenerator.LanguageServer.csproj',
    '--',
    '--workspace',
    vim.fn.getcwd(),
  },
  filetypes = { 'xml', 'xaml', 'axaml' },
  root_dir = require('lspconfig.util').root_pattern('*.sln', '*.csproj', '.git'),
}
```

## Migration Guide (XamlIl -> SourceGen)

1. Install `XamlToCSharpGenerator` package.
2. Set `<AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>`.
3. Add runtime bootstrap extension:
   `AppBuilder.Configure<App>().UsePlatformDetect().UseAvaloniaSourceGeneratedXaml();`
4. Build and fix diagnostics under `AXSG####`.
5. Keep fallback path by switching backend back to `XamlIl` if needed.

Detailed migration/release checklist:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/plan/32-ws73-packaging-migration-and-release-checklist.md`

## Compatibility Matrix

| Scenario | XamlIl | SourceGen |
| --- | --- | --- |
| C# Avalonia app | Supported | Supported (target path) |
| F#/VB Avalonia app | Supported | Not in v1 (stay on XamlIl) |
| Dynamic runtime XAML compilation API | Supported by Avalonia runtime paths | Not provided in v1 |
| Hot reload transient AXAML errors | N/A | Resilience mode supported (`AXSG0700`) |

## Diagnostics Bands

- `AXSG000x`: parse/document contract.
- `AXSG010x`: semantic binding and property/type conversion.
- `AXSG012x`: conditional XAML parsing/evaluation.
- `AXSG030x`: style/selector/control-theme semantics.
- `AXSG040x`: include graph and merge/source resolution.
- `AXSG050x`: template semantics/checkers.
- `AXSG060x`: integration/runtime wiring.
- `AXSG070x`: hot reload resilience/incremental behavior.
- `AXSG080x`: compile-time metrics and performance instrumentation.

## Notes

- Source generation is opt-in.
- Default Avalonia backend remains unchanged (`XamlIl`) unless switched.
- Build integration disables Avalonia XAML compile task and Avalonia name generator when SourceGen backend is enabled.

## Samples

- CRUD sample:
  `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample`
- Feature catalog sample (tabbed coverage of supported XAML features):
  `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample`
