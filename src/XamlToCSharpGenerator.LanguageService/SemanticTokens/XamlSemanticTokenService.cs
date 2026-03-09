using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.SemanticTokens;

public sealed class XamlSemanticTokenService
{
    private readonly record struct OffsetRange(int Start, int End);

    public static readonly ImmutableArray<string> TokenTypes =
    [
        "xamlDelimiter",
        "xamlName",
        "xamlAttribute",
        "xamlAttributeValue",
        "xamlAttributeQuotes",
        "xamlComment",
        "xamlKeyword",
        "xamlMarkupExtensionClass",
        "xamlMarkupExtensionParameterName",
        "xamlMarkupExtensionParameterValue",
        "xamlNamespacePrefix",
        "xamlText",
        "number",
        "keyword",
        "string",
        "operator",
        "property",
        "method",
        "parameter",
        "variable",
        "type"
    ];

    public ImmutableArray<XamlSemanticToken> GetTokens(XamlAnalysisResult analysis)
    {
        var text = analysis.Document.Text;
        if (string.IsNullOrEmpty(text))
        {
            return ImmutableArray<XamlSemanticToken>.Empty;
        }

        var tokens = ImmutableArray.CreateBuilder<XamlSemanticToken>();
        var mapper = new OffsetLineMap(text);
        var inlineCSharpContexts = XamlInlineCSharpNavigationService.EnumerateContexts(analysis);
        var markupExpressionInfoByOffset = BuildMarkupExpressionInfoMap(analysis, text);
        var inlineCodeContextsByOffset = BuildInlineCodeContextMap(text, inlineCSharpContexts);
        var inlineElementCodeRanges = BuildInlineElementCodeRanges(text, inlineCSharpContexts);

        var index = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '<')
            {
                TokenizeTag(
                    text,
                    ref index,
                    mapper,
                    tokens,
                    markupExpressionInfoByOffset,
                    inlineCodeContextsByOffset);
                continue;
            }

            TokenizeTextNode(text, ref index, mapper, tokens, inlineElementCodeRanges);
        }

        AddMarkupExpressionSemanticTokens(analysis, mapper, tokens);
        AddInlineCSharpSemanticTokens(inlineCSharpContexts, mapper, tokens);
        return tokens.ToImmutable();
    }

    private static void TokenizeTextNode(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        ImmutableArray<OffsetRange> skippedRanges)
    {
        var end = text.IndexOf('<', index);
        if (end < 0)
        {
            end = text.Length;
        }

        var cursor = index;
        foreach (var skippedRange in skippedRanges)
        {
            if (skippedRange.End <= cursor)
            {
                continue;
            }

            if (skippedRange.Start >= end)
            {
                break;
            }

            TokenizeTextNodeSegment(text, cursor, Math.Min(skippedRange.Start, end), mapper, tokens);
            cursor = Math.Max(cursor, Math.Min(skippedRange.End, end));
            if (cursor >= end)
            {
                break;
            }
        }

        TokenizeTextNodeSegment(text, cursor, end, mapper, tokens);
        index = end;
    }

    private static void TokenizeTextNodeSegment(
        string text,
        int start,
        int end,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var cursor = start;
        while (cursor < end)
        {
            while (cursor < end && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            var tokenStart = cursor;
            while (cursor < end && !char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }

            if (cursor > tokenStart)
            {
                AddToken(tokens, mapper, tokenStart, cursor - tokenStart, "xamlText");
            }
        }
    }

    private static void TokenizeTag(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        IReadOnlyDictionary<int, XamlCSharpMarkupExpressionInfo> markupExpressionInfoByOffset,
        IReadOnlyDictionary<int, XamlInlineCSharpContext> inlineCodeContextsByOffset)
    {
        if (index + 3 < text.Length &&
            text[index + 1] == '!' &&
            text[index + 2] == '-' &&
            text[index + 3] == '-')
        {
            AddToken(tokens, mapper, index, 4, "xamlDelimiter");
            var commentEnd = text.IndexOf("-->", index + 4, StringComparison.Ordinal);
            if (commentEnd < 0)
            {
                AddToken(tokens, mapper, index + 4, text.Length - index - 4, "xamlComment");
                index = text.Length;
                return;
            }

            if (commentEnd > index + 4)
            {
                AddToken(tokens, mapper, index + 4, commentEnd - index - 4, "xamlComment");
            }

            AddToken(tokens, mapper, commentEnd, 3, "xamlDelimiter");
            index = commentEnd + 3;
            return;
        }

        if (index + 8 < text.Length &&
            text[index + 1] == '!' &&
            text[index + 2] == '[' &&
            text[index + 3] == 'C' &&
            text[index + 4] == 'D' &&
            text[index + 5] == 'A' &&
            text[index + 6] == 'T' &&
            text[index + 7] == 'A' &&
            text[index + 8] == '[')
        {
            AddToken(tokens, mapper, index, 9, "xamlDelimiter");
            var cdataEnd = text.IndexOf("]]>", index + 9, StringComparison.Ordinal);
            if (cdataEnd < 0)
            {
                index = text.Length;
                return;
            }

            AddToken(tokens, mapper, cdataEnd, 3, "xamlDelimiter");
            index = cdataEnd + 3;
            return;
        }

        if (index + 1 < text.Length && text[index + 1] == '?')
        {
            AddToken(tokens, mapper, index, 2, "xamlDelimiter");
            var piStart = index + 2;
            var piLength = ReadIdentifierLength(text, piStart);
            if (piLength > 0)
            {
                AddToken(tokens, mapper, piStart, piLength, "xamlKeyword");
            }

            var piEnd = text.IndexOf("?>", index + 2, StringComparison.Ordinal);
            if (piEnd >= 0)
            {
                AddToken(tokens, mapper, piEnd, 2, "xamlDelimiter");
                index = piEnd + 2;
            }
            else
            {
                index = text.Length;
            }

            return;
        }

        AddToken(tokens, mapper, index, 1, "xamlDelimiter");
        index++;
        if (index < text.Length && text[index] == '/')
        {
            AddToken(tokens, mapper, index, 1, "xamlDelimiter");
            index++;
        }

        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        var elementNameStart = index;
        var elementNameLength = ReadIdentifierLength(text, elementNameStart);
        if (elementNameLength > 0)
        {
            AddQualifiedNameTokens(tokens, mapper, elementNameStart, elementNameLength, "xamlName");
            index += elementNameLength;
        }

        while (index < text.Length)
        {
            if (text[index] == '>')
            {
                AddToken(tokens, mapper, index, 1, "xamlDelimiter");
                index++;
                break;
            }

            if (text[index] == '/' && index + 1 < text.Length && text[index + 1] == '>')
            {
                AddToken(tokens, mapper, index, 2, "xamlDelimiter");
                index += 2;
                break;
            }

            if (text[index] == '"' || text[index] == '\'')
            {
                TokenizeQuotedValue(
                    text,
                    ref index,
                    mapper,
                    tokens,
                    markupExpressionInfoByOffset,
                    inlineCodeContextsByOffset);
                continue;
            }

            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            var attributeStart = index;
            var attributeLength = ReadIdentifierLength(text, attributeStart);
            if (attributeLength <= 0)
            {
                index++;
                continue;
            }

            TokenizeAttributeName(text, attributeStart, attributeLength, mapper, tokens);
            index += attributeLength;

            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index < text.Length && text[index] == '=')
            {
                AddToken(tokens, mapper, index, 1, "xamlDelimiter");
                index++;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                if (index < text.Length && (text[index] == '"' || text[index] == '\''))
                {
                    TokenizeQuotedValue(
                        text,
                        ref index,
                        mapper,
                        tokens,
                        markupExpressionInfoByOffset,
                        inlineCodeContextsByOffset);
                }
                else
                {
                    var valueStart = index;
                    while (index < text.Length &&
                           !char.IsWhiteSpace(text[index]) &&
                           text[index] is not '>' and not '/')
                    {
                        index++;
                    }

                    var valueLength = index - valueStart;
                    if (valueLength > 0)
                    {
                        TokenizeAttributeValueSpan(
                            text,
                            valueStart,
                            valueLength,
                            mapper,
                            tokens,
                            markupExpressionInfoByOffset,
                            inlineCodeContextsByOffset);
                    }
                }
            }
        }
    }

    private static void TokenizeQuotedValue(
        string text,
        ref int index,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        IReadOnlyDictionary<int, XamlCSharpMarkupExpressionInfo> markupExpressionInfoByOffset,
        IReadOnlyDictionary<int, XamlInlineCSharpContext> inlineCodeContextsByOffset)
    {
        var quote = text[index];
        AddToken(tokens, mapper, index, 1, "xamlAttributeQuotes");
        var valueStart = index + 1;
        var valueEnd = text.IndexOf(quote, valueStart);
        if (valueEnd < 0)
        {
            valueEnd = text.Length;
        }

        if (valueEnd > valueStart)
        {
            TokenizeAttributeValueSpan(
                text,
                valueStart,
                valueEnd - valueStart,
                mapper,
                tokens,
                markupExpressionInfoByOffset,
                inlineCodeContextsByOffset);
        }

        if (valueEnd < text.Length)
        {
            AddToken(tokens, mapper, valueEnd, 1, "xamlAttributeQuotes");
        }

        index = valueEnd < text.Length ? valueEnd + 1 : valueEnd;
    }

    private static void TokenizeAttributeName(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var name = text.Substring(start, length);
        if (string.Equals(name, "xmlns", StringComparison.Ordinal))
        {
            AddToken(tokens, mapper, start, length, "xamlKeyword");
            return;
        }

        if (name.StartsWith("xmlns:", StringComparison.Ordinal))
        {
            AddToken(tokens, mapper, start, "xmlns".Length, "xamlKeyword");
            AddToken(tokens, mapper, start + "xmlns".Length, 1, "xamlDelimiter");
            var namespacePrefixStart = start + "xmlns:".Length;
            var namespacePrefixLength = length - "xmlns:".Length;
            if (namespacePrefixLength > 0)
            {
                AddToken(tokens, mapper, namespacePrefixStart, namespacePrefixLength, "xamlNamespacePrefix");
            }

            return;
        }

        var dotIndex = name.IndexOf('.');
        if (dotIndex > 0)
        {
            var ownerTokenStart = start;
            var ownerTokenLength = dotIndex;
            AddQualifiedNameTokens(tokens, mapper, ownerTokenStart, ownerTokenLength, "xamlName");
            AddToken(tokens, mapper, start + dotIndex, 1, "xamlDelimiter");

            var propertyStart = start + dotIndex + 1;
            var propertyLength = length - dotIndex - 1;
            if (propertyLength > 0)
            {
                AddToken(tokens, mapper, propertyStart, propertyLength, "xamlAttribute");
            }

            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlAttribute");
    }

    private static void TokenizeAttributeValueSpan(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        IReadOnlyDictionary<int, XamlCSharpMarkupExpressionInfo> markupExpressionInfoByOffset,
        IReadOnlyDictionary<int, XamlInlineCSharpContext> inlineCodeContextsByOffset)
    {
        if (length <= 0)
        {
            return;
        }

        if (inlineCodeContextsByOffset.TryGetValue(start, out var inlineCodeContext) &&
            TextCoordinateHelper.GetOffset(text, inlineCodeContext.CodeRange.End) == start + length)
        {
            TokenizeCSharpExpressionSpan(text, start, start + length, mapper, tokens);
            return;
        }

        if (markupExpressionInfoByOffset.TryGetValue(start, out var expressionInfo) &&
            expressionInfo.ExpressionLength <= length)
        {
            TokenizeCSharpMarkupExpression(text, expressionInfo, mapper, tokens);
            return;
        }

        if (IsNumericToken(text, start, length))
        {
            AddToken(tokens, mapper, start, length, "number");
            return;
        }

        if (length >= 2 && text[start] == '{' && text[start + length - 1] == '}')
        {
            TokenizeMarkupExtension(text, start, length, mapper, tokens, inlineCodeContextsByOffset);
            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlAttributeValue");
    }

    private static Dictionary<int, XamlCSharpMarkupExpressionInfo> BuildMarkupExpressionInfoMap(
        XamlAnalysisResult analysis,
        string text)
    {
        var map = new Dictionary<int, XamlCSharpMarkupExpressionInfo>();
        foreach (var element in analysis.XmlDocument?.Root?.DescendantsAndSelf() ?? Enumerable.Empty<System.Xml.Linq.XElement>())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(text, attribute, out var attributeValueRange))
                {
                    continue;
                }

                var attributeValueStart = TextCoordinateHelper.GetOffset(text, attributeValueRange.Start);
                if (attributeValueStart < 0 ||
                    !XamlCSharpMarkupExpressionService.TryParseMarkupExpression(
                        analysis,
                        element,
                        attribute,
                        attributeValueStart,
                        out var expressionInfo))
                {
                    continue;
                }

                map[attributeValueStart] = expressionInfo;
            }
        }

        return map;
    }

    private static void TokenizeMarkupExtension(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        IReadOnlyDictionary<int, XamlInlineCSharpContext> inlineCodeContextsByOffset)
    {
        var end = start + length;
        AddToken(tokens, mapper, start, 1, "xamlDelimiter");
        if (length > 1)
        {
            AddToken(tokens, mapper, end - 1, 1, "xamlDelimiter");
        }

        var contentStart = start + 1;
        var contentEnd = end - 1;
        TrimRange(text, ref contentStart, ref contentEnd);
        if (contentStart >= contentEnd)
        {
            return;
        }

        var classLength = ReadMarkupExtensionIdentifierLength(text, contentStart, contentEnd);
        if (classLength > 0)
        {
            AddQualifiedNameTokens(tokens, mapper, contentStart, classLength, "xamlMarkupExtensionClass");
            contentStart += classLength;
        }

        while (contentStart < contentEnd)
        {
            while (contentStart < contentEnd && char.IsWhiteSpace(text[contentStart]))
            {
                contentStart++;
            }

            if (contentStart >= contentEnd)
            {
                break;
            }

            if (text[contentStart] == ',')
            {
                AddToken(tokens, mapper, contentStart, 1, "xamlDelimiter");
                contentStart++;
                continue;
            }

            var segmentStart = contentStart;
            var depth = 0;
            while (contentStart < contentEnd)
            {
                var current = text[contentStart];
                if (current == '{')
                {
                    depth++;
                }
                else if (current == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                    }
                }
                else if (current == ',' && depth == 0)
                {
                    break;
                }

                contentStart++;
            }

            var segmentEnd = contentStart;
            TrimRange(text, ref segmentStart, ref segmentEnd);
            if (segmentStart >= segmentEnd)
            {
                continue;
            }

            var equalsIndex = IndexOf(text, segmentStart, segmentEnd, '=');
            if (equalsIndex > segmentStart && equalsIndex < segmentEnd - 1)
            {
                AddQualifiedNameTokens(
                    tokens,
                    mapper,
                    segmentStart,
                    equalsIndex - segmentStart,
                    "xamlMarkupExtensionParameterName");
                AddToken(tokens, mapper, equalsIndex, 1, "xamlDelimiter");

                var valueStart = equalsIndex + 1;
                var valueEnd = segmentEnd;
                TrimRange(text, ref valueStart, ref valueEnd);
                if (valueStart < valueEnd)
                {
                    TokenizeMarkupExtensionParameterValue(
                        text,
                        valueStart,
                        valueEnd - valueStart,
                        mapper,
                        tokens,
                        inlineCodeContextsByOffset);
                }
            }
            else
            {
                TokenizeMarkupExtensionParameterValue(
                    text,
                    segmentStart,
                    segmentEnd - segmentStart,
                    mapper,
                    tokens,
                    inlineCodeContextsByOffset);
            }
        }
    }

    private static void TokenizeMarkupExtensionParameterValue(
        string text,
        int start,
        int length,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        IReadOnlyDictionary<int, XamlInlineCSharpContext> inlineCodeContextsByOffset)
    {
        if (length <= 0)
        {
            return;
        }

        if (inlineCodeContextsByOffset.TryGetValue(start, out var inlineCodeContext) &&
            TextCoordinateHelper.GetOffset(text, inlineCodeContext.CodeRange.End) == start + length)
        {
            TokenizeCSharpExpressionSpan(text, start, start + length, mapper, tokens);
            return;
        }

        if (IsNumericToken(text, start, length))
        {
            AddToken(tokens, mapper, start, length, "number");
            return;
        }

        AddQualifiedNameTokens(tokens, mapper, start, length, "xamlMarkupExtensionParameterValue");
    }

    private static void TokenizeCSharpMarkupExpression(
        string text,
        XamlCSharpMarkupExpressionInfo expressionInfo,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var valueStart = expressionInfo.ExpressionStartOffset;
        while (valueStart > 0 && char.IsWhiteSpace(text[valueStart - 1]))
        {
            valueStart--;
        }

        if (valueStart > 0 && text[valueStart - 1] == '{')
        {
            AddToken(tokens, mapper, valueStart - 1, 1, "xamlDelimiter");
        }

        if (expressionInfo.IsExplicitExpression)
        {
            var markerOffset = valueStart;
            while (markerOffset < text.Length && char.IsWhiteSpace(text[markerOffset]))
            {
                markerOffset++;
            }

            if (markerOffset < text.Length && text[markerOffset] == '=')
            {
                AddToken(tokens, mapper, markerOffset, 1, "xamlDelimiter");
            }
        }

        var expressionEnd = expressionInfo.ExpressionStartOffset + expressionInfo.ExpressionLength;
        TokenizeCSharpExpressionSpan(text, expressionInfo.ExpressionStartOffset, expressionEnd, mapper, tokens);

        var closingBraceOffset = expressionEnd;
        while (closingBraceOffset < text.Length && char.IsWhiteSpace(text[closingBraceOffset]))
        {
            closingBraceOffset++;
        }

        if (closingBraceOffset < text.Length && text[closingBraceOffset] == '}')
        {
            AddToken(tokens, mapper, closingBraceOffset, 1, "xamlDelimiter");
        }
    }

    private static void TokenizeCSharpExpressionSpan(
        string text,
        int start,
        int end,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var index = start;
        while (index < end)
        {
            var current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '$' &&
                index + 1 < end &&
                text[index + 1] is '"' or '\'')
            {
                var stringEnd = ScanStringLiteral(text, index + 1, end);
                AddToken(tokens, mapper, index, stringEnd - index, "string");
                index = stringEnd;
                continue;
            }

            if (current is '"' or '\'')
            {
                var stringEnd = ScanStringLiteral(text, index, end);
                AddToken(tokens, mapper, index, stringEnd - index, "string");
                index = stringEnd;
                continue;
            }

            if (char.IsDigit(current))
            {
                var numberEnd = ScanNumericLiteral(text, index, end);
                AddToken(tokens, mapper, index, numberEnd - index, "number");
                index = numberEnd;
                continue;
            }

            if (SyntaxFacts.IsIdentifierStartCharacter(current))
            {
                var identifierEnd = index + 1;
                while (identifierEnd < end && SyntaxFacts.IsIdentifierPartCharacter(text[identifierEnd]))
                {
                    identifierEnd++;
                }

                var tokenText = text.Substring(index, identifierEnd - index);
                if (SyntaxFacts.GetKeywordKind(tokenText) != SyntaxKind.None)
                {
                    AddToken(tokens, mapper, index, identifierEnd - index, "keyword");
                }

                index = identifierEnd;
                continue;
            }

            var operatorLength = GetOperatorLength(text, index, end);
            if (operatorLength > 0)
            {
                AddToken(tokens, mapper, index, operatorLength, "operator");
                index += operatorLength;
                continue;
            }

            index++;
        }
    }

    private static void AddQualifiedNameTokens(
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        OffsetLineMap mapper,
        int start,
        int length,
        string defaultTokenType)
    {
        var text = mapper.Text;
        var separatorIndex = text.AsSpan(start, length).IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= length - 1)
        {
            AddToken(tokens, mapper, start, length, defaultTokenType);
            return;
        }

        AddToken(tokens, mapper, start, separatorIndex, "xamlNamespacePrefix");
        AddToken(tokens, mapper, start + separatorIndex, 1, "xamlDelimiter");
        AddToken(tokens, mapper, start + separatorIndex + 1, length - separatorIndex - 1, defaultTokenType);
    }

    private static void AddToken(
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        OffsetLineMap mapper,
        int start,
        int length,
        string tokenType)
    {
        if (start < 0 || length <= 0 || start + length > mapper.Text.Length)
        {
            return;
        }

        var position = mapper.GetPosition(start);
        tokens.Add(new XamlSemanticToken(position.Line, position.Character, length, tokenType));
    }

    private static void AddMarkupExpressionSemanticTokens(
        XamlAnalysisResult analysis,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var text = analysis.Document.Text;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in analysis.XmlDocument?.Root?.DescendantsAndSelf() ?? Enumerable.Empty<System.Xml.Linq.XElement>())
        {
            foreach (var attribute in element.Attributes())
            {
                if (!XamlExpressionBindingNavigationService.TryResolveExpressionContext(
                        analysis,
                        text,
                        element,
                        attribute,
                        out var context))
                {
                    continue;
                }

                foreach (var reference in context.SymbolReferences)
                {
                    var absoluteStart = context.ExpressionStartOffset + reference.Start;
                    var tokenType = reference.Symbol switch
                    {
                        IMethodSymbol => "method",
                        ITypeSymbol => "type",
                        _ => "property"
                    };

                    AddUniqueToken(tokens, mapper, seen, absoluteStart, reference.Length, tokenType);
                }

                if (context.IsLambda)
                {
                    AddLambdaParameterTokens(context.RawExpression, context.ExpressionStartOffset, mapper, tokens, seen);
                }
            }
        }
    }

    private static void AddInlineCSharpSemanticTokens(
        ImmutableArray<XamlInlineCSharpContext> contexts,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var context in contexts)
        {
            var codeStartOffset = TextCoordinateHelper.GetOffset(mapper.Text, context.CodeRange.Start);
            var codeEndOffset = TextCoordinateHelper.GetOffset(mapper.Text, context.CodeRange.End);
            if (codeStartOffset < 0 || codeEndOffset <= codeStartOffset)
            {
                continue;
            }

            if (context.InlineCodeElement is not null && context.Attribute is null)
            {
                TokenizeCSharpExpressionSpan(mapper.Text, codeStartOffset, codeEndOffset, mapper, tokens);
            }

            foreach (var occurrence in context.SymbolOccurrences)
            {
                AddUniqueToken(
                    tokens,
                    mapper,
                    seen,
                    codeStartOffset + occurrence.Start,
                    occurrence.Length,
                    MapInlineCSharpTokenType(occurrence.TokenKind));
            }
        }
    }

    private static void AddLambdaParameterTokens(
        string rawExpression,
        int expressionStartOffset,
        OffsetLineMap mapper,
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        HashSet<string> seen)
    {
        var arrowIndex = FindLambdaArrow(rawExpression);
        if (arrowIndex <= 0)
        {
            return;
        }

        var header = rawExpression.Substring(0, arrowIndex);
        var segmentStart = 0;
        while (segmentStart < header.Length)
        {
            var commaIndex = header.IndexOf(',', segmentStart);
            var segmentEnd = commaIndex >= 0 ? commaIndex : header.Length;
            var segment = header.Substring(segmentStart, segmentEnd - segmentStart).Trim();
            if (segment.Length > 0)
            {
                if (segment[0] == '(')
                {
                    segment = segment.Substring(1).TrimStart();
                }

                if (segment.Length > 0 && segment[^1] == ')')
                {
                    segment = segment.Substring(0, segment.Length - 1).TrimEnd();
                }

                var lastSpace = segment.LastIndexOf(' ');
                var parameterName = lastSpace >= 0 && lastSpace < segment.Length - 1
                    ? segment.Substring(lastSpace + 1)
                    : segment;
                if (parameterName.Length > 0)
                {
                    var parameterOffset = header.IndexOf(parameterName, segmentStart, StringComparison.Ordinal);
                    if (parameterOffset >= 0)
                    {
                        AddUniqueToken(
                            tokens,
                            mapper,
                            seen,
                            expressionStartOffset + parameterOffset,
                            parameterName.Length,
                            "parameter");
                    }
                }
            }

            if (commaIndex < 0)
            {
                break;
            }

            segmentStart = commaIndex + 1;
        }
    }

    private static void AddUniqueToken(
        ImmutableArray<XamlSemanticToken>.Builder tokens,
        OffsetLineMap mapper,
        HashSet<string> seen,
        int start,
        int length,
        string tokenType)
    {
        var identity = start + ":" + length + ":" + tokenType;
        if (!seen.Add(identity))
        {
            return;
        }

        AddToken(tokens, mapper, start, length, tokenType);
    }

    private static string MapInlineCSharpTokenType(SourceContextSymbolTokenKind tokenKind)
    {
        return tokenKind switch
        {
            SourceContextSymbolTokenKind.Type => "type",
            SourceContextSymbolTokenKind.Method => "method",
            SourceContextSymbolTokenKind.Parameter => "parameter",
            SourceContextSymbolTokenKind.Variable => "variable",
            _ => "property"
        };
    }

    private static Dictionary<int, XamlInlineCSharpContext> BuildInlineCodeContextMap(
        string text,
        ImmutableArray<XamlInlineCSharpContext> contexts)
    {
        var map = new Dictionary<int, XamlInlineCSharpContext>();
        foreach (var context in contexts)
        {
            if (context.Attribute is null)
            {
                continue;
            }

            var startOffset = TextCoordinateHelper.GetOffset(text, context.CodeRange.Start);
            if (startOffset < 0)
            {
                continue;
            }

            map[startOffset] = context;
        }

        return map;
    }

    private static ImmutableArray<OffsetRange> BuildInlineElementCodeRanges(
        string text,
        ImmutableArray<XamlInlineCSharpContext> contexts)
    {
        var builder = ImmutableArray.CreateBuilder<OffsetRange>();
        foreach (var context in contexts)
        {
            if (context.InlineCodeElement is null || context.Attribute is not null)
            {
                continue;
            }

            var startOffset = TextCoordinateHelper.GetOffset(text, context.CodeRange.Start);
            var endOffset = TextCoordinateHelper.GetOffset(text, context.CodeRange.End);
            if (startOffset < 0 || endOffset <= startOffset)
            {
                continue;
            }

            builder.Add(new OffsetRange(startOffset, endOffset));
        }

        return builder
            .ToImmutable()
            .OrderBy(static range => range.Start)
            .ToImmutableArray();
    }

    private static bool IsNumericToken(string text, int start, int length)
    {
        var span = text.AsSpan(start, length);
        var hasDigit = false;
        for (var index = 0; index < span.Length; index++)
        {
            var value = span[index];
            if (char.IsDigit(value))
            {
                hasDigit = true;
                continue;
            }

            if (value is '+' or '-' or '.' or ',' or '%' or 'e' or 'E')
            {
                continue;
            }

            return false;
        }

        return hasDigit;
    }

    private static int ScanStringLiteral(string text, int start, int end)
    {
        var quote = text[start];
        var index = start + 1;
        while (index < end)
        {
            if (text[index] == quote && !IsEscaped(text, index))
            {
                return index + 1;
            }

            index++;
        }

        return end;
    }

    private static int ScanNumericLiteral(string text, int start, int end)
    {
        var index = start + 1;
        while (index < end)
        {
            var current = text[index];
            if (!(char.IsLetterOrDigit(current) || current is '.' or '_' or '+' or '-'))
            {
                break;
            }

            index++;
        }

        return index;
    }

    private static int GetOperatorLength(string text, int index, int end)
    {
        if (index + 1 < end)
        {
            var twoChars = text.Substring(index, 2);
            if (twoChars is "=>" or "&&" or "||" or "==" or "!=" or "<=" or ">=" or "??" or "?." or "++" or "--")
            {
                return 2;
            }
        }

        return text[index] is '+' or '-' or '*' or '/' or '%' or '<' or '>' or '?' or ':' or '!' or '.' or ',' or '(' or ')' or '[' or ']'
            ? 1
            : 0;
    }

    private static int FindLambdaArrow(string expressionText)
    {
        var inSingleQuotedString = false;
        var inDoubleQuotedString = false;
        for (var index = 0; index + 1 < expressionText.Length; index++)
        {
            var current = expressionText[index];
            if (!inDoubleQuotedString &&
                current == '\'' &&
                !IsEscaped(expressionText, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (!inSingleQuotedString &&
                current == '"' &&
                !IsEscaped(expressionText, index))
            {
                inDoubleQuotedString = !inDoubleQuotedString;
                continue;
            }

            if (!inSingleQuotedString &&
                !inDoubleQuotedString &&
                current == '=' &&
                expressionText[index + 1] == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsEscaped(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        var slashCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    private static int ReadMarkupExtensionIdentifierLength(string text, int start, int endExclusive)
    {
        if (start < 0 || start >= endExclusive || start >= text.Length)
        {
            return 0;
        }

        var index = start;
        while (index < endExclusive && index < text.Length)
        {
            var ch = text[index];
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-'))
            {
                break;
            }

            index++;
        }

        return index - start;
    }

    private static int IndexOf(string text, int start, int endExclusive, char value)
    {
        for (var index = start; index < endExclusive; index++)
        {
            if (text[index] == value)
            {
                return index;
            }
        }

        return -1;
    }

    private static void TrimRange(string text, ref int start, ref int endExclusive)
    {
        while (start < endExclusive && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (endExclusive > start && char.IsWhiteSpace(text[endExclusive - 1]))
        {
            endExclusive--;
        }
    }

    private static int ReadIdentifierLength(string text, int start)
    {
        if (start < 0 || start >= text.Length)
        {
            return 0;
        }

        var length = 0;
        var index = start;
        while (index < text.Length)
        {
            var ch = text[index];
            if (!(char.IsLetterOrDigit(ch) || ch is '_' or ':' or '.' or '-'))
            {
                break;
            }

            length++;
            index++;
        }

        return length;
    }

    private sealed class OffsetLineMap
    {
        private readonly ImmutableArray<int> _lineStartOffsets;

        public OffsetLineMap(string text)
        {
            Text = text;

            var starts = ImmutableArray.CreateBuilder<int>();
            starts.Add(0);
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] == '\n')
                {
                    starts.Add(index + 1);
                }
            }

            _lineStartOffsets = starts.ToImmutable();
        }

        public string Text { get; }

        public (int Line, int Character) GetPosition(int offset)
        {
            if (offset <= 0)
            {
                return (0, 0);
            }

            var boundedOffset = Math.Min(offset, Text.Length);
            var lineIndex = _lineStartOffsets.BinarySearch(boundedOffset);
            if (lineIndex < 0)
            {
                lineIndex = ~lineIndex - 1;
            }

            var lineStart = _lineStartOffsets[Math.Max(0, lineIndex)];
            return (lineIndex, boundedOffset - lineStart);
        }
    }
}
