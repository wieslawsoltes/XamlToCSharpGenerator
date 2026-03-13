---
title: "InitializeComponent and Loader Fallback"
---

# InitializeComponent and Loader Fallback

This page covers the most common migration mistake when enabling AXSG in an existing Avalonia app: leaving a hand-written `InitializeComponent()` method that still calls `AvaloniaXamlLoader.Load(this)`.

## The short rule

Keep constructor calls like:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

But do not keep an unconditional hand-written method like:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

when AXSG is enabled.

## Why

For class-backed XAML, AXSG generates an `InitializeComponent(bool loadXaml = true)` method in the generated partial class. That is the method expected to build the object graph from generated code.

If your code-behind also declares a parameterless `InitializeComponent()`, then the constructor call `InitializeComponent();` binds to the hand-written method, not the generated one. In that case AXSG output exists, but your view never uses it.

## What to do

### Sourcegen-only project

Remove the hand-written fallback method entirely:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

### Mixed-backend or multi-target project

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

This is the recommended portable pattern for libraries and apps that still need to build on both AXSG and non-AXSG paths.

## When the fallback is still useful

The guarded fallback remains appropriate when:

- the project still supports non-sourcegen Avalonia/XAML compiler paths
- some target frameworks use AXSG and others do not
- you are migrating incrementally and need one code-behind file to work in both modes

## What not to do

Avoid these patterns:

- unconditional `private void InitializeComponent() => AvaloniaXamlLoader.Load(this);`
- keeping both implementations and expecting the generated one to win
- removing the constructor call to `InitializeComponent();`

## Relationship to runtime bootstrap

This guidance is separate from `AppBuilder` bootstrap. You still need:

```csharp
.UseAvaloniaSourceGeneratedXaml()
```

in the app startup path. The generated `InitializeComponent` method and the AXSG runtime bootstrap solve different problems:

- generated `InitializeComponent` builds class-backed object graphs from generated code
- `.UseAvaloniaSourceGeneratedXaml()` enables the AXSG runtime loader, registries, and runtime integration surface

Both are part of the normal supported setup.

## Troubleshooting checklist

If a class-backed view seems to ignore AXSG output:

1. Check whether the project sets `AvaloniaXamlCompilerBackend=SourceGen`.
2. Check whether the app calls `.UseAvaloniaSourceGeneratedXaml()`.
3. Check whether the code-behind still declares an unconditional parameterless `InitializeComponent()`.
4. Inspect generated output under `obj/...` and confirm the class-backed partial includes AXSG-generated `InitializeComponent`.

## Related docs

- [Installation](installation/)
- [Quickstart](quickstart/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
- [Class-backed XAML and InitializeComponent Internals](../advanced/class-backed-xaml-and-initializecomponent/)
