using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XBindOptionExpressionService
{
    public delegate INamedTypeSymbol? ResolveContractTypeDelegate(Compilation compilation, TypeContractId contractId);
    public delegate bool TryGetWritablePropertyDelegate(INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol);
    public delegate bool TryConvertMarkupOptionValueDelegate(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    private readonly ResolveContractTypeDelegate _resolveContractType;
    private readonly TryGetWritablePropertyDelegate _tryGetWritableProperty;
    private readonly TryConvertMarkupOptionValueDelegate _tryConvertMarkupOptionValue;
    private readonly Func<int, string?> _getDefaultBindingPriorityToken;
    private readonly TypeContractId _bindingTypeContractId;
    private readonly string _defaultUpdateSourceTriggerExpression;
    private readonly string _defaultPriorityExpression;

    public XBindOptionExpressionService(
        ResolveContractTypeDelegate resolveContractType,
        TryGetWritablePropertyDelegate tryGetWritableProperty,
        TryConvertMarkupOptionValueDelegate tryConvertMarkupOptionValue,
        Func<int, string?> getDefaultBindingPriorityToken,
        TypeContractId bindingTypeContractId,
        string defaultUpdateSourceTriggerExpression,
        string defaultPriorityExpression)
    {
        _resolveContractType = resolveContractType;
        _tryGetWritableProperty = tryGetWritableProperty;
        _tryConvertMarkupOptionValue = tryConvertMarkupOptionValue;
        _getDefaultBindingPriorityToken = getDefaultBindingPriorityToken;
        _bindingTypeContractId = bindingTypeContractId;
        _defaultUpdateSourceTriggerExpression = defaultUpdateSourceTriggerExpression;
        _defaultPriorityExpression = defaultPriorityExpression;
    }

    public bool TryBuildOptionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string propertyName,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        return TryBuildPropertyExpression(
            compilation,
            document,
            propertyName,
            rawValue,
            setterTargetType,
            bindingPriorityScope: 0,
            out expression,
            out errorMessage);
    }

    public bool TryBuildDelayExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        expression = "0";
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (int.TryParse(rawValue!.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay))
        {
            expression = delay.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return TryBuildPropertyExpression(
            compilation,
            document,
            "Delay",
            rawValue,
            setterTargetType,
            bindingPriorityScope: 0,
            out expression,
            out errorMessage);
    }

    public bool TryBuildUpdateSourceTriggerExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            expression = _defaultUpdateSourceTriggerExpression;
            errorMessage = string.Empty;
            return true;
        }

        return TryBuildPropertyExpression(
            compilation,
            document,
            "UpdateSourceTrigger",
            rawValue,
            setterTargetType,
            bindingPriorityScope: 0,
            out expression,
            out errorMessage);
    }

    public bool TryBuildPriorityExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        int bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            var defaultToken = _getDefaultBindingPriorityToken(bindingPriorityScope);
            expression = string.IsNullOrWhiteSpace(defaultToken)
                ? _defaultPriorityExpression
                : "global::Avalonia.Data.BindingPriority." + defaultToken;
            errorMessage = string.Empty;
            return true;
        }

        return TryBuildPropertyExpression(
            compilation,
            document,
            "Priority",
            rawValue,
            setterTargetType,
            bindingPriorityScope,
            out expression,
            out errorMessage);
    }

    private bool TryBuildPropertyExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string propertyName,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression,
        out string errorMessage)
    {
        expression = "null";
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        var bindingType = _resolveContractType(compilation, _bindingTypeContractId);
        if (bindingType is null)
        {
            errorMessage = "Binding contract type '" + _bindingTypeContractId + "' is not available.";
            return false;
        }

        if (!_tryGetWritableProperty(bindingType, propertyName, out var propertySymbol) || propertySymbol is null)
        {
            errorMessage = "Binding property '" + propertyName + "' is not writable.";
            return false;
        }

        if (!_tryConvertMarkupOptionValue(
                rawValue,
                propertySymbol.Type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            errorMessage = "Could not convert x:Bind option '" + propertyName + "'.";
            return false;
        }

        return true;
    }
}
