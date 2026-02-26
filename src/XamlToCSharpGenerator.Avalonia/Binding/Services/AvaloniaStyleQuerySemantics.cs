using System;
using System.Globalization;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal readonly record struct AvaloniaStyleQueryDescriptor(
    string MethodName,
    string OperatorName,
    double Value);

internal static class AvaloniaStyleQuerySemantics
{
    public static bool TryParse(string rawToken, out AvaloniaStyleQueryDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        var trimmed = rawToken.Trim();
        if (!XamlTokenSplitSemantics.TrySplitAtFirstSeparator(
                trimmed,
                ':',
                out var queryToken,
                out var valueToken))
        {
            return false;
        }

        if (!TryMapQueryToken(queryToken, out var methodName, out var operatorName) ||
            !double.TryParse(valueToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        descriptor = new AvaloniaStyleQueryDescriptor(methodName, operatorName, value);
        return true;
    }

    private static bool TryMapQueryToken(string queryToken, out string methodName, out string operatorName)
    {
        methodName = string.Empty;
        operatorName = string.Empty;

        var normalized = queryToken.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        switch (normalized.ToLowerInvariant())
        {
            case "width":
                methodName = "Width";
                operatorName = "Equals";
                return true;
            case "min-width":
                methodName = "Width";
                operatorName = "GreaterThanOrEquals";
                return true;
            case "max-width":
                methodName = "Width";
                operatorName = "LessThanOrEquals";
                return true;
            case "height":
                methodName = "Height";
                operatorName = "Equals";
                return true;
            case "min-height":
                methodName = "Height";
                operatorName = "GreaterThanOrEquals";
                return true;
            case "max-height":
                methodName = "Height";
                operatorName = "LessThanOrEquals";
                return true;
            default:
                return false;
        }
    }
}
