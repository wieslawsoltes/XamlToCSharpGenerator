using System;
using System.Collections.Immutable;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal sealed class AxamlDiagnosticColorizer : DocumentColorizingTransformer
{
    private ImmutableArray<LanguageServiceDiagnostic> _diagnostics = ImmutableArray<LanguageServiceDiagnostic>.Empty;

    private static readonly TextDecorationCollection ErrorDecoration = CreateUnderline(Brushes.IndianRed);
    private static readonly TextDecorationCollection WarningDecoration = CreateUnderline(Brushes.DarkOrange);
    private static readonly TextDecorationCollection InformationDecoration = CreateUnderline(Brushes.DodgerBlue);

    public void UpdateDiagnostics(ImmutableArray<LanguageServiceDiagnostic> diagnostics)
    {
        _diagnostics = diagnostics.IsDefault ? ImmutableArray<LanguageServiceDiagnostic>.Empty : diagnostics;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        var lineIndex = line.LineNumber - 1;
        foreach (var diagnostic in _diagnostics)
        {
            if (lineIndex < diagnostic.Range.Start.Line || lineIndex > diagnostic.Range.End.Line)
            {
                continue;
            }

            var startCharacter = lineIndex == diagnostic.Range.Start.Line ? diagnostic.Range.Start.Character : 0;
            var endCharacter = lineIndex == diagnostic.Range.End.Line
                ? diagnostic.Range.End.Character
                : line.Length;

            var startOffset = line.Offset + Math.Max(0, startCharacter);
            var endOffset = line.Offset + Math.Min(Math.Max(startCharacter + 1, endCharacter), line.Length);
            if (startOffset >= endOffset)
            {
                continue;
            }

            var decoration = diagnostic.Severity switch
            {
                LanguageServiceDiagnosticSeverity.Error => ErrorDecoration,
                LanguageServiceDiagnosticSeverity.Warning => WarningDecoration,
                _ => InformationDecoration
            };

            ChangeLinePart(startOffset, endOffset, element =>
            {
                element.TextRunProperties.SetTextDecorations(decoration);
            });
        }
    }

    private static TextDecorationCollection CreateUnderline(IBrush brush)
    {
        return
        [
            new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeThicknessUnit = TextDecorationUnit.Pixel
            }
        ];
    }
}
