using System.Collections.Immutable;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlAnalysisResult(
    LanguageServiceDocument Document,
    string? ProjectPath,
    Compilation? Compilation,
    XamlDocumentModel? ParsedDocument,
    ResolvedViewModel? ViewModel,
    XDocument? XmlDocument,
    ImmutableDictionary<string, string> PrefixMap,
    AvaloniaTypeIndex? TypeIndex,
    XamlLanguageFrameworkInfo Framework,
    ImmutableArray<LanguageServiceDiagnostic> Diagnostics);
