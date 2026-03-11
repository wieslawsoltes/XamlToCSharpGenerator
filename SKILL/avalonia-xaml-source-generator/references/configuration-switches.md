# Configuration Switches

Load this file when the task is about AXSG MSBuild properties. Prefer the Avalonia-prefixed properties in app guidance. The generic `XamlSourceGen*` properties mirror the same concepts and are mainly useful for custom SDK or build integration work.

## Core app switches

| Property | Default | Use |
| --- | --- | --- |
| `AvaloniaXamlCompilerBackend` | `XamlIl` | Set to `SourceGen` to activate AXSG |
| `AvaloniaSourceGenCompilerEnabled` | `false` | Explicit master enable switch; implied by `AvaloniaXamlCompilerBackend=SourceGen` |
| `AvaloniaSourceGenUseCompiledBindingsByDefault` | `false` | Make bindings compiled by default when scopes support it |
| `AvaloniaSourceGenCSharpExpressionsEnabled` | `true` | Enable explicit C# expression bindings |
| `AvaloniaSourceGenImplicitCSharpExpressionsEnabled` | `true` | Enable implicit/shorthand C# expressions |
| `AvaloniaSourceGenCreateSourceInfo` | `false` | Emit source-info metadata useful for diagnostics/runtime inspection |
| `AvaloniaSourceGenStrictMode` | `false` | Tighten semantic/diagnostic behavior |

## Live-edit switches

| Property | Default | Use |
| --- | --- | --- |
| `AvaloniaSourceGenHotReloadEnabled` | `true` | Enable AXSG runtime hot reload integration |
| `AvaloniaSourceGenHotReloadErrorResilienceEnabled` | `true` | Keep last-known-good behavior when edits are transiently invalid |
| `AvaloniaSourceGenIdeHotReloadEnabled` | `true` | Enable IDE-driven hot reload coordination |
| `AvaloniaSourceGenHotDesignEnabled` | `false` | Enable hot design features |

## Diagnostics and metrics

| Property | Default | Use |
| --- | --- | --- |
| `AvaloniaSourceGenTracePasses` | `false` | Trace compiler passes for investigation |
| `AvaloniaSourceGenMetricsEnabled` | `false` | Emit compiler metrics |
| `AvaloniaSourceGenMetricsDetailed` | `false` | Emit more detailed metrics |

## Namespace, transform, and compatibility switches

| Property | Default | Use |
| --- | --- | --- |
| `AvaloniaSourceGenTransformRules` | empty | Supply transform-rule files |
| `AvaloniaSourceGenConfigurationPrecedence` | empty | Control configuration source precedence |
| `AvaloniaSourceGenAllowImplicitXmlnsDeclaration` | `false` | Allow implicit xmlns declaration behavior |
| `AvaloniaSourceGenImplicitStandardXmlnsPrefixesEnabled` | `true` | Enable implicit standard xmlns prefixes |
| `AvaloniaSourceGenImplicitDefaultXmlns` | `https://github.com/avaloniaui` | Override the implicit default xmlns |
| `AvaloniaSourceGenInferClassFromPath` | `false` | Infer class names from file paths |
| `AvaloniaSourceGenImplicitProjectNamespacesEnabled` | `false` | Enable project namespace inference |
| `AvaloniaSourceGenGlobalXmlnsPrefixes` | empty | Add global xmlns prefix mappings |
| `AvaloniaSourceGenMarkupParserLegacyInvalidNamedArgumentFallbackEnabled` | `false` | Enable legacy parser fallback behavior |
| `AvaloniaSourceGenTypeResolutionCompatibilityFallbackEnabled` | `false` | Enable compatibility fallback for type resolution |

## SDK and build integrator switches

| Property | Default | Use |
| --- | --- | --- |
| `XamlSourceGenBackend` | mirrors Avalonia backend | Framework-neutral alias for backend selection |
| `XamlSourceGenEnabled` | mirrors Avalonia enable switch | Framework-neutral alias for compiler enablement |
| `XamlSourceGenInputItemGroup` | `AvaloniaXaml` | Input item group for XAML files |
| `XamlSourceGenAdditionalFilesSourceItemGroup` | `AvaloniaXaml` | Item group projected into Roslyn `AdditionalFiles` |
| `XamlSourceGenTransformRules` | mirrors Avalonia transform rules | Framework-neutral alias for transform rules |
| `XamlSourceGenTransformRuleItemGroup` | `AvaloniaSourceGenTransformRule` | Item group used for transform rule files |
| `XamlSourceGenConfigurationPrecedence` | mirrors Avalonia precedence | Framework-neutral alias for precedence control |

## iOS and remote hot reload switches

Only surface these when the task explicitly targets iOS or remote-device hot reload.

| Property | Default | Use |
| --- | --- | --- |
| `AvaloniaSourceGenIosHotReloadEnabled` | `true` for Debug iOS targets, otherwise `false` | Enable AXSG iOS hot reload wiring |
| `AvaloniaSourceGenIosHotReloadUseInterpreter` | `true` when iOS hot reload is enabled, otherwise `false` | Turn on interpreter support required by the iOS hot reload path |
| `AvaloniaSourceGenIosHotReloadEnableStartupHookSupport` | `false` | Enable startup-hook support for iOS hot reload |
| `AvaloniaSourceGenIosHotReloadForwardWatchEnvironment` | `true` | Forward watch environment variables |
| `AvaloniaSourceGenIosHotReloadForwardStartupHooks` | `false` | Forward startup hooks |
| `AvaloniaSourceGenIosHotReloadForwardModifiableAssemblies` | `false` | Forward modifiable assemblies |
| `AvaloniaSourceGenIosHotReloadStartupBannerEnabled` | `true` | Show startup banner |
| `AvaloniaSourceGenIosHotReloadTransportMode` | `Auto` | Choose transport mode |
| `AvaloniaSourceGenIosHotReloadHandshakeTimeoutMs` | `3000` | Control handshake timeout |
| `AvaloniaSourceGenHotReloadRemoteEndpoint` | empty | Set explicit remote endpoint |
| `AvaloniaSourceGenHotReloadRemotePort` | `45820` | Set remote port |
| `AvaloniaSourceGenHotReloadRemoteAutoSimulatorEndpointEnabled` | `false` | Auto-configure simulator endpoint |
| `AvaloniaSourceGenHotReloadRemoteRequireExplicitDeviceEndpoint` | `true` | Require explicit device endpoint |
| `AvaloniaSourceGenIosDotNetWatchXamlBuildTriggersEnabled` | `false` | Enable dotnet-watch XAML triggers on iOS |
| `AvaloniaSourceGenIosDotNetWatchProxyProjectPath` | empty | Override proxy project path |
| `AvaloniaSourceGenIosDotNetWatchProxyPath` | empty | Override proxy executable path |

## Maintenance note

If the host repository includes AXSG build props and targets, re-check the current public property surface there before editing this table.
