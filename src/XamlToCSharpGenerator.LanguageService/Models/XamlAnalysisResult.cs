using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlAnalysisResult(
    LanguageServiceDocument Document,
    Compilation? Compilation,
    XamlDocumentModel? ParsedDocument,
    XDocument? XmlDocument,
    AvaloniaTypeIndex? TypeIndex,
    ImmutableArray<LanguageServiceDiagnostic> Diagnostics);
