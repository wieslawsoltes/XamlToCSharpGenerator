using System;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Parsing;

public static class EventBindingSourceModeSemantics
{
    public static bool TryParse(string? sourceToken, out ResolvedEventBindingSourceMode sourceMode)
    {
        sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
        if (sourceToken is null)
        {
            return false;
        }

        var normalized = sourceToken.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (normalized.Equals("DataContextThenRoot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
            return true;
        }

        if (normalized.Equals("DataContext", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContext;
            return true;
        }

        if (normalized.Equals("Root", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.Root;
            return true;
        }

        return false;
    }
}
