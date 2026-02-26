using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class TypeResolutionPolicyService
{
    internal delegate INamedTypeSymbol? ResolveFromNamespacePrefixesDelegate(
        Compilation compilation,
        ImmutableArray<string> namespacePrefixes,
        string typeName,
        int? genericArity,
        bool extensionSuffix,
        string strategy,
        bool reportFallbackUsage);

    internal delegate bool TryGetImplicitProjectNamespaceRootDelegate(
        Compilation compilation,
        out string rootNamespace);

    internal delegate ImmutableArray<string> GetProjectNamespaceCandidatesDelegate(
        Compilation compilation,
        string rootNamespace);

    internal delegate ImmutableArray<string> GetAvaloniaDefaultNamespaceCandidatesDelegate(
        Compilation compilation);

    internal delegate INamedTypeSymbol? ResolveXmlNamespaceTypeDelegate(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity);

    internal delegate bool IsCompatibilityFallbackEnabledDelegate();

    internal delegate bool IsStrictTypeResolutionModeDelegate();

    internal delegate bool IsAvaloniaDefaultXmlNamespaceDelegate(string xmlNamespace);

    private readonly ResolveFromNamespacePrefixesDelegate _resolveFromNamespacePrefixes;
    private readonly TryGetImplicitProjectNamespaceRootDelegate _tryGetImplicitProjectNamespaceRoot;
    private readonly GetProjectNamespaceCandidatesDelegate _getProjectNamespaceCandidates;
    private readonly GetAvaloniaDefaultNamespaceCandidatesDelegate _getAvaloniaDefaultNamespaceCandidates;
    private readonly ResolveXmlNamespaceTypeDelegate _resolveXmlNamespaceType;
    private readonly IsCompatibilityFallbackEnabledDelegate _isCompatibilityFallbackEnabled;
    private readonly IsStrictTypeResolutionModeDelegate _isStrictTypeResolutionMode;
    private readonly IsAvaloniaDefaultXmlNamespaceDelegate _isAvaloniaDefaultXmlNamespace;

    public TypeResolutionPolicyService(
        ResolveFromNamespacePrefixesDelegate resolveFromNamespacePrefixes,
        TryGetImplicitProjectNamespaceRootDelegate tryGetImplicitProjectNamespaceRoot,
        GetProjectNamespaceCandidatesDelegate getProjectNamespaceCandidates,
        GetAvaloniaDefaultNamespaceCandidatesDelegate getAvaloniaDefaultNamespaceCandidates,
        ResolveXmlNamespaceTypeDelegate resolveXmlNamespaceType,
        IsCompatibilityFallbackEnabledDelegate isCompatibilityFallbackEnabled,
        IsStrictTypeResolutionModeDelegate isStrictTypeResolutionMode,
        IsAvaloniaDefaultXmlNamespaceDelegate isAvaloniaDefaultXmlNamespace)
    {
        _resolveFromNamespacePrefixes = resolveFromNamespacePrefixes ??
                                        throw new ArgumentNullException(nameof(resolveFromNamespacePrefixes));
        _tryGetImplicitProjectNamespaceRoot = tryGetImplicitProjectNamespaceRoot ??
                                              throw new ArgumentNullException(nameof(tryGetImplicitProjectNamespaceRoot));
        _getProjectNamespaceCandidates = getProjectNamespaceCandidates ??
                                         throw new ArgumentNullException(nameof(getProjectNamespaceCandidates));
        _getAvaloniaDefaultNamespaceCandidates = getAvaloniaDefaultNamespaceCandidates ??
                                                 throw new ArgumentNullException(nameof(getAvaloniaDefaultNamespaceCandidates));
        _resolveXmlNamespaceType = resolveXmlNamespaceType ?? throw new ArgumentNullException(nameof(resolveXmlNamespaceType));
        _isCompatibilityFallbackEnabled = isCompatibilityFallbackEnabled ??
                                          throw new ArgumentNullException(nameof(isCompatibilityFallbackEnabled));
        _isStrictTypeResolutionMode = isStrictTypeResolutionMode ??
                                      throw new ArgumentNullException(nameof(isStrictTypeResolutionMode));
        _isAvaloniaDefaultXmlNamespace = isAvaloniaDefaultXmlNamespace ??
                                         throw new ArgumentNullException(nameof(isAvaloniaDefaultXmlNamespace));
    }

    public INamedTypeSymbol? TryResolveTokenFallback(
        Compilation compilation,
        XamlDocumentModel document,
        string normalizedTypeToken,
        string? fallbackClrNamespace)
    {
        if (!string.IsNullOrWhiteSpace(fallbackClrNamespace))
        {
            var inFallbackNamespace = compilation.GetTypeByMetadataName(fallbackClrNamespace + "." + normalizedTypeToken);
            if (inFallbackNamespace is not null)
            {
                return inFallbackNamespace;
            }
        }

        if (document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespace))
        {
            var inDefaultXmlNamespace = _resolveXmlNamespaceType(
                compilation,
                defaultXmlNamespace,
                normalizedTypeToken,
                genericArity: null);
            if (inDefaultXmlNamespace is not null)
            {
                return inDefaultXmlNamespace;
            }
        }

        if (_isCompatibilityFallbackEnabled())
        {
            var defaultNamespaceCandidates = _getAvaloniaDefaultNamespaceCandidates(compilation);

            var compatibilityMatch = _resolveFromNamespacePrefixes(
                compilation,
                defaultNamespaceCandidates,
                normalizedTypeToken,
                genericArity: null,
                extensionSuffix: false,
                strategy: "Avalonia default namespace compatibility fallback",
                reportFallbackUsage: true);
            if (compatibilityMatch is not null)
            {
                return compatibilityMatch;
            }

            if (!_isStrictTypeResolutionMode())
            {
                var compatibilityExtensionMatch = _resolveFromNamespacePrefixes(
                    compilation,
                    defaultNamespaceCandidates,
                    normalizedTypeToken,
                    genericArity: null,
                    extensionSuffix: true,
                    strategy: "Avalonia default namespace extension compatibility fallback",
                    reportFallbackUsage: true);
                if (compatibilityExtensionMatch is not null)
                {
                    return compatibilityExtensionMatch;
                }
            }
        }

        if (_tryGetImplicitProjectNamespaceRoot(compilation, out var rootNamespace))
        {
            var projectNamespaceCandidates = _getProjectNamespaceCandidates(compilation, rootNamespace);
            var projectMatch = _resolveFromNamespacePrefixes(
                compilation,
                projectNamespaceCandidates,
                normalizedTypeToken,
                genericArity: null,
                extensionSuffix: false,
                strategy: "implicit project namespace fallback",
                reportFallbackUsage: true);
            if (projectMatch is not null)
            {
                return projectMatch;
            }

            if (_isCompatibilityFallbackEnabled() &&
                !_isStrictTypeResolutionMode())
            {
                var projectExtensionMatch = _resolveFromNamespacePrefixes(
                    compilation,
                    projectNamespaceCandidates,
                    normalizedTypeToken,
                    genericArity: null,
                    extensionSuffix: true,
                    strategy: "implicit project namespace extension compatibility fallback",
                    reportFallbackUsage: true);
                if (projectExtensionMatch is not null)
                {
                    return projectExtensionMatch;
                }
            }
        }

        return null;
    }

    public INamedTypeSymbol? TryResolveXmlNamespaceFallback(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        if (!_isAvaloniaDefaultXmlNamespace(xmlNamespace) ||
            !_isCompatibilityFallbackEnabled())
        {
            return null;
        }

        var defaultNamespaceCandidates = _getAvaloniaDefaultNamespaceCandidates(compilation);
        var compatibilityMatch = _resolveFromNamespacePrefixes(
            compilation,
            defaultNamespaceCandidates,
            xmlTypeName,
            genericArity,
            extensionSuffix: false,
            strategy: "Avalonia default xml namespace compatibility fallback",
            reportFallbackUsage: true);
        if (compatibilityMatch is not null)
        {
            return compatibilityMatch;
        }

        if ((!genericArity.HasValue || genericArity.Value <= 0) &&
            !_isStrictTypeResolutionMode())
        {
            var compatibilityExtensionMatch = _resolveFromNamespacePrefixes(
                compilation,
                defaultNamespaceCandidates,
                xmlTypeName,
                genericArity: null,
                extensionSuffix: true,
                strategy: "Avalonia default xml namespace extension compatibility fallback",
                reportFallbackUsage: true);
            if (compatibilityExtensionMatch is not null)
            {
                return compatibilityExtensionMatch;
            }
        }

        if (!_tryGetImplicitProjectNamespaceRoot(compilation, out var projectRootNamespace))
        {
            return null;
        }

        var projectNamespaceCandidates = _getProjectNamespaceCandidates(compilation, projectRootNamespace);
        var projectMatch = _resolveFromNamespacePrefixes(
            compilation,
            projectNamespaceCandidates,
            xmlTypeName,
            genericArity,
            extensionSuffix: false,
            strategy: "implicit project namespace fallback",
            reportFallbackUsage: true);
        if (projectMatch is not null)
        {
            return projectMatch;
        }

        if ((!genericArity.HasValue || genericArity.Value <= 0) &&
            !_isStrictTypeResolutionMode())
        {
            var projectExtensionMatch = _resolveFromNamespacePrefixes(
                compilation,
                projectNamespaceCandidates,
                xmlTypeName,
                genericArity: null,
                extensionSuffix: true,
                strategy: "implicit project namespace extension compatibility fallback",
                reportFallbackUsage: true);
            if (projectExtensionMatch is not null)
            {
                return projectExtensionMatch;
            }
        }

        return null;
    }
}
