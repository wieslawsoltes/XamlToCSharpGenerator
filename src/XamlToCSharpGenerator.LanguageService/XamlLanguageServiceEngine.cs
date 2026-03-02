using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Hover;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.SemanticTokens;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService;

public sealed class XamlLanguageServiceEngine : IDisposable
{
    private readonly XamlDocumentStore _documentStore;
    private readonly ICompilationProvider _compilationProvider;
    private readonly XamlCompilerAnalysisService _analysisService;
    private readonly XamlCompletionService _completionService;
    private readonly XamlHoverService _hoverService;
    private readonly XamlDefinitionService _definitionService;
    private readonly XamlReferenceService _referenceService;
    private readonly XamlDocumentSymbolService _documentSymbolService;
    private readonly XamlSemanticTokenService _semanticTokenService;
    private readonly ConcurrentDictionary<string, (int Version, XamlAnalysisResult Result)> _analysisCache =
        new(StringComparer.Ordinal);

    public XamlLanguageServiceEngine()
        : this(new MsBuildCompilationProvider())
    {
    }

    public XamlLanguageServiceEngine(ICompilationProvider compilationProvider)
    {
        _documentStore = new XamlDocumentStore();
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
        _analysisService = new XamlCompilerAnalysisService(_compilationProvider);
        _completionService = new XamlCompletionService();
        _hoverService = new XamlHoverService();
        _definitionService = new XamlDefinitionService();
        _referenceService = new XamlReferenceService();
        _documentSymbolService = new XamlDocumentSymbolService();
        _semanticTokenService = new XamlSemanticTokenService();
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> OpenDocumentAsync(
        string uri,
        string text,
        int version,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var document = _documentStore.Open(uri, text, version);
        _compilationProvider.Invalidate(document.FilePath);
        _analysisCache.TryRemove(uri, out _);

        var analysis = await AnalyzeAsync(document, options, cancellationToken).ConfigureAwait(false);
        return analysis.Diagnostics;
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> UpdateDocumentAsync(
        string uri,
        string text,
        int version,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var document = _documentStore.Update(uri, text, version) ?? _documentStore.Open(uri, text, version);
        _analysisCache.TryRemove(uri, out _);

        var analysis = await AnalyzeAsync(document, options, cancellationToken).ConfigureAwait(false);
        return analysis.Diagnostics;
    }

    public void CloseDocument(string uri)
    {
        var document = _documentStore.Get(uri);
        if (document is not null)
        {
            _compilationProvider.Invalidate(document.FilePath);
        }

        _documentStore.Close(uri);
        _analysisCache.TryRemove(uri, out _);
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> GetDiagnosticsAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis?.Diagnostics ?? ImmutableArray<LanguageServiceDiagnostic>.Empty;
    }

    public async Task<ImmutableArray<XamlCompletionItem>> GetCompletionsAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlCompletionItem>.Empty
            : _completionService.GetCompletions(analysis, position);
    }

    public async Task<XamlHoverInfo?> GetHoverAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? null
            : _hoverService.GetHover(analysis, position);
    }

    public async Task<ImmutableArray<XamlDefinitionLocation>> GetDefinitionsAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlDefinitionLocation>.Empty
            : _definitionService.GetDefinitions(analysis, position);
    }

    public async Task<ImmutableArray<XamlReferenceLocation>> GetReferencesAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlReferenceLocation>.Empty
            : _referenceService.GetReferences(analysis, position);
    }

    public async Task<ImmutableArray<XamlDocumentSymbol>> GetDocumentSymbolsAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlDocumentSymbol>.Empty
            : _documentSymbolService.GetDocumentSymbols(analysis);
    }

    public async Task<ImmutableArray<XamlSemanticToken>> GetSemanticTokensAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var analysis = await GetAnalysisAsync(uri, options, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlSemanticToken>.Empty
            : _semanticTokenService.GetTokens(analysis.Document.Text);
    }

    public void Dispose()
    {
        _compilationProvider.Dispose();
    }

    private async Task<XamlAnalysisResult?> GetAnalysisAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var document = _documentStore.Get(uri);
        if (document is null)
        {
            return null;
        }

        if (_analysisCache.TryGetValue(uri, out var cached) && cached.Version == document.Version)
        {
            return cached.Result;
        }

        var analysis = await AnalyzeAsync(document, options, cancellationToken).ConfigureAwait(false);
        _analysisCache[uri] = (document.Version, analysis);
        return analysis;
    }

    private Task<XamlAnalysisResult> AnalyzeAsync(
        LanguageServiceDocument document,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        return _analysisService.AnalyzeAsync(document, options, cancellationToken);
    }
}
