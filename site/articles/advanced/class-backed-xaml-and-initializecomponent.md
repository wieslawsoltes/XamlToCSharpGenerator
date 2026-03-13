---
title: "Class-backed XAML and InitializeComponent Internals"
---

# Class-backed XAML and InitializeComponent Internals

This article explains how AXSG handles class-backed XAML, why it generates `InitializeComponent`, how that interacts with hand-written Avalonia code-behind, and why mixed-backend projects often keep a guarded `AvaloniaXamlLoader.Load(this)` fallback.

## Scope

This article is about class-backed documents such as:

- windows
- user controls
- class-backed resource dictionaries and themes
- other XAML documents that compile into a CLR partial type

It is not about runtime-only includes or non-class-backed resource fragments.

## Generated method shape

For class-backed XAML, AXSG emits:

```csharp
public void InitializeComponent(bool loadXaml = true)
```

The generated method is responsible for:

- populating the object graph from generated code
- rebinding named elements when loading is skipped
- registering hot reload or hot design hooks when those features are enabled

The important point is that the generated method is not a separate naming convention. It uses the same `InitializeComponent` name app authors already expect in Avalonia code-behind.

## Why constructor code usually stays the same

Most app constructors can remain:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

That call is fine because the generated method has an optional parameter. With no competing hand-written overload, `InitializeComponent();` naturally binds to the generated method.

## Where migrations go wrong

Older Avalonia code-behind often contains:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

That method is a different overload from AXSG's generated `InitializeComponent(bool loadXaml = true)`, but it is a better overload match for the parameterless constructor call.

So this constructor:

```csharp
public MainWindow()
{
    InitializeComponent();
}
```

will call the hand-written parameterless method, not the generated AXSG method.

That means:

- AXSG still generates code
- the project may still build
- but the generated initialization path is bypassed for that class

This is the key overload-resolution detail most integrations miss.

## Why `AXAML_SOURCEGEN_BACKEND` exists

AXSG's build integration defines the `AXAML_SOURCEGEN_BACKEND` conditional symbol when the project is using the AXSG backend. That lets a single code-behind file support both paths:

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

Under AXSG:

- the hand-written fallback is compiled out
- the constructor binds to the generated AXSG method

Under a non-AXSG backend:

- the hand-written fallback remains available
- the constructor binds to the classic Avalonia loader path

This pattern is why mixed-backend and multi-target repositories can migrate incrementally without splitting every code-behind file.

## When you can remove the fallback entirely

You can remove the hand-written `AvaloniaXamlLoader.Load(this)` method when all of the following are true:

- the project is sourcegen-only
- the target frameworks that matter all use AXSG
- you do not need to build the same code-behind on a non-AXSG path anymore

This is common in sample apps or fully migrated application repos.

## When you should keep the guarded fallback

Keep the guarded fallback when:

- shared libraries still build against non-AXSG paths
- some target frameworks use AXSG and others do not
- downstream consumers may still compile the same code against standard Avalonia loader behavior

This is the safer default for reusable libraries.

## Relationship to `AvaloniaXamlLoader`

`Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this)` is not inherently wrong. It is simply the non-AXSG initialization path.

The problem is not the method itself. The problem is leaving a parameterless `InitializeComponent()` wrapper around it in the same class when AXSG is active, because that steals the constructor call from the generated AXSG method.

## Relationship to `.UseAvaloniaSourceGeneratedXaml()`

These concerns operate at different layers:

- generated `InitializeComponent` handles class-backed object graph initialization
- `.UseAvaloniaSourceGeneratedXaml()` wires AXSG runtime services into `AppBuilder`

You still want both in a normal AXSG app:

1. generated class-backed initialization
2. AXSG runtime bootstrap on `AppBuilder`

Do not treat the generated `InitializeComponent` method as a replacement for runtime bootstrap.

## Behavior matrix

| Project shape | Hand-written fallback | Recommended state |
| --- | --- | --- |
| Sourcegen-only app | `private void InitializeComponent() { AvaloniaXamlLoader.Load(this); }` | remove it |
| Mixed-backend app | same fallback | wrap it in `#if !AXAML_SOURCEGEN_BACKEND` |
| Shared library with multiple TFMs/backends | same fallback | keep the guarded version |
| AXSG-enabled app with no manual fallback yet | none | leave constructor as `InitializeComponent();` |

## Common symptoms of a wrong setup

Typical signs that the fallback is still intercepting AXSG:

- generated code exists under `obj`, but the class behaves as if AXSG changes are ignored
- runtime behavior differs only for class-backed views that still have old code-behind
- a repo works on some TFMs and silently bypasses AXSG on others

## Recommended troubleshooting flow

1. Confirm the project sets `AvaloniaXamlCompilerBackend=SourceGen`.
2. Confirm the app uses `.UseAvaloniaSourceGeneratedXaml()`.
3. Inspect the code-behind file for a parameterless `InitializeComponent()`.
4. Inspect generated output under `obj/...` and confirm AXSG emitted `InitializeComponent(bool loadXaml = true)`.
5. If the project still needs non-AXSG support, switch the fallback to `#if !AXAML_SOURCEGEN_BACKEND`.

## Related docs

- [InitializeComponent and Loader Fallback](../getting-started/initializecomponent-and-loader-fallback/)
- [Installation](../getting-started/installation/)
- [Quickstart](../getting-started/quickstart/)
- [Package Selection and Integration](../guides/package-selection-and-integration/)
- [Runtime Loader and Fallback](../guides/runtime-loader-and-fallback/)
