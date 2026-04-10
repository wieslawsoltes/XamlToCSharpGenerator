using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class FrameworkBindingProjectionService
{
    public delegate bool TryMapBindingModeDelegate(string modeToken, out string expression);
    public delegate bool TryGetWritablePropertyDelegate(INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol);
    public delegate bool TryResolveFrameworkPropertyReferenceExpressionDelegate(
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        out string expression);

    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly BindingRuntimeProjectionService _bindingRuntimeProjectionService;
    private readonly BindingInitializerPlanService _bindingInitializerPlanService;
    private readonly ObjectInitializerExpressionService _objectInitializerExpressionService;
    private readonly MarkupContextTokenSet _markupContextTokens;
    private readonly Func<string, string> _buildReflectionBindingExpression;
    private readonly Func<string, string> _buildTemplateBindingExpression;
    private readonly string _templatedParentBindingExpression;
    private readonly TryMapBindingModeDelegate _tryMapBindingMode;
    private readonly TryGetWritablePropertyDelegate _tryGetWritableProperty;
    private readonly TryResolveFrameworkPropertyReferenceExpressionDelegate _tryResolveFrameworkPropertyReferenceExpression;
    private readonly TypeContractId _bindingContractId;
    private readonly TypeContractId _reflectionBindingContractId;
    private readonly TypeContractId _templateBindingContractId;

    public FrameworkBindingProjectionService(
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        BindingRuntimeProjectionService bindingRuntimeProjectionService,
        BindingInitializerPlanService bindingInitializerPlanService,
        ObjectInitializerExpressionService objectInitializerExpressionService,
        MarkupContextTokenSet markupContextTokens,
        Func<string, string> buildBindingConstructorExpression,
        Func<string, string> buildReflectionBindingExpression,
        Func<string, string> buildTemplateBindingExpression,
        string templatedParentBindingExpression,
        TryMapBindingModeDelegate tryMapBindingMode,
        TryGetWritablePropertyDelegate tryGetWritableProperty,
        TryResolveFrameworkPropertyReferenceExpressionDelegate tryResolveFrameworkPropertyReferenceExpression,
        TypeContractId bindingBaseContractId,
        TypeContractId bindingInterfaceContractId,
        TypeContractId bindingInterface2ContractId,
        TypeContractId bindingContractId,
        TypeContractId reflectionBindingContractId,
        TypeContractId templateBindingContractId,
        string assignBindingAttributeMetadataName)
    {
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _bindingRuntimeProjectionService = bindingRuntimeProjectionService ?? throw new ArgumentNullException(nameof(bindingRuntimeProjectionService));
        _bindingInitializerPlanService = bindingInitializerPlanService ?? throw new ArgumentNullException(nameof(bindingInitializerPlanService));
        _objectInitializerExpressionService = objectInitializerExpressionService ?? throw new ArgumentNullException(nameof(objectInitializerExpressionService));
        _markupContextTokens = markupContextTokens;
        _ = buildBindingConstructorExpression ?? throw new ArgumentNullException(nameof(buildBindingConstructorExpression));
        _buildReflectionBindingExpression = buildReflectionBindingExpression ?? throw new ArgumentNullException(nameof(buildReflectionBindingExpression));
        _buildTemplateBindingExpression = buildTemplateBindingExpression ?? throw new ArgumentNullException(nameof(buildTemplateBindingExpression));
        _templatedParentBindingExpression = templatedParentBindingExpression ?? throw new ArgumentNullException(nameof(templatedParentBindingExpression));
        _tryMapBindingMode = tryMapBindingMode ?? throw new ArgumentNullException(nameof(tryMapBindingMode));
        _tryGetWritableProperty = tryGetWritableProperty ?? throw new ArgumentNullException(nameof(tryGetWritableProperty));
        _tryResolveFrameworkPropertyReferenceExpression = tryResolveFrameworkPropertyReferenceExpression ?? throw new ArgumentNullException(nameof(tryResolveFrameworkPropertyReferenceExpression));
        _bindingContractId = bindingContractId;
        _reflectionBindingContractId = reflectionBindingContractId;
        _templateBindingContractId = templateBindingContractId;

        _ = bindingBaseContractId;
        _ = bindingInterfaceContractId;
        _ = bindingInterface2ContractId;
        _ = assignBindingAttributeMetadataName;
    }

    public bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        return _bindingRuntimeProjectionService.TryBuildRuntimeBindingExpression(
            compilation,
            document,
            bindingMarkup,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }

    public bool TryBuildReflectionBindingConversion(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion)
    {
        _ = _isTypeAssignableTo;
        _ = _tryMapBindingMode;
        _ = _tryGetWritableProperty;

        conversion = default;
        if (_resolveContractType(compilation, _reflectionBindingContractId) is not INamedTypeSymbol reflectionBindingType)
        {
            return false;
        }

        var normalizedPath = _bindingInitializerPlanService.NormalizeRuntimeBindingPath(bindingMarkup.Path);
        var initializerParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            _bindingInitializerPlanService.TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + _bindingInitializerPlanService.Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is RelativeSourceMarkup relativeSource &&
            _bindingInitializerPlanService.TryBuildRelativeSourceExpression(
                compilation,
                document,
                relativeSource,
                out var relativeSourceExpression,
                out _))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            reflectionBindingType,
            "Source",
            bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "DataType",
            bindingMarkup.DataType,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "Converter",
            bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "ConverterCulture",
            bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "ConverterParameter",
            bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "StringFormat",
            bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "FallbackValue",
            bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "TargetNullValue",
            bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "Delay",
            bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "Priority",
            !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : _bindingInitializerPlanService.GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingType,
            "UpdateSourceTrigger",
            bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            initializerParts);

        var constructorExpression = _buildReflectionBindingExpression(normalizedPath);
        var reflectionBindingExpression = _objectInitializerExpressionService.BuildObjectCreationExpression(
            "global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension",
            constructorExpression,
            BuildAssignmentMap(initializerParts));
        var expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(" +
            reflectionBindingExpression + ", " +
            _markupContextTokens.ServiceProviderToken + ", " +
            _markupContextTokens.RootObjectToken + ", " +
            _markupContextTokens.IntermediateRootObjectToken + ", " +
            _markupContextTokens.TargetObjectToken + ", " +
            _markupContextTokens.TargetPropertyToken + ", " +
            _markupContextTokens.BaseUriToken + ", " +
            _markupContextTokens.ParentStackToken + ")";

        conversion = new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Binding,
            RequiresRuntimeServiceProvider: true,
            RequiresParentStack: true,
            RequiresProvideValueTarget: true,
            RequiresRootObject: true,
            RequiresBaseUri: true,
            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
        return true;
    }

    public bool TryBuildTemplateBindingConversion(
        MarkupExtensionInfo markup,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        out ResolvedValueConversionResult conversion)
    {
        conversion = default;

        var propertyToken = markup.NamedArguments.TryGetValue("Property", out var namedProperty)
            ? namedProperty
            : (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            if (_resolveContractType(compilation, _bindingContractId) is null)
            {
                return false;
            }

            conversion = new ResolvedValueConversionResult(
                Expression: _templatedParentBindingExpression,
                ValueKind: ResolvedValueKind.Binding);
            return true;
        }

        if (_resolveContractType(compilation, _templateBindingContractId) is not INamedTypeSymbol templateBindingType)
        {
            return false;
        }

        if (setterTargetType is null)
        {
            return false;
        }

        var unquotedPropertyToken = propertyToken!.Trim().Trim('"');
        if (string.Equals(unquotedPropertyToken, ".", StringComparison.Ordinal))
        {
            conversion = new ResolvedValueConversionResult(
                Expression: _templatedParentBindingExpression,
                ValueKind: ResolvedValueKind.Binding);
            return true;
        }

        if (!_tryResolveFrameworkPropertyReferenceExpression(
                unquotedPropertyToken,
                compilation,
                document,
                setterTargetType,
                out var propertyExpression))
        {
            return false;
        }

        var initializerParts = new List<string>();
        var modeToken = markup.NamedArguments.TryGetValue("Mode", out var explicitMode)
            ? explicitMode
            : null;
        if (!string.IsNullOrWhiteSpace(modeToken) &&
            _bindingInitializerPlanService.TryMapBindingMode(modeToken!, out var bindingModeExpression) &&
            _bindingInitializerPlanService.TryGetWritableProperty(templateBindingType, "Mode", out _))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        AddBindingInitializerPart(
            templateBindingType,
            "Converter",
            TryGetNamedMarkupArgument(markup, "Converter"),
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope: 0,
            initializerParts);
        AddBindingInitializerPart(
            templateBindingType,
            "ConverterCulture",
            TryGetNamedMarkupArgument(markup, "ConverterCulture"),
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope: 0,
            initializerParts);
        AddBindingInitializerPart(
            templateBindingType,
            "ConverterParameter",
            TryGetNamedMarkupArgument(markup, "ConverterParameter"),
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope: 0,
            initializerParts);

        var expression = _buildTemplateBindingExpression(propertyExpression);
        if (initializerParts.Count > 0)
        {
            expression += " { " + string.Join(", ", initializerParts) + " }";
        }

        conversion = new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Binding);
        return true;
    }

    private void AddBindingInitializerPart(
        INamedTypeSymbol bindingType,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        List<string> initializerParts)
    {
        if (string.IsNullOrWhiteSpace(rawValue) ||
            !_bindingInitializerPlanService.TryGetWritableProperty(bindingType, propertyName, out var property) ||
            property is null)
        {
            return;
        }

        if (_bindingInitializerPlanService.TryConvertValueExpression(
                rawValue!,
                property.Type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var expression))
        {
            initializerParts.Add(propertyName + " = " + expression);
        }
    }

    private static Dictionary<string, string> BuildAssignmentMap(List<string> initializerParts)
    {
        var assignments = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < initializerParts.Count; index++)
        {
            var initializerPart = initializerParts[index];
            var separatorIndex = initializerPart.IndexOf(" = ", StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            assignments[initializerPart.Substring(0, separatorIndex)] =
                initializerPart.Substring(separatorIndex + 3);
        }

        return assignments;
    }

    private static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, string name)
    {
        return markup.NamedArguments.TryGetValue(name, out var value) ? value : null;
    }
}
