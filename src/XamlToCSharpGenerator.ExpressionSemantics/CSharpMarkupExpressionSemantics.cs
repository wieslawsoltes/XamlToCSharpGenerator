using System;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public enum CSharpShorthandExpressionScope
{
    Auto = 0,
    BindingContext = 1,
    Root = 2
}

public readonly record struct CSharpShorthandExpressionInfo(
    string Path,
    CSharpShorthandExpressionScope Scope,
    string RawExpression);

public static class CSharpMarkupExpressionSemantics
{
    public static bool TryParseMarkupExpression(
        string value,
        bool implicitExpressionsEnabled,
        Func<string, bool>? looksLikeMarkupExtensionStart,
        out string rawExpression,
        out bool isExplicitExpression,
        out bool isLambdaExpression)
    {
        rawExpression = string.Empty;
        isExplicitExpression = false;
        isLambdaExpression = false;

        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var innerExpression) ||
            innerExpression.Length == 0)
        {
            return false;
        }

        if (innerExpression.StartsWith("=", StringComparison.Ordinal))
        {
            rawExpression = innerExpression.Substring(1).Trim();
            if (rawExpression.Length == 0)
            {
                return false;
            }

            isExplicitExpression = true;
            isLambdaExpression = IsLambdaExpression(rawExpression);
            return true;
        }

        if (!implicitExpressionsEnabled)
        {
            return false;
        }

        var trimmed = innerExpression.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (!HasUnambiguousCSharpMarkupStart(trimmed) &&
            looksLikeMarkupExtensionStart?.Invoke(innerExpression) == true)
        {
            return false;
        }

        if (!IsImplicitCSharpExpressionMarkup(trimmed))
        {
            return false;
        }

        rawExpression = trimmed;
        isLambdaExpression = IsLambdaExpression(trimmed);
        return true;
    }

    public static bool IsImplicitCSharpExpressionMarkup(string expressionBody)
    {
        if (string.IsNullOrWhiteSpace(expressionBody))
        {
            return false;
        }

        var trimmed = expressionBody.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (HasUnambiguousCSharpMarkupStart(trimmed))
        {
            return true;
        }

        if (CSharpExpressionTextSemantics.ContainsImplicitExpressionOperator(trimmed))
        {
            return true;
        }

        if (CSharpExpressionTextSemantics.IsMethodCallLikeExpression(trimmed) ||
            CSharpExpressionTextSemantics.IsMemberAccessLikeExpression(trimmed))
        {
            return true;
        }

        return CSharpExpressionTextSemantics.IsBareIdentifierExpression(trimmed);
    }

    public static bool TryParseSimpleShorthandPath(
        string expressionText,
        out CSharpShorthandExpressionInfo shorthand)
    {
        shorthand = default;
        if (string.IsNullOrWhiteSpace(expressionText))
        {
            return false;
        }

        var trimmed = expressionText.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.StartsWith("this.", StringComparison.Ordinal))
        {
            var rootPath = trimmed.Substring("this.".Length).Trim();
            if (!TryNormalizeShorthandPath(rootPath, out var normalizedRootPath))
            {
                return false;
            }

            shorthand = new CSharpShorthandExpressionInfo(
                normalizedRootPath,
                CSharpShorthandExpressionScope.Root,
                trimmed);
            return true;
        }

        if (trimmed.StartsWith(".", StringComparison.Ordinal))
        {
            var bindingPath = trimmed.Substring(1).Trim();
            if (!TryNormalizeShorthandPath(bindingPath, out var normalizedBindingPath))
            {
                return false;
            }

            shorthand = new CSharpShorthandExpressionInfo(
                normalizedBindingPath,
                CSharpShorthandExpressionScope.BindingContext,
                trimmed);
            return true;
        }

        if (trimmed.StartsWith("BindingContext.", StringComparison.Ordinal))
        {
            var bindingPath = trimmed.Substring("BindingContext.".Length).Trim();
            if (!TryNormalizeShorthandPath(bindingPath, out var normalizedBindingPath))
            {
                return false;
            }

            shorthand = new CSharpShorthandExpressionInfo(
                normalizedBindingPath,
                CSharpShorthandExpressionScope.BindingContext,
                trimmed);
            return true;
        }

        if (!EventBindingPathSemantics.IsSimplePath(trimmed))
        {
            return false;
        }

        shorthand = new CSharpShorthandExpressionInfo(
            trimmed,
            CSharpShorthandExpressionScope.Auto,
            trimmed);
        return true;
    }

    public static bool IsLambdaExpression(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
        {
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(CSharpExpressionTextSemantics.NormalizeExpressionCode(expressionText));
        if (parsedExpression is not AnonymousFunctionExpressionSyntax)
        {
            return false;
        }

        foreach (var diagnostic in parsedExpression.GetDiagnostics())
        {
            if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsAsyncLambdaExpression(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
        {
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(CSharpExpressionTextSemantics.NormalizeExpressionCode(expressionText));
        if (parsedExpression is not AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return false;
        }

        foreach (var diagnostic in parsedExpression.GetDiagnostics())
        {
            if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            {
                return false;
            }
        }

        return anonymousFunction.AsyncKeyword.RawKind != 0;
    }

    private static bool HasUnambiguousCSharpMarkupStart(string expressionText)
    {
        return expressionText.StartsWith("(", StringComparison.Ordinal) ||
               expressionText.StartsWith("!", StringComparison.Ordinal) ||
               expressionText.StartsWith("new ", StringComparison.Ordinal) ||
               expressionText.StartsWith("$\"", StringComparison.Ordinal) ||
               expressionText.StartsWith("$'", StringComparison.Ordinal) ||
               expressionText.StartsWith("typeof(", StringComparison.Ordinal) ||
               expressionText.StartsWith("nameof(", StringComparison.Ordinal) ||
               expressionText.StartsWith("default(", StringComparison.Ordinal) ||
               expressionText.StartsWith("sizeof(", StringComparison.Ordinal) ||
               expressionText.StartsWith(".", StringComparison.Ordinal) ||
               expressionText.StartsWith("this.", StringComparison.Ordinal) ||
               expressionText.StartsWith("BindingContext.", StringComparison.Ordinal) ||
               IsLambdaExpression(expressionText);
    }

    private static bool TryNormalizeShorthandPath(string path, out string normalizedPath)
    {
        normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "."
            : path.Trim();
        return EventBindingPathSemantics.IsSimplePath(normalizedPath);
    }
}
