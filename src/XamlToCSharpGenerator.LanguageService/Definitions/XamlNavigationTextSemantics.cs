using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static partial class XamlNavigationTextSemantics
{
    internal enum NavigationSymbolKind
    {
        Unknown,
        NamedElement,
        ResourceKey
    }

    [GeneratedRegex(@"\bElementName\s*=\s*(?<id>[A-Za-z_][A-Za-z0-9_:\.-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex ElementNameReferenceRegex();

    [GeneratedRegex(@"x:Reference(?:\s+|=)(?<id>[A-Za-z_][A-Za-z0-9_:\.-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex XReferenceRegex();

    [GeneratedRegex(@"\bx:Name\s*=\s*""(?<id>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex XNameDeclarationRegex();

    [GeneratedRegex(@"\bx:Key\s*=\s*""(?<id>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex XKeyDeclarationRegex();

    public static string ExtractIdentifierAtOffset(string text, int offset)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var boundedOffset = Math.Max(0, Math.Min(offset, text.Length));
        var start = boundedOffset;
        while (start > 0 && IsWordCharacter(text[start - 1]))
        {
            start--;
        }

        var end = boundedOffset;
        while (end < text.Length && IsWordCharacter(text[end]))
        {
            end++;
        }

        if (end <= start)
        {
            return string.Empty;
        }

        return text.Substring(start, end - start);
    }

    public static bool IsElementNameReferenceContext(string text, int offset, string identifier)
    {
        var start = Math.Max(0, offset - 128);
        var length = Math.Min(text.Length - start, 256);
        var window = text.Substring(start, length);

        return window.Contains("ElementName=" + identifier, StringComparison.Ordinal) ||
               window.Contains("ElementName = " + identifier, StringComparison.Ordinal) ||
               window.Contains("x:Reference " + identifier, StringComparison.Ordinal) ||
               window.Contains("x:Reference=" + identifier, StringComparison.Ordinal);
    }

    public static ImmutableArray<SourceRange> FindElementReferenceRanges(string text, string identifier)
    {
        return FindRangesByIdentifier(text, identifier, ElementNameReferenceRegex(), XReferenceRegex());
    }

    public static ImmutableArray<SourceRange> FindResourceReferenceRanges(string text, string identifier)
    {
        return XamlResourceReferenceNavigationSemantics.FindResourceReferenceRanges(text, identifier);
    }

    public static NavigationSymbolKind DetectSymbolKindAtOffset(
        string text,
        int offset,
        string identifier,
        bool hasNamedDeclaration,
        bool hasResourceDeclaration)
    {
        if (IsOffsetInsideIdentifierGroup(ElementNameReferenceRegex(), text, offset, identifier) ||
            IsOffsetInsideIdentifierGroup(XReferenceRegex(), text, offset, identifier) ||
            IsOffsetInsideIdentifierGroup(XNameDeclarationRegex(), text, offset, identifier))
        {
            return NavigationSymbolKind.NamedElement;
        }

        if (XamlResourceReferenceNavigationSemantics.IsResourceReferenceContext(text, offset, identifier) ||
            IsOffsetInsideIdentifierGroup(XKeyDeclarationRegex(), text, offset, identifier))
        {
            return NavigationSymbolKind.ResourceKey;
        }

        if (hasNamedDeclaration && !hasResourceDeclaration)
        {
            return NavigationSymbolKind.NamedElement;
        }

        if (hasResourceDeclaration && !hasNamedDeclaration)
        {
            return NavigationSymbolKind.ResourceKey;
        }

        return NavigationSymbolKind.Unknown;
    }

    private static ImmutableArray<SourceRange> FindRangesByIdentifier(
        string text,
        string identifier,
        params Regex[] patterns)
    {
        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        var seen = new HashSet<(int Start, int Length)>();

        foreach (var pattern in patterns)
        {
            var matches = pattern.Matches(text);
            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                var idGroup = match.Groups["id"];
                if (!idGroup.Success ||
                    !string.Equals(idGroup.Value, identifier, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = (idGroup.Index, idGroup.Length);
                if (!seen.Add(key))
                {
                    continue;
                }

                var start = TextCoordinateHelper.GetPosition(text, idGroup.Index);
                var end = TextCoordinateHelper.GetPosition(text, idGroup.Index + idGroup.Length);
                builder.Add(new SourceRange(start, end));
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsOffsetInsideIdentifierGroup(Regex regex, string text, int offset, string identifier)
    {
        var matches = regex.Matches(text);
        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var idGroup = match.Groups["id"];
            if (!idGroup.Success ||
                !string.Equals(idGroup.Value, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var start = idGroup.Index;
            var end = idGroup.Index + idGroup.Length;
            if (offset >= start && offset <= end)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWordCharacter(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or ':' or '.';
    }
}
