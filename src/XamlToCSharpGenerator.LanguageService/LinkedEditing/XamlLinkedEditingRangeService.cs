using System;
using System.Collections.Immutable;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.LinkedEditing;

internal sealed class XamlLinkedEditingRangeService
{
    public XamlLinkedEditingRanges? GetLinkedEditingRanges(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (analysis.XmlDocument?.Root is null)
        {
            return null;
        }

        var text = analysis.Document.Text;
        XElement? bestElement = null;
        SourceRange bestRange = default;
        var bestSpanLength = int.MaxValue;

        foreach (var element in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            if (!TryMatchPosition(text, element, position, out var matchingRange))
            {
                continue;
            }

            var spanLength = GetSpanLength(text, matchingRange);
            if (spanLength >= bestSpanLength)
            {
                continue;
            }

            bestElement = element;
            bestRange = matchingRange;
            bestSpanLength = spanLength;
        }

        if (bestElement is null ||
            !XamlXmlSourceRangeService.TryCreateElementNameRange(text, bestElement, out var openRange) ||
            !XamlXmlSourceRangeService.TryCreateClosingElementNameRange(text, bestElement, out var closingRange))
        {
            return null;
        }

        return new XamlLinkedEditingRanges(
            ImmutableArray.Create(openRange, closingRange),
            @"[-.\w:]+");
    }

    private static bool TryMatchPosition(
        string text,
        XElement element,
        SourcePosition position,
        out SourceRange matchingRange)
    {
        matchingRange = default;
        if (XamlXmlSourceRangeService.TryCreateElementNameRange(text, element, out var openRange) &&
            ContainsPosition(text, openRange, position))
        {
            matchingRange = openRange;
            return true;
        }

        if (XamlXmlSourceRangeService.TryCreateClosingElementNameRange(text, element, out var closingRange) &&
            ContainsPosition(text, closingRange, position))
        {
            matchingRange = closingRange;
            return true;
        }

        return false;
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static int GetSpanLength(string text, SourceRange range)
    {
        return Math.Max(0, TextCoordinateHelper.GetOffset(text, range.End) - TextCoordinateHelper.GetOffset(text, range.Start));
    }
}
