---
title: "Configuration Model"
---

# Source Generator Configuration Model

This document describes the unified configuration model used by `XamlToCSharpGenerator`.

## Sources and precedence

Configuration is merged from these sources:

1. Built-in defaults.
2. File source (`xaml-sourcegen.config.json` in project root, and explicit AdditionalFiles with the same file name).
3. MSBuild source (`build_property.*` options).
4. Code source (`[assembly: AssemblyMetadata(...)]`).

Default precedence values:

| Source | Default precedence |
|---|---:|
| Project-default config file | 90 |
| Explicit config file AdditionalFiles | 100 |
| MSBuild | 200 |
| Code | 300 |

Higher precedence wins when multiple sources set the same value.

### Overriding source precedence

Set `XamlSourceGenConfigurationPrecedence` (or compatibility alias `AvaloniaSourceGenConfigurationPrecedence`) in MSBuild:

```xml
<PropertyGroup>
  <XamlSourceGenConfigurationPrecedence>ProjectDefaultFile=90;File=100;MsBuild=200;Code=300</XamlSourceGenConfigurationPrecedence>
</PropertyGroup>
```

Supported keys:

- `ProjectDefaultFile`
- `File`
- `MsBuild`
- `Code`

Aliases for `ProjectDefaultFile` are also accepted: `ProjectDefault`, `DefaultFile`.

Invalid segments produce warning `AXSG0933`.

## Canonical file

File name:

- `xaml-sourcegen.config.json`

The parser accepts JSON comments and trailing commas.

`schemaVersion` is optional; when specified it must currently be `1` (otherwise warning `AXSG0917`).

## Important Avalonia build switches outside `xaml-sourcegen.config.json`

Some settings that users commonly look for are not part of the JSON configuration file model at all. They are project-level MSBuild properties or conditional compilation symbols.

The most important Avalonia-facing switches are:

| Switch | Kind | Meaning |
|---|---|---|
| `AvaloniaXamlCompilerBackend` | MSBuild property | Selects the active XAML compiler backend. Use `SourceGen` for AXSG and `XamlIl` for the classic Avalonia path. |
| `AvaloniaSourceGenCompilerEnabled` | MSBuild property | Explicit AXSG enable switch. Usually implied by `AvaloniaXamlCompilerBackend=SourceGen`. |
| `EnableAvaloniaXamlCompilation` | MSBuild property | Controls Avalonia's XamlIl compilation path. AXSG build integration disables it when SourceGen is active. |
| `AvaloniaNameGeneratorIsEnabled` | MSBuild property | Controls Avalonia's legacy name generator path. This is not the AXSG-generated `InitializeComponent` switch. |
| `AXAML_SOURCEGEN_BACKEND` | compilation symbol | Defined when AXSG is the active backend. Use it to guard fallback `AvaloniaXamlLoader.Load(this)` code. |
| `XamlSourceGenIlWeavingEnabled` / `AvaloniaSourceGenIlWeavingEnabled` | MSBuild property | Enables AXSG post-compile rewriting of supported `AvaloniaXamlLoader.Load(...)` call sites to generated AXSG initializer helpers. |
| `XamlSourceGenIlWeavingStrict` / `AvaloniaSourceGenIlWeavingStrict` | MSBuild property | Fails the build when AXSG matches a supported loader call on a source-generated type but cannot find the generated initializer helper to rewrite to. |
| `XamlSourceGenIlWeavingVerbose` / `AvaloniaSourceGenIlWeavingVerbose` | MSBuild property | Emits inspection, match, and rewrite counts for the weaving pass. |
| `XamlSourceGenIlWeavingBackend` / `AvaloniaSourceGenIlWeavingBackend` | MSBuild property | Selects the IL-weaving scan backend. `Metadata` uses `System.Reflection.Metadata` and `Cecil` keeps the legacy Mono.Cecil scan path. |

For normal AXSG app configuration, do not try to express those as JSON config-file keys. Set them in the project file instead.

For a full explanation of the name-generator and `InitializeComponent` compatibility matrix, see:

- [Avalonia Name Generator and InitializeComponent Modes](../guides/avalonia-name-generator-and-initializecomponent-modes/)
- [Avalonia Loader Migration and IL Weaving](../guides/avalonia-loader-il-weaving/)

## Top-level schema

Top-level sections:

- `build`
- `parser`
- `semanticContract`
- `binding`
- `emitter`
- `transform`
- `diagnostics`
- `frameworkExtras`

Unknown keys are ignored. Known keys with invalid value shape produce `AXSG0918`-`AXSG0927` warnings.

## Section reference

### `build`

| Key | Type |
|---|---|
| `isEnabled` | bool |
| `backend` | string |
| `strictMode` | bool |
| `hotReloadEnabled` | bool |
| `hotReloadErrorResilienceEnabled` | bool |
| `ideHotReloadEnabled` | bool |
| `hotDesignEnabled` | bool |
| `iosHotReloadEnabled` | bool |
| `iosHotReloadUseInterpreter` | bool |
| `dotNetWatchBuild` | bool |
| `buildingInsideVisualStudio` | bool |
| `buildingByReSharper` | bool |
| `additionalProperties` | object<string,string> |

### `parser`

| Key | Type |
|---|---|
| `allowImplicitXmlnsDeclaration` | bool |
| `implicitStandardXmlnsPrefixesEnabled` | bool |
| `implicitDefaultXmlns` | string |
| `inferClassFromPath` | bool |
| `implicitProjectNamespacesEnabled` | bool |
| `globalXmlnsPrefixes` | object<string,string> |
| `additionalProperties` | object<string,string> |

### `semanticContract`

| Key | Type |
|---|---|
| `typeContracts` | object<string,string> |
| `propertyContracts` | object<string,string> |
| `eventContracts` | object<string,string> |
| `additionalProperties` | object<string,string> |

### `binding`

| Key | Type |
|---|---|
| `useCompiledBindingsByDefault` | bool |
| `cSharpExpressionsEnabled` | bool |
| `implicitCSharpExpressionsEnabled` | bool |
| `markupParserLegacyInvalidNamedArgumentFallbackEnabled` | bool |
| `typeResolutionCompatibilityFallbackEnabled` | bool |
| `additionalProperties` | object<string,string> |

### `emitter`

| Key | Type |
|---|---|
| `createSourceInfo` | bool |
| `tracePasses` | bool |
| `metricsEnabled` | bool |
| `metricsDetailed` | bool |
| `additionalProperties` | object<string,string> |

### `transform`

| Key | Type |
|---|---|
| `rawTransformDocuments` | object<string,string> |
| `additionalProperties` | object<string,string> |

### `diagnostics`

| Key | Type |
|---|---|
| `treatWarningsAsErrors` | bool |
| `severityOverrides` | object<string,`Info\|Warning\|Error`\|null> |
| `additionalProperties` | object<string,string> |

### `frameworkExtras`

| Key | Type |
|---|---|
| `sections` | object<sectionName, object<string,string>> |
| `additionalProperties` | object<string,string> |

## File example

```json
{
  "schemaVersion": 1,
  "build": {
    "isEnabled": true,
    "backend": "SourceGen",
    "strictMode": true
  },
  "parser": {
    "allowImplicitXmlnsDeclaration": true,
    "implicitDefaultXmlns": "https://github.com/avaloniaui",
    "globalXmlnsPrefixes": {
      "vm": "using:Demo.ViewModels"
    }
  },
  "binding": {
    "useCompiledBindingsByDefault": true
  },
  "diagnostics": {
    "severityOverrides": {
      "AXSG0113": "Info"
    }
  }
}
```

## Code-based configuration

Use assembly metadata keys with prefix `XamlSourceGen.`:

```csharp
using System.Reflection;

[assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
[assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]
[assembly: AssemblyMetadata("XamlSourceGen.Binding.UseCompiledBindingsByDefault", "true")]
[assembly: AssemblyMetadata("XamlSourceGen.Parser.GlobalXmlnsPrefixes.vm", "using:Demo.ViewModels")]
```

Section/key names are case-insensitive. Invalid keys/values produce warnings `AXSG0930`-`AXSG0932`.
