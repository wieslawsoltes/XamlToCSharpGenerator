using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

public static class SelectorBranchTokenizer
{
    public const string TemplateAxisToken = "/template/";

    public static bool TryTokenize(string selectorBranch, out ImmutableArray<SelectorBranchSegment> segments)
    {
        return TryTokenize(selectorBranch, out segments, out _, out _);
    }

    public static bool TryTokenize(
        string selectorBranch,
        out ImmutableArray<SelectorBranchSegment> segments,
        out string errorMessage,
        out int errorOffset)
    {
        segments = ImmutableArray<SelectorBranchSegment>.Empty;
        errorMessage = string.Empty;
        errorOffset = 0;
        if (string.IsNullOrWhiteSpace(selectorBranch))
        {
            errorMessage = "Unexpected end of selector.";
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<SelectorBranchSegment>();
        var index = 0;
        var hasSegment = false;
        var pendingCombinator = SelectorCombinatorKind.None;

        while (index < selectorBranch.Length)
        {
            var consumedWhitespace = false;
            while (index < selectorBranch.Length && char.IsWhiteSpace(selectorBranch[index]))
            {
                consumedWhitespace = true;
                index++;
            }

            var pendingDescendantCombinator = consumedWhitespace &&
                                              hasSegment &&
                                              pendingCombinator == SelectorCombinatorKind.None;

            if (index >= selectorBranch.Length)
            {
                break;
            }

            if (selectorBranch[index] == '>')
            {
                if (!hasSegment || pendingCombinator != SelectorCombinatorKind.None)
                {
                    errorMessage = "Unexpected combinator in selector.";
                    errorOffset = index;
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Child;
                index++;
                continue;
            }

            if (IsTemplateAxisAt(selectorBranch, index))
            {
                if (!hasSegment || pendingCombinator != SelectorCombinatorKind.None)
                {
                    errorMessage = "Unexpected template axis in selector.";
                    errorOffset = index;
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Template;
                index += TemplateAxisToken.Length;
                continue;
            }

            if (pendingDescendantCombinator &&
                pendingCombinator == SelectorCombinatorKind.None)
            {
                pendingCombinator = SelectorCombinatorKind.Descendant;
            }

            var segmentStart = index;
            var bracketDepth = 0;
            var parenthesisDepth = 0;
            var inQuote = false;
            var quoteChar = '\0';

            while (index < selectorBranch.Length)
            {
                var ch = selectorBranch[index];
                if (inQuote)
                {
                    if (ch == quoteChar)
                    {
                        inQuote = false;
                    }

                    index++;
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inQuote = true;
                    quoteChar = ch;
                    index++;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    index++;
                    continue;
                }

                if (ch == ']')
                {
                    if (bracketDepth == 0)
                    {
                        break;
                    }

                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    index++;
                    continue;
                }

                if (ch == '(')
                {
                    parenthesisDepth++;
                    index++;
                    continue;
                }

                if (ch == ')')
                {
                    if (parenthesisDepth == 0)
                    {
                        break;
                    }

                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    index++;
                    continue;
                }

                if (bracketDepth == 0 &&
                    parenthesisDepth == 0 &&
                    (char.IsWhiteSpace(ch) || ch == '>' || IsTemplateAxisAt(selectorBranch, index)))
                {
                    break;
                }

                index++;
            }

            if (inQuote)
            {
                errorMessage = "Unterminated string literal in selector.";
                errorOffset = segmentStart;
                return false;
            }

            if (bracketDepth != 0 || parenthesisDepth != 0)
            {
                errorMessage = "Unbalanced selector predicate or pseudo-function.";
                errorOffset = segmentStart;
                return false;
            }

            var segmentText = selectorBranch.Substring(segmentStart, index - segmentStart).Trim();
            if (segmentText.Length == 0)
            {
                errorMessage = "Unexpected empty selector segment.";
                errorOffset = segmentStart;
                return false;
            }

            var textStart = segmentStart;
            while (textStart < index && char.IsWhiteSpace(selectorBranch[textStart]))
            {
                textStart++;
            }

            var textEnd = index;
            while (textEnd > textStart && char.IsWhiteSpace(selectorBranch[textEnd - 1]))
            {
                textEnd--;
            }

            builder.Add(new SelectorBranchSegment(
                segmentText,
                pendingCombinator,
                textStart,
                textEnd - textStart));
            pendingCombinator = SelectorCombinatorKind.None;
            hasSegment = true;
        }

        if (builder.Count == 0)
        {
            errorMessage = "Unexpected end of selector.";
            return false;
        }

        if (pendingCombinator != SelectorCombinatorKind.None)
        {
            errorMessage = "Unexpected end of selector.";
            errorOffset = selectorBranch.Length;
            return false;
        }

        segments = builder.ToImmutable();
        return true;
    }

    public static bool TryReadTypeToken(string segmentText, ref int index, out string? typeToken)
    {
        typeToken = null;
        if (string.IsNullOrEmpty(segmentText))
        {
            return true;
        }

        while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
        {
            index++;
        }

        if (index >= segmentText.Length || !MiniLanguageSyntaxFacts.IsIdentifierStart(segmentText[index]))
        {
            return true;
        }

        var aliasStart = index;
        index++;
        while (index < segmentText.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(segmentText[index]))
        {
            index++;
        }

        var aliasOrType = segmentText.Substring(aliasStart, index - aliasStart);
        if (index < segmentText.Length && segmentText[index] == '|')
        {
            index++;
            if (index >= segmentText.Length || !MiniLanguageSyntaxFacts.IsIdentifierStart(segmentText[index]))
            {
                return false;
            }

            var typeStart = index;
            index++;
            while (index < segmentText.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(segmentText[index]))
            {
                index++;
            }

            var typeName = segmentText.Substring(typeStart, index - typeStart);
            typeToken = aliasOrType + ":" + typeName;
            return true;
        }

        typeToken = aliasOrType;
        return true;
    }

    public static bool IsTemplateAxisAt(string text, int index)
    {
        if (string.IsNullOrEmpty(text) || index < 0)
        {
            return false;
        }

        if (index + TemplateAxisToken.Length > text.Length)
        {
            return false;
        }

        for (var tokenIndex = 0; tokenIndex < TemplateAxisToken.Length; tokenIndex++)
        {
            if (text[index + tokenIndex] != TemplateAxisToken[tokenIndex])
            {
                return false;
            }
        }

        return true;
    }
}
