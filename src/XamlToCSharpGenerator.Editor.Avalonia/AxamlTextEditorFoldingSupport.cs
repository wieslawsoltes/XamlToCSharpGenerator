using System;
using System.Collections.Generic;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal sealed class AxamlTextEditorFoldingSupport
{
    private readonly TextEditor _editor;
    private FoldingManager? _foldingManager;
    private TextDocument? _installedDocument;

    public AxamlTextEditorFoldingSupport(TextEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        _editor = editor;
    }

    public void Clear()
    {
        _foldingManager?.UpdateFoldings(Array.Empty<NewFolding>(), firstErrorOffset: -1);
    }

    public void Reset()
    {
        if (_foldingManager is null)
        {
            _installedDocument = null;
            return;
        }

        FoldingManager.Uninstall(_foldingManager);
        _foldingManager = null;
        _installedDocument = null;
    }

    public void UpdateFoldings(TextDocument? document, IReadOnlyList<XamlFoldingRange> ranges)
    {
        if (document is null || !ReferenceEquals(document, _editor.Document))
        {
            Reset();
            return;
        }

        EnsureInstalled(document);
        if (_foldingManager is null || ranges.Count == 0)
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

        _foldingManager?.UpdateFoldings(foldings, firstErrorOffset: -1);
    }

    private void EnsureInstalled(TextDocument document)
    {
        if (_foldingManager is not null && ReferenceEquals(_installedDocument, document))
        {
            return;
        }

        Reset();

        if (!ReferenceEquals(_editor.TextArea.Document, document))
        {
            return;
        }

        _foldingManager = FoldingManager.Install(_editor.TextArea);
        _installedDocument = document;
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
