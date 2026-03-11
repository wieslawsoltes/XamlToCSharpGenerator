# Avalonia App Setup

Load this file when the task is about adding AXSG to an Avalonia app, switching a project from `XamlIl` to SourceGen, or wiring repo-local sample-style development.

## Recommended packaged setup

Use the umbrella package unless the user explicitly asks to compose lower-level packages.

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
</ItemGroup>

<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

Runtime bootstrap:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

## What this implies

- `AvaloniaXamlCompilerBackend=SourceGen` selects AXSG instead of the default `XamlIl` backend.
- `AvaloniaSourceGenCompilerEnabled=true` is implied by the backend switch.
- `XamlToCSharpGenerator.Build` automatically disables the legacy Avalonia XAML compilation path when SourceGen is enabled.
- `XamlToCSharpGenerator.Build` also disables `AvaloniaNameGenerator` to avoid duplicate `InitializeComponent` generation.

## When to use `XamlToCSharpGenerator.Build` directly

Use `XamlToCSharpGenerator.Build` when the user explicitly wants:

- direct control over imported props/targets
- custom SDK layering
- CI or packaging work centered on build behavior
- a split build/runtime/tooling composition instead of the umbrella package

If the request is only "add AXSG to my app", do not escalate to `Build`.

## Repo-local development pattern

When the host repository consumes local AXSG projects directly instead of NuGet packages, mirror the `SourceGenCrudSample` structure rather than inventing a custom integration shape.

Keep these elements:

- import the AXSG build props before the main project properties
- set `AvaloniaXamlCompilerBackend` to `SourceGen`
- add a project reference to the AXSG runtime layer used by the app
- add `XamlSourceGenLocalAnalyzerProject` items for the generator stack, typically `Generator`, `Core`, `Compiler`, `Framework.Abstractions`, `Avalonia`, `MiniLanguageParsing`, and `ExpressionSemantics`
- import the AXSG build targets at the end of the project file

Use this repo-local pattern only when consuming local projects directly. For shipped-package guidance, go back to the umbrella package.

## Runtime package choice

- Use `XamlToCSharpGenerator.Runtime` for the normal packaged runtime surface.
- Use `XamlToCSharpGenerator.Runtime.Core` plus `XamlToCSharpGenerator.Runtime.Avalonia` only when the user explicitly wants the runtime split or is integrating runtime layers separately.

## Maintenance note

If the host repository contains a sample AXSG app, use that sample as the canonical local-development pattern instead of hardcoding filesystem-specific import paths into this skill.
