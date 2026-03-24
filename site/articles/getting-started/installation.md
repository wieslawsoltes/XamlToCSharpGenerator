---
title: "Installation"
---

# Installation

AXSG ships multiple install surfaces because different users need different entry points.

## Choose the right artifact

### Application author

Use the umbrella package when you want the normal app-facing install path:

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
```

This is the recommended starting point for Avalonia applications.

For Avalonia apps, the package reference alone is not the whole setup. AXSG keeps backend selection explicit, so you also need to:

1. opt the project into the SourceGen backend
2. enable the AXSG runtime bootstrap on `AppBuilder`

Minimal project setup:

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
</ItemGroup>

<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

Minimal runtime bootstrap:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

### Build/package integrator

If you need explicit control over the imported build layer, start from:

```xml
<PackageReference Include="XamlToCSharpGenerator.Build" Version="x.y.z" />
```

### Editor/tooling integration

Use the .NET tool when you need the standalone language server:

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool --version x.y.z
```

Use the VS Code extension when you want the bundled editor experience instead of wiring the tool manually.

## Minimum project expectations

For the main Avalonia path, a project should have:

- the AXSG package reference
- `AvaloniaXamlCompilerBackend=SourceGen`
- an Avalonia application/runtime setup
- `.UseAvaloniaSourceGeneratedXaml()` in the app bootstrap path
- `x:DataType` on binding scopes where compiled binding semantics are required
- build enabled so generated files can be produced on the first pass

## Class-backed XAML code-behind

If the project already contains hand-written `InitializeComponent()` methods that call `AvaloniaXamlLoader.Load(this)`, AXSG now gives you two supported migration paths:

1. remove the wrapper entirely
2. keep supported legacy loader calls and let AXSG IL-weave them to generated initializer helpers during the build

AXSG generates `InitializeComponent(bool loadXaml = true)` for class-backed XAML, and normal constructor calls such as `InitializeComponent();` are expected to bind to that generated method. A hand-written parameterless overload still wins overload resolution in source.

When `XamlSourceGenIlWeavingEnabled=true`, supported same-instance loader calls inside that wrapper are rewritten after compile to AXSG-generated initializer helpers, so the compiled app still runs on the AXSG path.

This applies to `App.axaml.cs` too, not just windows, views, or controls.

Also note that `AvaloniaNameGeneratorIsEnabled` belongs to the legacy Avalonia path, not the AXSG-generated class initialization path. Under normal AXSG setup, `XamlToCSharpGenerator.Build` disables the legacy name generator automatically so the project does not end up with two competing initialization paths.

Use one of these patterns:

- sourcegen-only project: remove the manual `AvaloniaXamlLoader.Load(this)` method
- sourcegen-only project with migration bridge: keep the supported loader call and rely on AXSG IL weaving
- mixed-backend or multi-target project: keep it behind `#if !AXAML_SOURCEGEN_BACKEND`

Recommended `App.axaml.cs` pattern for a SourceGen-only application:

```csharp
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
}
```

Direct source cleanup is still the preferred long-term AXSG style, so do not keep this active in AXSG mode when weaving is intentionally disabled:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

Recommended portable pattern:

```csharp
public MainWindow()
{
    InitializeComponent();
}

#if !AXAML_SOURCEGEN_BACKEND
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
#endif
```

Use the dedicated article for the full explanation of why this works and how it interacts with generated partials:

- [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/)
- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)

## Recommended follow-up

After installing:

1. Build once so generated artifacts and diagnostics are available.
2. Confirm the app uses `.UseAvaloniaSourceGeneratedXaml()` before debugging runtime loading or hot reload.
3. If class-backed views and `App.axaml.cs` still keep a parameterless `InitializeComponent()` wrapper around `AvaloniaXamlLoader.Load(this)`, confirm `XamlSourceGenIlWeavingEnabled=true`.
4. Confirm you are not forcing `AvaloniaNameGeneratorIsEnabled=true` on an AXSG-enabled project.
5. Add or verify `x:DataType` on key views, templates, and themes.
6. Open the sample catalog if you want a feature-by-feature reference implementation.

## Related docs

- [Quickstart](quickstart/)
- [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/)
- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)
- [Package and Assembly](../reference/package-and-assembly/)
- [Package Catalog](../reference/package-catalog/)
