using System;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlExplicitExpressionMarkupDetector
{
    public static bool ContainsExplicitExpressionMarkup(string xamlText)
    {
        if (string.IsNullOrWhiteSpace(xamlText))
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(xamlText, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return false;
            }

            foreach (var attribute in document.Root.DescendantsAndSelf().Attributes())
            {
                if (IsExplicitExpressionMarkup(attribute.Value))
                {
                    return true;
                }
            }

            foreach (var textNode in document.Root.DescendantNodesAndSelf())
            {
                if (textNode is XText text &&
                    IsExplicitExpressionMarkup(text.Value))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return xamlText.Contains("{=", StringComparison.Ordinal);
        }
    }

    private static bool IsExplicitExpressionMarkup(string value)
    {
        return MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var innerContent) &&
               innerContent.StartsWith("=", StringComparison.Ordinal);
    }
}
