using System;
using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal sealed class AxamlCompletionData : ICompletionData
{
    private readonly XamlCompletionItem _item;

    public AxamlCompletionData(XamlCompletionItem item)
    {
        _item = item;
    }

    public IImage? Image => null;

    public string Text => _item.Label;

    public object Content => _item.Label;

    public object? Description => _item.Documentation ?? _item.Detail;

    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var replacementOffset = completionSegment.Offset;
        if (_item.InsertTextIsSnippet)
        {
            var expansion = AxamlCompletionSnippetParser.Expand(_item.InsertText);
            textArea.Document.Replace(completionSegment, expansion.Text);
            textArea.Caret.Offset = replacementOffset + expansion.CaretOffset;
            return;
        }

        textArea.Document.Replace(completionSegment, _item.InsertText);
        textArea.Caret.Offset = replacementOffset + _item.InsertText.Length;
    }
}
