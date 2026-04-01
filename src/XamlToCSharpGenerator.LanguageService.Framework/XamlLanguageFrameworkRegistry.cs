using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed class XamlLanguageFrameworkRegistry
{
    private readonly ImmutableArray<IXamlLanguageFrameworkProvider> _providers;
    private readonly ImmutableDictionary<string, IXamlLanguageFrameworkProvider> _providersById;
    private readonly ImmutableHashSet<string> _knownProjectXamlItemNames;
    private readonly ImmutableHashSet<string> _knownXmlnsPrefixAttributeMetadataNames;

    public XamlLanguageFrameworkRegistry(
        IEnumerable<IXamlLanguageFrameworkProvider> providers,
        string defaultFrameworkId)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var orderedProviders = providers
            .OrderByDescending(static provider => provider.DetectionPriority)
            .ThenBy(static provider => provider.Framework.Id, StringComparer.Ordinal)
            .ToImmutableArray();
        if (orderedProviders.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one framework provider must be registered.", nameof(providers));
        }

        _providers = orderedProviders;
        var providersById = ImmutableDictionary.CreateBuilder<string, IXamlLanguageFrameworkProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in orderedProviders)
        {
            if (!providersById.TryAdd(provider.Framework.Id, provider))
            {
                throw new ArgumentException(
                    $"Duplicate language-service framework id '{provider.Framework.Id}'.",
                    nameof(providers));
            }
        }

        _providersById = providersById.ToImmutable();
        if (!_providersById.TryGetValue(defaultFrameworkId, out var defaultProvider))
        {
            throw new ArgumentException(
                $"Default framework id '{defaultFrameworkId}' is not registered.",
                nameof(defaultFrameworkId));
        }

        DefaultFramework = defaultProvider.Framework;
        CacheKey = string.Join("|", _providers.Select(static provider => provider.Framework.Id));
        _knownProjectXamlItemNames = _providers
            .SelectMany(static provider => provider.Framework.ProjectXamlItemNames)
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        _knownXmlnsPrefixAttributeMetadataNames = _providers
            .SelectMany(static provider => provider.Framework.XmlnsPrefixAttributeMetadataNames)
            .ToImmutableHashSet(StringComparer.Ordinal);
    }

    public string CacheKey { get; }

    public ImmutableArray<IXamlLanguageFrameworkProvider> Providers => _providers;

    public XamlLanguageFrameworkInfo DefaultFramework { get; }

    public bool TryGetById(string? frameworkId, out XamlLanguageFrameworkInfo framework)
    {
        framework = DefaultFramework;
        if (string.IsNullOrWhiteSpace(frameworkId) ||
            !_providersById.TryGetValue(frameworkId.Trim(), out var provider))
        {
            return false;
        }

        framework = provider.Framework;
        return true;
    }

    public bool TryGetProviderById(string? frameworkId, out IXamlLanguageFrameworkProvider provider)
    {
        provider = default!;
        return !string.IsNullOrWhiteSpace(frameworkId) &&
               _providersById.TryGetValue(frameworkId.Trim(), out provider!);
    }

    public bool IsKnownProjectXamlItemName(string? localName)
    {
        return !string.IsNullOrWhiteSpace(localName) &&
               _knownProjectXamlItemNames.Contains(localName.Trim());
    }

    public bool IsKnownXmlnsPrefixAttribute(string? metadataName)
    {
        return !string.IsNullOrWhiteSpace(metadataName) &&
               _knownXmlnsPrefixAttributeMetadataNames.Contains(metadataName.Trim());
    }
}
