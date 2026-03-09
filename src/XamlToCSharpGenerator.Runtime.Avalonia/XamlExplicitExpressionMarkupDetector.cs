using System;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Runtime;

internal static class XamlExplicitExpressionMarkupDetector
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private const string LegacyRuntimeUsingNamespace = "using:XamlToCSharpGenerator.Runtime";
    private const string MarkupRuntimeUsingNamespace = "using:XamlToCSharpGenerator.Runtime.Markup";

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
                if (IsExplicitExpressionMarkup(attribute.Value) ||
                    IsInlineCSharpMarkup(attribute.Value))
                {
                    return true;
                }
            }

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (IsInlineCSharpElement(element))
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
            return xamlText.Contains("{=", StringComparison.Ordinal) ||
                   xamlText.Contains(":CSharp", StringComparison.Ordinal) ||
                   xamlText.Contains("<CSharp", StringComparison.Ordinal);
        }
    }

    private static bool IsExplicitExpressionMarkup(string value)
    {
        return MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var innerContent) &&
               innerContent.StartsWith("=", StringComparison.Ordinal);
    }

    private static bool IsInlineCSharpMarkup(string value)
    {
        if (!MarkupExpressionEnvelopeSemantics.TryExtractInnerContent(value, out var innerContent))
        {
            return false;
        }

        innerContent = innerContent.TrimStart();
        if (innerContent.Length == 0)
        {
            return false;
        }

        var tokenEnd = 0;
        while (tokenEnd < innerContent.Length &&
               !char.IsWhiteSpace(innerContent[tokenEnd]) &&
               innerContent[tokenEnd] != ',')
        {
            tokenEnd++;
        }

        if (tokenEnd == 0)
        {
            return false;
        }

        var token = innerContent.Substring(0, tokenEnd);
        return string.Equals(token, "CSharp", StringComparison.Ordinal) ||
               token.EndsWith(":CSharp", StringComparison.Ordinal);
    }

    private static bool IsInlineCSharpElement(XElement element)
    {
        return string.Equals(element.Name.LocalName, "CSharp", StringComparison.Ordinal) &&
               (string.Equals(element.Name.NamespaceName, LegacyRuntimeUsingNamespace, StringComparison.Ordinal) ||
                string.Equals(element.Name.NamespaceName, MarkupRuntimeUsingNamespace, StringComparison.Ordinal) ||
                string.Equals(element.Name.NamespaceName, AvaloniaDefaultXmlNamespace, StringComparison.Ordinal));
    }
}
