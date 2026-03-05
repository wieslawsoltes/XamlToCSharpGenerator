using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlResourceReferenceNavigationSemantics
{
    public static bool TryResolveResourceIdentifierAtOffset(
        string text,
        int offset,
        out string identifier)
    {
        identifier = string.Empty;
        foreach (var reference in EnumerateResourceReferences(text))
        {
            var startOffset = TextCoordinateHelper.GetOffset(text, reference.Range.Start);
            var endOffset = TextCoordinateHelper.GetOffset(text, reference.Range.End);
            if (offset < startOffset || offset > endOffset)
            {
                continue;
            }

            identifier = reference.ResourceKey;
            return true;
        }

        return false;
    }

    public static bool IsResourceReferenceContext(string text, int offset, string identifier)
    {
        foreach (var reference in EnumerateResourceReferences(text))
        {
            if (!string.Equals(reference.ResourceKey, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var startOffset = TextCoordinateHelper.GetOffset(text, reference.Range.Start);
            var endOffset = TextCoordinateHelper.GetOffset(text, reference.Range.End);
            if (offset >= startOffset && offset <= endOffset)
            {
                return true;
            }
        }

        return false;
    }

    public static ImmutableArray<SourceRange> FindResourceReferenceRanges(string text, string identifier)
    {
        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        foreach (var reference in EnumerateResourceReferences(text))
        {
            if (!string.Equals(reference.ResourceKey, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            builder.Add(reference.Range);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ResourceReferenceOccurrence> EnumerateResourceReferences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImmutableArray<ResourceReferenceOccurrence>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ResourceReferenceOccurrence>();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '{')
            {
                continue;
            }

            var parseIndex = index;
            if (!TopLevelTextParser.TryReadBalancedContent(text, ref parseIndex, '{', '}', out _))
            {
                continue;
            }

            var markupText = text.Substring(index, parseIndex - index);
            if (TryCreateOccurrence(text, markupText, index, out var occurrence))
            {
                builder.Add(occurrence);
            }

            index = parseIndex - 1;
        }

        return builder.ToImmutable();
    }

    private static bool TryCreateOccurrence(
        string documentText,
        string markupText,
        int absoluteStart,
        out ResourceReferenceOccurrence occurrence)
    {
        occurrence = default;
        if (!StaticResourceReferenceParser.TryExtractResourceKey(markupText, out var resourceKey) ||
            !TryFindResourceKeyRange(documentText, markupText, absoluteStart, resourceKey, out var range))
        {
            return false;
        }

        occurrence = new ResourceReferenceOccurrence(resourceKey, range);
        return true;
    }

    private static bool TryFindResourceKeyRange(
        string documentText,
        string markupText,
        int absoluteStart,
        string resourceKey,
        out SourceRange range)
    {
        range = default;
        if (!TryGetInnerSpan(markupText, out var innerStart, out var innerLength))
        {
            return false;
        }

        var innerText = markupText.Substring(innerStart, innerLength);
        var headLength = 0;
        while (headLength < innerText.Length &&
               !char.IsWhiteSpace(innerText[headLength]) &&
               innerText[headLength] != ',')
        {
            headLength++;
        }

        if (headLength == 0)
        {
            return false;
        }

        var markupName = innerText.Substring(0, headLength).Trim();
        if (!markupName.Equals("StaticResource", StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("DynamicResource", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var argumentsStart = headLength;
        while (argumentsStart < innerText.Length && char.IsWhiteSpace(innerText[argumentsStart]))
        {
            argumentsStart++;
        }

        if (argumentsStart < innerText.Length && innerText[argumentsStart] == ',')
        {
            argumentsStart++;
            while (argumentsStart < innerText.Length && char.IsWhiteSpace(innerText[argumentsStart]))
            {
                argumentsStart++;
            }
        }

        if (argumentsStart >= innerText.Length)
        {
            return false;
        }

        var argumentsText = innerText.Substring(argumentsStart);
        var arguments = TopLevelTextParser.SplitTopLevelSegments(argumentsText, ',', trimTokens: false, removeEmpty: false);
        foreach (var argument in arguments)
        {
            if (!TryResolveArgumentKeyRange(
                    documentText,
                    argument.Text,
                    absoluteStart + innerStart + argumentsStart + argument.Start,
                    resourceKey,
                    out range))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool TryResolveArgumentKeyRange(
        string documentText,
        string rawArgument,
        int rawArgumentAbsoluteStart,
        string expectedResourceKey,
        out SourceRange range)
    {
        range = default;
        var trimmedStart = 0;
        var trimmedEnd = rawArgument.Length;
        while (trimmedStart < trimmedEnd && char.IsWhiteSpace(rawArgument[trimmedStart]))
        {
            trimmedStart++;
        }

        while (trimmedEnd > trimmedStart && char.IsWhiteSpace(rawArgument[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd <= trimmedStart)
        {
            return false;
        }

        var argumentText = rawArgument.Substring(trimmedStart, trimmedEnd - trimmedStart);
        var argumentAbsoluteStart = rawArgumentAbsoluteStart + trimmedStart;
        var equalsIndex = TopLevelTextParser.IndexOfTopLevel(argumentText, '=');

        string valueText;
        int valueAbsoluteStart;
        if (equalsIndex <= 0 || equalsIndex >= argumentText.Length - 1)
        {
            valueText = argumentText;
            valueAbsoluteStart = argumentAbsoluteStart;
        }
        else
        {
            var argumentName = argumentText.Substring(0, equalsIndex).Trim();
            if (!argumentName.Equals("ResourceKey", StringComparison.OrdinalIgnoreCase) &&
                !argumentName.Equals("Key", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var valueOffset = equalsIndex + 1;
            while (valueOffset < argumentText.Length && char.IsWhiteSpace(argumentText[valueOffset]))
            {
                valueOffset++;
            }

            if (valueOffset >= argumentText.Length)
            {
                return false;
            }

            valueText = argumentText.Substring(valueOffset);
            valueAbsoluteStart = argumentAbsoluteStart + valueOffset;
        }

        valueText = TrimTrailingWhitespace(valueText);
        if (!TryUnquoteValue(valueText, valueAbsoluteStart, out var unquotedValue, out var unquotedStart))
        {
            return false;
        }

        if (!string.Equals(unquotedValue, expectedResourceKey, StringComparison.Ordinal))
        {
            return false;
        }

        var start = TextCoordinateHelper.GetPosition(documentText, unquotedStart);
        var end = TextCoordinateHelper.GetPosition(documentText, unquotedStart + unquotedValue.Length);

        range = new SourceRange(start, end);
        return true;
    }

    private static string TrimTrailingWhitespace(string value)
    {
        var end = value.Length;
        while (end > 0 && char.IsWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return end == value.Length ? value : value.Substring(0, end);
    }

    private static bool TryUnquoteValue(
        string value,
        int absoluteStart,
        out string unquotedValue,
        out int unquotedStart)
    {
        unquotedValue = value;
        unquotedStart = absoluteStart;
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[value.Length - 1] == '"') ||
             (value[0] == '\'' && value[value.Length - 1] == '\'')))
        {
            unquotedValue = value.Substring(1, value.Length - 2);
            unquotedStart = absoluteStart + 1;
        }

        return unquotedValue.Length > 0;
    }

    private static bool TryGetInnerSpan(string markupText, out int innerStart, out int innerLength)
    {
        innerStart = 0;
        innerLength = 0;
        if (string.IsNullOrWhiteSpace(markupText))
        {
            return false;
        }

        var start = 0;
        while (start < markupText.Length && char.IsWhiteSpace(markupText[start]))
        {
            start++;
        }

        if (start >= markupText.Length || markupText[start] != '{')
        {
            return false;
        }

        start++;
        while (start < markupText.Length && char.IsWhiteSpace(markupText[start]))
        {
            start++;
        }

        var end = markupText.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(markupText[end]))
        {
            end--;
        }

        if (end <= start || markupText[end] != '}')
        {
            return false;
        }

        end--;
        while (end >= start && char.IsWhiteSpace(markupText[end]))
        {
            end--;
        }

        if (end < start)
        {
            return false;
        }

        innerStart = start;
        innerLength = end - start + 1;
        return true;
    }

    private readonly record struct ResourceReferenceOccurrence(string ResourceKey, SourceRange Range);
}
