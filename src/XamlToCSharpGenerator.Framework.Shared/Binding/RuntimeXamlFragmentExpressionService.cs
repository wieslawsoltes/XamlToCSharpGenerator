using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class RuntimeXamlFragmentExpressionService
{
    public delegate string WrapWithTargetTypeCastDelegate(string expression, ITypeSymbol targetType);
    public delegate string BuildRuntimeExpressionDelegate(string escapedXaml, string escapedBaseUri);

    private readonly System.Func<string, bool> _isValidFragment;
    private readonly System.Func<string, string> _escape;
    private readonly WrapWithTargetTypeCastDelegate _wrapWithTargetTypeCast;
    private readonly BuildRuntimeExpressionDelegate _buildRuntimeExpression;

    public RuntimeXamlFragmentExpressionService(
        System.Func<string, bool> isValidFragment,
        System.Func<string, string> escape,
        WrapWithTargetTypeCastDelegate wrapWithTargetTypeCast,
        BuildRuntimeExpressionDelegate buildRuntimeExpression)
    {
        _isValidFragment = isValidFragment;
        _escape = escape;
        _wrapWithTargetTypeCast = wrapWithTargetTypeCast;
        _buildRuntimeExpression = buildRuntimeExpression;
    }

    public bool TryBuildExpression(
        string rawValue,
        ITypeSymbol targetType,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = XamlQuotedValueSemantics.TrimAndUnquote(rawValue);
        if (!_isValidFragment(trimmed))
        {
            return false;
        }

        var escapedXaml = _escape(trimmed);
        var escapedBaseUri = _escape(document.TargetPath);
        expression = _wrapWithTargetTypeCast(
            _buildRuntimeExpression(escapedXaml, escapedBaseUri),
            targetType);
        return true;
    }
}
