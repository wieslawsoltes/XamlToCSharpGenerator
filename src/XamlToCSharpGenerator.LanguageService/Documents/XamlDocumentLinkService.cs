using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Documents;

public sealed class XamlDocumentLinkService
{
    public ImmutableArray<XamlDocumentLink> GetDocumentLinks(XamlAnalysisResult analysis)
    {
        if (analysis.XmlDocument?.Root is null)
        {
            return ImmutableArray<XamlDocumentLink>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDocumentLink>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!string.Equals(attribute.Name.LocalName, "Source", StringComparison.Ordinal) ||
                    !XamlXmlSourceRangeService.TryCreateAttributeValueRange(
                        analysis.Document.Text,
                        attribute,
                        out var range) ||
                    !XamlUriValueNavigationService.TryResolveDocumentUri(
                        analysis,
                        attribute.Value,
                        out var targetUri))
                {
                    continue;
                }

                var identity = $"{range.Start.Line}:{range.Start.Character}:{range.End.Line}:{range.End.Character}:{targetUri}";
                if (seen.Add(identity))
                {
                    builder.Add(new XamlDocumentLink(range, targetUri));
                }
            }
        }

        return builder.ToImmutable();
    }
}
