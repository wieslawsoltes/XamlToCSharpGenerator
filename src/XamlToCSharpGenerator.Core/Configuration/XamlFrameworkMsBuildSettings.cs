using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace XamlToCSharpGenerator.Core.Configuration;

public sealed class XamlFrameworkMsBuildSettings
{
    private readonly ImmutableDictionary<XamlFrameworkMsBuildSettingKey, ImmutableArray<string>> _aliases;

    public XamlFrameworkMsBuildSettings(IEnumerable<KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>> aliases)
    {
        if (aliases is null)
        {
            throw new ArgumentNullException(nameof(aliases));
        }

        var builder = ImmutableDictionary.CreateBuilder<XamlFrameworkMsBuildSettingKey, ImmutableArray<string>>();
        foreach (var entry in aliases)
        {
            var values = entry.Value?
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray() ?? ImmutableArray<string>.Empty;

            builder[entry.Key] = values;
        }

        _aliases = builder.ToImmutable();
    }

    public IReadOnlyList<string> GetAliases(XamlFrameworkMsBuildSettingKey key)
    {
        return _aliases.TryGetValue(key, out var aliases)
            ? aliases
            : ImmutableArray<string>.Empty;
    }
}
