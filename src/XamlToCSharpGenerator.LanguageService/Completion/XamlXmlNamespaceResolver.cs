using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlXmlNamespaceResolver
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";

    // Matches xmlns:prefix="uri" or xmlns="uri" in raw text.
    // Works even when the document is not well-formed XML.
    private static readonly Regex XmlnsPattern = new(
        @"xmlns(?::(?<prefix>[A-Za-z_][\w.-]*))?=""(?<uri>[^""]+)""",
        RegexOptions.Compiled);

    public static ImmutableDictionary<string, string> BuildPrefixMap(XamlDocumentModel? document)
    {
        if (document is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var pair in document.XmlNamespaces)
        {
            builder[pair.Key] = pair.Value;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Best-effort prefix map extraction from raw XAML text.
    /// Used as a fallback when the full XAML parser and XDocument.Parse both fail
    /// (e.g. the user is mid-edit and the document is temporarily invalid XML).
    /// </summary>
    public static ImmutableDictionary<string, string> BuildPrefixMapFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (Match match in XmlnsPattern.Matches(text))
        {
            var prefix = match.Groups["prefix"].Success ? match.Groups["prefix"].Value : string.Empty;
            var uri = match.Groups["uri"].Value;
            if (!string.IsNullOrWhiteSpace(uri) && !builder.ContainsKey(prefix))
            {
                builder[prefix] = uri;
            }
        }

        return builder.ToImmutable();
    }

    public static bool TryResolveXmlNamespace(
        ImmutableDictionary<string, string> prefixMap,
        string qualifiedName,
        out string xmlNamespace,
        out string xmlTypeName)
    {
        xmlNamespace = string.Empty;
        xmlTypeName = qualifiedName;

        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return false;
        }

        var separator = qualifiedName.IndexOf(':');
        if (separator < 0)
        {
            if (!prefixMap.TryGetValue(string.Empty, out var defaultNamespace) ||
                string.IsNullOrWhiteSpace(defaultNamespace))
            {
                xmlNamespace = AvaloniaDefaultXmlNamespace;
                return true;
            }

            xmlNamespace = defaultNamespace;
            return true;
        }

        var prefix = qualifiedName.Substring(0, separator);
        xmlTypeName = qualifiedName.Substring(separator + 1);

        if (!prefixMap.TryGetValue(prefix, out var prefixedNamespace) ||
            string.IsNullOrWhiteSpace(prefixedNamespace))
        {
            xmlNamespace = string.Empty;
            return false;
        }

        xmlNamespace = prefixedNamespace;
        return true;
    }

    public static (string Prefix, string TypeName) SplitQualifiedName(string qualifiedName)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
        {
            return (string.Empty, string.Empty);
        }

        var separator = qualifiedName.IndexOf(':');
        if (separator < 0)
        {
            return (string.Empty, qualifiedName);
        }

        return (
            qualifiedName.Substring(0, separator),
            qualifiedName.Substring(separator + 1));
    }
}
