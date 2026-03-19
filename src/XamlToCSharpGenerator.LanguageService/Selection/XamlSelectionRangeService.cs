using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Selection;

internal sealed class XamlSelectionRangeService
{
    public XamlSelectionRange GetSelectionRange(XamlAnalysisResult analysis, SourcePosition position)
    {
        var text = analysis.Document.Text;
        var xmlDocument = analysis.XmlDocument;
        var ranges = new List<SourceRange>();

        if (XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                text,
                xmlDocument,
                position,
                out var attributeElement,
                out var attribute,
                out var attributeNameRange,
                out var attributeValueRange))
        {
            if (ContainsPosition(text, attributeValueRange, position))
            {
                AddIfValid(ranges, attributeValueRange);
            }
            else if (ContainsPosition(text, attributeNameRange, position))
            {
                AddIfValid(ranges, attributeNameRange);
            }

            if (XamlXmlSourceRangeService.TryCreateAttributeRange(text, attribute, out var attributeRange))
            {
                AddIfValid(ranges, attributeRange);
            }

            AddElementRangeChain(ranges, text, attributeElement);
            return BuildChain(ranges, text, position);
        }

        if (XamlXmlSourceRangeService.TryFindElementNameAtPosition(
                text,
                xmlDocument,
                position,
                out var nameElement,
                out var elementNameRange))
        {
            AddIfValid(ranges, elementNameRange);
            AddElementRangeChain(ranges, text, nameElement);
            return BuildChain(ranges, text, position);
        }

        if (XamlXmlSourceRangeService.TryFindInnermostElementAtPosition(
                text,
                xmlDocument,
                position,
                out var contentElement,
                out _))
        {
            AddElementRangeChain(ranges, text, contentElement);
            return BuildChain(ranges, text, position);
        }

        return CreateFallbackSelectionRange(text, position);
    }

    private static void AddElementRangeChain(List<SourceRange> ranges, string text, XElement element)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            if (XamlXmlSourceRangeService.TryCreateElementRange(text, current, out var range))
            {
                AddIfValid(ranges, range);
            }
        }
    }

    private static XamlSelectionRange BuildChain(List<SourceRange> ranges, string text, SourcePosition position)
    {
        if (ranges.Count == 0)
        {
            return CreateFallbackSelectionRange(text, position);
        }

        var ordered = ranges
            .Distinct()
            .OrderBy(static range => GetSpanLength(range))
            .ThenBy(static range => range.Start.Line)
            .ThenBy(static range => range.Start.Character)
            .ToArray();

        XamlSelectionRange? parent = null;
        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            parent = new XamlSelectionRange(ordered[index], parent);
        }

        return parent ?? CreateFallbackSelectionRange(text, position);
    }

    private static void AddIfValid(List<SourceRange> ranges, SourceRange range)
    {
        if (GetSpanLength(range) <= 0)
        {
            return;
        }

        ranges.Add(range);
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static int GetSpanLength(SourceRange range)
    {
        var lineDelta = range.End.Line - range.Start.Line;
        if (lineDelta != 0)
        {
            return lineDelta * 10_000 + (range.End.Character - range.Start.Character);
        }

        return range.End.Character - range.Start.Character;
    }

    private static XamlSelectionRange CreateFallbackSelectionRange(string text, SourcePosition position)
    {
        var sourceText = SourceText.From(text ?? string.Empty);
        var lineIndex = Math.Max(0, Math.Min(position.Line, sourceText.Lines.Count - 1));
        var line = sourceText.Lines[lineIndex];
        var startCharacter = Math.Max(0, Math.Min(position.Character, line.End - line.Start));
        var endCharacter = Math.Min(startCharacter + 1, line.End - line.Start);
        if (endCharacter <= startCharacter)
        {
            endCharacter = startCharacter;
        }

        return new XamlSelectionRange(
            new SourceRange(
                new SourcePosition(lineIndex, startCharacter),
                new SourcePosition(lineIndex, endCharacter)),
            Parent: null);
    }
}
