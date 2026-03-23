---
title: "Configuration Migration"
---

# Configuration Migration Guide

This guide maps legacy configuration surface (`AvaloniaSourceGen*`, transform-rule properties/items) to the unified configuration model.

See the full schema in [Configuration Model](configuration-model/).

## What stays compatible

These remain supported:

1. Existing `AvaloniaSourceGen*` MSBuild properties.
2. `XamlSourceGen*` MSBuild properties.
3. Transform rule files via `AvaloniaSourceGenTransformRules` and `XamlSourceGenTransformRules`.
4. Existing source-item-group transform rule inputs.

The unified model layers on top of this compatibility surface.

## Legacy MSBuild to unified section mapping

| Legacy property | Unified key |
|---|---|
| `XamlSourceGenEnabled` / `AvaloniaSourceGenCompilerEnabled` | `build.isEnabled` |
| `XamlSourceGenBackend` / `AvaloniaXamlCompilerBackend` | `build.backend` |
| `AvaloniaSourceGenStrictMode` | `build.strictMode` |
| `AvaloniaSourceGenHotReloadEnabled` | `build.hotReloadEnabled` |
| `AvaloniaSourceGenHotReloadErrorResilienceEnabled` | `build.hotReloadErrorResilienceEnabled` |
| `AvaloniaSourceGenIdeHotReloadEnabled` | `build.ideHotReloadEnabled` |
| `AvaloniaSourceGenHotDesignEnabled` | `build.hotDesignEnabled` |
| `AvaloniaSourceGenIosHotReloadEnabled` | `build.iosHotReloadEnabled` |
| `AvaloniaSourceGenIosHotReloadUseInterpreter` | `build.iosHotReloadUseInterpreter` |
| `DotNetWatchBuild` | `build.dotNetWatchBuild` |
| `BuildingInsideVisualStudio` | `build.buildingInsideVisualStudio` |
| `BuildingByReSharper` | `build.buildingByReSharper` |
| `AvaloniaSourceGenAllowImplicitXmlnsDeclaration` | `parser.allowImplicitXmlnsDeclaration` |
| `AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled` | `parser.implicitStandardXmlnsPrefixesEnabled` |
| `AvaloniaSourceGenImplicitDefaultXmlns` | `parser.implicitDefaultXmlns` |
| `AvaloniaSourceGenInferClassFromPath` | `parser.inferClassFromPath` |
| `AvaloniaSourceGenImplicitProjectNamespacesEnabled` | `parser.implicitProjectNamespacesEnabled` |
| `AvaloniaSourceGenGlobalXmlnsPrefixes` | `parser.globalXmlnsPrefixes` |
| `AvaloniaSourceGenUseCompiledBindingsByDefault` | `binding.useCompiledBindingsByDefault` |
| `AvaloniaSourceGenCSharpExpressionsEnabled` | `binding.cSharpExpressionsEnabled` |
| `AvaloniaSourceGenImplicitCSharpExpressionsEnabled` | `binding.implicitCSharpExpressionsEnabled` |
| `AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled` | `binding.markupParserLegacyInvalidNamedArgumentFallbackEnabled` |
| `AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled` | `binding.typeResolutionCompatibilityFallbackEnabled` |
| `AvaloniaSourceGenCreateSourceInfo` | `emitter.createSourceInfo` |
| `AvaloniaSourceGenTracePasses` | `emitter.tracePasses` |
| `AvaloniaSourceGenMetricsEnabled` | `emitter.metricsEnabled` |
| `AvaloniaSourceGenMetricsDetailed` | `emitter.metricsDetailed` |

`AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled`, `AvaloniaSourceGenIosDotNetWatchXamlBuildTriggersEnabled`, `XamlSourceGenIlWeavingEnabled`, `XamlSourceGenIlWeavingStrict`, `XamlSourceGenIlWeavingVerbose`, and their `AvaloniaSourceGenIlWeaving*` aliases remain MSBuild-only build integration switches. They intentionally do not map to unified configuration-file keys because they control build host behavior rather than compiler configuration-file semantics.

## Transform-rule migration

Legacy rule files still work:

```xml
<PropertyGroup>
  <AvaloniaSourceGenTransformRules>transform-rules.json</AvaloniaSourceGenTransformRules>
</PropertyGroup>
```

Unified configuration representation:

```json
{
  "schemaVersion": 1,
  "transform": {
    "rawTransformDocuments": {
      "inline-rules.json": "{ \"typeAliases\": [ ... ], \"propertyAliases\": [ ... ] }"
    }
  }
}
```

Merge order for transform rules:

1. Legacy rule files (`AvaloniaSourceGenTransformRules` / `XamlSourceGenTransformRules` and item-group sources).
2. Unified `transform.rawTransformDocuments`.
3. Unified typed transform object (`transform.configuration`, when provided internally).

When the same alias key exists in multiple layers, the later layer wins.

## Mode examples

### 1) MSBuild-only

```xml
<PropertyGroup>
  <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
  <AvaloniaSourceGenCompilerEnabled>true</AvaloniaSourceGenCompilerEnabled>
  <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>
</PropertyGroup>
```

### 2) File-only

Create `<ProjectDir>/xaml-sourcegen.config.json`:

```json
{
  "schemaVersion": 1,
  "build": {
    "isEnabled": true,
    "backend": "SourceGen"
  },
  "binding": {
    "useCompiledBindingsByDefault": true
  }
}
```

### 3) Code-only

```csharp
using System.Reflection;

[assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
[assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]
[assembly: AssemblyMetadata("XamlSourceGen.Binding.UseCompiledBindingsByDefault", "true")]
```

### 4) Mixed mode with precedence override

```xml
<PropertyGroup>
  <XamlSourceGenConfigurationPrecedence>ProjectDefaultFile=80;MsBuild=200;Code=300;File=400</XamlSourceGenConfigurationPrecedence>
</PropertyGroup>
```

This example gives explicit config files highest precedence, so file values can override code values.

## Recommended migration path

1. Keep existing MSBuild properties first.
2. Introduce `xaml-sourcegen.config.json` for values that should be source-controlled as a single config document.
3. Move specialized per-assembly behavior to code metadata keys only when needed.
4. Add precedence override only when you intentionally need non-default layering.
