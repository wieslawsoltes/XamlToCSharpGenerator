using System;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed record XamlLanguageFrameworkCompletion(
    string Label,
    string InsertText,
    string Detail)
{
    public static XamlLanguageFrameworkCompletion Create(
        string label,
        string insertText,
        string detail)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Completion label must be provided.", nameof(label));
        }

        return new XamlLanguageFrameworkCompletion(label, insertText, detail);
    }
}
