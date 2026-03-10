---
title: "Installation"
---

# Installation

AXSG ships multiple install surfaces because different users need different entry points.

## Choose the right artifact

### Application author

Use the umbrella package when you want the normal app-facing install path:

```xml
<PackageReference Include="XamlToCSharpGenerator" Version="0.1.0-alpha.3" />
```

This is the recommended starting point for Avalonia applications.

### Build/package integrator

If you need explicit control over the imported build layer, start from:

```xml
<PackageReference Include="XamlToCSharpGenerator.Build" Version="0.1.0-alpha.3" />
```

### Editor/tooling integration

Use the .NET tool when you need the standalone language server:

```bash
dotnet tool install --global XamlToCSharpGenerator.LanguageServer.Tool --version 0.1.0-alpha.3
```

Use the VS Code extension when you want the bundled editor experience instead of wiring the tool manually.

## Minimum project expectations

For the main Avalonia path, a project should have:

- the AXSG package reference
- an Avalonia application/runtime setup
- `x:DataType` on binding scopes where compiled binding semantics are required
- build enabled so generated files can be produced on the first pass

## Recommended follow-up

After installing:

1. Build once so generated artifacts and diagnostics are available.
2. Add or verify `x:DataType` on key views, templates, and themes.
3. Open the sample catalog if you want a feature-by-feature reference implementation.

## Related docs

- [Quickstart](quickstart)
- [Package and Assembly](../reference/package-and-assembly)
- [Package Catalog](../reference/package-catalog)
