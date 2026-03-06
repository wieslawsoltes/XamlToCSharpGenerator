using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Parsing;

internal readonly record struct MarkupSpanInfo(
    string ExtensionName,
    int Start,
    int Length,
    int ExtensionNameStart,
    int ExtensionNameLength,
    ImmutableArray<MarkupArgumentSpan> Arguments);

internal readonly record struct MarkupArgumentSpan(
    string? Name,
    int Start,
    int Length,
    int NameStart,
    int NameLength,
    int ValueStart,
    int ValueLength,
    string ValueText,
    int Ordinal);

internal static class XamlMarkupExtensionSpanParser
{
    public static bool TryParse(
        string attributeValue,
        int valueStartOffset,
        out MarkupSpanInfo markupSpanInfo)
    {
        markupSpanInfo = default;
        if (!MarkupExpressionEnvelopeSemantics.IsMarkupExpression(attributeValue))
        {
            return false;
        }

        var trimmedStart = 0;
        while (trimmedStart < attributeValue.Length && char.IsWhiteSpace(attributeValue[trimmedStart]))
        {
            trimmedStart++;
        }

        var trimmedEnd = attributeValue.Length;
        while (trimmedEnd > trimmedStart && char.IsWhiteSpace(attributeValue[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd - trimmedStart < 2 ||
            attributeValue[trimmedStart] != '{' ||
            attributeValue[trimmedEnd - 1] != '}')
        {
            return false;
        }

        var innerStart = trimmedStart + 1;
        var innerEnd = trimmedEnd - 1;
        while (innerStart < innerEnd && char.IsWhiteSpace(attributeValue[innerStart]))
        {
            innerStart++;
        }

        while (innerEnd > innerStart && char.IsWhiteSpace(attributeValue[innerEnd - 1]))
        {
            innerEnd--;
        }

        if (innerEnd <= innerStart)
        {
            return false;
        }

        var innerText = attributeValue.Substring(innerStart, innerEnd - innerStart);
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

        var extensionName = innerText.Substring(0, headLength).Trim();
        if (extensionName.Length == 0)
        {
            return false;
        }

        var argumentsStartInInner = headLength;
        while (argumentsStartInInner < innerText.Length && char.IsWhiteSpace(innerText[argumentsStartInInner]))
        {
            argumentsStartInInner++;
        }

        if (argumentsStartInInner < innerText.Length && innerText[argumentsStartInInner] == ',')
        {
            argumentsStartInInner++;
            while (argumentsStartInInner < innerText.Length && char.IsWhiteSpace(innerText[argumentsStartInInner]))
            {
                argumentsStartInInner++;
            }
        }

        var arguments = ImmutableArray<MarkupArgumentSpan>.Empty;
        var absoluteArgumentsStart = valueStartOffset + innerStart + argumentsStartInInner;
        if (argumentsStartInInner < innerText.Length)
        {
            var argumentsText = innerText.Substring(argumentsStartInInner);
            var segments = TopLevelTextParser.SplitTopLevelSegments(
                argumentsText,
                ',',
                trimTokens: true,
                removeEmpty: true);
            if (segments.Length > 0)
            {
                var builder = ImmutableArray.CreateBuilder<MarkupArgumentSpan>(segments.Length);
                for (var index = 0; index < segments.Length; index++)
                {
                    var segment = segments[index];
                    var absoluteSegmentStart = absoluteArgumentsStart + segment.Start;
                    var absoluteSegmentLength = segment.Length;
                    var valueText = segment.Text;
                    string? name = null;
                    var nameStart = 0;
                    var nameLength = 0;
                    var valueStart = absoluteSegmentStart;
                    var valueLength = absoluteSegmentLength;

                    var parseStatus = XamlMarkupArgumentSemantics.TryParseNamedArgument(
                        segment.Text,
                        out var parsedName,
                        out var parsedValue);
                    if (parseStatus == XamlMarkupNamedArgumentParseStatus.Parsed)
                    {
                        name = parsedName;
                        valueText = parsedValue;

                        var equalsIndex = TopLevelTextParser.IndexOfTopLevel(segment.Text, '=');
                        if (equalsIndex > 0)
                        {
                            nameStart = 0;
                            var nameEnd = equalsIndex - 1;
                            while (nameEnd >= nameStart && char.IsWhiteSpace(segment.Text[nameEnd]))
                            {
                                nameEnd--;
                            }

                            while (nameStart <= nameEnd && char.IsWhiteSpace(segment.Text[nameStart]))
                            {
                                nameStart++;
                            }

                            nameLength = Math.Max(0, nameEnd - nameStart + 1);

                            var valueStartInSegment = equalsIndex + 1;
                            while (valueStartInSegment < segment.Text.Length &&
                                   char.IsWhiteSpace(segment.Text[valueStartInSegment]))
                            {
                                valueStartInSegment++;
                            }

                            valueStart = absoluteSegmentStart + valueStartInSegment;
                            valueLength = segment.Text.Length - valueStartInSegment;
                        }
                    }

                    NormalizeQuotedToken(attributeValue, valueStart - valueStartOffset, valueLength, out var normalizedStart, out var normalizedLength);
                    valueStart = valueStartOffset + normalizedStart;
                    valueLength = normalizedLength;

                    builder.Add(new MarkupArgumentSpan(
                        Name: name,
                        Start: absoluteSegmentStart,
                        Length: absoluteSegmentLength,
                        NameStart: absoluteSegmentStart + nameStart,
                        NameLength: nameLength,
                        ValueStart: valueStart,
                        ValueLength: valueLength,
                        ValueText: valueLength > 0
                            ? attributeValue.Substring(normalizedStart, normalizedLength)
                            : string.Empty,
                        Ordinal: index));
                }

                arguments = builder.ToImmutable();
            }
        }

        markupSpanInfo = new MarkupSpanInfo(
            ExtensionName: extensionName,
            Start: valueStartOffset + trimmedStart,
            Length: trimmedEnd - trimmedStart,
            ExtensionNameStart: valueStartOffset + innerStart,
            ExtensionNameLength: headLength,
            Arguments: arguments);
        return true;
    }

    private static void NormalizeQuotedToken(
        string sourceText,
        int start,
        int length,
        out int normalizedStart,
        out int normalizedLength)
    {
        normalizedStart = start;
        normalizedLength = length;
        while (normalizedLength > 0 && char.IsWhiteSpace(sourceText[normalizedStart]))
        {
            normalizedStart++;
            normalizedLength--;
        }

        while (normalizedLength > 0 && char.IsWhiteSpace(sourceText[normalizedStart + normalizedLength - 1]))
        {
            normalizedLength--;
        }

        if (normalizedLength >= 2)
        {
            var first = sourceText[normalizedStart];
            var last = sourceText[normalizedStart + normalizedLength - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
            {
                normalizedStart++;
                normalizedLength -= 2;
            }
        }
    }
}
