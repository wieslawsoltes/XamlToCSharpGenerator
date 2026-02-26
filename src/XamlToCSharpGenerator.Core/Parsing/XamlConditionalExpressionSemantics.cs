using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;
namespace XamlToCSharpGenerator.Core.Parsing;

public readonly record struct XamlConditionalExpressionParseResult(
    string RawExpression,
    string MethodName,
    ImmutableArray<string> Arguments);

public static class XamlConditionalExpressionSemantics
{
    private static readonly ImmutableHashSet<string> SupportedMethodNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "IsTypePresent",
        "IsTypeNotPresent",
        "IsPropertyPresent",
        "IsPropertyNotPresent",
        "IsMethodPresent",
        "IsMethodNotPresent",
        "IsEventPresent",
        "IsEventNotPresent",
        "IsEnumNamedValuePresent",
        "IsEnumNamedValueNotPresent",
        "IsApiContractPresent",
        "IsApiContractNotPresent");

    public static bool TryParse(
        string rawExpression,
        out XamlConditionalExpressionParseResult expression,
        out string errorMessage)
    {
        expression = default;
        errorMessage = string.Empty;

        if (!TrySplitMethodCall(rawExpression, out var normalizedExpression, out var methodName, out var argumentsText, out errorMessage))
        {
            return false;
        }

        if (!SupportedMethodNames.Contains(methodName))
        {
            errorMessage = $"Unsupported conditional method '{methodName}'.";
            return false;
        }

        if (!TryParseArguments(argumentsText, out var arguments, out errorMessage))
        {
            return false;
        }

        if (!ValidateMethodArity(methodName, arguments.Length, out errorMessage))
        {
            return false;
        }

        expression = new XamlConditionalExpressionParseResult(
            normalizedExpression,
            methodName,
            arguments);
        return true;
    }

    public static bool TryParseMethodCallShape(
        string rawExpression,
        out string normalizedExpression,
        out string methodName,
        out string argumentsText,
        out string errorMessage)
    {
        return TrySplitMethodCall(
            rawExpression,
            out normalizedExpression,
            out methodName,
            out argumentsText,
            out errorMessage);
    }

    private static bool TrySplitMethodCall(
        string rawExpression,
        out string normalizedExpression,
        out string methodName,
        out string argumentsText,
        out string errorMessage)
    {
        return XamlConditionalMethodCallSemantics.TryParseMethodCall(
            rawExpression,
            out normalizedExpression,
            out methodName,
            out argumentsText,
            out errorMessage);
    }

    private static bool TryParseArguments(
        string rawArguments,
        out ImmutableArray<string> arguments,
        out string errorMessage)
    {
        arguments = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawArguments))
        {
            return true;
        }

        var rawTokens = TopLevelTextParser.SplitTopLevel(
            rawArguments,
            ',',
            trimTokens: false,
            removeEmpty: false);
        if (rawTokens.Length == 0)
        {
            return true;
        }

        var builder = ImmutableArray.CreateBuilder<string>(rawTokens.Length);
        for (var i = 0; i < rawTokens.Length; i++)
        {
            if (!TryNormalizeArgument(rawTokens[i], out var normalizedArgument, out errorMessage))
            {
                return false;
            }

            builder.Add(normalizedArgument);
        }

        arguments = builder.ToImmutable();
        return true;
    }

    private static bool TryNormalizeArgument(
        string rawArgument,
        out string normalizedArgument,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        normalizedArgument = string.Empty;

        var trimmed = rawArgument.Trim();
        if (trimmed.Length == 0)
        {
            errorMessage = "Condition expression has an empty argument.";
            return false;
        }

        normalizedArgument = XamlQuotedValueSemantics.UnquoteWrapped(trimmed);
        return true;
    }

    private static bool ValidateMethodArity(
        string methodName,
        int argumentCount,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var isApiContractMethod = methodName is "IsApiContractPresent" or "IsApiContractNotPresent";
        if (isApiContractMethod)
        {
            if (argumentCount is >= 1 and <= 3)
            {
                return true;
            }

            errorMessage = $"Method '{methodName}' expects between 1 and 3 arguments.";
            return false;
        }

        var expectedArguments = methodName is "IsTypePresent" or "IsTypeNotPresent" ? 1 : 2;
        if (argumentCount == expectedArguments)
        {
            return true;
        }

        errorMessage = $"Method '{methodName}' expects {expectedArguments} argument(s).";
        return false;
    }
}
