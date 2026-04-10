using System;
using System.IO;
using System.Linq;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaSemanticBinderDeHackGuardTests
{
    [Fact]
    public void Binder_Does_Not_Use_Legacy_Lexical_Heuristics()
    {
        var source = ReadBinderSource();

        Assert.DoesNotContain("ContainsMarkupContextTokens(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\".Binding\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EndsWith(\"Binding\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("propertyToken.EndsWith(\"Property\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("trimmed.EndsWith(\"Property\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var colonIndex = normalized.IndexOf(':');", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var separatorIndex = trimmed.IndexOf(':');", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var closingParenthesisIndex = normalized.IndexOf(')');", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var typeToken = normalized.Substring(1, closingParenthesisIndex - 1).Trim();", source, StringComparison.Ordinal);
        Assert.Contains("XamlRuntimeBindingPathSemantics.NormalizePath(", source, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrySplitAtFirstSeparator(", source, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrimTerminalSuffix(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Composition_Root_Uses_Shared_Services()
    {
        var source = ReadBinderCompositionRootSource();

        Assert.Contains("MarkupTypeConversionSemanticsService = new(", source, StringComparison.Ordinal);
        Assert.Contains("CompiledBindingSourceTypeResolutionService = new(", source, StringComparison.Ordinal);
        Assert.Contains("NamespaceDiscoveryService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeAttachmentPlanningService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeFinalizationService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeConstructionPlanningService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ObjectNodePropertyElementProjectionService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeAssemblyService = new(", source, StringComparison.Ordinal);
        Assert.Contains("XBindExpressionSemanticService = new(", source, StringComparison.Ordinal);
        Assert.Contains("XBindSourceConfigurationService = new(", source, StringComparison.Ordinal);
        Assert.Contains("XBindBindBackExpressionService = new(", source, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService = new(", source, StringComparison.Ordinal);
        Assert.Contains("EventHandlerBindingService = new(", source, StringComparison.Ordinal);
        Assert.Contains("EventBindingDefinitionService = new(", source, StringComparison.Ordinal);
        Assert.Contains("EventSubscriptionBindingService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ValueConversionSemanticService = new(", source, StringComparison.Ordinal);
        Assert.Contains("SetterPropertyBindingPlanService = new(", source, StringComparison.Ordinal);
        Assert.Contains("SetterValuePlanningService = new(", source, StringComparison.Ordinal);
        Assert.Contains("ResourceDefinitionBindingService = new(", source, StringComparison.Ordinal);
        Assert.Contains("TemplateDefinitionBindingService = new(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Event_Semantic_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var definitionSource = ReadFrameworkSharedBindingSource("EventBindingDefinitionService.cs");
        var subscriptionSource = ReadFrameworkSharedBindingSource("EventSubscriptionBindingService.cs");
        var handlerSource = ReadFrameworkSharedBindingSource("EventHandlerBindingService.cs");

        Assert.Contains("EventBindingDefinitionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("EventSubscriptionBindingService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("EventHandlerBindingService = new(", rootSource, StringComparison.Ordinal);

        Assert.Contains("BindingEventMarkupParser.IsEventBindingMarkupExtension(", definitionSource, StringComparison.Ordinal);
        Assert.Contains("BindingEventMarkupParser.TryParseEventBindingMarkup(", definitionSource, StringComparison.Ordinal);
        Assert.Contains("_eventBindingDefinitionService.TryBuildParsedDefinition(", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("_eventHandlerBindingService.TryParseHandlerName(", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("_eventHandlerBindingService.HasCompatibleInstanceMethod(", subscriptionSource, StringComparison.Ordinal);
        Assert.Contains("XamlEventHandlerNameSemantics.TryParseHandlerName(", handlerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_XBind_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var xBindSource = ReadBinderPartialSource("AvaloniaSemanticBinder.XBind.cs");
        var xBindServicesSource = ReadBinderPartialSource("AvaloniaSemanticBinder.XBindServices.cs");
        var xBindSemanticServiceSource = ReadFrameworkSharedBindingSource("XBindExpressionSemanticService.cs");
        var xBindBindBackServiceSource = ReadFrameworkSharedBindingSource("XBindBindBackExpressionService.cs");
        var xBindOptionServiceSource = ReadFrameworkSharedBindingSource("XBindOptionExpressionService.cs");

        Assert.Contains("XBindExpressionSemanticService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("XBindSourceConfigurationService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("XBindBindBackExpressionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("XBindEventBindingDefinitionService = new(", rootSource, StringComparison.Ordinal);

        Assert.Contains("XBindExpressionSemanticService.TryLowerExpression(", xBindSource, StringComparison.Ordinal);
        Assert.Contains("XBindExpressionSemanticService.CollectDependencies(", xBindSource, StringComparison.Ordinal);
        Assert.Contains("XBindExpressionSemanticService.IsMainSourceReference(", xBindSource, StringComparison.Ordinal);
        Assert.Contains("XBindExpressionSemanticService.BuildPathReferenceExpression(", xBindSource, StringComparison.Ordinal);
        Assert.Contains("XBindExpressionSemanticService.BuildPathReferenceArrayLiteral(", xBindSource, StringComparison.Ordinal);

        Assert.Contains("XBindBindBackExpressionService.TryBuildBindBackExpression(", xBindServicesSource, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService.TryBuildOptionExpression(", xBindServicesSource, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService.TryBuildDelayExpression(", xBindServicesSource, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService.TryBuildUpdateSourceTriggerExpression(", xBindServicesSource, StringComparison.Ordinal);
        Assert.Contains("XBindOptionExpressionService.TryBuildPriorityExpression(", xBindServicesSource, StringComparison.Ordinal);

        Assert.Contains("ResolveNamedElement<", xBindSemanticServiceSource, StringComparison.Ordinal);
        Assert.Contains("new global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency(", xBindSemanticServiceSource, StringComparison.Ordinal);
        Assert.Contains("_xBindExpressionSemanticService.TryBuildAssignmentExpression(", xBindBindBackServiceSource, StringComparison.Ordinal);
        Assert.Contains("_tryGetWritableProperty(", xBindOptionServiceSource, StringComparison.Ordinal);
        Assert.Contains("_tryConvertMarkupOptionValue(", xBindOptionServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Markup_And_Value_Conversion_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var markupHelpersSource = ReadBinderPartialSource("AvaloniaSemanticBinder.MarkupHelpers.cs");
        var bindingSemanticsSource = ReadBinderPartialSource("AvaloniaSemanticBinder.BindingSemantics.cs");
        var commonMarkupSource = ReadFrameworkSharedBindingSource("CommonMarkupExtensionConversionService.cs");
        var primitiveMarkupSource = ReadFrameworkSharedBindingSource("XamlPrimitiveMarkupExtensionConversionService.cs");
        var activationSource = ReadFrameworkSharedBindingSource("MarkupExtensionActivationService.cs");
        var runtimeResolutionSource = ReadFrameworkSharedBindingSource("MarkupRuntimeOperationResolutionService.cs");
        var runtimeEmissionSource = ReadFrameworkSharedBindingSource("MarkupRuntimeOperationEmissionService.cs");
        var typedLiteralSource = ReadFrameworkSharedBindingSource("TypedLiteralValueConversionService.cs");

        Assert.Contains("CommonMarkupExtensionConversionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("PrimitiveMarkupExtensionConversionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("MarkupExtensionActivationService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("MarkupRuntimeOperationResolutionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("MarkupRuntimeOperationEmissionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("TypedLiteralValueConversionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ValueConversionSemanticService = new(", rootSource, StringComparison.Ordinal);

        Assert.Contains("PrimitiveMarkupExtensionConversionService.TryConvert(", markupHelpersSource, StringComparison.Ordinal);
        Assert.Contains("MarkupExtensionActivationService.TryConvertGenericExpression(", markupHelpersSource, StringComparison.Ordinal);
        Assert.Contains("MarkupExtensionActivationService.TryResolveExtensionType(", markupHelpersSource, StringComparison.Ordinal);
        Assert.Contains("ValueConversionSemanticService.TryConvert(", bindingSemanticsSource, StringComparison.Ordinal);
        Assert.Contains("ValueConversionSemanticService.TryConvertForCollectionAdd(", bindingSemanticsSource, StringComparison.Ordinal);
        Assert.Contains("ValueConversionSemanticService.TryConvertMarkupExtension(", bindingSemanticsSource, StringComparison.Ordinal);

        Assert.Contains("XamlMarkupExtensionNameSemantics.Classify(", commonMarkupSource, StringComparison.Ordinal);
        Assert.Contains("XamlMarkupExtensionNameSemantics.Classify(", primitiveMarkupSource, StringComparison.Ordinal);
        Assert.Contains("XamlTimeSpanLiteralSemantics.TryParse(", primitiveMarkupSource, StringComparison.Ordinal);
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(", activationSource, StringComparison.Ordinal);
        Assert.Contains("XamlMarkupExtensionNameSemantics.Classify(", runtimeResolutionSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedValueKind.DynamicResourceBinding", runtimeEmissionSource, StringComparison.Ordinal);
        Assert.Contains("private static bool TryConvertPrimitive(", typedLiteralSource, StringComparison.Ordinal);
        Assert.Contains("bool.TryParse(value, out var boolValue)", typedLiteralSource, StringComparison.Ordinal);
        Assert.Contains("double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue)", typedLiteralSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Type_Resolution_And_Symbol_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var binderSource = ReadBinderSource();
        var typeResolutionSource = ReadBinderPartialSource("AvaloniaSemanticBinder.TypeResolution.cs");
        var namespaceDiscoverySource = ReadFrameworkSharedBindingSource("TypeResolutionNamespaceDiscoveryService.cs");
        var typeSymbolLookupSource = ReadFrameworkSharedBindingSource("TypeSymbolLookupSemanticsService.cs");

        Assert.Contains("TypeSymbolLookupSemanticsService.IsTypeAssignableTo", rootSource, StringComparison.Ordinal);
        Assert.Contains("NamespaceDiscoveryService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("MarkupObjectElementTypeResolutionService.TryResolve(", binderSource, StringComparison.Ordinal);

        Assert.Contains("NamespaceDiscoveryService.GetFrameworkDefaultNamespaceCandidates(", typeResolutionSource, StringComparison.Ordinal);
        Assert.Contains("NamespaceDiscoveryService.GetXmlnsDefinitionTargetsForXmlNamespace(", typeResolutionSource, StringComparison.Ordinal);
        Assert.Contains("NamespaceDiscoveryService.GetProjectNamespaceCandidates(", typeResolutionSource, StringComparison.Ordinal);
        Assert.Contains("NamespaceDiscoveryService.CollectTypeCandidatesFromXmlnsDefinitionTargets(", typeResolutionSource, StringComparison.Ordinal);

        Assert.Contains("ConditionalWeakTable<Compilation, NamespaceCandidateCacheEntry>", namespaceDiscoverySource, StringComparison.Ordinal);
        Assert.Contains("ConditionalWeakTable<Compilation, XmlnsDefinitionCacheEntry>", namespaceDiscoverySource, StringComparison.Ordinal);
        Assert.Contains("public static bool IsTypeAssignableTo(", typeSymbolLookupSource, StringComparison.Ordinal);
        Assert.Contains("public static IEnumerable<INamedTypeSymbol> EnumerateInstanceMemberLookupTypes(", typeSymbolLookupSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Object_Node_Planning_Projection_And_Assembly_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var planningSource = ReadBinderPartialSource("AvaloniaSemanticBinder.ObjectNodePlanning.cs");
        var assemblySource = ReadBinderPartialSource("AvaloniaSemanticBinder.ObjectNodeAssembly.cs");
        var propertyElementSource = ReadBinderPartialSource("AvaloniaSemanticBinder.ObjectNodePropertyElements.cs");
        var attachmentPlanningSource = ReadFrameworkSharedBindingSource("ObjectNodeAttachmentPlanningService.cs");
        var finalizationSource = ReadFrameworkSharedBindingSource("ObjectNodeFinalizationService.cs");
        var projectionSource = ReadFrameworkSharedBindingSource("ObjectNodePropertyElementProjectionService.cs");

        Assert.Contains("ObjectNodeAttachmentPlanningService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeFinalizationService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeConstructionPlanningService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodePropertyElementProjectionService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeAssemblyService = new(", rootSource, StringComparison.Ordinal);

        Assert.Contains("BuildGenericPropertyElementAssignmentPlan(", planningSource, StringComparison.Ordinal);
        Assert.Contains("PlanObjectNodeConstruction(", planningSource, StringComparison.Ordinal);
        Assert.Contains("FinalizeObjectNodeAttachmentPlan(", planningSource, StringComparison.Ordinal);
        Assert.Contains("FinalizeObjectNode(", planningSource, StringComparison.Ordinal);
        Assert.Contains("ProjectObjectNodePropertyElementAssignments(", assemblySource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeAssemblyService.Assemble(request)", assemblySource, StringComparison.Ordinal);
        Assert.Contains("ItemContainerTemplateWarningService.Validate(", propertyElementSource, StringComparison.Ordinal);

        Assert.Contains("ResolvedObjectNodeAttachmentFinalizationPlan", attachmentPlanningSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedObjectNodeAttachmentValidationIssueKind.MultipleContentChildren", attachmentPlanningSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension", finalizationSource, StringComparison.Ordinal);
        Assert.Contains("BuildGenericPropertyElementAssignmentPlanDelegate", projectionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Setter_Resource_And_Template_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var stylesTemplatesSource = ReadBinderPartialSource("AvaloniaSemanticBinder.StylesTemplates.cs");
        var transformExtensionsSource = ReadBinderPartialSource("AvaloniaSemanticBinder.TransformExtensions.cs");
        var templateValidationSource = ReadBinderPartialSource("AvaloniaSemanticBinder.TemplateValidation.cs");
        var setterPropertyPlanSource = ReadFrameworkSharedBindingSource("SetterPropertyBindingPlanService.cs");
        var setterValuePlanningSource = ReadFrameworkSharedBindingSource("SetterValuePlanningService.cs");
        var resourceDefinitionSource = ReadFrameworkSharedBindingSource("ResourceDefinitionBindingService.cs");
        var templateDefinitionSource = ReadFrameworkSharedBindingSource("TemplateDefinitionBindingService.cs");

        Assert.Contains("SetterPropertyBindingPlanService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("SetterValuePlanningService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ResourceDefinitionBindingService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("TemplateDefinitionBindingService = new(", rootSource, StringComparison.Ordinal);

        Assert.Contains("SetterPropertyBindingPlanService.BuildPlan(", stylesTemplatesSource, StringComparison.Ordinal);
        Assert.Contains("SetterValuePlanningService.TryBuildPlan(", stylesTemplatesSource, StringComparison.Ordinal);
        Assert.Contains("ResourceDefinitionBindingService.BindResources(", transformExtensionsSource, StringComparison.Ordinal);
        Assert.Contains("TemplateDefinitionBindingService.BindTemplates(", transformExtensionsSource, StringComparison.Ordinal);
        Assert.Contains("ControlThemeBasedOnValidationService.Validate(", templateValidationSource, StringComparison.Ordinal);

        Assert.Contains("public sealed record ResolvedSetterIdentityPlan", setterPropertyPlanSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record SetterPropertyBindingPlan", setterPropertyPlanSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedSetterValuePlan", setterValuePlanningSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedResourceDefinition", resourceDefinitionSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedTemplateDefinition", templateDefinitionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Shared_Include_ResolveByName_And_NameScope_Services()
    {
        var rootSource = ReadBinderCompositionRootSource();
        var includesSource = ReadBinderPartialSource("AvaloniaSemanticBinder.Includes.cs");
        var restoredCompatibilitySource = ReadBinderPartialSource("AvaloniaSemanticBinder.RestoredCompatibility.cs");
        var includeBindingServiceSource = ReadFrameworkSharedBindingSource("IncludeBindingService.cs");
        var resolveByNameSource = ReadFrameworkSharedBindingSource("ResolveByNameBindingService.cs");

        Assert.Contains("IncludeBindingService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("ResolveByNameBindingService = new(", rootSource, StringComparison.Ordinal);
        Assert.Contains("NameScopeRegistrationSemanticsService", rootSource, StringComparison.Ordinal);

        Assert.Contains("IncludeBindingService.BindIncludes(", includesSource, StringComparison.Ordinal);
        Assert.Contains("ResolveByNameBindingService.HasSemantics(", restoredCompatibilitySource, StringComparison.Ordinal);
        Assert.Contains("ResolveByNameBindingService.TryBuildLiteralExpression(", restoredCompatibilitySource, StringComparison.Ordinal);

        Assert.Contains("XamlIncludeUriResolutionService", includeBindingServiceSource, StringComparison.Ordinal);
        Assert.Contains("TryResolveIncludeUri(", includeBindingServiceSource, StringComparison.Ordinal);
        Assert.Contains("BindingEventMarkupParser.TryParseResolveByNameReferenceToken(", resolveByNameSource, StringComparison.Ordinal);
        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideReference(", resolveByNameSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Binder_Uses_Profile_Conventions_And_Avalonia_Helper_Services()
    {
        var binderSource = ReadBinderSource();
        var documentFeatureSource = ReadDocumentFeatureEnricherSource();
        var selectorPropertyReferencesSource = ReadBinderPartialSource("AvaloniaSemanticBinder.SelectorPropertyReferences.cs");
        var styleQuerySemanticsSource = ReadAvaloniaBindingServiceSource("AvaloniaStyleQuerySemantics.cs");

        Assert.Contains("SemanticConventions.InheritDataTypeFromItemsAttributeMetadataNames", binderSource, StringComparison.Ordinal);
        Assert.Contains("SemanticConventions.KnownTemplateKinds.ToImmutableHashSet(StringComparer.Ordinal)", binderSource, StringComparison.Ordinal);
        Assert.Contains("SemanticConventions.ControlThemeDefinitionRootTypeNames", binderSource, StringComparison.Ordinal);
        Assert.Contains("AvaloniaBindingEnumSemantics.TryMapBindingModeToken(", binderSource, StringComparison.Ordinal);

        Assert.Contains("XamlPropertyTokenSemantics.IsPropertyElementName(", documentFeatureSource, StringComparison.Ordinal);
        Assert.Contains("FrameworkPropertyReferenceResolutionService.TryResolveReferenceExpression(", selectorPropertyReferencesSource, StringComparison.Ordinal);
        Assert.Contains("public static bool TryParse(", styleQuerySemanticsSource, StringComparison.Ordinal);
        Assert.Contains("XamlTokenSplitSemantics.TrySplitAtFirstSeparator(", styleQuerySemanticsSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_Composition_Root_Is_Thin()
    {
        var source = ReadEmitterCompositionRootSource();

        Assert.Contains("var context = CreateEmitContext(viewModel);", source, StringComparison.Ordinal);
        Assert.Contains("EmitPreamble(context, sourceBuilder);", source, StringComparison.Ordinal);
        Assert.Contains("EmitTypeOpening(context, sourceBuilder);", source, StringComparison.Ordinal);
        Assert.Contains("EmitArtifactRegistrationMembers(context, sourceBuilder);", source, StringComparison.Ordinal);
        Assert.Contains("EmitObjectGraphMembers(context, sourceBuilder);", source, StringComparison.Ordinal);
        Assert.Contains("EmitRuntimeMembers(context, sourceBuilder);", source, StringComparison.Ordinal);

        Assert.DoesNotContain("__RegisterXamlSourceGenArtifacts()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__PopulateGeneratedObjectGraph(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__BuildGeneratedControlTheme(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("__InitializeXamlSourceGenComponent(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_Uses_Shared_Graph_And_Runtime_Services()
    {
        var emitterSource = ReadEmitterSource();
        var recursiveGraphSource = ReadFrameworkSharedEmissionSource("RecursiveObjectGraphEmissionService.cs");
        var objectNodeBodySource = ReadFrameworkSharedEmissionSource("ObjectNodeBodyEmissionService.cs");
        var hotReloadSource = ReadFrameworkSharedEmissionSource("HotReloadRuntimeEmissionService.cs");

        Assert.Contains("RecursiveObjectGraphEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeBodyEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeMemberEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("CollectionAttachmentEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("DeferredDictionaryEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("DeferredTemplateScaffoldEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeLifecycleEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ObjectNodeEventSubscriptionEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ContentChildAttachmentEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("AttachedNodeValueEmissionService = new();", emitterSource, StringComparison.Ordinal);
        Assert.Contains("RecursiveObjectGraphEmissionService.EmitNode(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("HotReloadRuntimeEmissionService.BuildRootHotReloadCollectionMembers(", emitterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string EmitNode(", emitterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool HasExplicitContentValue(", emitterSource, StringComparison.Ordinal);

        Assert.Contains("_emitObjectNodeBody(", recursiveGraphSource, StringComparison.Ordinal);
        Assert.Contains("_objectNodeMemberEmissionService.EmitPropertyAssignments(", objectNodeBodySource, StringComparison.Ordinal);
        Assert.Contains("_objectNodeEventSubscriptionEmissionService.EmitSubscriptions(", objectNodeBodySource, StringComparison.Ordinal);
        Assert.Contains("_eventBindingEmissionService.ResolveEmittedMethodName(", hotReloadSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitter_Uses_Shared_Scaffold_Literal_And_Identifier_Services()
    {
        var emitterSource = ReadEmitterCompositionRootSource();
        var viewModelScaffoldSource = ReadFrameworkSharedEmissionSource("ViewModelScaffoldEmissionService.cs");
        var literalSource = ReadFrameworkSharedEmissionSource("CSharpLiteralEmissionService.cs");
        var identifierSource = ReadFrameworkSharedEmissionSource("IdentifierSanitizationService.cs");
        var attachedNodeValueSource = ReadFrameworkSharedEmissionSource("AttachedNodeValueEmissionService.cs");
        var clrObjectNodeEmissionSource = ReadFrameworkSharedEmissionSource("ClrObjectNodeEmissionService.cs");

        Assert.Contains("CSharpLiteralEmissionService = new();", emitterSource, StringComparison.Ordinal);
        Assert.Contains("IdentifierSanitizationService = new();", emitterSource, StringComparison.Ordinal);
        Assert.Contains("ViewModelScaffoldEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.Contains("InitializeComponentBodyEmissionService = new(", emitterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string EscapeStringLiteral(", emitterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static string BuildHintName(", emitterSource, StringComparison.Ordinal);

        Assert.Contains("public int EstimateSourceCapacity(", viewModelScaffoldSource, StringComparison.Ordinal);
        Assert.Contains("public void EmitCompiledBindingAccessorMethods(", viewModelScaffoldSource, StringComparison.Ordinal);
        Assert.Contains("public string BuildHintName(", viewModelScaffoldSource, StringComparison.Ordinal);
        Assert.Contains("public string EscapeStringLiteral(", literalSource, StringComparison.Ordinal);
        Assert.Contains("public string NormalizeCommentText(", literalSource, StringComparison.Ordinal);
        Assert.Contains("public string SanitizeIdentifier(", identifierSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension", attachedNodeValueSource, StringComparison.Ordinal);
        Assert.Contains("ResolvedObjectNodeSemanticFlags.RequiresBaseUriConstructor", clrObjectNodeEmissionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_And_Parsing_Layers_Use_Current_Shared_Semantics()
    {
        var bindingEventParserSource = ReadCoreParsingSource("BindingEventMarkupParser.cs");
        var runtimeMarkupSource = ReadRuntimeAvaloniaSource("SourceGenMarkupExtensionRuntime.cs");
        var runtimeControlThemeRegistrySource = ReadRuntimeAvaloniaSource("XamlControlThemeRegistry.cs");
        var schemeResolverSource = ReadFrameworkSharedRuntimeSource("SchemeBasedDocumentUriResolver.cs");

        Assert.Contains("XamlMarkupExtensionNameSemantics.Classify(", bindingEventParserSource, StringComparison.Ordinal);
        Assert.Contains("BindingSourceQuerySemantics.TryParseElementName(", bindingEventParserSource, StringComparison.Ordinal);
        Assert.Contains("EventBindingSourceModeSemantics.TryParse(", bindingEventParserSource, StringComparison.Ordinal);

        Assert.Contains("ClassifyDeferredBindingFailure(", runtimeMarkupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("exception.Message.IndexOf(\"DataContext\"", runtimeMarkupSource, StringComparison.Ordinal);
        Assert.Contains("StaticResourceReferenceParser.TryExtractResourceKey(", runtimeControlThemeRegistrySource, StringComparison.Ordinal);
        Assert.Contains("XamlIncludePathSemantics.GetDirectory(", schemeResolverSource, StringComparison.Ordinal);
        Assert.Contains("XamlIncludePathSemantics.CombinePath(", schemeResolverSource, StringComparison.Ordinal);
    }

    private static string RepositoryRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string ReadBinderSource() =>
        ReadJoinedFiles(
            Path.Combine(RepositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "Binding"),
            "AvaloniaSemanticBinder*.cs");

    private static string ReadBinderCompositionRootSource() =>
        ReadFile("src", "XamlToCSharpGenerator.Avalonia", "Binding", "AvaloniaSemanticBinder.cs");

    private static string ReadBinderPartialSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Avalonia", "Binding", fileName);

    private static string ReadEmitterSource() =>
        ReadJoinedFiles(
            Path.Combine(RepositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "Emission"),
            "AvaloniaCodeEmitter*.cs");

    private static string ReadEmitterCompositionRootSource() =>
        ReadFile("src", "XamlToCSharpGenerator.Avalonia", "Emission", "AvaloniaCodeEmitter.cs");

    private static string ReadDocumentFeatureEnricherSource() =>
        ReadFile("src", "XamlToCSharpGenerator.Avalonia", "Parsing", "AvaloniaDocumentFeatureEnricher.cs");

    private static string ReadFrameworkSharedBindingSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Framework.Shared", "Binding", fileName);

    private static string ReadFrameworkSharedEmissionSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Framework.Shared", "Emission", fileName);

    private static string ReadFrameworkSharedRuntimeSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Framework.Shared", "Runtime", fileName);

    private static string ReadAvaloniaBindingServiceSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Avalonia", "Binding", "Services", fileName);

    private static string ReadCoreParsingSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Core", "Parsing", fileName);

    private static string ReadRuntimeAvaloniaSource(string fileName) =>
        ReadFile("src", "XamlToCSharpGenerator.Runtime.Avalonia", fileName);

    private static string ReadFile(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, Path.Combine(segments)));

    private static string ReadJoinedFiles(string directory, string searchPattern)
    {
        var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            files.Select(File.ReadAllText));
    }
}
