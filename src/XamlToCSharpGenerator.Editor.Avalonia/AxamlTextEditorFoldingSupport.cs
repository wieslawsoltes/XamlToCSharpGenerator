using System;
using System.Collections.Generic;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal sealed class AxamlTextEditorFoldingSupport
{
    private readonly FoldingManager _foldingManager;

    public AxamlTextEditorFoldingSupport(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _foldingManager = FoldingManager.Install(editor.TextArea);
    }

    public void Clear()
    {
        _foldingManager.UpdateFoldings(Array.Empty<NewFolding>(), firstErrorOffset: -1);
    }

    public void UpdateFoldings(TextDocument? document, IReadOnlyList<XamlFoldingRange> ranges)
    {
        if (document is null || ranges.Count == 0)
        {
            Clear();
            return;
        }

        var foldings = new List<NewFolding>(ranges.Count);
        for (var index = 0; index < ranges.Count; index++)
        {
            if (TryCreateFolding(document, ranges[index], out var folding))
            {
                foldings.Add(folding);
            }
        }

        _foldingManager.UpdateFoldings(foldings, firstErrorOffset: -1);
    }

    private static bool TryCreateFolding(TextDocument document, XamlFoldingRange range, out NewFolding folding)
    {
        folding = null!;

        var startLineNumber = range.StartLine + 1;
        var endLineNumber = range.EndLine + 1;
        if (startLineNumber < 1 || endLineNumber < startLineNumber || endLineNumber > document.LineCount)
        {
            return false;
        }

        var startLine = document.GetLineByNumber(startLineNumber);
        var endLine = document.GetLineByNumber(endLineNumber);
        var startOffset = startLine.EndOffset;
        var endOffset = endLine.EndOffset;
        if (endOffset <= startOffset)
        {
            return false;
        }

        folding = new NewFolding(startOffset, endOffset)
        {
            Name = BuildFoldTitle(document, startLine)
        };

        return true;
    }

    private static string BuildFoldTitle(TextDocument document, DocumentLine line)
    {
        var lineText = document.GetText(line.Offset, line.Length).Trim();
        if (string.IsNullOrWhiteSpace(lineText))
        {
            return "...";
        }

        return lineText.Length <= 96
            ? lineText
            : lineText[..96] + "...";
    }
}
