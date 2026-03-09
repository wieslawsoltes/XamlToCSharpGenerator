using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlExpressionCompletionService
{
    private const string CompletionMarker = "__AXSGCompletion__";

    public static bool TryGetCompletions(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableArray<XamlCompletionItem> completions)
    {
        completions = ImmutableArray<XamlCompletionItem>.Empty;
        if (!TryFindCompletionExpressionContext(
                analysis,
                position,
                out var element,
                out var attribute,
                out _,
                out var expressionInfo,
                out var caretOffsetInExpression))
        {
            return false;
        }

        if (TryResolveForcedShorthandCompletion(
                analysis,
                element,
                expressionInfo.RawExpression,
                caretOffsetInExpression,
                out var forcedReceiverType,
                out var forcedMemberPrefix))
        {
            completions = XamlClrMemberCompletionFactory.CreateMemberCompletions(
                    forcedReceiverType,
                    forcedMemberPrefix,
                    XamlMemberCompletionMode.Expression)
                .DistinctBy(static item => item.Label, StringComparer.Ordinal)
                .ToImmutableArray();
            return completions.Length > 0;
        }

        if (!TryResolveExpressionSourceType(analysis, element, expressionInfo.RawExpression, out var sourceType) ||
            !TryResolveCompletionExpression(
                expressionInfo,
                caretOffsetInExpression,
                out var completionExpression,
                out var completionCaretOffset,
                out var isLambdaExpression) ||
            !TryResolveExpressionReceiverType(
                analysis,
                sourceType,
                completionExpression,
                completionCaretOffset,
                out var receiverType,
                out var memberPrefix))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>();
        if (isLambdaExpression)
        {
            AddLambdaParameterCompletions(builder, expressionInfo.RawExpression, memberPrefix);
        }

        builder.AddRange(XamlClrMemberCompletionFactory.CreateMemberCompletions(
            receiverType,
            memberPrefix,
            XamlMemberCompletionMode.Expression));
        completions = builder
            .ToImmutable()
            .DistinctBy(static item => item.Label, StringComparer.Ordinal)
            .ToImmutableArray();
        return completions.Length > 0;
    }

    private static bool TryFindCompletionExpressionContext(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out SourceRange attributeValueRange,
        out XamlCSharpMarkupExpressionInfo expressionInfo,
        out int caretOffsetInExpression)
    {
        if (XamlCSharpMarkupExpressionService.TryFindMarkupExpressionAttributeContext(
                analysis,
                position,
                out element,
                out attribute,
                out attributeValueRange,
                out expressionInfo,
                out caretOffsetInExpression))
        {
            return true;
        }

        return TryFindEmptyExplicitExpressionContext(
            analysis,
            position,
            out element,
            out attribute,
            out attributeValueRange,
            out expressionInfo,
            out caretOffsetInExpression);
    }

    private static bool TryFindEmptyExplicitExpressionContext(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out SourceRange attributeValueRange,
        out XamlCSharpMarkupExpressionInfo expressionInfo,
        out int caretOffsetInExpression)
    {
        element = null!;
        attribute = null!;
        attributeValueRange = default;
        expressionInfo = default;
        caretOffsetInExpression = -1;

        if (analysis.XmlDocument?.Root is null)
        {
            return false;
        }

        var sourceText = analysis.Document.Text;
        var absoluteOffset = TextCoordinateHelper.GetOffset(sourceText, position);
        if (absoluteOffset < 0)
        {
            return false;
        }

        foreach (var candidateElement in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var candidateAttribute in candidateElement.Attributes())
            {
                if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(
                        sourceText,
                        candidateAttribute,
                        out var valueRange))
                {
                    continue;
                }

                var valueStart = TextCoordinateHelper.GetOffset(sourceText, valueRange.Start);
                var valueEnd = TextCoordinateHelper.GetOffset(sourceText, valueRange.End);
                if (valueStart < 0 || valueEnd < valueStart || absoluteOffset < valueStart || absoluteOffset > valueEnd)
                {
                    continue;
                }

                var attributeValue = candidateAttribute.Value;
                if (!TryGetEmptyExplicitExpressionSpan(attributeValue, out var expressionStartInValue, out var expressionEndInValue))
                {
                    continue;
                }

                var expressionStartOffset = valueStart + expressionStartInValue;
                var expressionEndOffset = valueStart + expressionEndInValue;
                if (absoluteOffset < expressionStartOffset || absoluteOffset > expressionEndOffset)
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                attributeValueRange = valueRange;
                expressionInfo = new XamlCSharpMarkupExpressionInfo(
                    RawExpression: string.Empty,
                    NormalizedExpression: string.Empty,
                    ExpressionStartOffset: expressionStartOffset,
                    ExpressionLength: 0,
                    IsExplicitExpression: true,
                    Kind: XamlCSharpMarkupExpressionKind.Expression);
                caretOffsetInExpression = 0;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEmptyExplicitExpressionSpan(
        string attributeValue,
        out int expressionStartInValue,
        out int expressionEndInValue)
    {
        expressionStartInValue = 0;
        expressionEndInValue = 0;

        if (string.IsNullOrWhiteSpace(attributeValue))
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

        if (trimmedEnd - trimmedStart < 3 ||
            attributeValue[trimmedStart] != '{' ||
            attributeValue[trimmedEnd - 1] != '}')
        {
            return false;
        }

        var cursor = trimmedStart + 1;
        while (cursor < trimmedEnd - 1 && char.IsWhiteSpace(attributeValue[cursor]))
        {
            cursor++;
        }

        if (cursor >= trimmedEnd - 1 || attributeValue[cursor] != '=')
        {
            return false;
        }

        cursor++;
        while (cursor < trimmedEnd - 1 && char.IsWhiteSpace(attributeValue[cursor]))
        {
            cursor++;
        }

        if (cursor != trimmedEnd - 1)
        {
            return false;
        }

        expressionStartInValue = cursor;
        expressionEndInValue = cursor;
        return true;
    }

    private static bool TryResolveExpressionSourceType(
        XamlAnalysisResult analysis,
        XElement element,
        string rawExpression,
        out INamedTypeSymbol sourceType)
    {
        sourceType = null!;
        if (rawExpression.StartsWith("this.", StringComparison.Ordinal))
        {
            return TryResolveRootType(analysis, out sourceType);
        }

        if (rawExpression.StartsWith(".", StringComparison.Ordinal) ||
            rawExpression.StartsWith("BindingContext.", StringComparison.Ordinal))
        {
            return XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(
                analysis,
                element,
                out sourceType,
                out _);
        }

        return XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(
                   analysis,
                   element,
                    out sourceType,
                   out _) ||
               TryResolveRootType(analysis, out sourceType);
    }

    private static bool TryResolveForcedShorthandCompletion(
        XamlAnalysisResult analysis,
        XElement element,
        string rawExpression,
        int caretOffsetInExpression,
        out INamedTypeSymbol receiverType,
        out string memberPrefix)
    {
        receiverType = null!;
        memberPrefix = string.Empty;

        if (rawExpression.StartsWith("this.", StringComparison.Ordinal))
        {
            if (!TryResolveRootType(analysis, out receiverType))
            {
                return false;
            }

            memberPrefix = ExtractForcedMemberPrefix(rawExpression, "this.".Length, caretOffsetInExpression);
            return true;
        }

        if (rawExpression.StartsWith("BindingContext.", StringComparison.Ordinal))
        {
            if (!XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(
                    analysis,
                    element,
                    out receiverType,
                    out _))
            {
                return false;
            }

            memberPrefix = ExtractForcedMemberPrefix(rawExpression, "BindingContext.".Length, caretOffsetInExpression);
            return true;
        }

        if (rawExpression.StartsWith(".", StringComparison.Ordinal))
        {
            if (!XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(
                    analysis,
                    element,
                    out receiverType,
                    out _))
            {
                return false;
            }

            memberPrefix = ExtractForcedMemberPrefix(rawExpression, 1, caretOffsetInExpression);
            return true;
        }

        return false;
    }

    private static string ExtractForcedMemberPrefix(
        string rawExpression,
        int prefixLength,
        int caretOffsetInExpression)
    {
        var boundedCaret = Math.Clamp(caretOffsetInExpression, prefixLength, rawExpression.Length);
        var prefix = rawExpression.Substring(prefixLength, boundedCaret - prefixLength);
        return prefix.Trim();
    }

    private static bool TryResolveRootType(
        XamlAnalysisResult analysis,
        out INamedTypeSymbol sourceType)
    {
        sourceType = null!;
        var classFullName = analysis.ParsedDocument?.ClassFullName;
        if (string.IsNullOrWhiteSpace(classFullName))
        {
            return false;
        }

        var resolvedType = XamlSemanticSourceTypeResolver.ResolveTypeSymbolByFullTypeName(
            analysis.Compilation,
            classFullName);
        if (resolvedType is null)
        {
            return false;
        }

        sourceType = resolvedType;
        return true;
    }

    private static bool TryResolveCompletionExpression(
        XamlCSharpMarkupExpressionInfo expressionInfo,
        int caretOffsetInExpression,
        out string completionExpression,
        out int completionCaretOffset,
        out bool isLambdaExpression)
    {
        completionExpression = expressionInfo.RawExpression;
        completionCaretOffset = Math.Clamp(caretOffsetInExpression, 0, completionExpression.Length);
        isLambdaExpression = expressionInfo.Kind == XamlCSharpMarkupExpressionKind.Lambda;

        if (!isLambdaExpression)
        {
            return true;
        }

        var arrowIndex = FindLambdaArrow(expressionInfo.RawExpression);
        if (arrowIndex < 0)
        {
            return false;
        }

        var bodyStart = arrowIndex + 2;
        while (bodyStart < expressionInfo.RawExpression.Length &&
               char.IsWhiteSpace(expressionInfo.RawExpression[bodyStart]))
        {
            bodyStart++;
        }

        if (caretOffsetInExpression <= bodyStart)
        {
            completionExpression = string.Empty;
            completionCaretOffset = 0;
            return true;
        }

        completionExpression = expressionInfo.RawExpression.Substring(bodyStart);
        completionCaretOffset = Math.Clamp(caretOffsetInExpression - bodyStart, 0, completionExpression.Length);
        return true;
    }

    private static bool TryResolveExpressionReceiverType(
        XamlAnalysisResult analysis,
        INamedTypeSymbol sourceType,
        string expressionText,
        int caretOffsetInExpression,
        out INamedTypeSymbol receiverType,
        out string memberPrefix)
    {
        receiverType = sourceType;
        memberPrefix = string.Empty;

        if (string.IsNullOrEmpty(expressionText))
        {
            return true;
        }

        if (!TryCreateCompletionExpression(expressionText, caretOffsetInExpression, out memberPrefix, out var completionExpression))
        {
            return false;
        }

        if (memberPrefix.Length > 0 && !IsIdentifierPrefix(memberPrefix))
        {
            return false;
        }

        if (!TryResolveReceiverExpressionText(completionExpression, out var receiverExpression))
        {
            receiverType = sourceType;
            return true;
        }

        if (!TryResolveReceiverExpressionType(
                analysis.Compilation,
                sourceType,
                receiverExpression,
                out receiverType))
        {
            receiverType = sourceType;
        }

        return true;
    }

    private static bool TryResolveReceiverExpressionType(
        Compilation? compilation,
        INamedTypeSymbol sourceType,
        string receiverExpression,
        out INamedTypeSymbol receiverType)
    {
        receiverType = sourceType;
        if (compilation is null ||
            string.IsNullOrWhiteSpace(receiverExpression) ||
            !CSharpSourceContextExpressionAnalysisService.TryAnalyze(
                compilation,
                sourceType,
                receiverExpression,
                "source",
                out var analysisResult,
                out _))
        {
            return false;
        }

        receiverType = analysisResult.ResultTypeSymbol as INamedTypeSymbol ?? sourceType;
        return analysisResult.ResultTypeSymbol is INamedTypeSymbol;
    }

    private static bool TryCreateCompletionExpression(
        string expressionText,
        int caretOffsetInExpression,
        out string memberPrefix,
        out string completionExpression)
    {
        memberPrefix = string.Empty;
        completionExpression = string.Empty;
        if (caretOffsetInExpression < 0 || caretOffsetInExpression > expressionText.Length)
        {
            return false;
        }

        var prefixStart = caretOffsetInExpression;
        while (prefixStart > 0 && MiniLanguageSyntaxFacts.IsIdentifierPart(expressionText[prefixStart - 1]))
        {
            prefixStart--;
        }

        memberPrefix = expressionText.Substring(prefixStart, caretOffsetInExpression - prefixStart);
        completionExpression = expressionText.Substring(0, prefixStart) +
                               CompletionMarker +
                               expressionText.Substring(caretOffsetInExpression);

        completionExpression = AppendSyntheticClosures(completionExpression);
        return completionExpression.Length > 0;
    }

    private static string AppendSyntheticClosures(string expressionText)
    {
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < expressionText.Length; index++)
        {
            var ch = expressionText[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    break;
            }
        }

        var builder = new System.Text.StringBuilder(expressionText);
        while (bracketDepth-- > 0)
        {
            builder.Append(']');
        }

        while (parenthesisDepth-- > 0)
        {
            builder.Append(')');
        }

        while (braceDepth-- > 0)
        {
            builder.Append('}');
        }

        return builder.ToString();
    }

    private static void AddLambdaParameterCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        string rawExpression,
        string memberPrefix)
    {
        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            return;
        }

        var arrowIndex = FindLambdaArrow(rawExpression);
        if (arrowIndex <= 0)
        {
            return;
        }

        var header = rawExpression.Substring(0, arrowIndex).Trim();
        if (header.Length == 0)
        {
            return;
        }

        if (header[0] == '(' && header[^1] == ')' && header.Length >= 2)
        {
            header = header.Substring(1, header.Length - 2);
        }

        foreach (var parameterToken in header.Split(','))
        {
            var candidate = parameterToken.Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            var lastSpace = candidate.LastIndexOf(' ');
            if (lastSpace >= 0 && lastSpace < candidate.Length - 1)
            {
                candidate = candidate.Substring(lastSpace + 1);
            }

            if (!IsIdentifierPrefix(candidate))
            {
                continue;
            }

            builder.Add(new XamlCompletionItem(
                candidate,
                candidate,
                XamlCompletionItemKind.Property,
                "lambda parameter"));
        }
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
                !IsEscapedChar(expressionText, index))
            {
                inSingleQuotedString = !inSingleQuotedString;
                continue;
            }

            if (!inSingleQuotedString &&
                current == '"' &&
                !IsEscapedChar(expressionText, index))
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

    private static bool IsEscapedChar(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        var escapeCount = 0;
        for (var current = index - 1; current >= 0 && text[current] == '\\'; current--)
        {
            escapeCount++;
        }

        return escapeCount % 2 == 1;
    }

    private static bool IsIdentifierPrefix(string prefix)
    {
        for (var index = 0; index < prefix.Length; index++)
        {
            if (!MiniLanguageSyntaxFacts.IsIdentifierPart(prefix[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveReceiverExpressionText(string completionExpression, out string receiverExpression)
    {
        receiverExpression = string.Empty;
        var parsedExpression = SyntaxFactory.ParseExpression(completionExpression);
        if (parsedExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault(static identifier =>
                string.Equals(identifier.Identifier.ValueText, CompletionMarker, StringComparison.Ordinal)) is not { } markerIdentifier)
        {
            return false;
        }

        if (markerIdentifier.Parent is MemberAccessExpressionSyntax memberAccess &&
            ReferenceEquals(memberAccess.Name, markerIdentifier))
        {
            receiverExpression = memberAccess.Expression.ToFullString().Trim();
            return receiverExpression.Length > 0;
        }

        if (markerIdentifier.Parent is MemberBindingExpressionSyntax &&
            markerIdentifier.Parent.Parent is ConditionalAccessExpressionSyntax conditionalAccess)
        {
            receiverExpression = conditionalAccess.Expression.ToFullString().Trim();
            return receiverExpression.Length > 0;
        }

        return false;
    }
}
