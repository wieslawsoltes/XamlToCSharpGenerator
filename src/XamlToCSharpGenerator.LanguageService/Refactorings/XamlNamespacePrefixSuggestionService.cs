using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlNamespacePrefixSuggestionService
{
    private const string AvaloniaXmlnsPrefixAttributeMetadataName = "Avalonia.Metadata.XmlnsPrefixAttribute";
    private const string SourceGenGlobalXmlnsPrefixAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute";

    public string SuggestPrefix(
        Compilation? compilation,
        ImmutableDictionary<string, string> inScopePrefixMap,
        string xmlNamespace,
        string clrNamespace)
    {
        var reservedPrefixes = new HashSet<string>(inScopePrefixMap.Keys, StringComparer.Ordinal);
        foreach (var candidate in EnumerateCandidatePrefixes(compilation, xmlNamespace, clrNamespace))
        {
            if (reservedPrefixes.Add(candidate))
            {
                return candidate;
            }
        }

        for (var index = 2; ; index++)
        {
            var fallback = "local" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (reservedPrefixes.Add(fallback))
            {
                return fallback;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidatePrefixes(
        Compilation? compilation,
        string xmlNamespace,
        string clrNamespace)
    {
        foreach (var declaredPrefix in GetDeclaredPrefixes(compilation, xmlNamespace))
        {
            yield return declaredPrefix;
        }

        var clrNamespaceCandidate = BuildClrNamespaceCandidate(clrNamespace);
        if (!string.IsNullOrWhiteSpace(clrNamespaceCandidate))
        {
            yield return clrNamespaceCandidate;
        }

        yield return "local";
    }

    private static IEnumerable<string> GetDeclaredPrefixes(Compilation? compilation, string xmlNamespace)
    {
        if (compilation is null)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsXmlnsPrefixAttribute(attribute) ||
                    attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not string attributeXmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string prefix ||
                    !string.Equals(attributeXmlNamespace.Trim(), xmlNamespace, StringComparison.Ordinal))
                {
                    continue;
                }

                var normalizedPrefix = NormalizePrefix(prefix);
                if (!string.IsNullOrWhiteSpace(normalizedPrefix) &&
                    seen.Add(normalizedPrefix))
                {
                    yield return normalizedPrefix;
                }
            }
        }
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var seen = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        if (seen.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly is not null && seen.Add(assembly))
            {
                yield return assembly;
            }
        }
    }

    private static bool IsXmlnsPrefixAttribute(AttributeData attribute)
    {
        var metadataName = attribute.AttributeClass?.ToDisplayString();
        return string.Equals(metadataName, AvaloniaXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal) ||
               string.Equals(metadataName, SourceGenGlobalXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal);
    }

    private static string BuildClrNamespaceCandidate(string clrNamespace)
    {
        if (string.IsNullOrWhiteSpace(clrNamespace))
        {
            return "local";
        }

        string[] segments = clrNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            var segment = segments[index];
            if (IsGenericNamespaceBucket(segment))
            {
                continue;
            }

            var normalized = NormalizePrefix(segment);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "local";
    }

    private static bool IsGenericNamespaceBucket(string segment)
    {
        return string.Equals(segment, "Controls", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "Control", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "Views", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "ViewModels", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "Pages", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "Models", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "Components", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(segment, "UI", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrefix(string rawPrefix)
    {
        if (string.IsNullOrWhiteSpace(rawPrefix))
        {
            return string.Empty;
        }

        var characters = rawPrefix.Trim()
            .Where(static character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.')
            .ToArray();
        if (characters.Length == 0)
        {
            return string.Empty;
        }

        var normalized = new string(characters);
        if (!(char.IsLetter(normalized[0]) || normalized[0] == '_'))
        {
            normalized = "_" + normalized;
        }

        if (normalized.Length > 0)
        {
            normalized = char.ToLowerInvariant(normalized[0]) + normalized.Substring(1);
        }

        return normalized;
    }
}
