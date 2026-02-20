# Event Binding Implementation Report

## Summary
Implemented first-class SourceGen event bindings for Avalonia AXAML via `{EventBinding ...}` syntax with binder/emitter/runtime support, diagnostics, tests, and catalog sample coverage.

## Delivered

### 1) Core model extensions
Added event-binding metadata models and wired them into event subscriptions:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedEventBindingDefinition.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Core/Models/ResolvedEventSubscription.cs`

### 2) Binder support (`AvaloniaSemanticBinder`)
Implemented EventBinding parsing/validation for event attributes:
- Recognizes `{EventBinding ...}` and `x:EventBinding`.
- Supports `Command`/`Path`, `Method`, `Parameter`/`CommandParameter`, `PassEventArgs`, `Source`.
- Supports command/parameter path extraction from `Binding` / `CompiledBinding` tokens.
- Generates deterministic wrapper method names.
- Preserves existing handler syntax (`Click="OnClick"`) unchanged.

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`

### 3) Emitter support (`AvaloniaCodeEmitter`)
Implemented generated wrapper method emission and event wiring:
- Collects event-binding definitions from object graph.
- Emits instance wrapper methods per EventBinding.
- Keeps idempotent `-=` / `+=` event rewiring using generated wrapper names.
- Wrapper methods call runtime dispatcher.

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

### 4) Runtime dispatcher
Added runtime dispatcher for command/method invocation:
- Source resolution modes: `DataContext`, `Root`, `DataContextThenRoot`.
- Command execution with `CanExecute`.
- Method invocation with best-match argument binding.
- Parameter resolution from path/literal/event args.
- Non-throwing failure semantics for runtime resilience.

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/src/XamlToCSharpGenerator.Runtime/SourceGenEventBindingRuntime.cs`

### 5) Tests
#### Generator tests
Added:
- command EventBinding emission and wrapper assertions,
- method EventBinding emission assertions,
- invalid shape diagnostic assertions.

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

#### Runtime tests
Added:
- command invocation,
- parameter-path resolution,
- root-source method invocation,
- pass-event-args method invocation.

File:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenEventBindingRuntimeTests.cs`

### 6) Catalog sample integration
Added a dedicated Event Bindings tab/page with examples:
- command binding,
- command parameter binding,
- method binding,
- pass-event-args,
- root source mode.

Files:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Pages/EventBindingsPage.axaml`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Pages/EventBindingsPage.axaml.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/ViewModels/EventBindingsPageViewModel.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/MainWindow.axaml`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/ViewModels/MainWindowViewModel.cs`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/Infrastructure/RelayCommand.cs`

### 7) Docs
Updated user docs with EventBinding syntax and options:
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/README.md`
- `/Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/README.md`

## Validation
Executed successfully:
1. Focused generator/runtime tests:
- `dotnet test /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj -v minimal --filter "FullyQualifiedName~Generates_EventBinding_Command_Wrapper_And_Subscription|FullyQualifiedName~Generates_EventBinding_Method_Wrapper_And_Subscription|FullyQualifiedName~Reports_Diagnostic_For_EventBinding_With_Both_Command_And_Method|FullyQualifiedName~SourceGenEventBindingRuntimeTests"`

2. Catalog sample build:
- `dotnet build /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj -v minimal`

3. Catalog runtime smoke run:
- `dotnet run --project /Users/wieslawsoltes/GitHub/XamlToCSharpGenerator/samples/SourceGenXamlCatalogSample/SourceGenXamlCatalogSample.csproj`
- startup validated without unhandled exception.

## Notes
- Existing plain event handler syntax remains fully supported.
- EventBinding currently emits warnings for invalid shapes using existing `AXSG0600` event diagnostic band.
