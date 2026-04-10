using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TypeResolutionPolicyService
{
    public delegate INamedTypeSymbol? ResolveFromNamespacePrefixesDelegate(
        Compilation compilation,
        ImmutableArray<string> namespacePrefixes,
        string typeName,
        int? genericArity,
        bool extensionSuffix,
        string strategy,
        bool reportFallbackUsage);

    public delegate bool TryGetImplicitProjectNamespaceRootDelegate(
        Compilation compilation,
        out string rootNamespace);

    public delegate ImmutableArray<string> GetProjectNamespaceCandidatesDelegate(
        Compilation compilation,
        string rootNamespace);

    public delegate ImmutableArray<string> GetFrameworkDefaultNamespaceCandidatesDelegate(
        Compilation compilation);

    public delegate INamedTypeSymbol? ResolveXmlNamespaceTypeDelegate(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity);

    public delegate bool IsCompatibilityFallbackEnabledDelegate();

    public delegate bool IsStrictTypeResolutionModeDelegate();

    public delegate bool IsFrameworkDefaultXmlNamespaceDelegate(string xmlNamespace);

    private readonly ResolveFromNamespacePrefixesDelegate _resolveFromNamespacePrefixes;
    private readonly TryGetImplicitProjectNamespaceRootDelegate _tryGetImplicitProjectNamespaceRoot;
    private readonly GetProjectNamespaceCandidatesDelegate _getProjectNamespaceCandidates;
    private readonly GetFrameworkDefaultNamespaceCandidatesDelegate _getFrameworkDefaultNamespaceCandidates;
    private readonly ResolveXmlNamespaceTypeDelegate _resolveXmlNamespaceType;
    private readonly IsCompatibilityFallbackEnabledDelegate _isCompatibilityFallbackEnabled;
    private readonly IsStrictTypeResolutionModeDelegate _isStrictTypeResolutionMode;
    private readonly IsFrameworkDefaultXmlNamespaceDelegate _isFrameworkDefaultXmlNamespace;

    public TypeResolutionPolicyService(
        ResolveFromNamespacePrefixesDelegate resolveFromNamespacePrefixes,
        TryGetImplicitProjectNamespaceRootDelegate tryGetImplicitProjectNamespaceRoot,
        GetProjectNamespaceCandidatesDelegate getProjectNamespaceCandidates,
        GetFrameworkDefaultNamespaceCandidatesDelegate getFrameworkDefaultNamespaceCandidates,
        ResolveXmlNamespaceTypeDelegate resolveXmlNamespaceType,
        IsCompatibilityFallbackEnabledDelegate isCompatibilityFallbackEnabled,
        IsStrictTypeResolutionModeDelegate isStrictTypeResolutionMode,
        IsFrameworkDefaultXmlNamespaceDelegate isFrameworkDefaultXmlNamespace)
    {
        _resolveFromNamespacePrefixes = resolveFromNamespacePrefixes ?? throw new ArgumentNullException(nameof(resolveFromNamespacePrefixes));
        _tryGetImplicitProjectNamespaceRoot = tryGetImplicitProjectNamespaceRoot ?? throw new ArgumentNullException(nameof(tryGetImplicitProjectNamespaceRoot));
        _getProjectNamespaceCandidates = getProjectNamespaceCandidates ?? throw new ArgumentNullException(nameof(getProjectNamespaceCandidates));
        _getFrameworkDefaultNamespaceCandidates = getFrameworkDefaultNamespaceCandidates ?? throw new ArgumentNullException(nameof(getFrameworkDefaultNamespaceCandidates));
        _resolveXmlNamespaceType = resolveXmlNamespaceType ?? throw new ArgumentNullException(nameof(resolveXmlNamespaceType));
        _isCompatibilityFallbackEnabled = isCompatibilityFallbackEnabled ?? throw new ArgumentNullException(nameof(isCompatibilityFallbackEnabled));
        _isStrictTypeResolutionMode = isStrictTypeResolutionMode ?? throw new ArgumentNullException(nameof(isStrictTypeResolutionMode));
        _isFrameworkDefaultXmlNamespace = isFrameworkDefaultXmlNamespace ?? throw new ArgumentNullException(nameof(isFrameworkDefaultXmlNamespace));
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
            var inDefaultXmlNamespace = _resolveXmlNamespaceType(compilation, defaultXmlNamespace, normalizedTypeToken, genericArity: null);
            if (inDefaultXmlNamespace is not null)
            {
                return inDefaultXmlNamespace;
            }
        }

        if (_isCompatibilityFallbackEnabled())
        {
            var defaultNamespaceCandidates = _getFrameworkDefaultNamespaceCandidates(compilation);

            var compatibilityMatch = _resolveFromNamespacePrefixes(
                compilation,
                defaultNamespaceCandidates,
                normalizedTypeToken,
                genericArity: null,
                extensionSuffix: false,
                strategy: "framework default namespace compatibility fallback",
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
                    strategy: "framework default namespace extension compatibility fallback",
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

            if (_isCompatibilityFallbackEnabled() && !_isStrictTypeResolutionMode())
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
        if (!_isFrameworkDefaultXmlNamespace(xmlNamespace) || !_isCompatibilityFallbackEnabled())
        {
            return null;
        }

        var defaultNamespaceCandidates = _getFrameworkDefaultNamespaceCandidates(compilation);
        var compatibilityMatch = _resolveFromNamespacePrefixes(
            compilation,
            defaultNamespaceCandidates,
            xmlTypeName,
            genericArity,
            extensionSuffix: false,
            strategy: "framework default xml namespace compatibility fallback",
            reportFallbackUsage: true);
        if (compatibilityMatch is not null)
        {
            return compatibilityMatch;
        }

        if ((!genericArity.HasValue || genericArity.Value <= 0) && !_isStrictTypeResolutionMode())
        {
            var compatibilityExtensionMatch = _resolveFromNamespacePrefixes(
                compilation,
                defaultNamespaceCandidates,
                xmlTypeName,
                genericArity: null,
                extensionSuffix: true,
                strategy: "framework default xml namespace extension compatibility fallback",
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

        if ((!genericArity.HasValue || genericArity.Value <= 0) && !_isStrictTypeResolutionMode())
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
