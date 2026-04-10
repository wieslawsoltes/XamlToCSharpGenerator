using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ValueConversionSemanticService
{
    public delegate bool TryParseBindingMarkupDelegate(string value, out BindingMarkup bindingMarkup);
    public delegate bool TryParseReflectionBindingMarkupDelegate(string value, out BindingMarkup bindingMarkup);
    public delegate bool TryConvertPrimitiveMarkupExtensionDelegate(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression);
    public delegate bool TryBuildBindingValueExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol targetType,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool TryConvertGenericMarkupExtensionExpressionDelegate(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);
    public delegate bool IsFrameworkPropertyTypeDelegate(ITypeSymbol type);
    public delegate bool TryResolveFrameworkPropertyReferenceExpressionDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        out string expression);
    public delegate bool IsSelectorTypeDelegate(ITypeSymbol type);
    public delegate bool TryBuildSelectorExpressionDelegate(
        string selector,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        INamedTypeSymbol? selectorNestingTypeHint,
        out string expression);

    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly TryParseBindingMarkupDelegate _tryParseBindingMarkup;
    private readonly TryParseReflectionBindingMarkupDelegate _tryParseReflectionBindingMarkup;
    private readonly TryConvertPrimitiveMarkupExtensionDelegate _tryConvertPrimitiveMarkupExtension;
    private readonly TryBuildBindingValueExpressionDelegate _tryBuildBindingValueExpression;
    private readonly TryConvertGenericMarkupExtensionExpressionDelegate _tryConvertGenericMarkupExtensionExpression;
    private readonly IsFrameworkPropertyTypeDelegate _isFrameworkPropertyType;
    private readonly TryResolveFrameworkPropertyReferenceExpressionDelegate _tryResolveFrameworkPropertyReferenceExpression;
    private readonly IsSelectorTypeDelegate _isSelectorType;
    private readonly TryBuildSelectorExpressionDelegate _tryBuildSelectorExpression;
    private readonly MarkupRuntimeOperationResolutionService _markupRuntimeOperationResolutionService;
    private readonly MarkupRuntimeOperationEmissionService _markupRuntimeOperationEmissionService;
    private readonly CommonMarkupExtensionConversionService _commonMarkupExtensionConversionService;
    private readonly FrameworkBindingProjectionService _frameworkBindingProjectionService;
    private readonly TypedLiteralValueConversionService _typedLiteralValueConversionService;

    public ValueConversionSemanticService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        TryParseBindingMarkupDelegate tryParseBindingMarkup,
        TryParseReflectionBindingMarkupDelegate tryParseReflectionBindingMarkup,
        TryConvertPrimitiveMarkupExtensionDelegate tryConvertPrimitiveMarkupExtension,
        TryBuildBindingValueExpressionDelegate tryBuildBindingValueExpression,
        TryConvertGenericMarkupExtensionExpressionDelegate tryConvertGenericMarkupExtensionExpression,
        IsFrameworkPropertyTypeDelegate isFrameworkPropertyType,
        TryResolveFrameworkPropertyReferenceExpressionDelegate tryResolveFrameworkPropertyReferenceExpression,
        IsSelectorTypeDelegate isSelectorType,
        TryBuildSelectorExpressionDelegate tryBuildSelectorExpression,
        MarkupRuntimeOperationResolutionService markupRuntimeOperationResolutionService,
        MarkupRuntimeOperationEmissionService markupRuntimeOperationEmissionService,
        CommonMarkupExtensionConversionService commonMarkupExtensionConversionService,
        FrameworkBindingProjectionService frameworkBindingProjectionService,
        TypedLiteralValueConversionService typedLiteralValueConversionService)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
        _tryParseBindingMarkup = tryParseBindingMarkup ?? throw new ArgumentNullException(nameof(tryParseBindingMarkup));
        _tryParseReflectionBindingMarkup = tryParseReflectionBindingMarkup ?? throw new ArgumentNullException(nameof(tryParseReflectionBindingMarkup));
        _tryConvertPrimitiveMarkupExtension = tryConvertPrimitiveMarkupExtension ?? throw new ArgumentNullException(nameof(tryConvertPrimitiveMarkupExtension));
        _tryBuildBindingValueExpression = tryBuildBindingValueExpression ?? throw new ArgumentNullException(nameof(tryBuildBindingValueExpression));
        _tryConvertGenericMarkupExtensionExpression = tryConvertGenericMarkupExtensionExpression ?? throw new ArgumentNullException(nameof(tryConvertGenericMarkupExtensionExpression));
        _isFrameworkPropertyType = isFrameworkPropertyType ?? throw new ArgumentNullException(nameof(isFrameworkPropertyType));
        _tryResolveFrameworkPropertyReferenceExpression = tryResolveFrameworkPropertyReferenceExpression ?? throw new ArgumentNullException(nameof(tryResolveFrameworkPropertyReferenceExpression));
        _isSelectorType = isSelectorType ?? throw new ArgumentNullException(nameof(isSelectorType));
        _tryBuildSelectorExpression = tryBuildSelectorExpression ?? throw new ArgumentNullException(nameof(tryBuildSelectorExpression));
        _markupRuntimeOperationResolutionService = markupRuntimeOperationResolutionService ?? throw new ArgumentNullException(nameof(markupRuntimeOperationResolutionService));
        _markupRuntimeOperationEmissionService = markupRuntimeOperationEmissionService ?? throw new ArgumentNullException(nameof(markupRuntimeOperationEmissionService));
        _commonMarkupExtensionConversionService = commonMarkupExtensionConversionService ?? throw new ArgumentNullException(nameof(commonMarkupExtensionConversionService));
        _frameworkBindingProjectionService = frameworkBindingProjectionService ?? throw new ArgumentNullException(nameof(frameworkBindingProjectionService));
        _typedLiteralValueConversionService = typedLiteralValueConversionService ?? throw new ArgumentNullException(nameof(typedLiteralValueConversionService));
    }

    public bool TryConvertExpression(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null)
    {
        if (TryConvert(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback: true,
                selectorNestingTypeHint,
                converterAttributes: default))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    public bool TryConvertForCollectionAdd(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback)
    {
        return TryConvert(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out conversion,
            allowObjectStringLiteralFallback: allowObjectStringLiteralFallback);
    }

    public bool TryConvert(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        bool allowStaticParseMethodFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        conversion = default;

        if (TryConvertMarkupExtension(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion))
        {
            return true;
        }

        if (type is INamedTypeSymbol nullableType &&
            nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nullableType.TypeArguments.Length == 1)
        {
            if (XamlScalarLiteralSemantics.IsNullLiteral(value))
            {
                conversion = CreateLiteralConversion("null");
                return true;
            }

            return TryConvert(
                value,
                nullableType.TypeArguments[0],
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback,
                selectorNestingTypeHint,
                converterAttributes);
        }

        if (_isFrameworkPropertyType(type) &&
            _tryResolveFrameworkPropertyReferenceExpression(
                value,
                compilation,
                document,
                setterTargetType,
                out var propertyReferenceExpression))
        {
            conversion = CreateLiteralConversion(propertyReferenceExpression);
            return true;
        }

        if (_isSelectorType(type) &&
            _tryBuildSelectorExpression(
                value,
                compilation,
                document,
                setterTargetType,
                selectorNestingTypeHint,
                out var selectorExpression))
        {
            conversion = CreateLiteralConversion(selectorExpression);
            return true;
        }

        return _typedLiteralValueConversionService.TryConvert(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out conversion,
            allowObjectStringLiteralFallback,
            allowStaticParseMethodFallback,
            converterAttributes);
    }

    public bool TryConvertMarkupExtensionExpression(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true)
    {
        if (TryConvertMarkupExtension(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    public bool TryConvertMarkupExtension(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true)
    {
        conversion = default;
        if (!_tryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        if (_tryConvertPrimitiveMarkupExtension(markup, targetType, out var primitiveExpression))
        {
            conversion = CreateLiteralConversion(primitiveExpression);
            return true;
        }

        if (_markupRuntimeOperationResolutionService.TryResolve(markup, compilation, document, out var markupRuntimeOperation) &&
            _markupRuntimeOperationEmissionService.TryConvert(
                markupRuntimeOperation,
                targetType,
                compilation,
                preferTypedStaticResourceCoercion,
                out conversion))
        {
            return true;
        }

        if (_commonMarkupExtensionConversionService.TryConvert(
                value,
                markup,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                "__AXSG_CTX_SERVICE_PROVIDER__",
                out conversion))
        {
            return true;
        }

        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.Binding:
            case XamlMarkupExtensionKind.CompiledBinding:
            {
                if (!_tryParseBindingMarkup(value, out var bindingMarkup) ||
                    bindingMarkup.HasSourceConflict)
                {
                    return false;
                }

                if (!_tryBuildBindingValueExpression(
                        compilation,
                        document,
                        bindingMarkup,
                        targetType,
                        setterTargetType,
                        bindingPriorityScope,
                        out var bindingExpression))
                {
                    return false;
                }

                conversion = CreateBindingConversion(
                    bindingExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }

            case XamlMarkupExtensionKind.ReflectionBinding:
            {
                if (!_tryParseReflectionBindingMarkup(value, out var reflectionBindingMarkup) ||
                    reflectionBindingMarkup.HasSourceConflict ||
                    !_frameworkBindingProjectionService.TryBuildReflectionBindingConversion(
                        compilation,
                        document,
                        reflectionBindingMarkup,
                        setterTargetType,
                        bindingPriorityScope,
                        out conversion))
                {
                    return false;
                }

                return true;
            }

            case XamlMarkupExtensionKind.TemplateBinding:
                return _frameworkBindingProjectionService.TryBuildTemplateBindingConversion(
                    markup,
                    compilation,
                    document,
                    setterTargetType,
                    out conversion);

            default:
            {
                if (!_tryConvertGenericMarkupExtensionExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var genericExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    genericExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
        }
    }

    private static ResolvedValueConversionResult CreateLiteralConversion(string expression)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Literal);
    }

    private static ResolvedValueConversionResult CreateMarkupExtensionConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false,
        bool requiresStaticResourceResolver = false,
        bool isRuntimeFallback = false,
        ResolvedResourceKeyExpression? resourceKey = null)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.MarkupExtension,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            IsRuntimeFallback: isRuntimeFallback,
            ResourceKey: resourceKey,
            ValueRequirements: requirements);
    }

    private static ResolvedValueConversionResult CreateBindingConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Binding,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            ValueRequirements: requirements);
    }
}
