using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class BindingInitializerPlanService
{
    public delegate bool TryMapBindingModeDelegate(string modeToken, out string expression);
    public delegate bool TryBuildRelativeSourceExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        RelativeSourceMarkup relativeSource,
        out string expression,
        out string errorMessage);
    public delegate bool TryGetWritablePropertyDelegate(INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol);
    public delegate bool TryConvertValueExpressionDelegate(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    private readonly Func<string, string> _normalizeRuntimeBindingPath;
    private readonly TryMapBindingModeDelegate _tryMapBindingMode;
    private readonly TryBuildRelativeSourceExpressionDelegate _tryBuildRelativeSourceExpression;
    private readonly TryGetWritablePropertyDelegate _tryGetWritableProperty;
    private readonly TryConvertValueExpressionDelegate _tryConvertValueExpression;
    private readonly Func<string, string> _escape;
    private readonly Func<int, string?> _getDefaultBindingPriorityToken;

    public BindingInitializerPlanService(
        Func<string, string> normalizeRuntimeBindingPath,
        TryMapBindingModeDelegate tryMapBindingMode,
        TryBuildRelativeSourceExpressionDelegate tryBuildRelativeSourceExpression,
        TryGetWritablePropertyDelegate tryGetWritableProperty,
        TryConvertValueExpressionDelegate tryConvertValueExpression,
        Func<string, string> escape,
        Func<int, string?> getDefaultBindingPriorityToken)
    {
        _normalizeRuntimeBindingPath = normalizeRuntimeBindingPath ?? throw new ArgumentNullException(nameof(normalizeRuntimeBindingPath));
        _tryMapBindingMode = tryMapBindingMode ?? throw new ArgumentNullException(nameof(tryMapBindingMode));
        _tryBuildRelativeSourceExpression = tryBuildRelativeSourceExpression ?? throw new ArgumentNullException(nameof(tryBuildRelativeSourceExpression));
        _tryGetWritableProperty = tryGetWritableProperty ?? throw new ArgumentNullException(nameof(tryGetWritableProperty));
        _tryConvertValueExpression = tryConvertValueExpression ?? throw new ArgumentNullException(nameof(tryConvertValueExpression));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
        _getDefaultBindingPriorityToken = getDefaultBindingPriorityToken ?? throw new ArgumentNullException(nameof(getDefaultBindingPriorityToken));
    }

    public string NormalizeRuntimeBindingPath(string path) => _normalizeRuntimeBindingPath(path);
    public bool TryMapBindingMode(string modeToken, out string expression) => _tryMapBindingMode(modeToken, out expression);
    public bool TryBuildRelativeSourceExpression(Compilation compilation, XamlDocumentModel document, RelativeSourceMarkup relativeSource, out string expression, out string errorMessage) => _tryBuildRelativeSourceExpression(compilation, document, relativeSource, out expression, out errorMessage);
    public bool TryGetWritableProperty(INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol) => _tryGetWritableProperty(typeSymbol, propertyName, out propertySymbol);
    public bool TryConvertValueExpression(string value, ITypeSymbol targetType, Compilation compilation, XamlDocumentModel document, INamedTypeSymbol? setterTargetType, int bindingPriorityScope, out string expression) => _tryConvertValueExpression(value, targetType, compilation, document, setterTargetType, bindingPriorityScope, out expression);
    public string Escape(string value) => _escape(value);
    public string? GetDefaultBindingPriorityToken(int bindingPriorityScope) => _getDefaultBindingPriorityToken(bindingPriorityScope);
}
