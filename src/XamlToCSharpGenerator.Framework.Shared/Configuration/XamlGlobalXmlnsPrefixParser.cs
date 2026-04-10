using System;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Framework.Shared.Configuration;

public static class XamlGlobalXmlnsPrefixParser
{
    public static ImmutableDictionary<string, string> Parse(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);
        }

        var mapBuilder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var entries = rawValue.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            var prefix = entry.Substring(0, separatorIndex).Trim();
            var xmlNamespace = entry.Substring(separatorIndex + 1).Trim();
            if (prefix.Length == 0 || xmlNamespace.Length == 0)
            {
                continue;
            }

            mapBuilder[prefix] = xmlNamespace;
        }

        return mapBuilder.ToImmutable();
    }
}
