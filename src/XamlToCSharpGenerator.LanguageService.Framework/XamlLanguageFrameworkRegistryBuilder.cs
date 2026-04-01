using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed class XamlLanguageFrameworkRegistryBuilder
{
    private readonly List<IXamlLanguageFrameworkProvider> _providers = [];

    public XamlLanguageFrameworkRegistryBuilder Add(IXamlLanguageFrameworkProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers.Add(provider);
        return this;
    }

    public XamlLanguageFrameworkRegistryBuilder AddRange(IEnumerable<IXamlLanguageFrameworkProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers)
        {
            Add(provider);
        }

        return this;
    }

    public XamlLanguageFrameworkRegistry Build(string defaultFrameworkId)
    {
        return new XamlLanguageFrameworkRegistry(_providers, defaultFrameworkId);
    }
}
