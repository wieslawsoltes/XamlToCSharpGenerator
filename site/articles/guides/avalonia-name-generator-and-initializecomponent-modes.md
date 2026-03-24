---
title: "Avalonia Name Generator and InitializeComponent Modes"
---

# Avalonia Name Generator and InitializeComponent Modes

This guide explains which build switches control `InitializeComponent` generation in Avalonia projects, how that changes when AXSG is enabled, and how AXSG IL weaving changes the migration story for legacy `AvaloniaXamlLoader.Load(...)` code-behind.

## The short version

For AXSG-backed class-backed XAML:

- set `<AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>`
- let AXSG generate `InitializeComponent(bool loadXaml = true)`
- prefer removing legacy `AvaloniaXamlLoader` wrappers over time
- rely on AXSG IL weaving when you need to keep supported legacy `Load(this)` or `Load(serviceProvider, this)` call sites during migration
- treat `AvaloniaNameGeneratorIsEnabled` as the legacy Avalonia path, not the AXSG path

This includes `App.axaml.cs`. The application root must follow the same rule as any other class-backed AXAML file.

## Which switch controls `InitializeComponent`

There are two different systems that can participate in class-backed XAML initialization:

1. Avalonia's legacy name-generator and loader path
2. AXSG's generated object-graph path

The main switches are:

| Switch | Layer | What it means |
| --- | --- | --- |
| `AvaloniaXamlCompilerBackend` | project MSBuild property | Chooses the XAML compiler backend. `SourceGen` selects AXSG; `XamlIl` keeps the standard Avalonia path. |
| `AvaloniaSourceGenCompilerEnabled` | project MSBuild property | Explicit AXSG master enable switch. Usually implied by `AvaloniaXamlCompilerBackend=SourceGen`. |
| `EnableAvaloniaXamlCompilation` | project MSBuild property | Controls Avalonia XamlIl compilation. AXSG build integration disables this when SourceGen is active. |
| `AvaloniaNameGeneratorIsEnabled` | project MSBuild property | Controls Avalonia's legacy name generator that participates in the classic `InitializeComponent` path. |
| `XamlSourceGenIlWeavingEnabled` / `AvaloniaSourceGenIlWeavingEnabled` | project MSBuild property | Enables AXSG post-compile rewriting of supported `AvaloniaXamlLoader.Load(...)` call sites to generated AXSG initializer helpers. |
| `AXAML_SOURCEGEN_BACKEND` | conditional compilation symbol | Defined by AXSG build integration when the active backend is AXSG. Use it to guard compatibility fallback code. |

## What `AvaloniaNameGeneratorIsEnabled` really means

`AvaloniaNameGeneratorIsEnabled` is not the AXSG switch. It belongs to the legacy Avalonia build path.

When AXSG is active, `XamlToCSharpGenerator.Build` turns it off so the project does not end up with two competing class initialization paths.

That is the important distinction:

- `AvaloniaNameGeneratorIsEnabled=true` means the classic Avalonia path is still allowed to generate its helper surface
- `AvaloniaXamlCompilerBackend=SourceGen` means AXSG is responsible for generated class-backed initialization

## Mode matrix

### Classic Avalonia / XamlIl mode

Typical shape:

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>XamlIl</AvaloniaXamlCompilerBackend>
  <EnableAvaloniaXamlCompilation>true</EnableAvaloniaXamlCompilation>
  <AvaloniaNameGeneratorIsEnabled>true</AvaloniaNameGeneratorIsEnabled>
</PropertyGroup>
```

In this mode:

- Avalonia's normal XAML compilation path is active
- a hand-written loader fallback such as `AvaloniaXamlLoader.Load(this)` is compatible with the active backend
- AXSG-generated `InitializeComponent` is not the active path

### AXSG / SourceGen mode

Typical shape:

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

With AXSG build integration, that implies the effective behavior is:

- `AvaloniaSourceGenCompilerEnabled=true`
- `EnableAvaloniaXamlCompilation=false`
- `AvaloniaNameGeneratorIsEnabled=false`

In this mode:

- AXSG generates the class-backed `InitializeComponent(bool loadXaml = true)` method
- the generated method is the supported initialization path for class-backed XAML
- supported legacy `AvaloniaXamlLoader.Load(...)` call sites can be rewritten to the generated AXSG initializer helpers
- if IL weaving is disabled or the call shape is unsupported, a raw `AvaloniaXamlLoader.Load(this)` wrapper still bypasses the generated AXSG path

### Mixed-backend or multi-target mode

This is the migration-friendly shape when one code-behind file still has to build in both worlds.

Recommended pattern:

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

In this mode:

- AXSG builds compile out the manual fallback
- non-AXSG builds keep the old loader path
- the constructor stays stable across both modes

## Why raw `AvaloniaXamlLoader.Load(this)` is not directly compatible without IL weaving

For AXSG class-backed XAML, the source-generated partial already contains the generated object-graph population code.

If your code-behind keeps:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

then this constructor:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

binds to the hand-written parameterless method, not the AXSG-generated `InitializeComponent(bool loadXaml = true)`.

That makes raw `AvaloniaXamlLoader.Load(this)` incompatible with AXSG as the active class initialization path because it:

- bypasses the generated AXSG object graph
- bypasses AXSG-generated name rebinding behavior
- can make the project appear to "build with source gen" while the view still initializes through the old runtime loader path

The issue is not just "duplicate code". It is that the wrong method wins overload resolution.

## How IL weaving changes the migration story

When AXSG IL weaving is enabled, supported legacy call sites are rewritten after compile:

- `AvaloniaXamlLoader.Load(this)` becomes the generated AXSG initializer helper for that same type
- `AvaloniaXamlLoader.Load(serviceProvider, this)` becomes the service-provider-aware generated AXSG initializer helper

That means a common legacy wrapper can stay in source temporarily and still run on AXSG's generated initialization path at runtime.

This is a migration bridge, not the preferred end-state. The clearest long-term AXSG style is still to remove the manual wrapper and call the generated `InitializeComponent()` directly.

## What to do in each case

### SourceGen-only application

Preferred:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

The same applies to `App`:

```csharp
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
}
```

Compatibility alternative during migration:

```csharp
private void InitializeComponent()
{
    AvaloniaXamlLoader.Load(this);
}
```

That source shape is supported when AXSG IL weaving is enabled, but it still relies on a build-time rewrite pass instead of the direct generated method.

### Reusable library that must still support non-AXSG consumers

Use:

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

### Manual override of the legacy name generator

If you explicitly set:

```xml
<AvaloniaNameGeneratorIsEnabled>true</AvaloniaNameGeneratorIsEnabled>
```

while also enabling AXSG, you are re-enabling a legacy path that AXSG normally disables to avoid conflicts. That is not the recommended AXSG configuration.

For normal AXSG app usage, do not force the name generator back on.

## Recommended project snippet

```xml
<ItemGroup>
  <PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
</ItemGroup>

<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

And in app startup:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

## Troubleshooting checklist

If a view still behaves like classic Avalonia loading after switching to AXSG:

1. Confirm the project sets `AvaloniaXamlCompilerBackend=SourceGen`.
2. Confirm the app uses `.UseAvaloniaSourceGeneratedXaml()`.
3. Confirm `XamlSourceGenIlWeavingEnabled` is `true` if you expect legacy wrappers to be bridged automatically.
4. Confirm the code-behind uses a supported direct same-instance loader call shape.
5. Confirm `AvaloniaXamlLoader.Load(this)` is only present under `#if !AXAML_SOURCEGEN_BACKEND` when mixed-backend support is intentional and weaving is not the chosen bridge.
6. Confirm generated output under `obj/...` contains AXSG-generated `InitializeComponent(bool loadXaml = true)` and the `__InitializeXamlSourceGenComponent(...)` helper overloads.

## Related docs

- [Installation](../getting-started/installation/)
- [InitializeComponent and Loader Fallback](../getting-started/initializecomponent-and-loader-fallback/)
- [Avalonia Loader Migration and IL Weaving](avalonia-loader-il-weaving/)
- [Class-backed XAML and InitializeComponent Internals](../advanced/class-backed-xaml-and-initializecomponent/)
- [Package Selection and Integration](package-selection-and-integration/)
