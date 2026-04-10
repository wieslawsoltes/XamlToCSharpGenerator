# Framework.Shared Restoration And XBind Event Extraction

Date: 2026-04-09

## Current State

The checkout is in a partially-applied shared-mode refactor state:

- `src/XamlToCSharpGenerator.Avalonia` already references many `Framework.Shared` services and models.
- `src/XamlToCSharpGenerator.Framework.Shared` was not present in the worktree.
- several new framework-neutral contracts are referenced from `Core`, `Framework.Abstractions`, `Avalonia`, `NoUi`, and tests, but their source files are also still missing.

This means the repository was not on a compiling baseline before the next extraction wave started.

## This Slice

This step continues the requested refactor at the next narrow seam while also restoring the missing shared project shell needed for that seam:

- added `src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj`
- added shared x:Bind semantic model types in `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindSharedModels.cs`
- extracted x:Bind event-binding definition planning into `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindEventBindingDefinitionService.cs`
- added the thin Avalonia integration partial `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBindServices.cs`
- removed the inline `TryBuildXBindEventBindingDefinition(...)` implementation from `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBind.cs`
- wired the new shared service in `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- updated de-hack guards and added focused service tests
- restored the missing framework profile/runtime/config surface:
  - `src/XamlToCSharpGenerator.Framework.Abstractions/IXamlFrameworkDocumentUriResolver.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Runtime/SchemeBasedDocumentUriResolver.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Runtime/XamlIncludeUriResolutionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Configuration/XamlFrameworkBuildContract.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Configuration/XamlGlobalXmlnsPrefixParser.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Configuration/XamlJsonTransformProvider.cs`
- restored the next x:Bind/runtime-helper shared slice:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/WritablePropertyResolutionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/MarkupOptionValueExpressionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/XamlFragmentDetectionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/RuntimeXamlFragmentExpressionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindOptionExpressionService.cs`
- restored the next x:Bind semantic services and Avalonia wrappers:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindExpressionSemanticService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindSourceConfigurationService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/XBindBindBackExpressionService.cs`
  - restored missing Avalonia wrappers in `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBindServices.cs`
- restored the next type-resolution and transform shared models/services:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/TransformResolutionModels.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/PropertyAliasResolutionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/TransformExtensionResolutionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/TypeResolutionNamespaceDiscoveryService.cs`
- restored the first shared emitter and binder baseline models/utilities:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/BindingScopeContext.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/MarkupContextTokenSet.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/GeneratedSourceHintNameService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/SourceMappedLineEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/ParentStackEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/FrameworkHotReloadScaffoldContext.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/FrameworkHotReloadPropertyCleanupPlan.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/FrameworkObjectGraphEmissionContext.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/CompiledBindingAccessorEmissionPlan.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/CompiledBindingEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/ResolvedViewModelEmissionMetadataService.cs`
- restored the next emitter-side shared services and Avalonia adapters:
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/EventBindingEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/FrameworkEventBindingMethodEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Emission/SourceInfoRegistrationEmissionService.cs`
  - `src/XamlToCSharpGenerator.Framework.Abstractions/IXamlFrameworkEventBindingEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Framework.Abstractions/IXamlFrameworkEventSubscriptionEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Framework.Abstractions/IXamlFrameworkObjectNodeLifecycleEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Framework.Abstractions/IXamlFrameworkDeferredDictionaryEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaFrameworkEventBindingEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaFrameworkEventSubscriptionEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaFrameworkDeferredDictionaryEmitterAdapter.cs`
- restored the first binder-evaluation helpers:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ConditionalXamlEvaluationService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ExplicitConstructionBindingService.cs`
- restored the object-node planning and compatibility layer:
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeAttachmentPlanningService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodePropertyElementBindingService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodePropertyElementAssignmentPlanningService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeConstructionPlanningService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeFinalizationService.cs`
  - `src/XamlToCSharpGenerator.Framework.Shared/Binding/SymbolConstructionSemanticsService.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodePlanning.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.RestoredCompatibility.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaSemanticContractMap.cs`
- repaired the last Avalonia constructor/delegate compatibility call sites in `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
- removed stale solution references to absent pilot sample projects from `XamlToCSharpGenerator.slnx`
- fixed framework semantic-convention imports in:
  - `src/XamlToCSharpGenerator.Avalonia/Framework/AvaloniaFrameworkSemanticConventions.cs`
  - `src/XamlToCSharpGenerator.NoUi/Framework/NoUiFrameworkSemanticConventions.cs`

## Why This Is The Correct Next Extraction

The removed method in `AvaloniaSemanticBinder.XBind.cs` was framework-neutral orchestration:

- explicit x:Bind source-type validation
- shared source-configuration planning
- shared expression lowering
- shared delegate-compatibility candidate generation
- Roslyn lambda analysis and stable generated method planning

The only Avalonia-specific part is the composition root wiring. That now stays in Avalonia, while the semantic planner moves to shared.

The follow-up slices in this update continue the same ownership rule:

- x:Bind lowering, bind-back planning, source resolution, and named-element dependency tracking are framework-neutral semantic work
- transform alias materialization and xmlns namespace discovery are framework-neutral graph-building work
- Avalonia remains only the composition root and runtime projection layer for those services

## Verification Status

The shared restoration baseline is back on a compiling state:

- `dotnet build src/XamlToCSharpGenerator.Framework.Abstractions/XamlToCSharpGenerator.Framework.Abstractions.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`

All three now succeed.

The missing shared-service frontier behind `AvaloniaSemanticBinder.cs` is no longer the active blocker. Verification is now limited by checkout/environment state:

- `tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj` still needs restore-generated assets before `dotnet test --no-restore` can run.
- several sample projects also need restore-generated assets before `--no-restore` solution builds can run.
- `samples/ControlCatalog.iOS/ControlCatalog.iOS.csproj` still requires the `ios` workload in environments that try to build the full solution.
- the solution also contained stale references to `samples/WpfLikeFrameworkPilotSample/WpfLikeFrameworkPilotSample.csproj` and `samples/MauiLikeFrameworkPilotSample/MauiLikeFrameworkPilotSample.csproj`, but those projects are not present in this checkout. Those stale entries were removed in this slice.

After restoring the x:Bind option/runtime-helper slice, the missing-error count on `src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj` dropped from `98` to `93`.

After restoring the x:Bind semantic services and the transform/type-resolution shared models/services in this update, the missing-error count dropped again from `93` to `80`.

After restoring the first shared emitter baseline models/utilities and the next event-binding/source-info slice, the missing-error count dropped from `80` to `59`.

After restoring the remaining emitter scaffold cluster and the first binder utility batch in the worktree, the mixed Avalonia frontier dropped further from `59` to `35`.

After restoring the next historical binder-service batch into `Framework.Shared`, the current Avalonia frontier dropped again from `35` to `30`.

After restoring the next constructor-safe shared service slice, the current Avalonia frontier dropped again from `30` to `24`.

After restoring the next live helper slice (`FrameworkPropertyReferenceResolutionService`, `ObjectNodeKeyExpressionService`, `BindingObjectNodeMarkupParser`, `XamlTypeNodeBindingService`, and `NameScopeRegistrationParsingService`), the current Avalonia frontier dropped again from `24` to `19`.

After restoring the next binding-expression, template/resource, and scope-inference slice:

- `src/XamlToCSharpGenerator.Framework.Shared/Binding/CompiledBindingAccessorResolutionService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/CSharpExpressionBindingService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/EventBindingSemanticBindingService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/ResourceDefinitionBindingService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/TemplateDefinitionBindingService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/TemplateValidationService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/BindingScopeDataTypeInferenceService.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TemplateValidation.cs`

the shared build remained clean and the current Avalonia frontier dropped again from `19` to `12`.

After restoring the next binding-projection and typed-literal conversion slice:

- `src/XamlToCSharpGenerator.Framework.Shared/Binding/BindingRuntimeProjectionService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/FrameworkBindingProjectionService.cs`
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/TypedLiteralValueConversionService.cs`
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingProjection.cs`

the shared build remained clean and the current Avalonia frontier dropped again from `12` to `9`.

After restoring the next object-node planning and compatibility slice, plus the last Avalonia-side constructor/delegate call-shape repairs:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
  succeeds with `0` warnings and `0` errors.
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
  now succeeds again.

After resuming parity work on top of the restored shared baseline:

- `samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj` now builds successfully again.
- the following shared/emitter fixes were implemented:
  - style child attachment routing now differentiates nested `Style` children from `Setter` children in the shared collection-attachment emitter path
  - binding namescope attachment now targets public `Avalonia.Data.BindingBase` with `WeakReference<INameScope>` instead of inaccessible `IBinding2`
  - writable binding-property lookup now walks base types, restoring typed converter coercion for inherited binding properties
  - initialize-component hot-reload registration no longer emits the invalid `null` state-transfer branch
  - compiled-binding object accessor emission now rewrites the shared `__source` placeholder to the typed local receiver before emission
  - shared CLR property emission now routes attached/static setter assignments through static owner calls, including `ApplyClassValue(...)` for `Classes.Foo` bindings and static setter calls such as `DataValidationErrors.SetError(...)`
  - class-backed named field hookup now uses `SourceGenNameReferenceHelper.ResolveByName(...)` rather than emitting `.FindNameScope()` on arbitrary root types

That is the current measurable progress marker for this recovery wave: the shared restore is back on a compiling Avalonia baseline.

## Latest Parity Slice

The remaining `ControlCatalog` hard errors from this recovery wave have now been cleared.

Implemented in this slice:

- extended `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeAttachmentPlanningService.cs`
  so discovered content properties are planned semantically instead of falling through to owner-type direct-add semantics
- fixed content-attributed collection attachment for `Span.Inlines`, so object children now emit `.Inlines.Add(...)` instead of `.AddChild(...)`
- refined discovered content-property planning so scalar container properties such as `NativeMenuItem.Menu` still emit scalar assignment (`.Menu = ...`) even though the assigned value type is itself a collection container
- preserved the earlier object-element normalization for `OnPlatformExtension`, which now emits direct platform property assignments and `ProvideMarkupExtension(...)` instead of `Options.Add(...)`/`AddChild(...)`
- preserved the earlier literal/delegate/concrete-collection parity fixes:
  - `ThemeVariant` and similar static-member-backed literals no longer fall back to `new ...()`
  - delegate-typed runtime assignments no longer emit `object` casts
  - specialized collection/value-family emission no longer falls back to `List<T>` for cases like `FontFeatureCollection`

Regression coverage added in `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`:

- `Resolves_OnPlatform_Object_Element_To_Markup_Extension_Type`
- `Uses_Content_Attributed_Collection_Property_For_Object_Children`
- `Treats_Content_Property_Value_Container_As_Scalar_Assignment`

Verification completed in this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet build samples/ControlCatalog/ControlCatalog.csproj --disable-build-servers --no-restore`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~Resolves_OnPlatform_Object_Element_To_Markup_Extension_Type|FullyQualifiedName~Uses_Content_Attributed_Collection_Property_For_Object_Children|FullyQualifiedName~Treats_Content_Property_Value_Container_As_Scalar_Assignment"`

## Latest Refactor Slice

The next reusable event-binding seam is now shared as well.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/EventBindingDefinitionService.cs`
  to own inline event-code definition building, `EventBinding` markup parsing, semantic binding, and warning-message shaping over the existing shared parser/semantic services
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  to delegate `EventBinding` inline-code and markup-extension orchestration through the shared service
- removed the local Avalonia-only wrappers:
  - `TryBuildInlineEventCodeDefinition(...)`
  - `TryBindEventBinding(...)`
  - local `EventBinding` parser wrapper helpers
- added focused unit coverage in `tests/XamlToCSharpGenerator.Tests/Generator/EventBindingDefinitionServiceTests.cs`
- updated de-hack ownership guards in `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `2843` lines
- `src/XamlToCSharpGenerator.Framework.Shared/Binding/EventBindingDefinitionService.cs`: `142` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~EventBindingDefinitionServiceTests|FullyQualifiedName~BindingEventMarkupParserTests|FullyQualifiedName~XBindEventBindingDefinitionServiceTests|FullyQualifiedName~Binder_Uses_Centralized_EventBinding_Source_Validation|FullyQualifiedName~Binder_Uses_Centralized_Binding_And_Event_Markup_Parser|FullyQualifiedName~Binder_Uses_Shared_Event_Binding_Semantic_Service"`

## Latest Refactor Slice

The next reusable handler-binding seam is now shared too.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/EventHandlerBindingService.cs`
  to own CLR handler-name parsing, compatible delegate-method lookup, and root-method-group expression construction
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  so event-subscription validation no longer owns local handler parsing/signature checks
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so delegate-typed object-node value binding now uses the shared handler-binding service
- removed the local Avalonia-only helpers:
  - `TryBuildDelegateMethodGroupValueExpression(...)`
  - `HasCompatibleInstanceMethod(...)`
  - `HasInstanceMethod(...)`
  - `IsMethodCompatibleWithDelegate(...)`
  - `TryParseHandlerName(...)`
- added focused unit coverage in `tests/XamlToCSharpGenerator.Tests/Generator/EventHandlerBindingServiceTests.cs`
- updated de-hack ownership guards in `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~EventHandlerBindingServiceTests|FullyQualifiedName~Binder_And_Object_Node_Binding_Use_Shared_Event_Handler_Binding_Service"`

## Latest Refactor Slice

The Roslyn type/member lookup semantics previously trapped in `BindingSemantics` are now shared.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/TypeSymbolLookupSemanticsService.cs`
  to own:
  - nullable-insensitive type assignability checks
  - deterministic instance-member lookup traversal
  - base/interface property and event resolution
  - accessible property/method lookup helpers
  - generated-code accessibility-within symbol selection
- rewired Avalonia binder composition in `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  so shared services now depend on `TypeSymbolLookupSemanticsService` instead of binder-local lookup helpers
- rewired these Avalonia partials to use the shared lookup service directly:
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodePlanning.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.NodeTypeResolution.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.RestoredCompatibility.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs`
  - `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs`
- removed the old binder-local implementations from `BindingSemantics`:
  - `IsTypeAssignableTo(...)`
  - `AreEquivalentTypesIgnoringNullable(...)`
  - `FindEvent(...)`
  - `FindProperty(...)`
  - `EnumerateInstanceMemberLookupTypes(...)`
  - the unused local accessible/property getter helper block that depended on that same traversal
- added focused unit coverage in `tests/XamlToCSharpGenerator.Tests/Generator/TypeSymbolLookupSemanticsServiceTests.cs`
- updated de-hack ownership guards in `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after these two slices:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `2505` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `2046` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBind.cs`: `334` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1053` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~TypeSymbolLookupSemanticsServiceTests|FullyQualifiedName~EventHandlerBindingServiceTests|FullyQualifiedName~Binder_Uses_Shared_Type_Symbol_Lookup_Semantics_Service|FullyQualifiedName~Binder_And_Object_Node_Binding_Use_Shared_Event_Handler_Binding_Service"`

## Latest Refactor Slice

The remaining event-subscription orchestration seam is now shared too.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/EventSubscriptionBindingService.cs`
  to own:
  - unified CLR-vs-routed event target resolution over the existing shared event lookup services
  - inline event-code subscription binding
  - assignment-based event subscription binding for handler names, `EventBinding`, `x:Bind`, and inline lambdas
  - deterministic `ResolvedEventSubscription` construction for both CLR and routed events
- tightened `src/XamlToCSharpGenerator.Framework.Shared/Binding/FrameworkRoutedEventResolutionService.cs`
  so non-generic routed events prefer the framework-specific routed-event handler contract instead of falling back to `EventHandler<EventArgs>`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose the new shared event-subscription service
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  so both:
  - `TryBindEventSubscription(...)`
  - `TryBindInlineEventCodeSubscription(...)`
  now delegate through `EventSubscriptionBindingService`
- added focused unit coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/EventSubscriptionBindingServiceTests.cs`
  - `tests/XamlToCSharpGenerator.Tests/Generator/FrameworkRoutedEventResolutionServiceTests.cs`
- updated de-hack ownership guards in `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `2393` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `2046` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.XBind.cs`: `334` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1053` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~FrameworkRoutedEventResolutionServiceTests|FullyQualifiedName~EventSubscriptionBindingServiceTests|FullyQualifiedName~Binder_Uses_Shared_Framework_Routed_Event_Resolution_Service|FullyQualifiedName~Binder_Uses_Shared_Event_Subscription_Binding_Service|FullyQualifiedName~TypeSymbolLookupSemanticsServiceTests|FullyQualifiedName~EventHandlerBindingServiceTests"`

## Latest Refactor Slice

The typed value-conversion seam is now shared instead of being inlined inside `BindingSemantics`.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ValueConversionSemanticService.cs`
  to own:
  - markup-extension-aware value conversion orchestration
  - nullable unwrapping for typed targets
  - framework-property reference conversion
  - selector expression conversion
  - binding / reflection-binding / template-binding conversion dispatch
  - typed literal fallback delegation to `TypedLiteralValueConversionService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose `ValueConversionSemanticService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
  so these methods now delegate to the shared service:
  - `TryConvertValueExpression(...)`
  - `TryConvertValueConversion(...)`
  - `TryConvertValueForCollectionAdd(...)`
  - `TryConvertMarkupExtensionExpression(...)`
  - `TryConvertMarkupExtensionConversion(...)`
- added focused unit coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ValueConversionSemanticServiceTests.cs`
- updated de-hack ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

## Latest Refactor Slice

The next reusable object-node and emitter seams are now shared too.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ClrPropertyAssignmentCreationService.cs`
  to centralize CLR property-assignment model creation with shared object-initializer policy
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so repeated CLR property-assignment record construction now delegates through the shared service
- added `src/XamlToCSharpGenerator.Framework.Shared/Emission/AttachedNodeValueEmissionService.cs`
  to own attached-node markup-extension `ProvideMarkupExtension(...)` wrapping
- rewired `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  so attached-node value wrapping is no longer emitter-local
- added focused unit coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ClrPropertyAssignmentCreationServiceTests.cs`
  - `tests/XamlToCSharpGenerator.Tests/Generator/AttachedNodeValueEmissionServiceTests.cs`
- updated de-hack ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after these slices:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `1609` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `2015` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1029` lines

Verification completed for these slices:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~ValueConversionSemanticServiceTests|FullyQualifiedName~ClrPropertyAssignmentCreationServiceTests|FullyQualifiedName~AttachedNodeValueEmissionServiceTests|FullyQualifiedName~Binder_Uses_Shared_Value_Conversion_Semantic_Service|FullyQualifiedName~Object_Node_Binder_Uses_Shared_Clr_Property_Assignment_Creation_Service|FullyQualifiedName~Emitter_Uses_Shared_Attached_Node_Value_Emission_Service"`
- `dotnet build XamlToCSharpGenerator.slnx --disable-build-servers --no-restore -clp:ErrorsOnly`
  - still blocked in this environment by missing restore-generated assets for several sample projects and the existing `ios` workload requirement

## Latest Refactor Slice

The remaining object-node property-element projection seam is now shared as well.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodePropertyElementProjectionService.cs`
  to own:
  - property-element resolution ordering across aliased framework properties, owner-qualified attached properties, generic property lookup, framework property projection, and generic fallback assignment
  - deterministic property-element diagnostic shaping for missing-property, unsupported-property, and single-value cardinality failures
- added the thin Avalonia adapter partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodePropertyElements.cs`
  to keep only:
  - Avalonia property-field projection
  - owner-qualified attached-property projection
  - attached setter-method projection
  - Avalonia-specific item-container validation wiring
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose `ObjectNodePropertyElementProjectionService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so the long inline property-element projection loop is replaced by `ProjectObjectNodePropertyElementAssignments(...)`
- removed the now-dead local `TryBindAttachedSetterPropertyElementAssignment(...)` implementation from `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
- added focused unit coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ObjectNodePropertyElementProjectionServiceTests.cs`
- updated de-hack ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `1775` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `1552` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1029` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~ObjectNodePropertyElementProjectionServiceTests|FullyQualifiedName~Object_Node_Binder_Uses_Shared_Property_Element_Projection_Service|FullyQualifiedName~Object_Node_Binder_Uses_Shared_Clr_Property_Assignment_Creation_Service"`

## Immediate Next Steps

1. Resume the intended deeper shared-mode refactor rather than restoration work:
   - continue shrinking `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`, with the next target being the remaining attached-assignment orchestration and post-loop node-assembly helpers
   - continue emitter scaffold extraction from `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`, with the next target being the remaining local helper/utilities and adapter-heavy hot-reload/runtime projections
   - keep peeling remaining genuinely reusable micro-seams out of `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`
2. Decide whether the absent WPF/MAUI pilot sample projects should be restored in this branch or remain out of the solution.

## Current Active Frontier

The active frontier is no longer missing shared binder/emitter services behind `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`. That restore surface is now sufficient for `src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj` to compile.

The current constraints are:

- sample parity frontier
  - `samples/ControlCatalog/ControlCatalog.csproj` now builds successfully again; the previous hard-error frontier around `ThemeVariant`, delegate assignments, `OnPlatform`, `Span`, and `NativeMenuItem.Menu` is closed
  - the remaining sample output is warning-heavy parity work rather than compiler-break regressions
- verification state
  - the focused test project is no longer blocked by the `ControlCatalog` hard errors from this recovery wave
- environment-specific solution validation
  - the iOS sample still requires the `ios` workload for full-solution builds in this environment
- resumed refactor work
  - `BindingSemantics` is no longer the main size blocker; the remaining work there is narrower and more Avalonia-specific
  - the next high-value binder target is now `ObjectNodeBinding`, especially the remaining attached-assignment and post-loop orchestration after the shared standard-property binding slice
  - the next emitter target is the remaining local adapter-heavy logic in `AvaloniaCodeEmitter`

The important shift is that work is back in planned parity/refactor mode instead of emergency shared-service restoration.

## Latest Refactor Slice

The main non-attached object-node assignment chain is now shared as well.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeStandardPropertyAssignmentBindingService.cs`
  to own deterministic non-attached assignment orchestration for:
  - getter-only collection literal projection
  - CLR property assignment binding dispatch
  - event-subscription fallback
  - framework-property fallback
  - missing-property diagnostic shaping
- added the thin Avalonia adapter partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeAssignments.cs`
  to keep only:
  - request shaping
  - Avalonia CLR-property binding wrapper
  - Avalonia framework-property fallback wrapper
  - Avalonia event-subscription wrapper
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose `ObjectNodeStandardPropertyAssignmentBindingService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so the old inline collection/CLR/event/framework-property fallback chain is replaced by `BindStandardObjectNodePropertyAssignment(...)`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ClrPropertyAssignments.cs`
  so CLR-property binding now returns a resolved assignment instead of mutating the outer assignment builder directly
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ObjectNodeStandardPropertyAssignmentBindingServiceTests.cs`
- updated de-hack ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `1075` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `1552` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1029` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore`

Current verification blocker:

- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore`
  is still blocked by the existing local-analyzer sample failure in `samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj`
  (`FluentTheme.xaml.cs(33,13): CS0103 InitializeComponent`)
- direct binder initialization is healthy in a standalone harness, so this frontier currently looks like a Roslyn local-analyzer load-context issue rather than a new semantic-binder regression in the extracted slice

## Latest Fix And Refactor Slice

The sample/test blocker is now closed, and the next `ObjectNodeBinding` seam is shared.

Implemented in this slice:

- fixed the Avalonia sample generator failure in
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  by replacing an eager closed-instance delegate capture against `EventHandlerBindingService`
  with a deferred static lambda inside `ClrPropertyAssignmentBindingService` composition
  - this removed the `TypeInitializationException` that had been preventing generated `InitializeComponent` output from contributing during `Avalonia.Themes.Fluent` compilation
- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeAttachedPropertyAssignmentBindingService.cs`
  to own deterministic attached-assignment fallback ordering for:
  - attached Avalonia-property assignment
  - attached static setter assignment
  - attached `Classes.*` assignment
  - attached event-subscription projection
  - attached-assignment missing-property diagnostic shaping
- added the thin Avalonia adapter partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeAttachedAssignments.cs`
  to keep only request shaping and Avalonia-specific delegate wrappers
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose `ObjectNodeAttachedPropertyAssignmentBindingService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so the inline attached-property/static-setter/class-property/event-subscription chain is replaced by `BindAttachedObjectNodePropertyAssignment(...)`
- repaired test-source drift in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ClrPropertyAssignmentBindingServiceTests.cs`
  - `tests/XamlToCSharpGenerator.Tests/Generator/ObjectNodeStandardPropertyAssignmentBindingServiceTests.cs`
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ObjectNodeAttachedPropertyAssignmentBindingServiceTests.cs`
- updated ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `1017` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `1552` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `1029` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: `0 warnings`, `0 errors`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~ObjectNodeAttachedPropertyAssignmentBindingServiceTests|FullyQualifiedName~ObjectNodeStandardPropertyAssignmentBindingServiceTests|FullyQualifiedName~ClrPropertyAssignmentBindingServiceTests"`
  - result: `6` passed

Current next step:

- continue shrinking `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  from the remaining post-loop node-assembly helper residue
- then move to the remaining adapter-heavy helper seams in
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

## Latest Binder And Emitter Slice

The remaining post-loop object-node assembly seam is now shared, and the next
`InitializeComponent` body scaffold is shared as well.

Implemented in this slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Binding/ObjectNodeAssemblyService.cs`
  to own the post-loop bound object-node assembly flow:
  - child merge with property-element child output
  - platform-markup child normalization
  - projected property-element assignment append
  - construction-plan dispatch
  - attachment finalization and validation reporting
  - name registration lookup
  - key-expression lookup
  - final `ResolvedObjectNode` shaping
- added the thin Avalonia adapter partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeAssembly.cs`
  to keep only request shaping and the immutable projection wrapper
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.cs`
  to compose `ObjectNodeAssemblyService`
- rewired `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so the old inline post-loop normalization/construction/finalization block is
  replaced by `AssembleBoundObjectNode(...)`
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ObjectNodeAssemblyServiceTests.cs`
- updated ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Emitter work in the same slice:

- added `src/XamlToCSharpGenerator.Framework.Shared/Emission/InitializeComponentBodyEmissionService.cs`
  to own deterministic `InitializeComponent` body generation for:
  - source-gen load path
  - `x:Bind` reset
  - named-element fallback resolution
  - hot-reload / hot-design registration emission callback dispatch
- rewired `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  to compose and call `InitializeComponentBodyEmissionService`
  instead of carrying the inline local helper
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/InitializeComponentBodyEmissionServiceTests.cs`
- updated emitter ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `940` lines
- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs`: `1552` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `994` lines

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~ObjectNodeAssemblyServiceTests|FullyQualifiedName~InitializeComponentBodyEmissionServiceTests|FullyQualifiedName~Object_Node_Binder_Uses_Shared_Object_Node_Assembly_Service|FullyQualifiedName~Emitter_Uses_Shared_Initialize_Component_Body_Emission_Service" -clp:ErrorsOnly`
  - result: `5` passed

Current next step:

- continue shrinking `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  from the remaining adapter-only object-node helpers and compatibility wrappers
- then continue reducing the still-local recursive/object-graph coordination and
  remaining utility helpers in
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`

## Latest Object-Node Compatibility And Recursive Emission Slice

The remaining adapter-only object-node compatibility helpers are now isolated
out of the main Avalonia binder file, and the recursive object-graph emission
loop is now shared.

Implemented in this slice:

- added the thin Avalonia compatibility partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeCompatibility.cs`
  for the remaining object-node compatibility helpers:
  - `x:Bind` default-mode normalization
  - binding result-type inference helpers
  - inline binding-markup extraction
  - `x:Array` / `x:Type` compatibility helpers
- added the thin Avalonia special-node partial
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeSpecialNodes.cs`
  for the remaining object-node special-node helpers:
  - owner-qualified attached-assignment classification
  - setter target-type ambient resolution
  - inline C# object-node extraction
  - `x:Array` binding and object-node key shaping
- rewired
  `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  so the above helper clusters are no longer mixed into the main assignment loop
- added
  `src/XamlToCSharpGenerator.Framework.Shared/Emission/RecursiveObjectGraphEmissionService.cs`
  to own framework-neutral recursive object-graph coordination:
  - parent-stack extension and parent-stack expression building
  - `FrameworkObjectGraphEmissionContext` creation
  - top-down attachment template expansion
  - recursive delegate dispatch into `ObjectNodeBodyEmissionService`
- rewired
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  to compose `RecursiveObjectGraphEmissionService`
  instead of carrying local recursive/object-graph helper methods
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/RecursiveObjectGraphEmissionServiceTests.cs`
- updated ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~RecursiveObjectGraphEmissionServiceTests|FullyQualifiedName~Object_Node_Binder_Isolates_Compatibility_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Object_Node_Binder_Isolates_Special_Node_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Emitter_Uses_Shared_Recursive_Object_Graph_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Deferred_Dictionary_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Collection_Attachment_And_Deferred_Template_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Source_Mapped_Line_And_Parent_Stack_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Initialize_Component_Body_Emission_Service" -clp:ErrorsOnly`
  - result: `9` passed

Shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `357` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `800` lines

## Latest ViewModel Scaffold Slice

The remaining framework-neutral root-class scaffold helpers are now shared as
well.

Implemented in this slice:

- added
  `src/XamlToCSharpGenerator.Framework.Shared/Emission/ViewModelScaffoldEmissionService.cs`
  to own:
  - source-capacity estimation
  - hot-design role/kind/scope-hint expression planning
  - hint-name construction
  - compiled-binding accessor method emission
  - unsafe-accessor method emission
- rewired
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  to compose `ViewModelScaffoldEmissionService`
  and to call existing shared metadata services directly for:
  - named-field map construction
  - known-type collection
  - binding XML namespace map emission
  - `typeof(...)` argument list emission
- removed the old local helper cluster from
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`:
  - source-capacity estimation helpers
  - compiled-binding accessor helper emission
  - unsafe-accessor helper emission
  - hot-design metadata helper emission
  - hint-name helper emission
  - dead local utility residue from that cluster
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/ViewModelScaffoldEmissionServiceTests.cs`
- updated ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: `0` errors, warning-heavy sample output only
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore --filter "FullyQualifiedName~ViewModelScaffoldEmissionServiceTests|FullyQualifiedName~RecursiveObjectGraphEmissionServiceTests|FullyQualifiedName~Emitter_Uses_Shared_View_Model_Scaffold_Emission_Service|FullyQualifiedName~Emitters_Use_Shared_Generated_Source_Hint_Name_Service|FullyQualifiedName~Object_Node_Binder_Isolates_Compatibility_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Object_Node_Binder_Isolates_Special_Node_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Emitter_Uses_Shared_Recursive_Object_Graph_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Deferred_Dictionary_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Collection_Attachment_And_Deferred_Template_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Source_Mapped_Line_And_Parent_Stack_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Initialize_Component_Body_Emission_Service" -clp:ErrorsOnly`
  - result: `15` passed

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `357` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `582` lines
- `src/XamlToCSharpGenerator.Framework.Shared/Emission/RecursiveObjectGraphEmissionService.cs`: `217` lines
- `src/XamlToCSharpGenerator.Framework.Shared/Emission/ViewModelScaffoldEmissionService.cs`: `183` lines

Current next step:

- keep `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`
  as a thin Avalonia adapter and only peel additional helpers if a genuinely
  framework-neutral planning seam appears
- continue reducing
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  from the remaining local utility/helpers that are still framework-neutral:
  - string/quoted-literal helper shaping
  - any remaining reusable source-registration/runtime helper scaffolds
  - possible migration of generic literal/identifier escaping utilities into a
  shared emission utility surface when it reduces duplication across emitters

## Latest Literal And Identifier Utility Slice

The last worthwhile framework-neutral helper seam in the Avalonia emitter is now
shared as focused utility services rather than local emitter helpers.

Implemented in this slice:

- added
  `src/XamlToCSharpGenerator.Framework.Shared/Emission/CSharpLiteralEmissionService.cs`
  to own:
  - string literal escaping
  - quoted-or-null literal shaping
  - boolean literal shaping
  - single-line comment text normalization
- added
  `src/XamlToCSharpGenerator.Framework.Shared/Emission/IdentifierSanitizationService.cs`
  to own deterministic identifier sanitization for generated member names
- rewired
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  to compose those services for:
  - trace-comment emission
  - named field identifiers
  - registry descriptor string literals
  - include graph registration literals
  - framework service wiring that already consumed escape/sanitize delegates
- removed the remaining local utility helper cluster from
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`:
  - `ToQuotedOrNull(...)`
  - `BoolLiteral(...)`
  - `Escape(...)`
  - `EscapeComment(...)`
  - `SanitizeIdentifier(...)`
- reused `CSharpLiteralEmissionService` in:
  - `src/XamlToCSharpGenerator.NoUi/Emission/NoUiCodeEmitter.cs`
  - `src/XamlToCSharpGenerator.NoUi/Emission/PilotCodeEmitterBase.cs`
  so the duplicate local `EscapeStringLiteral(...)` implementations are removed
  instead of leaving a shared service used only by Avalonia
- added focused coverage in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/CSharpLiteralEmissionServiceTests.cs`
  - `tests/XamlToCSharpGenerator.Tests/Generator/IdentifierSanitizationServiceTests.cs`
- updated ownership guards in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaSemanticBinderDeHackGuardTests.cs`

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Framework.Shared/XamlToCSharpGenerator.Framework.Shared.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: `0` warnings, `0` errors
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~CSharpLiteralEmissionServiceTests|FullyQualifiedName~IdentifierSanitizationServiceTests|FullyQualifiedName~ViewModelScaffoldEmissionServiceTests|FullyQualifiedName~RecursiveObjectGraphEmissionServiceTests|FullyQualifiedName~Emitter_Uses_Shared_CSharp_Literal_And_Identifier_Services|FullyQualifiedName~Emitter_Uses_Shared_View_Model_Scaffold_Emission_Service|FullyQualifiedName~Emitters_Use_Shared_Generated_Source_Hint_Name_Service|FullyQualifiedName~Object_Node_Binder_Isolates_Compatibility_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Object_Node_Binder_Isolates_Special_Node_Helpers_From_Main_Object_Node_Binding_File|FullyQualifiedName~Emitter_Uses_Shared_Recursive_Object_Graph_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Deferred_Dictionary_Emission_Service|FullyQualifiedName~Emitter_Uses_Shared_Collection_Attachment_And_Deferred_Template_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Source_Mapped_Line_And_Parent_Stack_Scaffold_Services|FullyQualifiedName~Emitter_Uses_Shared_Initialize_Component_Body_Emission_Service" -clp:ErrorsOnly`
  - result: `23` passed

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.ObjectNodeBinding.cs`: `357` lines
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `531` lines
- `src/XamlToCSharpGenerator.Framework.Shared/Emission/CSharpLiteralEmissionService.cs`: `65` lines
- `src/XamlToCSharpGenerator.Framework.Shared/Emission/IdentifierSanitizationService.cs`: `30` lines
- `src/XamlToCSharpGenerator.NoUi/Emission/NoUiCodeEmitter.cs`: `153` lines
- `src/XamlToCSharpGenerator.NoUi/Emission/PilotCodeEmitterBase.cs`: `164` lines

Current next step:

- the remaining code in
  `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
  is now mostly real emitter orchestration and Avalonia/runtime projection, not
  an obviously reusable helper seam
- additional extraction from that file should now be driven only by a concrete
  duplicated planning/runtime concern, not by file-size pressure alone

## Latest AvaloniaCodeEmitter Partial Split

The next maintenance step after the shared-helper extractions is now complete:
`AvaloniaCodeEmitter` is split into focused partials, and the main file is a
thin composition root instead of a mixed monolith.

Implemented in this slice:

- split the emitter into focused partials:
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`
    - thin composition root
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.EmitContext.cs`
    - precomputed emission context and hot-reload scaffold state
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.Scaffold.cs`
    - preamble and type-opening emission
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.Registrations.cs`
    - registry/module-initializer/source-info/resource/template/style/include
      emission
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.ObjectGraph.cs`
    - generated object-graph population/root construction members
  - `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.RuntimeMembers.cs`
    - hot reload/runtime members/compiled-binding dispatcher/control-theme
      factory/InitializeComponent/x:Bind members
- preserved the existing generated-code contract while splitting the file:
  - retained local Avalonia runtime-member orchestration where the generated
    output shape is currently test-sensitive
  - kept the shared service composition already extracted earlier
- fixed two regressions exposed while wiring the split:
  - compiled-binding accessors now rewrite `__source` to `source` before
    emitting the switch body
  - class-backed named-element rebinding now uses
    `SourceGenNameReferenceHelper.ResolveByName(...)` instead of assuming
    `FindNameScope()` is valid for every root type
- updated the de-hack guard to:
  - read all `AvaloniaCodeEmitter*.cs` partials when checking emitter ownership
  - assert that the main emitter file stays a thin composition root
  - reflect the current local `InitializeComponent` body choice and shared
    name-reference fallback
- updated generator coverage for the named-element rebinding output in:
  - `tests/XamlToCSharpGenerator.Tests/Generator/AvaloniaXamlSourceGeneratorTests.cs`

Verification completed for this slice:

- `dotnet build src/XamlToCSharpGenerator.Avalonia/XamlToCSharpGenerator.Avalonia.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build src/XamlToCSharpGenerator.NoUi/XamlToCSharpGenerator.NoUi.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
- `dotnet build tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: `3` warnings, `0` errors
- `dotnet build samples/Avalonia.Themes.Fluent/Avalonia.Themes.Fluent.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: warning-heavy sample output only, `0` errors
- `dotnet build samples/ControlCatalog/ControlCatalog.csproj --disable-build-servers --no-restore -clp:ErrorsOnly`
  - result: warning-heavy sample output only, `0` errors
- `dotnet test tests/XamlToCSharpGenerator.Tests/XamlToCSharpGenerator.Tests.csproj --disable-build-servers --no-build --filter "FullyQualifiedName~AvaloniaCodeEmitterTests|FullyQualifiedName~Emitter_Composition_Root_Is_Thin_And_Delegates_To_Partial_Sections|FullyQualifiedName~Emitter_Uses_Shared_Hot_Reload_Runtime_Emission_Service_For_State_Planning|FullyQualifiedName~Emitter_Keeps_InitializeComponent_Body_Local_While_Using_Shared_Name_Reference_Helper"`
  - result: `8` passed

Current shrink point after this slice:

- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.cs`: `126`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.EmitContext.cs`: `118`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.Scaffold.cs`: `60`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.Registrations.cs`: `97`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.ObjectGraph.cs`: `64`
- `src/XamlToCSharpGenerator.Avalonia/Emission/AvaloniaCodeEmitter.RuntimeMembers.cs`: `360`

Current next step:

- keep `AvaloniaCodeEmitter` in this split form and only extract more if a
  concrete duplicated framework-neutral emission concern appears
- future emitter work should now focus on behavioral fixes or new typed
  emission seams, not file-size reduction for its own sake
