# Routed Event Transform Parity Closure Report

Date: 2026-02-19

## Scope
Close the remaining routed-event hookup gap between SourceGen and Avalonia XamlIl:
1. Support routed-event field assignment (`Foo="Handler"` resolving `FooEvent` static field) when no CLR event wrapper is present.
2. Emit deterministic rewire-safe generated code for routed events.
3. Validate handler delegate compatibility up front and report deterministic diagnostics.

## Implementation
1. Extended event subscription semantic model:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedEventSubscription.cs`
   - Added subscription kind metadata and routed-event owner/field/delegate type payload.
2. Updated binder event resolution:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
   - Added routed-event field discovery (`FooEvent`) across base types.
   - Added routed-event delegate type inference (`EventHandler<TEventArgs>` fallback).
   - Added delegate-compatibility validation for both CLR and routed events.
   - Added explicit invalid-routed-event-field diagnostics (`FooEvent` exists but is not `RoutedEvent`-compatible).
   - Added deterministic `AXSG0600` diagnostics for incompatible handlers or missing class-backed root handlers.
3. Updated emitter wiring:
   - `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
   - CLR events keep `-=`/`+=` rewiring.
   - Routed events now emit `RemoveHandler`/`AddHandler` using resolved `FooEvent` field and typed delegate cast.

## Test Coverage
1. Added routed-event success path test:
   - `Generates_Routed_Event_Field_Subscription_Using_AddHandler`
2. Added CLR-event delegate compatibility diagnostic test:
   - `Reports_Diagnostic_For_Incompatible_Clr_Event_Handler_Signature`
3. Added routed-event invalid-handler diagnostic test:
   - `Reports_Diagnostic_For_Incompatible_Routed_Event_Handler_Signature`
4. Added invalid routed-event-field diagnostic test:
   - `Reports_Diagnostic_For_Invalid_Routed_Event_Field_Definition`
5. Existing CLR event tests remain green.

File:
`/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

## Validation
1. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --filter "FullyQualifiedName~AvaloniaXamlSourceGeneratorTests"`
   - Passed.
2. `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
   - Passed (`143` total, `0` failed, `1` skipped perf lane).

## Status Impact
1. Parity matrix row updated:
   - Routed event hookup moved from `Partial` to `Implemented`.
2. Evidence dashboard updated with routed-event-specific tests.
