using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal readonly record struct XamlMarkupExtensionClassToken(
    string Name,
    int Start,
    int Length);

internal static class XamlMarkupExtensionNavigationSemantics
{
    public static bool TryResolveClassTokenAtOffset(
        string text,
        int offset,
        out XamlMarkupExtensionClassToken classToken)
    {
        classToken = default;
        if (string.IsNullOrWhiteSpace(text) || offset < 0 || offset > text.Length)
        {
            return false;
        }

        var braceStart = text.LastIndexOf('{', Math.Max(0, offset - 1));
        if (braceStart < 0)
        {
            return false;
        }

        var closerBetween = text.LastIndexOf('}', Math.Max(0, offset - 1));
        if (closerBetween > braceStart)
        {
            return false;
        }

        var cursor = braceStart + 1;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        var classStart = cursor;
        while (cursor < text.Length && IsMarkupClassCharacter(text[cursor]))
        {
            cursor++;
        }

        var classLength = cursor - classStart;
        if (classLength <= 0)
        {
            return false;
        }

        if (offset < classStart || offset > classStart + classLength)
        {
            return false;
        }

        classToken = new XamlMarkupExtensionClassToken(
            text.Substring(classStart, classLength),
            classStart,
            classLength);
        return true;
    }

    public static ImmutableArray<XamlMarkupExtensionClassToken> EnumerateClassTokens(string attributeValue)
    {
        var builder = ImmutableArray.CreateBuilder<XamlMarkupExtensionClassToken>();
        if (string.IsNullOrWhiteSpace(attributeValue))
        {
            return builder.ToImmutable();
        }

        for (var index = 0; index < attributeValue.Length; index++)
        {
            if (attributeValue[index] != '{')
            {
                continue;
            }

            var cursor = index + 1;
            while (cursor < attributeValue.Length && char.IsWhiteSpace(attributeValue[cursor]))
            {
                cursor++;
            }

            var classStart = cursor;
            while (cursor < attributeValue.Length && IsMarkupClassCharacter(attributeValue[cursor]))
            {
                cursor++;
            }

            var classLength = cursor - classStart;
            if (classLength <= 0)
            {
                continue;
            }

            builder.Add(new XamlMarkupExtensionClassToken(
                attributeValue.Substring(classStart, classLength),
                classStart,
                classLength));
        }

        return builder.ToImmutable();
    }

    public static bool TryResolveExtensionTypeReference(
        XamlAnalysisResult analysis,
        ImmutableDictionary<string, string> prefixMap,
        string extensionToken,
        out XamlResolvedTypeReference resolvedTypeReference)
    {
        resolvedTypeReference = default;
        if (analysis.TypeIndex is null || string.IsNullOrWhiteSpace(extensionToken))
        {
            return false;
        }

        foreach (var candidate in EnumerateTypeTokenCandidates(extensionToken))
        {
            if (XamlClrSymbolResolver.TryResolveTypeInfo(
                    analysis.TypeIndex,
                    prefixMap,
                    candidate,
                    out var typeInfo) &&
                typeInfo is not null)
            {
                resolvedTypeReference = new XamlResolvedTypeReference(
                    typeInfo.FullTypeName,
                    typeInfo.AssemblyName,
                    typeInfo.SourceLocation);
                return true;
            }
        }

        if (analysis.Compilation is null)
        {
            return false;
        }

        foreach (var metadataName in EnumerateMetadataNameCandidates(
                     extensionToken,
                     analysis.Framework.MarkupExtensionNamespaces))
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

    private static IEnumerable<string> EnumerateTypeTokenCandidates(string extensionToken)
    {
        yield return extensionToken;

        var localToken = extensionToken;
        var prefixSeparator = extensionToken.IndexOf(':');
        if (prefixSeparator >= 0 && prefixSeparator + 1 < extensionToken.Length)
        {
            var prefix = extensionToken.Substring(0, prefixSeparator);
            var name = extensionToken.Substring(prefixSeparator + 1);
            if (name.Length > 0)
            {
                yield return prefix + ":" + XamlMarkupExtensionNameSemantics.ToClrExtensionTypeToken(name);
            }
        }
        else
        {
            yield return XamlMarkupExtensionNameSemantics.ToClrExtensionTypeToken(extensionToken);
        }
    }

    private static IEnumerable<string> EnumerateMetadataNameCandidates(
        string extensionToken,
        ImmutableArray<string> markupExtensionNamespaces)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var plainName = extensionToken;
        var prefixSeparator = extensionToken.IndexOf(':');
        if (prefixSeparator >= 0 && prefixSeparator + 1 < extensionToken.Length)
        {
            plainName = extensionToken.Substring(prefixSeparator + 1);
        }

        if (plainName.Length == 0)
        {
            yield break;
        }

        var extensionName = XamlMarkupExtensionNameSemantics.ToClrExtensionTypeToken(plainName);
        foreach (var ns in markupExtensionNamespaces)
        {
            if (names.Add(ns + "." + plainName))
            {
                yield return ns + "." + plainName;
            }

            if (names.Add(ns + "." + extensionName))
            {
                yield return ns + "." + extensionName;
            }
        }
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

    private static bool IsMarkupClassCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or ':' or '.';
    }
}
