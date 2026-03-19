using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlResourceDeclarationRangeService
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static ImmutableArray<SourceRange> FindDeclarationRanges(
        XamlAnalysisResult analysis,
        string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) ||
            string.IsNullOrWhiteSpace(analysis.Document.Text) ||
            analysis.XmlDocument?.Root is null)
        {
            return ImmutableArray<SourceRange>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!IsResourceKeyDeclarationAttribute(attribute) ||
                    !string.Equals(attribute.Value, identifier, StringComparison.Ordinal) ||
                    !XamlXmlSourceRangeService.TryCreateAttributeValueRange(analysis.Document.Text, attribute, out var range))
                {
                    continue;
                }

                var identity = $"{range.Start.Line}:{range.Start.Character}:{range.End.Line}:{range.End.Character}";
                if (seen.Add(identity))
                {
                    builder.Add(range);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsResourceKeyDeclarationAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }
}
