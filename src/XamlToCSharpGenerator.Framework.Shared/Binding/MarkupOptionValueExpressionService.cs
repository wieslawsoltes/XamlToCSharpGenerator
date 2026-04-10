using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupOptionValueExpressionService
{
    public delegate bool TryConvertValueDelegate(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression);

    private readonly TryConvertValueDelegate _tryConvertValue;

    public MarkupOptionValueExpressionService(TryConvertValueDelegate tryConvertValue)
    {
        _tryConvertValue = tryConvertValue;
    }

    public bool TryConvert(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        expression = "null";
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return true;
        }

        var trimmed = XamlQuotedValueSemantics.TrimAndUnquote(rawToken!);
        return _tryConvertValue(
            trimmed,
            targetType,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }
}
