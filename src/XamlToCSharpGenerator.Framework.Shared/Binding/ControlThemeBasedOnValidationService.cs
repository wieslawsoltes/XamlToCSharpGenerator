using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ControlThemeBasedOnValidationService
{
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;

    public ControlThemeBasedOnValidationService(
        TryParseMarkupExtensionDelegate tryParseMarkupExtension)
    {
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
    }

    public void Validate(
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document)
    {
        var themesByKey = new Dictionary<string, List<ResolvedControlThemeDefinition>>(StringComparer.Ordinal);
        foreach (var theme in controlThemes)
        {
            if (!TryNormalizeResourceKey(theme.Key, out var normalizedKey))
            {
                continue;
            }

            if (!themesByKey.TryGetValue(normalizedKey, out var bucket))
            {
                bucket = new List<ResolvedControlThemeDefinition>();
                themesByKey[normalizedKey] = bucket;
            }

            bucket.Add(theme);
        }

        foreach (var theme in controlThemes)
        {
            if (!TryNormalizeStaticResourceReference(theme.BasedOn, out var normalizedBasedOnKey) ||
                !TryNormalizeResourceKey(theme.Key, out var normalizedThemeKey) ||
                string.Equals(normalizedThemeKey, normalizedBasedOnKey, StringComparison.Ordinal))
            {
                continue;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal) { normalizedThemeKey };
            if (HasCycle(normalizedBasedOnKey, normalizedThemeKey, themesByKey, visited))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0306",
                    $"ControlTheme '{theme.Key}' participates in a BasedOn cycle through '{theme.BasedOn}'.",
                    document.FilePath,
                    theme.Line,
                    theme.Column,
                    false));
            }
        }
    }

    private bool HasCycle(
        string currentKey,
        string originalKey,
        IReadOnlyDictionary<string, List<ResolvedControlThemeDefinition>> themesByKey,
        HashSet<string> visited)
    {
        if (string.Equals(currentKey, originalKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (!visited.Add(currentKey) ||
            !themesByKey.TryGetValue(currentKey, out var themes))
        {
            return false;
        }

        foreach (var theme in themes)
        {
            if (!TryNormalizeStaticResourceReference(theme.BasedOn, out var nextKey) ||
                string.Equals(nextKey, currentKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (HasCycle(nextKey, originalKey, themesByKey, visited))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryNormalizeStaticResourceReference(string? rawValue, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue) ||
            !_tryParseMarkupExtension(rawValue, out var markup))
        {
            return false;
        }

        var extensionKind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
        if (extensionKind is not XamlMarkupExtensionKind.StaticResource &&
            extensionKind is not XamlMarkupExtensionKind.DynamicResource)
        {
            return false;
        }

        var rawKey = markup.PositionalArguments.Length > 0
            ? markup.PositionalArguments[0]
            : string.Empty;
        return TryNormalizeResourceKey(rawKey, out normalizedKey);
    }

    private bool TryNormalizeResourceKey(string? rawKey, out string normalizedKey)
    {
        normalizedKey = string.Empty;
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return false;
        }

        var trimmed = rawKey.Trim();
        if (_tryParseMarkupExtension(trimmed, out var markup) &&
            XamlMarkupExtensionNameSemantics.Classify(markup.Name) == XamlMarkupExtensionKind.Type)
        {
            var typeToken = markup.PositionalArguments.Length > 0
                ? markup.PositionalArguments[0].Trim()
                : string.Empty;
            if (typeToken.Length == 0)
            {
                return false;
            }

            normalizedKey = "type:" + typeToken;
            return true;
        }

        normalizedKey = "text:" + trimmed;
        return true;
    }
}
