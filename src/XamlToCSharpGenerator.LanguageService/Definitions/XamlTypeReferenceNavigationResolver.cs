using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal readonly record struct XamlResolvedTypeReference(
    string FullTypeName,
    string? AssemblyName,
    AvaloniaSymbolSourceLocation? SourceLocation);

internal static class XamlTypeReferenceNavigationResolver
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static bool IsTypeReferenceAttributeName(string? attributeName)
    {
        var localName = GetLocalName(attributeName);
        return string.Equals(localName, "DataType", StringComparison.Ordinal) ||
               string.Equals(localName, "Class", StringComparison.Ordinal);
    }

    public static bool IsTypeReferenceAttribute(XAttribute attribute)
    {
        if (attribute is null)
        {
            return false;
        }

        if (string.Equals(attribute.Name.LocalName, "DataType", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(attribute.Name.NamespaceName) ||
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    public static ImmutableDictionary<string, string> BuildPrefixMapForElement(XElement element)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var scopeElement in element.AncestorsAndSelf().Reverse())
        {
            foreach (var attribute in scopeElement.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                var prefix = attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName;
                builder[prefix] = attribute.Value;
            }
        }

        return builder.ToImmutable();
    }

    public static bool TryResolve(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string? attributeName,
        string? rawTypeValue,
        out XamlResolvedTypeReference resolvedTypeReference)
    {
        resolvedTypeReference = default;
        if (!IsTypeReferenceAttributeName(attributeName) ||
            string.IsNullOrWhiteSpace(rawTypeValue))
        {
            return false;
        }

        var localAttributeName = GetLocalName(attributeName);
        var normalizedTypeToken = NormalizeTypeReferenceToken(rawTypeValue);
        if (string.IsNullOrWhiteSpace(normalizedTypeToken))
        {
            return false;
        }

        if (string.Equals(localAttributeName, "Class", StringComparison.Ordinal))
        {
            return TryResolveByFullTypeName(analysis, normalizedTypeToken, out resolvedTypeReference);
        }

        return TryResolveQualifiedTypeToken(analysis, prefixMap, normalizedTypeToken, out resolvedTypeReference);
    }

    public static bool TryResolveQualifiedTypeToken(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string? rawTypeValue,
        out XamlResolvedTypeReference resolvedTypeReference)
    {
        resolvedTypeReference = default;
        if (string.IsNullOrWhiteSpace(rawTypeValue))
        {
            return false;
        }

        var normalizedTypeToken = NormalizeTypeReferenceToken(rawTypeValue);
        if (string.IsNullOrWhiteSpace(normalizedTypeToken))
        {
            return false;
        }

        if (analysis.TypeIndex is not null &&
            XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex,
                prefixMap,
                normalizedTypeToken,
                out var typeInfo) &&
            typeInfo is not null)
        {
            resolvedTypeReference = new XamlResolvedTypeReference(
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                typeInfo.SourceLocation);
            return true;
        }

        if (TryResolveByQualifiedTypeToken(analysis, prefixMap, normalizedTypeToken, out resolvedTypeReference))
        {
            return true;
        }

        return TryResolveByFullTypeName(analysis, normalizedTypeToken, out resolvedTypeReference);
    }

    private static bool TryResolveByQualifiedTypeToken(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string qualifiedTypeToken,
        out XamlResolvedTypeReference resolvedTypeReference)
    {
        resolvedTypeReference = default;
        if (analysis.Compilation is null ||
            !XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                prefixMap,
                qualifiedTypeToken,
                out var xmlNamespace,
                out var xmlTypeName) ||
            !XamlClrSymbolResolver.TryResolveClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return false;
        }

        foreach (var metadataName in EnumerateMetadataNames(clrNamespace, xmlTypeName))
        {
            var symbol = analysis.Compilation.GetTypeByMetadataName(metadataName);
            if (symbol is null)
            {
                continue;
            }

            resolvedTypeReference = new XamlResolvedTypeReference(
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                symbol.ContainingAssembly.Identity.Name,
                TryCreateSourceLocation(symbol));
            return true;
        }

        return false;
    }

    private static ImmutableArray<string> EnumerateMetadataNames(string clrNamespace, string xmlTypeName)
    {
        var trimmedTypeName = xmlTypeName.Trim();
        if (trimmedTypeName.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var typeArgumentsIndex = trimmedTypeName.IndexOf('(');
        if (typeArgumentsIndex > 0)
        {
            trimmedTypeName = trimmedTypeName.Substring(0, typeArgumentsIndex).Trim();
        }

        var fullName = clrNamespace + "." + trimmedTypeName;
        if (!trimmedTypeName.Contains('.', StringComparison.Ordinal))
        {
            return [fullName];
        }

        var nestedCandidate = clrNamespace + "." + trimmedTypeName.Replace(".", "+", StringComparison.Ordinal);
        if (string.Equals(fullName, nestedCandidate, StringComparison.Ordinal))
        {
            return [fullName];
        }

        return [fullName, nestedCandidate];
    }

    private static bool TryResolveByFullTypeName(
        XamlAnalysisResult analysis,
        string candidateTypeName,
        out XamlResolvedTypeReference resolvedTypeReference)
    {
        resolvedTypeReference = default;
        var typeIndex = analysis.TypeIndex!;
        if (typeIndex.TryGetTypeByFullTypeName(candidateTypeName, out var typeInfo) && typeInfo is not null)
        {
            resolvedTypeReference = new XamlResolvedTypeReference(
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                typeInfo.SourceLocation);
            return true;
        }

        if (analysis.Compilation is not null)
        {
            var symbol = analysis.Compilation.GetTypeByMetadataName(candidateTypeName);
            if (symbol is not null)
            {
                resolvedTypeReference = new XamlResolvedTypeReference(
                    symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    symbol.ContainingAssembly.Identity.Name,
                    TryCreateSourceLocation(symbol));
                return true;
            }
        }

        if (TrySplitFullTypeName(candidateTypeName, out var clrNamespace, out var xmlTypeName) &&
            typeIndex.TryGetTypeByClrNamespace(clrNamespace, xmlTypeName, out typeInfo) &&
            typeInfo is not null)
        {
            resolvedTypeReference = new XamlResolvedTypeReference(
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                typeInfo.SourceLocation);
            return true;
        }

        return false;
    }

    private static string NormalizeTypeReferenceToken(string rawTypeValue)
    {
        var candidate = rawTypeValue.Trim();
        if (candidate.Length == 0)
        {
            return string.Empty;
        }

        if (!candidate.StartsWith("{", StringComparison.Ordinal))
        {
            return candidate;
        }

        // Minimal markup-extension support: {x:Type vm:MyType}
        var closingBrace = candidate.LastIndexOf('}');
        if (closingBrace > 0)
        {
            candidate = candidate.Substring(1, closingBrace - 1).Trim();
        }
        else
        {
            candidate = candidate.TrimStart('{').Trim();
        }

        if (!candidate.StartsWith("x:Type", StringComparison.Ordinal))
        {
            return candidate;
        }

        var typePayload = candidate.Substring("x:Type".Length).Trim();
        if (typePayload.Length == 0)
        {
            return string.Empty;
        }

        var commaIndex = typePayload.IndexOf(',');
        if (commaIndex >= 0)
        {
            typePayload = typePayload.Substring(0, commaIndex).Trim();
        }

        return typePayload;
    }

    private static bool TrySplitFullTypeName(string fullTypeName, out string clrNamespace, out string typeName)
    {
        clrNamespace = string.Empty;
        typeName = string.Empty;
        var separator = fullTypeName.LastIndexOf('.');
        if (separator <= 0 || separator >= fullTypeName.Length - 1)
        {
            return false;
        }

        clrNamespace = fullTypeName.Substring(0, separator).Trim();
        typeName = fullTypeName.Substring(separator + 1).Trim();
        return clrNamespace.Length > 0 && typeName.Length > 0;
    }

    private static string GetLocalName(string? attributeName)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            return string.Empty;
        }

        var separator = attributeName.IndexOf(':');
        return separator >= 0 && separator + 1 < attributeName.Length
            ? attributeName.Substring(separator + 1)
            : attributeName;
    }

    private static AvaloniaSymbolSourceLocation? TryCreateSourceLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree?.FilePath is null)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();
            var start = new SourcePosition(
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character);
            var end = new SourcePosition(
                lineSpan.EndLinePosition.Line,
                lineSpan.EndLinePosition.Character);
            return new AvaloniaSymbolSourceLocation(
                UriPathHelper.ToDocumentUri(location.SourceTree.FilePath),
                new SourceRange(start, end));
        }

        return null;
    }
}
