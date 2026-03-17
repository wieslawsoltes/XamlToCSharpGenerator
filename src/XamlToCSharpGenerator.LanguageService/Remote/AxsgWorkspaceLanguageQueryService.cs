using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Refactorings;

namespace XamlToCSharpGenerator.LanguageService.Remote;

/// <summary>
/// Provides transport-neutral AXSG workspace language-service queries used by MCP and editor adapters.
/// </summary>
public sealed class AxsgWorkspaceLanguageQueryService
{
    private const string MetadataScheme = "axsg-metadata";

    private readonly XamlLanguageServiceEngine _engine;
    private readonly XamlLanguageServiceOptions _defaultOptions;

    /// <summary>
    /// Creates a new workspace language query service over the shared language-service engine.
    /// </summary>
    public AxsgWorkspaceLanguageQueryService(
        XamlLanguageServiceEngine engine,
        XamlLanguageServiceOptions defaultOptions)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _defaultOptions = defaultOptions ?? throw new ArgumentNullException(nameof(defaultOptions));
    }

    /// <summary>
    /// Resolves metadata-as-source document text from either a direct document id or a metadata URI.
    /// </summary>
    public string? GetMetadataDocumentText(string? documentId, string? metadataUri)
    {
        string? resolvedDocumentId = NormalizeOptionalText(documentId) ?? TryGetMetadataDocumentId(metadataUri);
        return string.IsNullOrWhiteSpace(resolvedDocumentId)
            ? null
            : _engine.GetMetadataDocumentText(resolvedDocumentId);
    }

    /// <summary>
    /// Gets inline C# projections for the specified XAML document, optionally using in-memory text.
    /// </summary>
    public async Task<ImmutableArray<XamlInlineCSharpProjection>> GetInlineCSharpProjectionsAsync(
        string uri,
        string? workspaceRoot,
        string? documentTextOverride,
        int version,
        CancellationToken cancellationToken)
    {
        string? normalizedText = NormalizeOptionalText(documentTextOverride, preserveWhitespace: true);
        if (normalizedText is not null)
        {
            _engine.UpsertDocument(uri, normalizedText, Math.Max(0, version));
        }

        return await _engine.GetInlineCSharpProjectionsAsync(
            uri,
            ResolveOptions(workspaceRoot),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets XAML references for the C# symbol at the requested source position.
    /// </summary>
    public Task<ImmutableArray<XamlReferenceLocation>> GetCSharpReferencesAsync(
        string uri,
        SourcePosition position,
        string? workspaceRoot,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _engine.GetXamlReferencesForCSharpSymbolAsync(
            uri,
            position,
            ResolveOptions(workspaceRoot),
            NormalizeOptionalText(documentTextOverride, preserveWhitespace: true),
            cancellationToken);
    }

    /// <summary>
    /// Gets XAML declarations for the C# symbol at the requested source position.
    /// </summary>
    public Task<ImmutableArray<XamlDefinitionLocation>> GetCSharpDeclarationsAsync(
        string uri,
        SourcePosition position,
        string? workspaceRoot,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _engine.GetXamlDeclarationsForCSharpSymbolAsync(
            uri,
            position,
            ResolveOptions(workspaceRoot),
            NormalizeOptionalText(documentTextOverride, preserveWhitespace: true),
            cancellationToken);
    }

    /// <summary>
    /// Gets rename propagation edits from a C# symbol into XAML documents.
    /// </summary>
    public Task<XamlWorkspaceEdit> GetRenamePropagationEditsAsync(
        string uri,
        SourcePosition position,
        string newName,
        string? workspaceRoot,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _engine.GetCSharpRenamePropagationEditsAsync(
            uri,
            position,
            newName,
            ResolveOptions(workspaceRoot),
            NormalizeOptionalText(documentTextOverride, preserveWhitespace: true),
            cancellationToken);
    }

    /// <summary>
    /// Prepares an AXSG rename operation for the requested XAML position.
    /// </summary>
    public Task<XamlPrepareRenameResult?> PrepareRenameAsync(
        string uri,
        SourcePosition position,
        string? workspaceRoot,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _engine.PrepareRenameAsync(
            uri,
            position,
            ResolveOptions(workspaceRoot),
            NormalizeOptionalText(documentTextOverride, preserveWhitespace: true),
            cancellationToken);
    }

    /// <summary>
    /// Computes AXSG rename edits for the requested XAML position.
    /// </summary>
    public Task<XamlWorkspaceEdit> RenameAsync(
        string uri,
        SourcePosition position,
        string newName,
        string? workspaceRoot,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _engine.RenameAsync(
            uri,
            position,
            newName,
            ResolveOptions(workspaceRoot),
            NormalizeOptionalText(documentTextOverride, preserveWhitespace: true),
            cancellationToken);
    }

    private XamlLanguageServiceOptions ResolveOptions(string? workspaceRoot)
    {
        string? normalizedWorkspaceRoot = NormalizeOptionalText(workspaceRoot);
        return normalizedWorkspaceRoot is null
            ? _defaultOptions
            : _defaultOptions with { WorkspaceRoot = normalizedWorkspaceRoot };
    }

    private static string? TryGetMetadataDocumentId(string? metadataUri)
    {
        string? normalizedMetadataUri = NormalizeOptionalText(metadataUri);
        if (normalizedMetadataUri is null ||
            !Uri.TryCreate(normalizedMetadataUri, UriKind.Absolute, out Uri? uri) ||
            !string.Equals(uri.Scheme, MetadataScheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        string[] pairs = query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (string pair in pairs)
        {
            int separatorIndex = pair.IndexOf('=');
            string key = separatorIndex >= 0 ? pair[..separatorIndex] : pair;
            if (!string.Equals(Uri.UnescapeDataString(key), "id", StringComparison.Ordinal))
            {
                continue;
            }

            string value = separatorIndex >= 0 && separatorIndex + 1 < pair.Length
                ? pair[(separatorIndex + 1)..]
                : string.Empty;
            return NormalizeOptionalText(Uri.UnescapeDataString(value));
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? value, bool preserveWhitespace = false)
    {
        if (value is null)
        {
            return null;
        }

        if (preserveWhitespace)
        {
            return value;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }
}
