using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static ImmutableArray<ResolvedIncludeDefinition> BindIncludes(
        XamlDocumentModel document,
        Compilation compilation,
        string currentDocumentUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var includes = ImmutableArray.CreateBuilder<ResolvedIncludeDefinition>(document.Includes.Length);

        foreach (var include in document.Includes)
        {
            if (ShouldSkipConditionalBranch(
                    include.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(include.Source))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0400",
                    $"Include '{include.Kind}' is missing Source.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedIncludeSource = NormalizeIncludeSourceForResolution(include.Source);
            if (!Uri.TryCreate(normalizedIncludeSource, UriKind.RelativeOrAbsolute, out var sourceUri))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0401",
                    $"Include source '{include.Source}' is not a valid URI.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            if (include.MergeTarget == "Unknown" &&
                !include.Kind.Equals("StyleInclude", StringComparison.Ordinal))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0402",
                    $"Include '{include.Kind}' is outside known merge groups (MergedDictionaries/Styles).",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    false));
            }

            var resolvedIncludeUri = ResolveIncludedBuildUri(
                normalizedIncludeSource,
                document.TargetPath,
                currentDocumentUri,
                out var isProjectLocalInclude);

            includes.Add(new ResolvedIncludeDefinition(
                Kind: include.Kind,
                Source: include.Source,
                MergeTarget: include.MergeTarget,
                IsAbsoluteUri: sourceUri.IsAbsoluteUri,
                ResolvedSourceUri: resolvedIncludeUri,
                IsProjectLocal: isProjectLocalInclude,
                RawXaml: include.RawXaml,
                Line: include.Line,
                Column: include.Column,
                Condition: include.Condition));
        }

        return includes.ToImmutable();
    }

    private static string? ResolveIncludedBuildUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        out bool isProjectLocal)
    {
        isProjectLocal = false;
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return null;
        }

        var trimmedSource = includeSource.Trim();
        if (!Uri.TryCreate(currentDocumentUri, UriKind.Absolute, out var currentUri) ||
            !currentUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentAssembly = currentUri.Host;
        if (string.IsNullOrWhiteSpace(currentAssembly))
        {
            return null;
        }

        if (trimmedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var normalizedRootedPath = NormalizeIncludePath(trimmedSource.TrimStart('/'));
            if (normalizedRootedPath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedRootedPath;
        }

        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var absoluteSource))
        {
            if (!absoluteSource.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            if (!string.Equals(absoluteSource.Host, currentAssembly, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            var normalizedAbsolutePath = NormalizeIncludePath(absoluteSource.AbsolutePath.TrimStart('/'));
            if (normalizedAbsolutePath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedAbsolutePath;
        }

        var normalizedCurrentPath = NormalizeIncludePath(currentTargetPath);
        var currentDirectory = GetIncludeDirectory(normalizedCurrentPath);
        var normalizedIncludePath = trimmedSource.StartsWith("/", StringComparison.Ordinal)
            ? NormalizeIncludePath(trimmedSource.TrimStart('/'))
            : NormalizeIncludePath(CombineIncludePath(currentDirectory, trimmedSource));

        if (normalizedIncludePath.Length == 0)
        {
            return null;
        }

        isProjectLocal = true;
        return "avares://" + currentAssembly + "/" + normalizedIncludePath;
    }

    private static string NormalizeIncludeSourceForResolution(string includeSource)
    {
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return includeSource;
        }

        var trimmedSource = includeSource.Trim();
        if (!TryParseMarkupExtension(trimmedSource, out var markup))
        {
            return trimmedSource;
        }

        var markupName = markup.Name.ToLowerInvariant();
        if (markupName is not ("x:uri" or "uri"))
        {
            return trimmedSource;
        }

        var uriToken = markup.NamedArguments.TryGetValue("Uri", out var explicitUri)
            ? explicitUri
            : markup.NamedArguments.TryGetValue("Value", out var explicitValue)
                ? explicitValue
                : markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : null;

        return string.IsNullOrWhiteSpace(uriToken)
            ? trimmedSource
            : Unquote(uriToken!).Trim();
    }

    private static string GetIncludeDirectory(string targetPath)
    {
        var lastSeparator = targetPath.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return string.Empty;
        }

        return targetPath.Substring(0, lastSeparator);
    }

    private static string CombineIncludePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        return baseDirectory + "/" + relativePath;
    }

    private static string NormalizeIncludePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedSeparators = path.Replace('\\', '/');
        var parts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join("/", stack);
    }
}
