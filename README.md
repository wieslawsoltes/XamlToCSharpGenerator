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
  <AvaloniaSourceGenCreateSourceInfo>true</AvaloniaSourceGenCreateSourceInfo>
  <AvaloniaSourceGenStrictMode>true</AvaloniaSourceGenStrictMode>
  <AvaloniaSourceGenHotReloadEnabled>true</AvaloniaSourceGenHotReloadEnabled>
  <AvaloniaSourceGenHotReloadErrorResilienceEnabled>true</AvaloniaSourceGenHotReloadErrorResilienceEnabled>
  <AvaloniaSourceGenIdeHotReloadEnabled>true</AvaloniaSourceGenIdeHotReloadEnabled>
</PropertyGroup>
```

## Bootstrap Runtime

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
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
- `AXSG030x`: style/selector/control-theme semantics.
- `AXSG040x`: include graph and merge/source resolution.
- `AXSG050x`: template semantics/checkers.
- `AXSG060x`: integration/runtime wiring.
- `AXSG070x`: hot reload resilience/incremental behavior.

## Notes

- Source generation is opt-in.
- Default Avalonia backend remains unchanged (`XamlIl`) unless switched.
- Build integration disables Avalonia XAML compile task and Avalonia name generator when SourceGen backend is enabled.

## Samples

- CRUD sample:
  `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenCrudSample`
- Feature catalog sample (tabbed coverage of supported XAML features):
  `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample`
