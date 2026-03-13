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

## Recommended follow-up

After installing:

1. Build once so generated artifacts and diagnostics are available.
2. Confirm the app uses `.UseAvaloniaSourceGeneratedXaml()` before debugging runtime loading or hot reload.
3. Add or verify `x:DataType` on key views, templates, and themes.
4. Open the sample catalog if you want a feature-by-feature reference implementation.

## Related docs

- [Quickstart](quickstart/)
- [Package and Assembly](../reference/package-and-assembly/)
- [Package Catalog](../reference/package-catalog/)
