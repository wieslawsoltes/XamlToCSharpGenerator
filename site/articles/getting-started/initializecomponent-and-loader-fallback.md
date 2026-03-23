---
title: "InitializeComponent and Loader Fallback"
---

# InitializeComponent and Loader Fallback

This page covers the most common migration issue when enabling AXSG in an existing Avalonia app: deciding what to do with hand-written `InitializeComponent()` methods that still call `AvaloniaXamlLoader.Load(...)`.

That rule includes `App.axaml.cs`. The application root is a class-backed XAML type too, so `App` must follow the same AXSG migration rules as any other class-backed AXAML file.

## The short rule

Keep constructor calls like:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

AXSG now supports two migration paths:

1. remove the hand-written `AvaloniaXamlLoader` wrapper entirely
2. keep supported legacy loader calls and let AXSG IL-weave them to generated initializer helpers during the build

The clean long-term shape is still to remove the wrapper, but the common loader forms no longer require immediate source edits when IL weaving is enabled.

Without IL weaving, or when the call shape is unsupported, an unconditional hand-written method like:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

still intercepts the constructor call and bypasses AXSG's generated parameterized `InitializeComponent`.

Also do not treat `AvaloniaNameGeneratorIsEnabled` as the AXSG switch. That property belongs to the legacy Avalonia path.

## Migration choices

### 1) Clean AXSG source shape

Remove the hand-written fallback method entirely:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

That includes the application root:

```csharp
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
}
```

### 2) Compatibility bridge with AXSG IL weaving

Keep the common legacy wrapper temporarily:

```csharp
public MainWindow()
{
    InitializeComponent();
}

private void InitializeComponent()
{
    AvaloniaXamlLoader.Load(this);
}
```

When `XamlSourceGenIlWeavingEnabled=true` and the build is using AXSG, that `Load(this)` call is rewritten after compile to the AXSG-generated initializer helper for the same type.

AXSG also supports:

```csharp
AvaloniaXamlLoader.Load(serviceProvider, this);
```

### 3) Mixed-backend or multi-target project

Keep the fallback only when AXSG is not the active backend:

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

This is still the recommended portable pattern for libraries and apps that truly build in both AXSG and non-AXSG modes.

## Why the old problem still matters

For class-backed XAML, AXSG generates an `InitializeComponent(bool loadXaml = true)` method in the generated partial class. That is the method expected to build the object graph from generated code.

If your code-behind also declares a parameterless `InitializeComponent()`, then the constructor call `InitializeComponent();` binds to the hand-written method, not the generated one. In that case AXSG output exists, but your view never uses it.

AXSG IL weaving changes that story for supported legacy loader calls because the wrapper body is rewritten after compile to AXSG's generated initializer helper instead of calling Avalonia's runtime loader directly.

For the full backend and name-generator matrix, use:

- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)

## Supported woven call shapes

AXSG's weaving pass is intentionally narrow. It supports:

- direct `AvaloniaXamlLoader.Load(this)`
- direct `AvaloniaXamlLoader.Load(serviceProvider, this)`
- imported or fully-qualified source spellings that compile to those call shapes

It does not rewrite arbitrary object-loader calls such as:

- `AvaloniaXamlLoader.Load(otherInstance)`
- `var self = this; AvaloniaXamlLoader.Load(self)`

## What not to do

Avoid these assumptions:

- assuming AXSG removes `AvaloniaXamlLoader` from Avalonia packages
- assuming every possible `AvaloniaXamlLoader` call is rewritten
- disabling IL weaving and expecting the old wrapper to keep using AXSG automatically
- removing the constructor call to `InitializeComponent();`

## Relationship to runtime bootstrap

This guidance is separate from `AppBuilder` bootstrap. You still need:

```csharp
.UseAvaloniaSourceGeneratedXaml()
```

in the app startup path. The generated `InitializeComponent` method, the IL weaving bridge, and the AXSG runtime bootstrap solve different problems:

- generated `InitializeComponent` builds class-backed object graphs from generated code
- IL weaving redirects supported legacy loader call sites to the generated initialization path
- `.UseAvaloniaSourceGeneratedXaml()` enables the AXSG runtime loader, registries, and runtime integration surface

All are part of the normal supported setup.

## Troubleshooting checklist

If a class-backed view or `App.axaml.cs` seems to ignore AXSG output:

1. Check whether the project sets `AvaloniaXamlCompilerBackend=SourceGen`.
2. Check whether the app calls `.UseAvaloniaSourceGeneratedXaml()`.
3. Check whether `XamlSourceGenIlWeavingEnabled` is `true` when you expect the migration bridge to apply.
4. Check whether the code-behind, including `App.axaml.cs`, uses a supported loader call shape.
5. Check whether the project is forcing `AvaloniaNameGeneratorIsEnabled=true`.
6. Inspect the build output for the AXSG IL weaving log or inspect generated output under `obj/...` and confirm the class-backed partial includes AXSG-generated initializer helpers.

## Related docs

- [Installation](installation/)
- [Quickstart](quickstart/)
- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
- [Class-backed XAML and InitializeComponent Internals](../advanced/class-backed-xaml-and-initializecomponent/)
