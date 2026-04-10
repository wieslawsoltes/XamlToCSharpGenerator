using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Avalonia.Framework;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.Framework.Shared.Runtime;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private const string AvaloniaDefaultXmlNamespaceWithSlash = "https://github.com/avaloniaui/";
    private const string AvaloniaXmlnsDefinitionAttributeMetadataName = "Avalonia.Metadata.XmlnsDefinitionAttribute";
    private const string SourceGenXmlnsDefinitionAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute";
    private const string SourceGenXamlTypeAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlTypeAliasAttribute";
    private const string SourceGenXamlPropertyAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlPropertyAliasAttribute";
    private const string SourceGenXamlFrameworkPropertyAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlFrameworkPropertyAliasAttribute";
    private const string SourceGenXamlAvaloniaPropertyAliasAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXamlAvaloniaPropertyAliasAttribute";
    private const string AvaloniaPropertyMetadataName = "Avalonia.AvaloniaProperty";
    private const string AvaloniaDocumentUriScheme = "avares";
    private const string MarkupContextServiceProviderToken = "__AXSG_CTX_SERVICE_PROVIDER__";
    private const string MarkupContextRootObjectToken = "__AXSG_CTX_ROOT_OBJECT__";
    private const string MarkupContextIntermediateRootObjectToken = "__AXSG_CTX_INTERMEDIATE_ROOT_OBJECT__";
    private const string MarkupContextTargetObjectToken = "__AXSG_CTX_TARGET_OBJECT__";
    private const string MarkupContextTargetPropertyToken = "__AXSG_CTX_TARGET_PROPERTY__";
    private const string MarkupContextBaseUriToken = "__AXSG_CTX_BASE_URI__";
    private const string MarkupContextParentStackToken = "__AXSG_CTX_PARENT_STACK__";
    private const string ExpressionSourceParameterName = "source";
    private static readonly MarkupExpressionParser StrictMarkupExpressionParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: false));
    private static readonly MarkupExpressionParser LegacyMarkupExpressionParser = new(
        new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));
    private static readonly TransformExtensionResolutionService TransformExtensionResolutionService = new(
        EnumerateAssemblies,
        XamlTypeTokenSemantics.TrimGlobalQualifier,
        NormalizePropertyName);
    private static readonly ConditionalXamlEvaluationService ConditionalXamlEvaluationService = new();
    private static readonly ExplicitConstructionBindingService ExplicitConstructionBindingService = new(
        ConditionalXamlEvaluationService);
    private static readonly IXamlFrameworkDocumentUriResolver AvaloniaDocumentUriResolver =
        new SchemeBasedDocumentUriResolver(AvaloniaDocumentUriScheme);
    private static readonly XamlIncludeUriResolutionService IncludeUriResolutionService = new();
    private static readonly IncludeBindingService IncludeBindingService = new(
        AvaloniaDocumentUriResolver,
        IncludeUriResolutionService,
        ConditionalXamlEvaluationService.ShouldSkipBranch);
    private static readonly MarkupContextTokenSet MarkupContextTokens = new(
        MarkupContextServiceProviderToken,
        MarkupContextRootObjectToken,
        MarkupContextIntermediateRootObjectToken,
        MarkupContextTargetObjectToken,
        MarkupContextTargetPropertyToken,
        MarkupContextBaseUriToken,
        MarkupContextParentStackToken);
    private static readonly MarkupTypeConversionService MarkupTypeConversionSemanticsService = new(
        Escape,
        ResolveTypeToken,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        MarkupContextTokens);
    private static readonly ResourceKeyResolutionService ResourceKeyResolutionService = new(
        TryParseMarkupExtension,
        ResolveTypeToken,
        MarkupTypeConversionSemanticsService.TryResolveStaticMemberExpression,
        Escape);
    private static readonly XamlPrimitiveMarkupExtensionConversionService PrimitiveMarkupExtensionConversionService = new(
        Escape);
    private static readonly MarkupExtensionActivationService MarkupExtensionActivationService = new(
        ResolveTypeToken,
        static compilation => ResolveContractType(compilation, TypeContractId.AvaloniaMarkupExtensionBase),
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        static (
            string value,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertValueExpression(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        static (
            string value,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertMarkupExtensionExpression(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        Escape,
        MarkupTypeConversionSemanticsService,
        MarkupContextTokens);
    private static readonly MarkupRuntimeOperationResolutionService MarkupRuntimeOperationResolutionService = new(
        ResourceKeyResolutionService);
    private static readonly MarkupRuntimeOperationEmissionService MarkupRuntimeOperationEmissionService = new(
        static compilation => ResolveContractType(compilation, TypeContractId.DynamicResourceExtension) is not null,
        BuildStaticResourceOperationExpression,
        BuildDynamicResourceOperationExpression,
        BuildReferenceOperationExpression,
        BuildTypedStaticResourceCoercionExpression,
        MarkupTypeConversionSemanticsService.WrapWithTargetTypeCast);
    private static readonly WritablePropertyResolutionService WritablePropertyResolutionService = new();
    private static readonly MarkupOptionValueExpressionService MarkupOptionValueExpressionService = new(
        static (
            string value,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertValueExpression(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression));
    private static readonly CommonMarkupExtensionConversionService CommonMarkupExtensionConversionService = new(
        ResolveContractType,
        ResolveTypeToken,
        MarkupTypeConversionSemanticsService.TryResolveStaticMemberExpression,
        static (
            string? rawToken,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            MarkupOptionValueExpressionService.TryConvert(
                rawToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression),
        TryParseRelativeSourceMarkup,
        TryBuildRelativeSourceExpression);
    private static readonly ObjectInitializerExpressionService ObjectInitializerExpressionService = new();
    private static readonly ResolveByNameBindingService ResolveByNameBindingService = new(
        TypeSymbolLookupSemanticsService.FindProperty,
        Escape,
        MarkupTypeConversionSemanticsService.WrapWithTargetTypeCast,
        TryParseMarkupExtension,
        MarkupContextTokens,
        ImmutableArray.Create(
            "ResolveByNameAttribute",
            "global::Avalonia.Controls.ResolveByNameAttribute"));
    private static readonly ItemContainerTemplateWarningService ItemContainerTemplateWarningService = new(
        ResolveContractType,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        IsDataTemplateNode,
        TryGetTemplateContentNode,
        ResolveObjectTypeSymbol);

    private static readonly ImmutableHashSet<string> KnownMarkupExtensionNames = ImmutableHashSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "Binding",
        "Bind",
        "x:Bind",
        "CompiledBinding",
        "ReflectionBinding",
        "StaticResource",
        "DynamicResource",
        "TemplateBinding",
        "RelativeSource",
        "OnPlatform",
        "OnFormFactor",
        "x:Reference",
        "Reference",
        "ResolveByName",
        "CSharp",
        "x:Static",
        "Static",
        "x:Type",
        "Type",
        "x:Null",
        "Null",
        "x:String",
        "String",
        "x:Char",
        "Char",
        "x:Byte",
        "Byte",
        "x:SByte",
        "SByte",
        "x:Int16",
        "Int16",
        "x:UInt16",
        "UInt16",
        "x:Int32",
        "Int32",
        "x:UInt32",
        "UInt32",
        "x:Int64",
        "Int64",
        "x:UInt64",
        "UInt64",
        "x:Single",
        "Single",
        "x:Double",
        "Double",
        "x:Decimal",
        "Decimal",
        "x:DateTime",
        "DateTime",
        "x:TimeSpan",
        "TimeSpan",
        "x:Uri",
        "Uri",
        "x:Array",
        "Array");

    private static readonly string[] AvaloniaDefaultNamespaceCandidateSeed =
    [
        "Avalonia.Controls.",
        "Avalonia.Controls.Primitives.",
        "Avalonia.Controls.Presenters.",
        "Avalonia.Controls.Shapes.",
        "Avalonia.Controls.Documents.",
        "Avalonia.Controls.Chrome.",
        "Avalonia.Controls.Embedding.",
        "Avalonia.Controls.Notifications.",
        "Avalonia.Controls.Converters.",
        "Avalonia.Markup.Xaml.Templates.",
        "Avalonia.Markup.Xaml.Styling.",
        "Avalonia.Markup.Xaml.MarkupExtensions.",
        "Avalonia.Styling.",
        "Avalonia.Controls.Templates.",
        "Avalonia.Input.",
        "Avalonia.Automation.",
        "Avalonia.Dialogs.",
        "Avalonia.Dialogs.Internal.",
        "Avalonia.Layout.",
        "Avalonia.Media.",
        "Avalonia.Media.Transformation.",
        "Avalonia.Media.Imaging.",
        "Avalonia.Animation.",
        "Avalonia.Animation.Easings.",
        "Avalonia."
    ];

    private static readonly AsyncLocal<ResolvedTransformExtensions?> ActiveTransformExtensions = new();
    private static readonly AsyncLocal<GeneratorOptions?> ActiveGeneratorOptions = new();
    private static readonly AsyncLocal<TypeResolutionDiagnosticContext?> ActiveTypeResolutionDiagnosticContext = new();
    private static readonly AsyncLocal<ITypeSymbolCatalog?> ActiveTypeSymbolCatalog = new();
    private static readonly XamlFrameworkSemanticConventions SemanticConventions =
        AvaloniaFrameworkSemanticConventions.Instance;
    private static readonly SemanticContractMap AvaloniaSemanticContractMap =
        XamlToCSharpGenerator.Avalonia.Framework.AvaloniaSemanticContractMap.Instance;
    private static readonly CSharpExpressionClassificationService ExpressionClassificationService = new(
        TryParseMarkupExtension,
        KnownMarkupExtensionNames,
        TryResolveMarkupExtensionType);
    private static readonly XamlTypeExpressionResolutionService TypeExpressionResolutionService = new(
        TryParseMarkupExtension,
        ResolveTypeToken);
    private static readonly CompiledBindingSourceTypeResolutionService CompiledBindingSourceTypeResolutionService = new(
        ResolveTypeFromTypeExpression,
        ResolveTypeToken,
        static (compilation, document) =>
            ResolveTypeSymbol(
                compilation,
                document.RootObject.XmlNamespace,
                document.RootObject.XmlTypeName));
    private static readonly TypeResolutionNamespaceDiscoveryService NamespaceDiscoveryService = new(
        AvaloniaDefaultNamespaceCandidateSeed,
        IsXmlnsDefinitionAttribute,
        IsAvaloniaXmlnsDefinitionAttribute,
        IsAvaloniaDefaultXmlNamespace,
        NormalizeXmlNamespaceKey,
        IsAccessibleTypeCandidate);
    private static readonly TypeResolutionPolicyService TypeResolutionPolicyService = new(
        TryResolveTypeFromNamespacePrefixes,
        TryGetImplicitProjectNamespaceRoot,
        GetProjectNamespaceCandidates,
        GetAvaloniaDefaultNamespaceCandidates,
        ResolveTypeSymbol,
        IsTypeResolutionCompatibilityFallbackEnabled,
        IsStrictTypeResolutionMode,
        IsAvaloniaDefaultXmlNamespace);
    private static readonly MarkupObjectElementTypeResolutionService MarkupObjectElementTypeResolutionService = new(
        IsAvaloniaDefaultXmlNamespace,
        Xaml2006.NamespaceName,
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            new[]
            {
                new KeyValuePair<string, TypeContractId>("StaticResource", TypeContractId.StaticResourceExtension),
                new KeyValuePair<string, TypeContractId>("StaticResourceExtension", TypeContractId.StaticResourceExtension),
                new KeyValuePair<string, TypeContractId>("DynamicResource", TypeContractId.DynamicResourceExtension),
                new KeyValuePair<string, TypeContractId>("DynamicResourceExtension", TypeContractId.DynamicResourceExtension),
                new KeyValuePair<string, TypeContractId>("OnPlatform", TypeContractId.OnPlatformExtension),
                new KeyValuePair<string, TypeContractId>("OnPlatformExtension", TypeContractId.OnPlatformExtension),
                new KeyValuePair<string, TypeContractId>("OnFormFactor", TypeContractId.OnFormFactorExtension),
                new KeyValuePair<string, TypeContractId>("OnFormFactorExtension", TypeContractId.OnFormFactorExtension),
                new KeyValuePair<string, TypeContractId>("On", TypeContractId.OnMarkupExtension)
            }));
    private static readonly XamlFragmentDetectionService RuntimeXamlFragmentDetectionService = new();
    private static readonly RuntimeXamlFragmentExpressionService RuntimeXamlFragmentExpressionService = new(
        RuntimeXamlFragmentDetectionService.IsValidFragment,
        Escape,
        WrapWithTargetTypeCast,
        static (escapedXaml, escapedBaseUri) =>
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideRuntimeXamlValue(\"" +
            escapedXaml +
            "\", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", \"" +
            escapedBaseUri +
            "\", " +
            MarkupContextParentStackToken +
            ")");
    private static readonly CollectionAddBindingService CollectionAddService = new(
        ResolveTypeToken,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        TryGetCollectionElementType,
        TryConvertValueForCollectionAdd,
        Escape);
    private static readonly ObjectNodeAttachmentPlanningService ObjectNodeAttachmentPlanningService = new(
        FindBindableProperty,
        TypeSymbolLookupSemanticsService.FindProperty,
        CollectionAddService.HasDirectAddMethod,
        CollectionAddService.HasDictionaryAddMethod,
        IsStyleBaseType,
        ResolveTypeToken,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        CollectionAddService.ResolveCollectionAddInstructionsForValues);
    private static readonly ObjectNodeFinalizationService ObjectNodeFinalizationService = new(
        ShouldUseServiceProviderConstructor,
        IsUsableDuringInitialization,
        ObjectNodeAttachmentPlanningService.ResolveChildAddInstructions,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        IsMarkupExtensionObjectNodeType);
    private static readonly ObjectNodeConstructionPlanningService ObjectNodeConstructionPlanningService = new(
        TryBuildExplicitConstructionExpressionForObjectNode,
        TypeSymbolLookupSemanticsService.FindProperty,
        TryBuildInlineTextContentPropertyAssignment,
        TryBuildInlineTextContentCollectionAssignment,
        TryBuildInlineTextFactoryExpression);
    private static readonly ObjectNodePropertyElementBindingService ObjectNodePropertyElementBindingService = new(
        ConditionalXamlEvaluationService.ShouldSkipBranch,
        IsDesignTimePropertyToken,
        ResolvePropertyAlias,
        ResolvePropertyElementSetterTargetType,
        TryExtractInlineCSharpObjectNodeCode,
        TryBindInlineObjectNodePropertyElementCodeSubscription);
    private static readonly ObjectNodePropertyElementAssignmentPlanningService ObjectNodePropertyElementAssignmentPlanningService = new(
        TypeSymbolLookupSemanticsService.FindProperty,
        CanMergeDictionaryProperty,
        TryBuildPropertyElementSpecialAssignmentPlan,
        MaterializePropertyElementValuesForTargetTypeIfNeeded,
        HasAssignBindingAttribute);
    private static readonly ObjectNodePropertyElementProjectionService ObjectNodePropertyElementProjectionService = new(
        TryResolveAliasedFrameworkPropertyElementAssignment,
        TryResolveOwnerQualifiedFrameworkPropertyElementAssignment,
        BuildGenericPropertyElementAssignmentPlan,
        ValidatePropertyElementTargetProperty,
        TryResolveFrameworkPropertyElementAssignment);
    private static readonly ObjectNodeAttachedPropertyAssignmentBindingService ObjectNodeAttachedPropertyAssignmentBindingService = new(
        TryBindAttachedPropertyAssignment,
        TryBindAttachedStaticSetterAssignment,
        TryBindAttachedClassPropertyAssignment,
        TryBindAttachedEventSubscription);
    private static readonly ObjectNodeAssemblyService ObjectNodeAssemblyService = new(
        TryNormalizePlatformMarkupExtensionChildren,
        ProjectObjectNodePropertyElementAssignmentsToImmutable,
        static (
            node,
            objectType,
            typeName,
            contentPropertyName,
            compilation,
            diagnostics,
            document,
            options,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            currentSetterTargetType,
            currentBindingPriorityScope,
            rootTypeSymbol,
            propertyAssignments,
            propertyElementAssignments,
            children) =>
            PlanObjectNodeConstruction(
                node,
                objectType,
                typeName,
                contentPropertyName,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                (BindingPriorityScope)currentBindingPriorityScope,
                rootTypeSymbol,
                propertyAssignments,
                propertyElementAssignments,
                children),
        FinalizeObjectNodeAttachmentPlan,
        ReportObjectNodeAttachmentValidationIssues,
        ResolveObjectNodeNameScopeRegistration,
        BuildObjectNodeKeyExpression,
        FinalizeObjectNode,
        IsBindingObjectType);
    private static readonly ObjectNodeStandardPropertyAssignmentBindingService ObjectNodeStandardPropertyAssignmentBindingService = new(
        TryBindCollectionLiteralPropertyAssignment,
        TryBindClrPropertyAssignment,
        TryBindEventSubscription,
        TryBindFrameworkPropertyAssignment);
    private static readonly ClrPropertyAssignmentBindingService ClrPropertyAssignmentBindingService = new(
        static type => MarkupTypeConversionSemanticsService.IsFrameworkPropertyType(type, AvaloniaPropertyMetadataName),
        TryResolveAvaloniaPropertyReferenceExpression,
        static (
            ClrPropertyAssignmentBindingRequest request,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics,
            bool allowCompiledBindingRegistration,
            string? compiledBindingAccessorPlaceholderToken,
            out ResolvedPropertyAssignment? resolvedAssignment) =>
            TryBindAvaloniaPropertyAssignment(
                request.OwnerType,
                request.OwnerTypeName,
                request.Property.Name,
                request.Assignment,
                request.Compilation,
                request.Document,
                request.Options,
                diagnostics,
                request.CompiledBindings,
                request.UnsafeAccessors,
                request.CompileBindingsEnabled,
                request.AssignmentDataType,
                request.Property.Type,
                (BindingPriorityScope)request.BindingPriorityScope,
                request.CurrentSetterTargetType,
                request.RootTypeSymbol,
                out resolvedAssignment,
                allowCompiledBindingRegistration,
                compiledBindingAccessorPlaceholderToken,
                isInsideDataTemplate: request.IsInsideDataTemplate,
                xBindDefaultMode: request.XBindDefaultMode,
                currentNode: request.CurrentNode),
        TryParseInlineCSharpMarkupExtensionCode,
        TryBuildInlineCodeBindingExpression,
        IsPotentialCSharpExpressionMarkup,
        TryResolveImplicitCSharpShorthandExpression,
        TryConvertCSharpExpressionMarkupToBindingExpression,
        TryParseXBindMarkup,
        static (
            Compilation compilation,
            XamlDocumentModel document,
            XamlObjectNode currentNode,
            XBindMarkup markup,
            INamedTypeSymbol? ambientDataContextType,
            INamedTypeSymbol? rootType,
            INamedTypeSymbol? targetType,
            ITypeSymbol bindingValueType,
            int bindingPriorityScope,
            bool isInsideDataTemplate,
            string defaultMode,
            out string bindingExpression,
            out string? resultTypeName,
            out string errorCode,
            out string errorMessage) =>
            TryBuildXBindBindingExpression(
                compilation,
                document,
                currentNode,
                markup,
                ambientDataContextType,
                rootType,
                targetType,
                bindingValueType,
                (BindingPriorityScope)bindingPriorityScope,
                isInsideDataTemplate,
                defaultMode,
                out bindingExpression,
                out resultTypeName,
                out errorCode,
                out errorMessage),
        CanAssignBindingValue,
        TryParseBindingMarkup,
        TryReportBindingSourceConflict,
        TryResolveCompiledBindingSourceType,
        static (
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol sourceType,
            string rawPath,
            ITypeSymbol? targetPropertyType,
            ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
            out CompiledBindingAccessorResolutionResult resolution,
            out string errorMessage) =>
        {
            if (TryBuildCompiledBindingAccessorExpression(
                    compilation,
                    document,
                    sourceType,
                    rawPath,
                    targetPropertyType,
                    unsafeAccessors,
                    out var sharedResolution,
                    out errorMessage))
            {
                resolution = new CompiledBindingAccessorResolutionResult(
                    sharedResolution.AccessorExpression,
                    sharedResolution.NormalizedPath,
                    sharedResolution.ResultTypeName,
                    sharedResolution.ResultTypeSymbol,
                    sharedResolution.DependencyNames);
                return true;
            }

            resolution = default;
            return false;
        },
        BuildCompiledBindingAccessorPlaceholderToken,
        static (
            Compilation compilation,
            XamlDocumentModel document,
            BindingMarkup bindingMarkup,
            ITypeSymbol targetType,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryBuildBindingValueExpression(
                compilation,
                document,
                bindingMarkup,
                targetType,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        TryParseMarkupExtension,
        IsSetterType,
        RuntimeXamlFragmentExpressionService.TryBuildExpression,
        ResolveByNameBindingService.HasSemantics,
        ResolveByNameBindingService.TryBuildLiteralExpression,
        static (
            string rawValue,
            INamedTypeSymbol delegateType,
            INamedTypeSymbol? rootTypeSymbol,
            out string expression) =>
            EventHandlerBindingService.TryBuildDelegateMethodGroupValueExpression(
                rawValue,
                delegateType,
                rootTypeSymbol,
                out expression),
        TryResolveClrPropertySetterValueWithPolicy,
        TryConvertClrPropertyLiteralValue,
        CreateClrPropertyAssignment,
        HasAssignBindingAttribute);
    private static readonly XamlLiteralConversionPrimitivesService LiteralConversionPrimitivesService = new(
        TryConvertValueExpressionForLiteralPrimitives,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        TryGetCollectionSplitConfiguration);
    private static readonly XBindExpressionSemanticService XBindExpressionSemanticService = new(
        ResolveTypeToken,
        ResolveTypeSymbol,
        IsXBindNameScopeBoundary,
        Escape);
    private static readonly XBindSourceConfigurationService XBindSourceConfigurationService = new(
        ResolveTypeToken,
        XBindExpressionSemanticService.TryResolveNamedElementType,
        TryBuildRelativeSourceExpression,
        TryExtractReferenceElementName,
        TryBuildXBindExplicitSourceExpression);
    private static readonly XBindBindBackExpressionService XBindBindBackExpressionService = new(
        XBindExpressionSemanticService,
        ResolveContractType);
    private static readonly XBindOptionExpressionService XBindOptionExpressionService = new(
        ResolveContractType,
        WritablePropertyResolutionService.TryGetWritableProperty,
        MarkupOptionValueExpressionService.TryConvert,
        static bindingPriorityScope => GetDefaultBindingPriorityToken((BindingPriorityScope)bindingPriorityScope),
        TypeContractId.AvaloniaBinding,
        "global::Avalonia.Data.UpdateSourceTrigger.Default",
        "global::Avalonia.Data.BindingPriority.LocalValue");
    private static readonly EventBindingSemanticBindingService EventBindingSemanticBindingService = new(
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        static compilation => ResolveContractType(compilation, TypeContractId.SystemICommand));
    private static readonly EventHandlerBindingService EventHandlerBindingService = new(
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        MarkupContextRootObjectToken);
    private static readonly FrameworkRoutedEventResolutionService RoutedEventResolutionService = new(
        ResolveContractType,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        "Avalonia.Interactivity",
        "RoutedEvent");
    private static readonly EventBindingDefinitionService EventBindingDefinitionService = new(
        EventBindingSemanticBindingService,
        TryParseMarkupExtension,
        TryConvertUntypedValueExpression);
    private static readonly EventSubscriptionBindingService EventSubscriptionBindingService = new(
        TypeSymbolLookupSemanticsService.FindEvent,
        NormalizePropertyName,
        RoutedEventResolutionService,
        EventBindingDefinitionService,
        EventHandlerBindingService,
        TryParseInlineCSharpMarkupExtensionCode,
        TryParseXBindMarkup,
        TryParseMarkupExtension,
        TryBuildXBindEventBindingDefinition,
        TryBindInlineEventLambda);
    private static readonly XBindEventBindingDefinitionService XBindEventBindingDefinitionService = new(
        TryResolveExplicitXBindSourceType,
        XBindSourceConfigurationService.TryResolveSourceConfiguration,
        EventBindingSemanticBindingService.TryBuildDelegateSignature,
        XBindExpressionSemanticService.TryLowerExpression,
        XBindExpressionSemanticService.BuildEventCandidateBodies,
        XBindExpressionSemanticService.BuildPathReferenceExpression,
        EventBindingPathSemantics.BuildGeneratedMethodName,
        EventBindingSemanticBindingService.BuildInlineStableKey);
    private static readonly BindingObjectNodeMarkupParser BindingObjectNodeMarkupParser = new(
        Xaml2006.NamespaceName,
        NormalizePropertyName,
        TryParseMarkupExtension);
    private static readonly RelativeSourceBindingPlanService RelativeSourceBindingPlanService = new(
        static compilation => ResolveContractType(compilation, TypeContractId.AvaloniaRelativeSource) is not null,
        TryMapRelativeSourceMode,
        TryMapTreeType,
        ResolveTypeToken);
    private static readonly BindingInitializerPlanService BindingInitializerPlanService = new(
        NormalizeRuntimeBindingPath,
        TryMapBindingMode,
        TryBuildRelativeSourceExpression,
        WritablePropertyResolutionService.TryGetWritableProperty,
        static (
            string value,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertValueExpression(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        Escape,
        static bindingPriorityScope => GetDefaultBindingPriorityToken((BindingPriorityScope)bindingPriorityScope));
    private static readonly BindingRuntimeProjectionService BindingRuntimeProjectionService = new(
        ResolveContractType,
        BindingInitializerPlanService,
        ObjectInitializerExpressionService,
        Escape);
    private static readonly FrameworkBindingProjectionService FrameworkBindingProjectionService = new(
        ResolveContractType,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        BindingRuntimeProjectionService,
        BindingInitializerPlanService,
        ObjectInitializerExpressionService,
        MarkupContextTokens,
        static escapedNormalizedPath => "new global::Avalonia.Data.Binding(\"" + escapedNormalizedPath + "\")",
        static escapedNormalizedPath =>
            "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
            escapedNormalizedPath +
            "\")",
        static propertyExpression => "new global::Avalonia.Data.TemplateBinding(" + propertyExpression + ")",
        "new global::Avalonia.Data.Binding(\".\") { RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.TemplatedParent), Priority = global::Avalonia.Data.BindingPriority.Template }",
        TryMapBindingMode,
        WritablePropertyResolutionService.TryGetWritableProperty,
        TryResolveAvaloniaPropertyReferenceExpression,
        TypeContractId.AvaloniaBindingBase,
        TypeContractId.AvaloniaBindingInterface,
        TypeContractId.AvaloniaBindingInterface2,
        TypeContractId.AvaloniaBinding,
        TypeContractId.AvaloniaReflectionBindingExtension,
        TypeContractId.AvaloniaTemplateBinding,
        "global::Avalonia.Data.AssignBindingAttribute");
    private static readonly FrameworkPropertyReferenceResolutionService FrameworkPropertyReferenceResolutionService = new(
        XamlPropertyReferenceTokenSemantics.TryNormalize,
        XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty,
        ResolveTypeToken,
        static (INamedTypeSymbol ownerType, string propertyName, out INamedTypeSymbol resolvedOwnerType, out IFieldSymbol propertyField) =>
            TryFindAvaloniaPropertyField(ownerType, propertyName, out resolvedOwnerType, out propertyField),
        static propertyFieldType => TryGetAvaloniaPropertyValueType(propertyFieldType));
    private static readonly TypedLiteralValueConversionService TypedLiteralValueConversionService = new(
        MarkupExpressionEnvelopeSemantics.UnescapeEscapedLiteral,
        Escape,
        ResolveTypeFromTypeExpression,
        TryConvertTimeSpanLiteralExpression,
        TryConvertStaticPropertyValueExpression,
        static (
            ITypeSymbol type,
            string value,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertCollectionLiteralExpression(
                type,
                value,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        TryConvertEnumValueExpression,
        static (
            ITypeSymbol type,
            string value,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertAvaloniaSpecificLiteralExpression(
                type,
                value,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        static (
            ITypeSymbol type,
            string value,
            Compilation compilation,
            out string expression,
            out ResolvedValueRequirements requirements,
            ImmutableArray<AttributeData> converterAttributes) =>
            MarkupTypeConversionSemanticsService.TryConvertByTypeConverter(
                type,
                value,
                compilation,
                out expression,
                out requirements,
                converterAttributes),
        static (ITypeSymbol type, string value, out string expression) =>
            MarkupTypeConversionSemanticsService.TryConvertByStaticParseMethod(type, value, out expression));
    private static readonly ValueConversionSemanticService ValueConversionSemanticService = new(
        TryParseMarkupExtension,
        TryParseBindingMarkup,
        TryParseReflectionBindingMarkup,
        TryConvertXamlPrimitiveMarkupExtension,
        static (
            Compilation compilation,
            XamlDocumentModel document,
            BindingMarkup bindingMarkup,
            ITypeSymbol targetType,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryBuildBindingValueExpression(
                compilation,
                document,
                bindingMarkup,
                targetType,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        static (
            MarkupExtensionInfo markup,
            ITypeSymbol targetType,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out string expression) =>
            TryConvertGenericMarkupExtensionExpression(
                markup,
                targetType,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        static type => MarkupTypeConversionSemanticsService.IsFrameworkPropertyType(type, AvaloniaPropertyMetadataName),
        TryResolveAvaloniaPropertyReferenceExpression,
        AvaloniaSelectorSemanticAdapter.IsSelectorType,
        static (
            string selector,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            INamedTypeSymbol? selectorNestingTypeHint,
            out string expression) =>
            AvaloniaSelectorSemanticAdapter.TryBuildSelectorExpression(
                selector,
                compilation,
                document,
                setterTargetType,
                selectorNestingTypeHint,
                ResolveSelectorTypeToken,
                TryResolvePropertyReference,
                TryConvertUntypedValueExpression,
                TryConvertSelectorTypedValue,
                out expression),
        MarkupRuntimeOperationResolutionService,
        MarkupRuntimeOperationEmissionService,
        CommonMarkupExtensionConversionService,
        FrameworkBindingProjectionService,
        TypedLiteralValueConversionService);
    private static readonly ClrPropertyAssignmentCreationService ClrPropertyAssignmentCreationService = new(
        RequiresObjectInitializer);
    private static readonly ObjectNodeKeyExpressionService ObjectNodeKeyExpressionService = new(
        ResourceKeyResolutionService,
        Escape);
    private static readonly XamlTypeNodeBindingService XamlTypeNodeBindingService = new(
        BindingObjectNodeMarkupParser,
        ObjectNodeKeyExpressionService,
        NormalizeObjectNodeName,
        ResolveTypeToken,
        ResolveTypeSymbol);
    private static readonly BindingScopeDataTypeInferenceService BindingScopeDataTypeInferenceService = new(
        ResolveTypeFromTypeExpression,
        IsDataTemplateNode,
        NormalizePropertyName,
        TryParseBindingMarkup,
        TryResolveBindingResultTypeForScopeInference,
        TryResolveImplicitCSharpShorthandResultType,
        TypeSymbolLookupSemanticsService.FindProperty,
        ResolveAliasedPropertyName,
        TryGetCollectionElementTypeForInference,
        BindingObjectNodeMarkupParser.TryParseBindingMarkupFromObjectNode,
        ResolveObjectTypeSymbol,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        TrySplitOwnerQualifiedPropertyToken,
        ResolveOwnerQualifiedMemberOwnerType,
        SemanticConventions.InheritDataTypeFromItemsAttributeMetadataNames,
        Xaml2006.NamespaceName);
    private static readonly NameScopeRegistrationParsingService NameScopeRegistrationParsingService = new(
        TryParseMarkupExtension,
        NormalizePropertyName);
    private static readonly TemplateObjectNodeSearchService TemplateObjectNodeSearchService = new(
        NormalizePropertyName,
        SemanticConventions.KnownTemplateKinds.ToImmutableHashSet(StringComparer.Ordinal));
    private static readonly SetterIdentityPlanningService SetterIdentityPlanningService = new();
    private static readonly SetterPropertyBindingPlanService SetterPropertyBindingPlanService = new(
        FrameworkProfileIds.Avalonia,
        ResolvePropertyAlias,
        TrySplitOwnerQualifiedPropertyToken,
        ResolveTypeToken,
        TypeSymbolLookupSemanticsService.FindProperty,
        static (
            INamedTypeSymbol ownerType,
            string propertyName,
            string? explicitFieldName,
            out INamedTypeSymbol resolvedOwnerType,
            out IFieldSymbol propertyField) =>
            TryFindAvaloniaPropertyField(
                ownerType,
                propertyName,
                out resolvedOwnerType,
                out propertyField,
                explicitFieldName),
        TryGetAvaloniaPropertyValueType,
        SetterIdentityPlanningService,
        static (propertyOwnerTypeName, propertyFieldName) =>
            CreateAvaloniaFrameworkPropertyOperation(propertyOwnerTypeName, propertyFieldName));
    private static readonly SetterValuePlanningService SetterValuePlanningService = new(
        TryParseInlineCSharpMarkupExtensionCode,
        TryBuildInlineCodeBindingExpression,
        TryResolveSetterShorthandPlan,
        static (Compilation compilation, XamlDocumentModel document, BindingMarkup bindingMarkup, INamedTypeSymbol? targetType, int bindingPriorityScope, out string expression) =>
            TryBuildRuntimeBindingExpression(
                compilation,
                document,
                bindingMarkup,
                targetType,
                (BindingPriorityScope)bindingPriorityScope,
                out expression),
        TryConvertCSharpExpressionMarkupToBindingExpression,
        TryParseBindingMarkup,
        TryReportBindingSourceConflict,
        TryResolveCompiledBindingSourceType,
        TryBuildSetterCompiledBindingAccessorExpression,
        TryResolveSetterValueWithSharedPolicy,
        BuildCompiledBindingAccessorPlaceholderToken);
    private static readonly SetterValuePolicyResolutionService SetterValuePolicyResolutionService = new(
        TryBuildRuntimeXamlFragmentExpression,
        static (
            string value,
            ITypeSymbol type,
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol? setterTargetType,
            int bindingPriorityScope,
            out ResolvedValueConversionResult conversion,
            bool preferTypedStaticResourceCoercion,
            bool allowObjectStringLiteralFallback,
            INamedTypeSymbol? selectorNestingTypeHint,
            ImmutableArray<AttributeData> converterAttributes) =>
            TryConvertValueConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                (BindingPriorityScope)bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion: preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback: allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback: true,
                selectorNestingTypeHint: selectorNestingTypeHint,
                converterAttributes: converterAttributes),
        TryGetAvaloniaUnsetValueExpression,
        Escape);
    private static readonly ControlThemeBasedOnValidationService ControlThemeBasedOnValidationService = new(
        TryParseMarkupExtension);
    private static readonly TemplateValidationService TemplateValidationService = new(
        TemplateObjectNodeSearchService,
        NameScopeRegistrationParsingService.TryGetNodeNameScopeRegistration,
        ResolveTemplateNodeType,
        ResolveTemplateContentRootExpectedType,
        ResolveContractType,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo);
    private static readonly ResourceDefinitionBindingService ResourceDefinitionBindingService = new(
        ConditionalXamlEvaluationService.ShouldSkipBranch,
        ResolveResourceTypeSymbol);
    private static readonly TemplateDefinitionBindingService TemplateDefinitionBindingService = new(
        ConditionalXamlEvaluationService.ShouldSkipBranch,
        IsKnownTemplateKind,
        ResolveTypeFromTypeExpression,
        ValidateControlTemplateParts,
        ValidateTemplateContentRootType);
    private static readonly PropertyAliasResolutionService PropertyAliasResolutionService = new(
        GetPropertyAliasTargetMatchScore,
        NormalizePropertyName,
        PropertyNameFromField);
    private static readonly CompiledBindingAccessorResolutionService CompiledBindingAccessorResolutionService = new(
        ResolveTypeToken,
        ResolveContractType,
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo);
    private static readonly CSharpExpressionBindingService CSharpExpressionBindingService = new(
        static (
            string value,
            Compilation compilation,
            XamlDocumentModel document,
            bool csharpExpressionsEnabled,
            bool implicitCSharpExpressionsEnabled,
            out string csharpExpressionCode,
            out bool isExplicitExpression) =>
            ExpressionClassificationService.TryParseCSharpExpressionMarkup(
                value,
                compilation,
                document,
                csharpExpressionsEnabled,
                implicitCSharpExpressionsEnabled,
                out csharpExpressionCode,
                out isExplicitExpression),
        static (
            Compilation compilation,
            XamlDocumentModel document,
            INamedTypeSymbol sourceType,
            string rawPath,
            ITypeSymbol? targetPropertyType,
            ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
            out CompiledBindingAccessorResolutionResult resolution,
            out string errorMessage) =>
            CompiledBindingAccessorResolutionService.TryBuildAccessorExpression(
                compilation,
                document,
                sourceType,
                rawPath,
                targetPropertyType,
                unsafeAccessors,
                out resolution,
                out errorMessage),
        MarkupContextTokens,
        Escape);
    private static readonly NameScopeRegistrationSemanticsService NameScopeRegistrationSemanticsService = new(
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        TypeContractId.AvaloniaInamed);
    private static readonly HotDesignArtifactClassificationService HotDesignArtifactClassificationService = new(
        TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
        new HotDesignArtifactClassificationRules(
            ApplicationRootTypeNames: ImmutableArray.Create("Application"),
            ApplicationTypeContractId: TypeContractId.Application,
            StyleRootTypeNames: ImmutableArray.Create("Style", "Styles"),
            StyleTypeContractId: TypeContractId.Styles,
            ResourceDictionaryRootTypeNames: ImmutableArray.Create("ResourceDictionary"),
            ResourceDictionaryTypeContractId: TypeContractId.ResourceDictionary,
            ControlThemeRootTypeNames: SemanticConventions.ControlThemeDefinitionRootTypeNames,
            ControlThemeTypeContractId: TypeContractId.ControlTheme,
            TemplateRootTypeNames: SemanticConventions.KnownTemplateKinds,
            ViewScopeHint: "control",
            ApplicationScopeHint: "application",
            StyleScopeHint: "styles",
            ResourceDictionaryScopeHint: "resources",
            ControlThemeScopeHint: "theme",
            TemplateScopeHint: "template"));

    private static bool IsMarkupParserLegacyFallbackEnabled()
    {
        var options = ActiveGeneratorOptions.Value;
        return options?.MarkupParserLegacyInvalidNamedArgumentFallbackEnabled == true;
    }

    private static MarkupExpressionParser GetActiveMarkupExpressionParser()
    {
        return IsMarkupParserLegacyFallbackEnabled()
            ? LegacyMarkupExpressionParser
            : StrictMarkupExpressionParser;
    }

    private static readonly ImmutableArray<IAvaloniaTransformPass> TransformPasses =
        ImmutableArray.Create<IAvaloniaTransformPass>(
            new BindCustomTransformsPass(),
            new BindRootObjectPass(),
            new BindNamedElementsPass(),
            new BindResourcesPass(),
            new BindTemplatesPass(),
            new BindStylesPass(),
            new BindControlThemesPass(),
            new BindIncludesPass(),
            new FinalizeViewModelPass());

    public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options,
        XamlTransformConfiguration transformConfiguration)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? compilation.AssemblyName ?? "UnknownAssembly";
        var uri = "avares://" + assemblyName + "/" + document.TargetPath;
        var previousTransformExtensions = ActiveTransformExtensions.Value;
        var previousGeneratorOptions = ActiveGeneratorOptions.Value;
        var previousTypeResolutionDiagnostics = ActiveTypeResolutionDiagnosticContext.Value;
        var previousTypeSymbolCatalog = ActiveTypeSymbolCatalog.Value;

        try
        {
            ActiveGeneratorOptions.Value = options;
            ActiveTypeResolutionDiagnosticContext.Value = new TypeResolutionDiagnosticContext(
                diagnostics,
                document.FilePath,
                options.StrictMode);
            var typeSymbolCatalog = CompilationTypeSymbolCatalog.Create(compilation, AvaloniaSemanticContractMap);
            ActiveTypeSymbolCatalog.Value = typeSymbolCatalog;
            foreach (var contractDiagnostic in typeSymbolCatalog.Diagnostics)
            {
                diagnostics.Add(new DiagnosticInfo(
                    contractDiagnostic.Code,
                    contractDiagnostic.Message,
                    document.FilePath,
                    1,
                    1,
                    true));
            }
            var context = new BindingTransformContext(
                document,
                compilation,
                options,
                transformConfiguration,
                uri,
                diagnostics);

            ExecuteTransformPasses(context);

            return (context.ViewModel, diagnostics.ToImmutable());
        }
        finally
        {
            ActiveTransformExtensions.Value = previousTransformExtensions;
            ActiveGeneratorOptions.Value = previousGeneratorOptions;
            ActiveTypeResolutionDiagnosticContext.Value = previousTypeResolutionDiagnostics;
            ActiveTypeSymbolCatalog.Value = previousTypeSymbolCatalog;
        }
    }

    private static ITypeSymbolCatalog GetActiveTypeSymbolCatalog(Compilation compilation)
    {
        var catalog = ActiveTypeSymbolCatalog.Value;
        if (catalog is not null &&
            ReferenceEquals(catalog.Compilation, compilation))
        {
            return catalog;
        }

        return CompilationTypeSymbolCatalog.Create(compilation, AvaloniaSemanticContractMap);
    }

    private static INamedTypeSymbol? ResolveContractType(
        Compilation compilation,
        TypeContractId contractId)
    {
        var catalog = GetActiveTypeSymbolCatalog(compilation);
        if (catalog.TryGet(contractId, out var symbol))
        {
            return symbol;
        }

        return null;
    }

    private static void ExecuteTransformPasses(BindingTransformContext context)
    {
        foreach (var pass in TransformPasses)
        {
            if (context.Options.TracePasses)
            {
                var upstream = pass.UpstreamTransformerIds.Length == 0
                    ? "none"
                    : string.Join(", ", pass.UpstreamTransformerIds);
                context.PassExecutionTrace.Add(pass.PassId + " => " + upstream);
            }

            pass.Execute(context);
        }
    }

    private interface IAvaloniaTransformPass
    {
        string PassId { get; }

        ImmutableArray<string> UpstreamTransformerIds { get; }

        void Execute(BindingTransformContext context);
    }

    private sealed class BindingTransformContext
    {
        public BindingTransformContext(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration,
            string buildUri,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics)
        {
            Document = document;
            Compilation = compilation;
            Options = options;
            TransformConfiguration = transformConfiguration;
            BuildUri = buildUri;
            Diagnostics = diagnostics;
            NamedElements = ImmutableArray.CreateBuilder<ResolvedNamedElement>(document.NamedElements.Length);
            CompiledBindings = ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>();
            UnsafeAccessors = ImmutableArray.CreateBuilder<ResolvedUnsafeAccessorDefinition>();
            Resources = ImmutableArray<ResolvedResourceDefinition>.Empty;
            Templates = ImmutableArray<ResolvedTemplateDefinition>.Empty;
            Styles = ImmutableArray<ResolvedStyleDefinition>.Empty;
            ControlThemes = ImmutableArray<ResolvedControlThemeDefinition>.Empty;
            Includes = ImmutableArray<ResolvedIncludeDefinition>.Empty;
        }

        public XamlDocumentModel Document { get; }

        public Compilation Compilation { get; }

        public GeneratorOptions Options { get; }

        public XamlTransformConfiguration TransformConfiguration { get; }

        public string BuildUri { get; }

        public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

        public ImmutableArray<ResolvedNamedElement>.Builder NamedElements { get; }

        public ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings { get; }

        public ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder UnsafeAccessors { get; }

        public List<string> PassExecutionTrace { get; } = new();

        public INamedTypeSymbol? ClassSymbol { get; set; }

        public string ClassModifier { get; set; } = "internal";

        public ResolvedObjectNode? RootObject { get; set; }

        public ImmutableArray<ResolvedResourceDefinition> Resources { get; set; }

        public ImmutableArray<ResolvedTemplateDefinition> Templates { get; set; }

        public ImmutableArray<ResolvedStyleDefinition> Styles { get; set; }

        public ImmutableArray<ResolvedControlThemeDefinition> ControlThemes { get; set; }

        public ImmutableArray<ResolvedIncludeDefinition> Includes { get; set; }

        public bool EmitNameScopeRegistration { get; set; }

        public bool EmitStaticResourceResolver { get; set; }

        public bool HasXBind { get; set; }

        public ResolvedViewModel? ViewModel { get; set; }

        public ResolvedTransformExtensions TransformExtensions { get; set; } = ResolvedTransformExtensions.Empty;
    }
}
