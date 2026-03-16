using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Hover;
using XamlToCSharpGenerator.LanguageService.InlineCode;
using XamlToCSharpGenerator.LanguageService.InlayHints;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Refactorings;
using XamlToCSharpGenerator.LanguageService.SemanticTokens;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService;

public sealed class XamlLanguageServiceEngine : IDisposable
{
    private static readonly ConcurrentDictionary<string, XamlLanguageServiceOptions> SharedAnalysisOptionsCache =
        new(StringComparer.Ordinal);

    private readonly XamlDocumentStore _documentStore;
    private readonly ICompilationProvider _compilationProvider;
    private readonly XamlCompilerAnalysisService _analysisService;
    private readonly XamlCompletionService _completionService;
    private readonly XamlHoverService _hoverService;
    private readonly XamlInlayHintService _inlayHintService;
    private readonly XamlDefinitionService _definitionService;
    private readonly XamlReferenceService _referenceService;
    private readonly CSharpToXamlNavigationService _csharpToXamlNavigationService;
    private readonly XamlDocumentSymbolService _documentSymbolService;
    private readonly XamlSemanticTokenService _semanticTokenService;
    private readonly XamlInlineCSharpProjectionService _inlineCSharpProjectionService;
    private readonly XamlRefactoringService _refactoringService;
    private readonly ConcurrentDictionary<string, int> _uriGenerations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<AnalysisCacheKey, (int Version, XamlAnalysisResult Result)> _analysisCache =
        new();
    private readonly ConcurrentDictionary<InflightAnalysisCacheKey, Lazy<Task<XamlAnalysisResult>>> _inflightAnalysisCache =
        new();
    private readonly ConcurrentDictionary<DocumentCacheKey, ImmutableArray<XamlSemanticToken>> _semanticTokenCache =
        new();
    private readonly ConcurrentDictionary<PositionRequestCacheKey, ImmutableArray<XamlDefinitionLocation>> _definitionCache =
        new();
    private readonly ConcurrentDictionary<PositionRequestCacheKey, ImmutableArray<XamlReferenceLocation>> _referenceCache =
        new();
    private readonly ConcurrentDictionary<InlayHintCacheKey, ImmutableArray<XamlInlayHint>> _inlayHintCache =
        new();

    public XamlLanguageServiceEngine()
        : this(new DeferredCompilationProvider(static () => new MsBuildCompilationProvider()))
    {
    }

    public XamlLanguageServiceEngine(ICompilationProvider compilationProvider)
    {
        _documentStore = new XamlDocumentStore();
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
        _analysisService = new XamlCompilerAnalysisService(_compilationProvider);
        _completionService = new XamlCompletionService();
        _hoverService = new XamlHoverService();
        _inlayHintService = new XamlInlayHintService();
        _definitionService = new XamlDefinitionService();
        _referenceService = new XamlReferenceService();
        _csharpToXamlNavigationService = new CSharpToXamlNavigationService(
            _documentStore,
            _analysisService,
            new CSharpSymbolResolutionService(_compilationProvider));
        _documentSymbolService = new XamlDocumentSymbolService();
        _semanticTokenService = new XamlSemanticTokenService();
        _inlineCSharpProjectionService = new XamlInlineCSharpProjectionService();
        var renameService = new XamlRenameService(_documentStore, _compilationProvider, _analysisService);
        var renameRefactoringProvider = new XamlRenameRefactoringProvider(renameService);
        _refactoringService = new XamlRefactoringService(
            ImmutableArray.Create<IXamlRefactoringProvider>(renameRefactoringProvider),
            renameRefactoringProvider);
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> OpenDocumentAsync(
        string uri,
        string text,
        int version,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        options ??= XamlLanguageServiceOptions.Default;
        _documentStore.Open(uri, text, version);
        InvalidateUriCaches(uri);
        var generation = GetCurrentUriGeneration(uri);

        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<LanguageServiceDiagnostic>.Empty
            : FilterDiagnostics(analysis.Diagnostics, options);
    }

    public void UpsertDocument(string uri, string text, int version)
    {
        var document = _documentStore.Update(uri, text, version) ?? _documentStore.Open(uri, text, version);
        _ = document;
        InvalidateUriCaches(uri);
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> UpdateDocumentAsync(
        string uri,
        string text,
        int version,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        options ??= XamlLanguageServiceOptions.Default;
        _ = _documentStore.Update(uri, text, version) ?? _documentStore.Open(uri, text, version);
        InvalidateUriCaches(uri);
        var generation = GetCurrentUriGeneration(uri);

        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<LanguageServiceDiagnostic>.Empty
            : FilterDiagnostics(analysis.Diagnostics, options);
    }

    public void CloseDocument(string uri)
    {
        _documentStore.Close(uri);
        InvalidateUriCaches(uri);
    }

    public async Task<ImmutableArray<LanguageServiceDiagnostic>> GetDiagnosticsAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<LanguageServiceDiagnostic>.Empty
            : FilterDiagnostics(analysis.Diagnostics, options);
    }

    public async Task<ImmutableArray<XamlCompletionItem>> GetCompletionsAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
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
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
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
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var cacheKey = BuildPositionRequestCacheKey(uri, analysis.Document.Version, generation, options, position);
        if (_definitionCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var definitions = _definitionService.GetDefinitions(analysis, position);
        _definitionCache[cacheKey] = definitions;
        return definitions;
    }

    public async Task<ImmutableArray<XamlReferenceLocation>> GetReferencesAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var cacheKey = BuildPositionRequestCacheKey(uri, analysis.Document.Version, generation, options, position);
        if (_referenceCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var references = _referenceService.GetReferences(analysis, position);
        _referenceCache[cacheKey] = references;
        return references;
    }

    public Task<ImmutableArray<XamlReferenceLocation>> GetXamlReferencesForCSharpSymbolAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _csharpToXamlNavigationService.GetReferencesAsync(
            uri,
            position,
            documentTextOverride,
            options,
            cancellationToken);
    }

    public Task<ImmutableArray<XamlDefinitionLocation>> GetXamlDeclarationsForCSharpSymbolAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _csharpToXamlNavigationService.GetDeclarationsAsync(
            uri,
            position,
            documentTextOverride,
            options,
            cancellationToken);
    }

    public async Task<ImmutableArray<XamlInlayHint>> GetInlayHintsAsync(
        string uri,
        SourceRange requestedRange,
        XamlLanguageServiceOptions options,
        XamlInlayHintOptions inlayHintOptions,
        CancellationToken cancellationToken)
    {
        options ??= XamlLanguageServiceOptions.Default;
        inlayHintOptions ??= XamlInlayHintOptions.Default;

        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        var cacheKey = new InlayHintCacheKey(
            Uri: uri,
            Version: analysis.Document.Version,
            Generation: generation,
            WorkspaceRoot: options.WorkspaceRoot ?? string.Empty,
            IncludeCompilationDiagnostics: options.IncludeCompilationDiagnostics,
            IncludeSemanticDiagnostics: options.IncludeSemanticDiagnostics,
            EnableBindingTypeHints: inlayHintOptions.EnableBindingTypeHints,
            DisplayStyle: inlayHintOptions.TypeDisplayStyle);
        if (_inlayHintCache.TryGetValue(cacheKey, out var cached))
        {
            return FilterInlayHints(cached, requestedRange);
        }

        var hints = _inlayHintService.GetInlayHints(analysis, inlayHintOptions);
        _inlayHintCache[cacheKey] = hints;
        return FilterInlayHints(hints, requestedRange);
    }

    public async Task<ImmutableArray<XamlDocumentSymbol>> GetDocumentSymbolsAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlDocumentSymbol>.Empty
            : _documentSymbolService.GetDocumentSymbols(analysis);
    }

    public async Task<ImmutableArray<XamlSemanticToken>> GetSemanticTokensAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            return ImmutableArray<XamlSemanticToken>.Empty;
        }

        var cacheKey = new DocumentCacheKey(uri, analysis.Document.Version, generation);
        if (_semanticTokenCache.TryGetValue(cacheKey, out var cachedTokens))
        {
            return cachedTokens;
        }

        var tokens = _semanticTokenService.GetTokens(analysis);
        _semanticTokenCache[cacheKey] = tokens;
        return tokens;
    }

    public string? GetMetadataDocumentText(string documentId)
    {
        return XamlMetadataAsSourceService.TryGetDocumentText(documentId, out var text)
            ? text
            : null;
    }

    public async Task<ImmutableArray<XamlInlineCSharpProjection>> GetInlineCSharpProjectionsAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        var generation = GetCurrentUriGeneration(uri);
        var analysis = await GetAnalysisAsync(uri, options, generation, cancellationToken).ConfigureAwait(false);
        return analysis is null
            ? ImmutableArray<XamlInlineCSharpProjection>.Empty
            : _inlineCSharpProjectionService.GetProjections(analysis);
    }

    public Task<XamlPreviewProjectContext?> GetPreviewProjectContextAsync(
        string uri,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(uri) ||
            (Uri.TryCreate(uri, UriKind.Absolute, out var documentUri) && !documentUri.IsFile))
        {
            return Task.FromResult<XamlPreviewProjectContext?>(null);
        }

        var filePath = UriPathHelper.ToFilePath(uri);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Task.FromResult<XamlPreviewProjectContext?>(null);
        }

        var projectPath = XamlProjectFileDiscoveryService.ResolveProjectPath(
            projectPath: null,
            currentFilePath: filePath);
        if (string.IsNullOrWhiteSpace(projectPath) &&
            !string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            projectPath = XamlProjectFileDiscoveryService.ResolveProjectPath(options.WorkspaceRoot, filePath);
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            if (!XamlProjectFileDiscoveryService.TryResolveOwningProjectXamlEntry(
                    filePath,
                    options.WorkspaceRoot,
                    out projectPath,
                    out var resolvedOwningEntry))
            {
                return Task.FromResult<XamlPreviewProjectContext?>(null);
            }

            var owningProjectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(owningProjectDirectory))
            {
                return Task.FromResult<XamlPreviewProjectContext?>(null);
            }

            return Task.FromResult<XamlPreviewProjectContext?>(new XamlPreviewProjectContext(
                projectPath,
                owningProjectDirectory,
                resolvedOwningEntry.FilePath,
                resolvedOwningEntry.TargetPath));
        }

        if (!XamlProjectFileDiscoveryService.TryResolveProjectXamlEntryByFilePath(
                projectPath,
                filePath,
                filePath,
                out var entry))
        {
            if (XamlProjectFileDiscoveryService.TryResolveOwningProjectXamlEntry(
                    filePath,
                    options.WorkspaceRoot,
                    out var owningProjectPath,
                    out var resolvedOwningEntry))
            {
                var owningProjectDirectory = Path.GetDirectoryName(owningProjectPath);
                if (string.IsNullOrWhiteSpace(owningProjectDirectory))
                {
                    return Task.FromResult<XamlPreviewProjectContext?>(null);
                }

                return Task.FromResult<XamlPreviewProjectContext?>(new XamlPreviewProjectContext(
                    owningProjectPath,
                    owningProjectDirectory,
                    resolvedOwningEntry.FilePath,
                    resolvedOwningEntry.TargetPath));
            }

            var projectDirectory = Path.GetDirectoryName(projectPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return Task.FromResult<XamlPreviewProjectContext?>(null);
            }

            entry = new XamlProjectFileDiscoveryService.ProjectXamlFileEntry(
                filePath,
                Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/'));
        }

        var resolvedProjectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(resolvedProjectDirectory))
        {
            return Task.FromResult<XamlPreviewProjectContext?>(null);
        }

        return Task.FromResult<XamlPreviewProjectContext?>(new XamlPreviewProjectContext(
            projectPath,
            resolvedProjectDirectory,
            entry.FilePath,
            entry.TargetPath));
    }

    public Task<XamlPrepareRenameResult?> PrepareRenameAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _refactoringService.PrepareRenameAsync(uri, position, options, documentTextOverride, cancellationToken);
    }

    public Task<XamlWorkspaceEdit> RenameAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _refactoringService.RenameAsync(uri, position, newName, options, documentTextOverride, cancellationToken);
    }

    public Task<XamlWorkspaceEdit> GetCSharpRenamePropagationEditsAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _refactoringService.GetCSharpRenamePropagationEditsAsync(
            uri,
            position,
            newName,
            options,
            documentTextOverride,
            cancellationToken);
    }

    public Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _refactoringService.GetCodeActionsAsync(
            uri,
            position,
            options,
            documentTextOverride,
            cancellationToken);
    }

    public void Dispose()
    {
        _compilationProvider.Dispose();
    }

    private async Task<XamlAnalysisResult?> GetAnalysisAsync(
        string uri,
        XamlLanguageServiceOptions options,
        int generation,
        CancellationToken cancellationToken)
    {
        options ??= XamlLanguageServiceOptions.Default;

        var document = _documentStore.Get(uri);
        if (document is null)
        {
            return null;
        }

        var cacheKey = BuildAnalysisCacheKey(uri, generation, options);

        if (_analysisCache.TryGetValue(cacheKey, out var cached) && cached.Version == document.Version)
        {
            return cached.Result;
        }

        var inflightKey = new InflightAnalysisCacheKey(uri, cacheKey.Generation, cacheKey.WorkspaceRoot, document.Version);
        var lazyAnalysis = _inflightAnalysisCache.GetOrAdd(
            inflightKey,
            _ => new Lazy<Task<XamlAnalysisResult>>(
                () => AnalyzeAsync(document, GetSharedAnalysisOptions(options), CancellationToken.None),
                LazyThreadSafetyMode.ExecutionAndPublication));

        XamlAnalysisResult analysis;
        try
        {
            analysis = cancellationToken.CanBeCanceled
                ? await lazyAnalysis.Value.WaitAsync(cancellationToken).ConfigureAwait(false)
                : await lazyAnalysis.Value.ConfigureAwait(false);
        }
        finally
        {
            if (lazyAnalysis.IsValueCreated && lazyAnalysis.Value.IsCompleted)
            {
                _inflightAnalysisCache.TryRemove(inflightKey, out _);
            }
        }

        if (_documentStore.Get(uri) is { Version: var currentVersion } && currentVersion == document.Version)
        {
            _analysisCache[cacheKey] = (document.Version, analysis);
        }

        return analysis;
    }

    private Task<XamlAnalysisResult> AnalyzeAsync(
        LanguageServiceDocument document,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        return _analysisService.AnalyzeAsync(document, options, cancellationToken);
    }

    private void InvalidateUriCaches(string uri)
    {
        _uriGenerations.AddOrUpdate(uri, 1, static (_, generation) => generation + 1);
    }

    private static PositionRequestCacheKey BuildPositionRequestCacheKey(
        string uri,
        int documentVersion,
        int generation,
        XamlLanguageServiceOptions options,
        SourcePosition position)
    {
        options ??= XamlLanguageServiceOptions.Default;
        return new PositionRequestCacheKey(
            Uri: uri,
            Version: documentVersion,
            Generation: generation,
            WorkspaceRoot: options.WorkspaceRoot ?? string.Empty,
            IncludeCompilationDiagnostics: options.IncludeCompilationDiagnostics,
            IncludeSemanticDiagnostics: options.IncludeSemanticDiagnostics,
            Line: position.Line,
            Character: position.Character);
    }

    private static AnalysisCacheKey BuildAnalysisCacheKey(
        string uri,
        int generation,
        XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        return new AnalysisCacheKey(
            Uri: uri,
            Generation: generation,
            WorkspaceRoot: options.WorkspaceRoot ?? string.Empty);
    }

    private int GetCurrentUriGeneration(string uri)
    {
        return _uriGenerations.TryGetValue(uri, out var generation)
            ? generation
            : 0;
    }

    private static XamlLanguageServiceOptions GetSharedAnalysisOptions(XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        if (options.IncludeCompilationDiagnostics && options.IncludeSemanticDiagnostics)
        {
            return options;
        }

        var workspaceRoot = options.WorkspaceRoot ?? string.Empty;
        return SharedAnalysisOptionsCache.GetOrAdd(
            workspaceRoot,
            static root => new XamlLanguageServiceOptions(
                WorkspaceRoot: root.Length == 0 ? null : root,
                IncludeCompilationDiagnostics: true,
                IncludeSemanticDiagnostics: true));
    }

    private static ImmutableArray<LanguageServiceDiagnostic> FilterDiagnostics(
        ImmutableArray<LanguageServiceDiagnostic> diagnostics,
        XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        if (diagnostics.IsDefaultOrEmpty ||
            (options.IncludeCompilationDiagnostics && options.IncludeSemanticDiagnostics))
        {
            return diagnostics;
        }

        ImmutableArray<LanguageServiceDiagnostic>.Builder? builder = null;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (!options.IncludeCompilationDiagnostics &&
                string.Equals(diagnostic.Source, "MSBuildWorkspace", StringComparison.Ordinal))
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(diagnostics[preservedIndex]);
                    }
                }

                continue;
            }

            if (!options.IncludeSemanticDiagnostics &&
                string.Equals(diagnostic.Source, "AXSG.Semantic", StringComparison.Ordinal))
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(diagnostics[preservedIndex]);
                    }
                }

                continue;
            }

            builder?.Add(diagnostic);
        }

        return builder?.ToImmutable() ?? diagnostics;
    }

    private static ImmutableArray<XamlInlayHint> FilterInlayHints(
        ImmutableArray<XamlInlayHint> hints,
        SourceRange requestedRange)
    {
        if (hints.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        ImmutableArray<XamlInlayHint>.Builder? builder = null;
        for (var index = 0; index < hints.Length; index++)
        {
            var hint = hints[index];
            if (ComparePositions(hint.Position, requestedRange.Start) < 0 ||
                ComparePositions(hint.Position, requestedRange.End) > 0)
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<XamlInlayHint>(hints.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(hints[preservedIndex]);
                    }
                }

                continue;
            }

            builder?.Add(hint);
        }

        return builder?.ToImmutable() ?? hints;
    }

    private static int ComparePositions(SourcePosition left, SourcePosition right)
    {
        var lineComparison = left.Line.CompareTo(right.Line);
        if (lineComparison != 0)
        {
            return lineComparison;
        }

        return left.Character.CompareTo(right.Character);
    }

    private readonly record struct AnalysisCacheKey(
        string Uri,
        int Generation,
        string WorkspaceRoot);

    private readonly record struct InflightAnalysisCacheKey(
        string Uri,
        int Generation,
        string WorkspaceRoot,
        int Version);

    private readonly record struct PositionRequestCacheKey(
        string Uri,
        int Version,
        int Generation,
        string WorkspaceRoot,
        bool IncludeCompilationDiagnostics,
        bool IncludeSemanticDiagnostics,
        int Line,
        int Character);

    private readonly record struct InlayHintCacheKey(
        string Uri,
        int Version,
        int Generation,
        string WorkspaceRoot,
        bool IncludeCompilationDiagnostics,
        bool IncludeSemanticDiagnostics,
        bool EnableBindingTypeHints,
        XamlInlayHintTypeDisplayStyle DisplayStyle);

    private readonly record struct DocumentCacheKey(string Uri, int Version, int Generation);
}
