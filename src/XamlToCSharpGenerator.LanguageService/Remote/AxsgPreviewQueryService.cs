using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Remote;

/// <summary>
/// Provides transport-neutral workspace queries used by LSP, MCP, and preview hosts.
/// </summary>
public sealed class AxsgPreviewQueryService
{
    private readonly XamlLanguageServiceEngine _engine;
    private readonly XamlLanguageServiceOptions _defaultOptions;

    /// <summary>
    /// Creates a new preview query service over the shared language-service engine.
    /// </summary>
    public AxsgPreviewQueryService(
        XamlLanguageServiceEngine engine,
        XamlLanguageServiceOptions defaultOptions)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _defaultOptions = defaultOptions ?? throw new ArgumentNullException(nameof(defaultOptions));
    }

    /// <summary>
    /// Resolves the owning project and project-relative XAML target path for preview.
    /// </summary>
    public Task<XamlPreviewProjectContext?> GetPreviewProjectContextAsync(
        string uri,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        var options = string.IsNullOrWhiteSpace(workspaceRoot)
            ? _defaultOptions
            : _defaultOptions with { WorkspaceRoot = workspaceRoot };
        return _engine.GetPreviewProjectContextAsync(uri, options, cancellationToken);
    }
}
