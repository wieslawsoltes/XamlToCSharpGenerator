using System;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Runtime;

public sealed class SchemeBasedDocumentUriResolver : IXamlFrameworkDocumentUriResolver
{
    private readonly string _scheme;

    public SchemeBasedDocumentUriResolver(string scheme)
    {
        _scheme = scheme.Trim();
    }

    public string BuildDocumentUri(string assemblyName, string normalizedTargetPath)
    {
        var normalizedPath = XamlIncludePathSemantics.NormalizePath(normalizedTargetPath);
        return _scheme + "://" + assemblyName + "/" + normalizedPath;
    }

    public bool TryResolveIncludeUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        out string resolvedUri,
        out bool isProjectLocal)
    {
        resolvedUri = string.Empty;
        isProjectLocal = false;

        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return false;
        }

        if (includeSource.StartsWith("/", StringComparison.Ordinal))
        {
            if (!TryGetCurrentAssemblyName(currentDocumentUri, out var rootedAssemblyName))
            {
                return false;
            }

            var rootedPath = XamlIncludePathSemantics.NormalizePath(includeSource.TrimStart('/'));
            if (rootedPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildDocumentUri(rootedAssemblyName, rootedPath);
            isProjectLocal = true;
            return true;
        }

        if (Uri.TryCreate(includeSource, UriKind.Absolute, out var absoluteSource))
        {
            if (!string.Equals(absoluteSource.Scheme, _scheme, StringComparison.OrdinalIgnoreCase))
            {
                resolvedUri = absoluteSource.ToString();
                return true;
            }

            if (!TryGetCurrentAssemblyName(currentDocumentUri, out var currentAssemblyName))
            {
                resolvedUri = includeSource;
                return true;
            }

            if (!string.Equals(absoluteSource.Host, currentAssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedUri = includeSource;
                return true;
            }

            var absoluteTargetPath = XamlIncludePathSemantics.NormalizePath(absoluteSource.AbsolutePath.TrimStart('/'));
            if (absoluteTargetPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildDocumentUri(currentAssemblyName, absoluteTargetPath);
            isProjectLocal = true;
            return true;
        }

        if (!TryGetCurrentAssemblyName(currentDocumentUri, out var assemblyName))
        {
            return false;
        }

        var currentDirectory = XamlIncludePathSemantics.GetDirectory(currentTargetPath);
        var combinedPath = XamlIncludePathSemantics.CombinePath(currentDirectory, includeSource);
        var normalizedCombinedPath = XamlIncludePathSemantics.NormalizePath(combinedPath);
        if (normalizedCombinedPath.Length == 0)
        {
            return false;
        }

        resolvedUri = BuildDocumentUri(assemblyName, normalizedCombinedPath);
        isProjectLocal = true;
        return true;
    }

    private static bool TryGetCurrentAssemblyName(string currentDocumentUri, out string assemblyName)
    {
        assemblyName = string.Empty;
        if (string.IsNullOrWhiteSpace(currentDocumentUri))
        {
            return false;
        }

        var schemeSeparatorIndex = currentDocumentUri.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparatorIndex < 0)
        {
            return false;
        }

        var authorityStartIndex = schemeSeparatorIndex + 3;
        if (authorityStartIndex >= currentDocumentUri.Length)
        {
            return false;
        }

        var authorityEndIndex = currentDocumentUri.IndexOf('/', authorityStartIndex);
        if (authorityEndIndex < 0)
        {
            authorityEndIndex = currentDocumentUri.Length;
        }

        assemblyName = currentDocumentUri.Substring(authorityStartIndex, authorityEndIndex - authorityStartIndex);
        return assemblyName.Length > 0;
    }
}
