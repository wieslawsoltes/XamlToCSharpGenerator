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
