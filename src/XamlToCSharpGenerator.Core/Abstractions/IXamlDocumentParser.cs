using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Abstractions;

public interface IXamlDocumentParser
{
    (XamlDocumentModel? Document, ImmutableArray<DiagnosticInfo> Diagnostics) Parse(XamlFileInput input);
}
