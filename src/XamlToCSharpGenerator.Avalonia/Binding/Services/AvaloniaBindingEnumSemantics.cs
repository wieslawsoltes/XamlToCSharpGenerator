using System;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal static class AvaloniaBindingEnumSemantics
{
    private const string BindingModePrefix = "global::Avalonia.Data.BindingMode.";
    private const string RelativeSourceModePrefix = "global::Avalonia.Data.RelativeSourceMode.";
    private const string TreeTypePrefix = "global::Avalonia.Data.TreeType.";

    public static bool TryMapBindingModeToken(string token, out string expression)
    {
        expression = string.Empty;
        if (!TryNormalizeToken(token, out var normalized))
        {
            return false;
        }

        if (normalized.StartsWith(BindingModePrefix, StringComparison.Ordinal))
        {
            expression = normalized;
            return true;
        }

        expression = normalized.ToLowerInvariant() switch
        {
            "default" => "global::Avalonia.Data.BindingMode.Default",
            "oneway" => "global::Avalonia.Data.BindingMode.OneWay",
            "twoway" => "global::Avalonia.Data.BindingMode.TwoWay",
            "onewaytosource" => "global::Avalonia.Data.BindingMode.OneWayToSource",
            "onetime" => "global::Avalonia.Data.BindingMode.OneTime",
            _ => string.Empty
        };
        return expression.Length > 0;
    }

    public static bool TryMapRelativeSourceModeToken(string token, out string expression)
    {
        expression = string.Empty;
        if (!TryNormalizeToken(token, out var normalized))
        {
            return false;
        }

        if (normalized.StartsWith(RelativeSourceModePrefix, StringComparison.Ordinal))
        {
            expression = normalized;
            return true;
        }

        expression = normalized.ToLowerInvariant() switch
        {
            "self" => "global::Avalonia.Data.RelativeSourceMode.Self",
            "templatedparent" => "global::Avalonia.Data.RelativeSourceMode.TemplatedParent",
            "datacontext" => "global::Avalonia.Data.RelativeSourceMode.DataContext",
            "findancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            "ancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            _ => string.Empty
        };
        return expression.Length > 0;
    }

    public static bool TryMapTreeTypeToken(string token, out string expression)
    {
        expression = string.Empty;
        if (!TryNormalizeToken(token, out var normalized))
        {
            return false;
        }

        if (normalized.StartsWith(TreeTypePrefix, StringComparison.Ordinal))
        {
            expression = normalized;
            return true;
        }

        expression = normalized.ToLowerInvariant() switch
        {
            "visual" => "global::Avalonia.Data.TreeType.Visual",
            "logical" => "global::Avalonia.Data.TreeType.Logical",
            _ => string.Empty
        };
        return expression.Length > 0;
    }

    private static bool TryNormalizeToken(string? token, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        normalized = token.Trim();
        return normalized.Length > 0;
    }
}
