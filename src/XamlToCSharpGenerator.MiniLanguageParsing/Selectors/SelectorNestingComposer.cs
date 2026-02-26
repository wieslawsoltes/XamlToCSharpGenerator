using System;
using System.Collections.Generic;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public static class SelectorNestingComposer
{
    public static string ComposeNestedStyleSelector(string? parentSelector, string? childSelector)
    {
        var parentText = parentSelector is null
            ? string.Empty
            : parentSelector.Trim();
        var childText = childSelector is null
            ? string.Empty
            : childSelector.Trim();

        if (parentText.Length == 0)
        {
            return childText;
        }

        if (childText.Length == 0)
        {
            return parentText;
        }

        var parentBranches = TopLevelTextParser.SplitTopLevel(
            parentText,
            ',',
            trimTokens: true,
            removeEmpty: true);
        var childBranches = TopLevelTextParser.SplitTopLevel(
            childText,
            ',',
            trimTokens: true,
            removeEmpty: true);

        if (parentBranches.Length == 0 || childBranches.Length == 0)
        {
            return parentText;
        }

        var composed = new List<string>(parentBranches.Length * Math.Max(childBranches.Length, 1));
        foreach (var parentBranch in parentBranches)
        {
            foreach (var childBranch in childBranches)
            {
                var trimmedChildSelector = TrimNestingPrefix(childBranch);
                if (trimmedChildSelector.StartsWith("/template/", StringComparison.Ordinal))
                {
                    composed.Add(parentBranch + " " + trimmedChildSelector);
                }
                else if (trimmedChildSelector.StartsWith(".", StringComparison.Ordinal) ||
                         trimmedChildSelector.StartsWith("#", StringComparison.Ordinal) ||
                         trimmedChildSelector.StartsWith(":", StringComparison.Ordinal) ||
                         trimmedChildSelector.StartsWith("[", StringComparison.Ordinal) ||
                         trimmedChildSelector.StartsWith(">", StringComparison.Ordinal))
                {
                    composed.Add(parentBranch + trimmedChildSelector);
                }
                else
                {
                    composed.Add(parentBranch + " " + trimmedChildSelector);
                }
            }
        }

        return string.Join(", ", composed);
    }

    private static string TrimNestingPrefix(string selector)
    {
        var text = selector ?? string.Empty;
        var index = 0;
        while (index < text.Length && text[index] == '^')
        {
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return text.Substring(index).Trim();
    }
}
