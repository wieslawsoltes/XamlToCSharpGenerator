using System;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed record XamlLanguageFrameworkCompletion(
    string Label,
    string InsertText,
    string Detail,
    bool InsertTextIsSnippet = false)
{
    public static XamlLanguageFrameworkCompletion Create(
        string label,
        string insertText,
        string detail,
        bool insertTextIsSnippet = false)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new ArgumentException("Completion label must be provided.", nameof(label));
        }

        return new XamlLanguageFrameworkCompletion(label, insertText, detail, insertTextIsSnippet);
    }
}
