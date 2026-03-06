using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal sealed class CSharpToXamlNavigationService
{
    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;
    private readonly CSharpSymbolResolutionService _symbolResolutionService;
    private readonly XamlReferenceService _referenceService;

    public CSharpToXamlNavigationService(
        XamlDocumentStore documentStore,
        XamlCompilerAnalysisService analysisService,
        CSharpSymbolResolutionService symbolResolutionService)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _symbolResolutionService = symbolResolutionService ?? throw new ArgumentNullException(nameof(symbolResolutionService));
        _referenceService = new XamlReferenceService();
    }

    public async Task<ImmutableArray<XamlReferenceLocation>> GetReferencesAsync(
        string uri,
        SourcePosition position,
        string? documentTextOverride,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var resolvedSymbol = await _symbolResolutionService
            .ResolveSymbolAtPositionAsync(uri, position, documentTextOverride, options, cancellationToken)
            .ConfigureAwait(false);
        if (resolvedSymbol is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var analysis = await CreateProjectXamlAnalysisAsync(resolvedSymbol, options, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        return FilterToProjectXamlLocations(
            _referenceService.GetReferencesForClrSymbol(analysis, resolvedSymbol.Symbol));
    }

    public async Task<ImmutableArray<XamlDefinitionLocation>> GetDeclarationsAsync(
        string uri,
        SourcePosition position,
        string? documentTextOverride,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var resolvedSymbol = await _symbolResolutionService
            .ResolveSymbolAtPositionAsync(uri, position, documentTextOverride, options, cancellationToken)
            .ConfigureAwait(false);
        if (resolvedSymbol?.Symbol is not INamedTypeSymbol typeSymbol)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var analysis = await CreateProjectXamlAnalysisAsync(resolvedSymbol, options, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        return CollectXamlTypeDeclarations(analysis, typeSymbol);
    }

    private async Task<XamlAnalysisResult?> CreateProjectXamlAnalysisAsync(
        CSharpResolvedSymbol resolvedSymbol,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var xamlFilePaths = XamlProjectFileDiscoveryService.DiscoverProjectXamlFilePaths(
            resolvedSymbol.Snapshot.ProjectPath,
            resolvedSymbol.FilePath);
        if (xamlFilePaths.IsDefaultOrEmpty)
        {
            return null;
        }

        var anchorPath = xamlFilePaths
            .FirstOrDefault(path => _documentStore.GetByFilePath(path) is not null)
            ?? xamlFilePaths[0];

        var anchorDocument = CreateLanguageServiceDocument(anchorPath);
        if (anchorDocument is null)
        {
            return null;
        }

        return await _analysisService.AnalyzeAsync(anchorDocument, options, cancellationToken).ConfigureAwait(false);
    }

    private LanguageServiceDocument? CreateLanguageServiceDocument(string filePath)
    {
        var openDocument = _documentStore.GetByFilePath(filePath);
        if (openDocument is not null)
        {
            return openDocument;
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        var text = File.ReadAllText(filePath);
        return new LanguageServiceDocument(
            UriPathHelper.ToDocumentUri(filePath),
            filePath,
            text,
            0);
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectXamlTypeDeclarations(
        XamlAnalysisResult analysis,
        INamedTypeSymbol typeSymbol)
    {
        var fullTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectXamlPaths = XamlProjectFileDiscoveryService.DiscoverProjectXamlFilePaths(
            analysis.ProjectPath,
            analysis.Document.FilePath);

        foreach (var filePath in projectXamlPaths)
        {
            XDocument xmlDocument;
            string text;
            try
            {
                text = File.Exists(filePath)
                    ? File.ReadAllText(filePath)
                    : string.Empty;
                if (text.Length == 0)
                {
                    continue;
                }

                xmlDocument = XDocument.Parse(text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            }
            catch
            {
                continue;
            }

            foreach (var element in xmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
            {
                var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration ||
                        !IsXClassAttribute(attribute))
                    {
                        continue;
                    }

                    if (!XamlTypeReferenceNavigationResolver.TryResolve(
                            analysis,
                            prefixMap,
                            attribute.Name.LocalName,
                            attribute.Value,
                            out var typeReference) ||
                        !string.Equals(typeReference.FullTypeName, fullTypeName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(text, attribute, out var range))
                    {
                        continue;
                    }

                    var uri = UriPathHelper.ToDocumentUri(filePath);
                    var identity = uri + "|" + range.Start.Line + ":" + range.Start.Character + ":" + range.End.Line + ":" + range.End.Character;
                    if (!seen.Add(identity))
                    {
                        continue;
                    }

                    builder.Add(new XamlDefinitionLocation(uri, range));
                }
            }
        }

        return builder
            .ToImmutable()
            .OrderBy(static item => item.Uri, StringComparer.Ordinal)
            .ThenBy(static item => item.Range.Start.Line)
            .ThenBy(static item => item.Range.Start.Character)
            .ToImmutableArray();
    }

    private static bool IsXClassAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, "http://schemas.microsoft.com/winfx/2006/xaml", StringComparison.Ordinal);
    }

    private static ImmutableArray<XamlReferenceLocation> FilterToProjectXamlLocations(
        ImmutableArray<XamlReferenceLocation> references)
    {
        if (references.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>(references.Length);
        foreach (var reference in references)
        {
            if (IsProjectXamlUri(reference.Uri))
            {
                builder.Add(reference);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsProjectXamlUri(string uri)
    {
        var filePath = UriPathHelper.ToFilePath(uri);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        return string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(filePath), ".axaml", StringComparison.OrdinalIgnoreCase);
    }
}
