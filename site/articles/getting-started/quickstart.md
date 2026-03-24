---
title: "Quickstart"
---

# Quickstart

This is the shortest path from an Avalonia project to generated AXSG output. It assumes you want the standard application-facing integration path, not a custom SDK or tooling-host composition.

## 1. Add the package

For most applications:

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="x.y.z" />
```

If your repo already has custom SDK logic, generator composition rules, or a non-standard runtime host, stop here and read [Package Selection and Integration](../guides/package-selection-and-integration/). The umbrella package is the right default for app projects, but it is not the only supported integration model.

## 2. Enable the SourceGen backend

AXSG does not silently replace Avalonia's default backend. Opt into the generated compiler path explicitly:

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
</PropertyGroup>
```

If you need a custom project item group, set `XamlSourceGenInputItemGroup` and mirror your `@(AvaloniaXaml)` items into that group. Do not override `XamlSourceGenAdditionalFilesSourceItemGroup` for Avalonia applications; AXSG always projects Avalonia XAML into `AdditionalFiles` as `AvaloniaXaml`.

## 3. Enable the runtime bootstrap

Register the AXSG runtime loader on `AppBuilder`:

```csharp
using XamlToCSharpGenerator.Runtime;

public static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvaloniaSourceGeneratedXaml();
```

## 4. Fix class-backed `InitializeComponent` patterns

If a class-backed view or theme already has:

```csharp
private void InitializeComponent()
{
    global::Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}
```

AXSG generates `InitializeComponent(bool loadXaml = true)` for class-backed XAML, and the hand-written parameterless overload will intercept `InitializeComponent();` calls in constructors.

Use one of these approaches:

- remove the manual method in sourcegen-only projects
- keep the supported loader call and rely on AXSG IL weaving during migration
- wrap it in `#if !AXAML_SOURCEGEN_BACKEND` for mixed-backend or multi-target projects

The full behavior, including overload resolution and build constants, is documented in:

- [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)

## 5. Annotate binding scopes

Add `x:DataType` to views, templates, and themes where you want compiled binding semantics:

```xml
<UserControl
    x:Class="MyApp.Views.MainView"
    x:DataType="vm:MainViewModel">
```

Do this consistently in:

- root windows and views
- `DataTemplate` scopes
- control themes and templates
- resource dictionaries that contain compiled-binding-capable content

AXSG can still compile some scenarios without `x:DataType`, but that is no longer the strongest validation path. The best diagnostics, completion, navigation, and inlay-hint behavior all assume explicit type scopes where the XAML feature allows them.

## 6. Build the project

Run a normal build first:

```bash
dotnet build
```

This lets AXSG generate code, surface diagnostics, and register the runtime artifacts used by later tooling.

After the first successful build, inspect `obj/<Configuration>/<TFM>/` once. AXSG is intentionally inspectable. Confirming that generated partials and helper code exist is the fastest way to separate package/build issues from authored-XAML issues.

## 7. Verify generated behavior

At this point you should be able to:

- inspect generated code if you need to debug output shape
- get binding diagnostics at build time
- use the language service in supported editors
- exercise runtime features such as hot reload if your host is configured for them

If any of those are missing, move to [Troubleshooting](../guides/troubleshooting/) before adding more features. Most downstream editor and hot-reload problems are much easier to diagnose once the base compiler/runtime path is known-good.

## 8. Explore feature areas

Once the basic path works, use the sample catalog to validate more advanced features:

- compiled bindings, x:Bind, and shorthand expressions
- inline C# and event code
- selectors, control themes, and resource includes
- language-service navigation and refactorings

## 9. Validate one feature per layer

Before migrating a larger application, prove one feature from each layer:

- one compiled binding
- one theme/resource/include scenario
- one editor workflow such as completion or definition
- one runtime workflow such as hot reload or runtime-loader fallback

That confirms the compiler, runtime, and tooling surfaces are all present and aligned.

## Expected output shape

In a healthy AXSG setup you should see:

- generated XAML-backed partial classes
- generated `InitializeComponent` methods for class-backed documents
- helper methods or descriptors for complex lowering paths
- runtime registration artifacts for source info, URIs, or hot reload
- editor features that match compiler semantics instead of inventing a separate model

If the generated output shape looks wrong, start with the build/package docs before assuming the XAML syntax is at fault.

## Where to go next

- [Samples and Feature Tour](samples-and-feature-tour/)
- [InitializeComponent and Loader Fallback](initializecomponent-and-loader-fallback/)
- [XAML feature docs](../xaml/)
- [Compiler Configuration and Transform Rules](../advanced/compiler-configuration-and-transform-rules/)
- [VS Code and Language Service](../guides/vscode-language-service/)
