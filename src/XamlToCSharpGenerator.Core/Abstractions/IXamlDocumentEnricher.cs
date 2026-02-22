using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Abstractions;

public interface IXamlDocumentEnricher
{
    (XamlDocumentModel Document, ImmutableArray<DiagnosticInfo> Diagnostics) Enrich(
        XamlDocumentModel document,
        XamlDocumentParseContext parseContext);
}
