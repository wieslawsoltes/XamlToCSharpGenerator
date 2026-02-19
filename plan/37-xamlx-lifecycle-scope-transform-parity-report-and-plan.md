# XamlX Lifecycle/Scope/Transform Parity Report And Execution Plan (Wave 7)

## Context
This report compares Avalonia's XamlX IL pipeline with the current source-generator C# backend, focusing on lifecycle semantics (`BeginInit`/`EndInit`), scope/name registration, service-provider constructor injection, and transform ordering.

Primary reference points analyzed:
- `external/XamlX/src/XamlX/IL/Emitters/ObjectInitializationNodeEmitter.cs`
- `external/XamlX/src/XamlX/Transform/Transformers/TopDownInitializationTransformer.cs`
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs`
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AddNameScopeRegistration.cs`
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlRootObjectScopeTransformer.cs`
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/Transformers/AvaloniaXamlIlConstructorServiceProviderTransformer.cs`
- `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlLanguage.cs`
- `tests/Avalonia.Markup.Xaml.UnitTests/Xaml/BasicTests.cs`
- `external/XamlX/tests/XamlParserTests/InitializationTests.cs`

## Avalonia/XamlX Ground Truth

## Transform pipeline shape
Avalonia extends XamlX with an ordered chain in `AvaloniaXamlIlCompiler`.
Critical lifecycle-affecting transformers/emitters in that chain are:
- `TopDownInitializationTransformer` (from XamlX imperative compiler path)
- `AddNameScopeRegistration`
- `AvaloniaXamlIlRootObjectScope`
- `AvaloniaXamlIlConstructorServiceProviderTransformer`
- `DeferredContentTransformer` + Avalonia deferred customizations
- `ObjectInitializationNodeEmitter`

## Lifecycle semantics in IL
`ObjectInitializationNodeEmitter` enforces:
- call `BeginInit` for `ISupportInitialize` nodes
- execute object manipulation/assignments/children
- call `EndInit` after manipulations

`TopDownInitializationTransformer` rewrites assignment/argument nodes for types marked with `UsableDuringInitializationAttribute`:
- object is `BeginInit`'d
- attached to parent early
- remaining initialization executes
- object ends with `EndInit`

## Scope semantics in IL
`AddNameScopeRegistration` and `AvaloniaNameScopeRegistrationXamlIlNodeEmitter`:
- register names as objects are constructed

`AvaloniaXamlIlRootObjectScope.Emitter`:
- set root namescope on styled root
- call `INameScope.Complete()` on root scope

## Service provider semantics in IL
`AvaloniaXamlIlConstructorServiceProviderTransformer`:
- when no public parameterless constructor exists and public `(IServiceProvider)` exists, inject SP ctor argument

## Deferred/template semantics
Deferred content uses runtime helper factory (`DeferredTransformationFactoryV3`) with scoped service provider + namescope behavior.

## SourceGen Gap Audit (Before This Wave)
- Missing explicit object lifecycle calls around generated object graph nodes.
- Missing top-down early attach behavior for `UsableDuringInitialization` types.
- Missing name-scope `Complete()` call parity after root/template graph materialization.
- Missing `(IServiceProvider)` constructor fallback when parameterless constructor is absent.

## Spec: SourceGen Parity Behavior

## SG-L1 Object lifecycle
For every generated object node:
- invoke `__BeginInit(node)` before node property/event/children population
- invoke `__EndInit(node)` after population

`__BeginInit`/`__EndInit` are interface-based (`ISupportInitialize`) and no-reflection.

## SG-L2 Top-down initialization
For nodes with `UseTopDownInitialization=true` (derived from `UsableDuringInitializationAttribute`):
- emit parent attachment before property assignment in the same node emission block
- avoid duplicate parent attach emission

## SG-L3 Scope completion
- keep current root namescope attachment (`NameScope.SetNameScope`)
- complete namescope once graph registration is finished (`__TryCompleteNameScope(scope)`)
- apply same completion behavior for template-local namescope

## SG-L4 Service provider constructor selection
During binding, per resolved object:
- if public parameterless constructor exists: use parameterless
- else if public constructor `(IServiceProvider)` exists: use service-provider constructor

Emitter behavior:
- object creation uses `__serviceProvider` when SG-L4 flag is set

## SG-L5 Registry/build pipeline flow
Generated root creation path should preserve provider:
- registry factory takes provider and creates root via `__CreateRootInstance(__serviceProvider)`
- populate receives the same provider

## Implemented In This Wave

## Code changes
- `src/XamlToCSharpGenerator.Core/Models/ResolvedObjectNode.cs`
  - added `UseServiceProviderConstructor`
  - added `UseTopDownInitialization`

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  - computes constructor strategy (`(IServiceProvider)` fallback)
  - computes top-down lifecycle eligibility from `Avalonia.Metadata.UsableDuringInitializationAttribute`
  - carries both flags in `ResolvedObjectNode`

- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  - introduced generated helpers: `__BeginInit`, `__EndInit`, `__TryCompleteNameScope`
  - service-provider-aware root creation (`__CreateRootInstance`)
  - service-provider-aware per-node object creation expression
  - per-node lifecycle wrapping (`BeginInit`/`EndInit`)
  - top-down early-attach templates for content/collections/property-element assignments
  - namescope completion on root and template scopes
  - registry and populate wiring now forward `IServiceProvider`
  - deferred template factories now create scoped providers via runtime helper and pass those providers into template node materialization

- `src/XamlToCSharpGenerator.Runtime/SourceGenDeferredServiceProviderFactory.cs`
  - added deferred-template provider runtime helper exposing `INameScope`, `IRootObjectProvider`, and `IAvaloniaXamlIlControlTemplateProvider`
  - added chained name-scope lookup behavior (local scope with parent fallback)

## Tests added/updated
- `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`
  - updated root creation assertion for new root factory
  - added lifecycle + namescope completion coverage
  - added service-provider constructor emission coverage
  - added top-down attach ordering coverage
  - added deferred-template emission assertions for scoped provider factory usage

- `tests/XamlToCSharpGenerator.Tests/Runtime/SourceGenDeferredServiceProviderFactoryTests.cs`
  - added runtime tests for parent-scope fallback and deferred provider service exposure/forwarding

## Validation
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj`
- Result: Passed `149`, Skipped `1`, Failed `0`

## Remaining parity backlog
The following still require explicit implementation for full XamlX-equivalent behavior:

1. Full deferred content runtime parity with inner/parent service-provider stack semantics matching `XamlIlRuntimeHelpers` (`CreateInnerServiceProviderV1`, parent stack provider exposure).  
   Status: partially implemented (template scope + deferred provider wrapper complete; parent-stack/resource-node parity still pending).
2. Full ProvideValue target/service scope parity for all markup extension execution paths where runtime service context is required.
3. Full parent-stack semantics for transforms/nodes requiring traversal context equivalent to XamlX `XamlNeedsParentStackCache` behavior.
4. Final verification sweep against Avalonia XAML unit scenarios covering initialization ordering + template/deferred scope lookup edge cases.

## Execution plan for remaining work

1. WS7.1 Deferred service/scope runtime parity
- add sourcegen runtime equivalents for deferred root/inner providers
- align template/deferred name-scope lookup chaining behavior
- add focused runtime tests for deferred lookup and scope completion

2. WS7.2 Markup extension provider-context parity
- pass provider context into generated extension conversion paths that require `IProvideValueTarget`, `IRootObjectProvider`, `IUriContext`
- add binding/resource extension coverage tests

3. WS7.3 Parent-stack parity
- add lightweight parent-stack runtime context abstraction
- wire emitter generation for nodes marked as needing parent stack
- add tests mirroring XamlX initialization/parent-stack ordering

4. WS7.4 Differential parity suite
- port/author parity fixtures matching Avalonia `BasicTests` initialization-order cases
- add regression gate that compares sourcegen behavior against known XamlX expectations

## Exit criteria
- no known lifecycle/scope/service-provider ordering deltas in parity fixtures
- all new tests green in local suite and CI
- no watch/hot-reload regressions in sample app
