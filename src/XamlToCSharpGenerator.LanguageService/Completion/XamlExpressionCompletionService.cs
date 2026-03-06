using System;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Models;
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
        if (!TryFindExpressionAttributeContext(analysis, position, out var element, out var expressionText, out var caretOffsetInExpression) ||
            !XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(analysis, element, out var sourceType, out _) ||
            !TryResolveExpressionReceiverType(analysis, sourceType, expressionText, caretOffsetInExpression, out var receiverType, out var memberPrefix))
        {
            return false;
        }

        completions = XamlClrMemberCompletionFactory.CreateMemberCompletions(
            receiverType,
            memberPrefix,
            XamlMemberCompletionMode.Expression);
        return completions.Length > 0;
    }

    private static bool TryFindExpressionAttributeContext(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out string expressionText,
        out int caretOffsetInExpression)
    {
        element = null!;
        expressionText = string.Empty;
        caretOffsetInExpression = -1;
        if (analysis.XmlDocument?.Root is null)
        {
            return false;
        }

        var absoluteOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (absoluteOffset < 0)
        {
            return false;
        }

        foreach (var candidateElement in analysis.XmlDocument.Root.DescendantsAndSelf())
        {
            foreach (var candidateAttribute in candidateElement.Attributes())
            {
                if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(
                        analysis.Document.Text,
                        candidateAttribute,
                        out var valueRange))
                {
                    continue;
                }

                var valueStart = TextCoordinateHelper.GetOffset(analysis.Document.Text, valueRange.Start);
                var valueEnd = TextCoordinateHelper.GetOffset(analysis.Document.Text, valueRange.End);
                if (valueStart < 0 || valueEnd < valueStart || absoluteOffset < valueStart || absoluteOffset > valueEnd)
                {
                    continue;
                }

                var caretOffsetInValue = absoluteOffset - valueStart;
                if (!TryExtractExplicitExpression(candidateAttribute.Value, caretOffsetInValue, out expressionText, out _, out caretOffsetInExpression))
                {
                    continue;
                }

                element = candidateElement;
                return true;
            }
        }

        return false;
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

    private static bool TryExtractExplicitExpression(
        string attributeValue,
        int caretOffsetInValue,
        out string expressionText,
        out int expressionStartInValue,
        out int caretOffsetInExpression)
    {
        expressionText = string.Empty;
        expressionStartInValue = 0;
        caretOffsetInExpression = -1;
        if (string.IsNullOrWhiteSpace(attributeValue))
        {
            return false;
        }

        var trimmedStart = 0;
        while (trimmedStart < attributeValue.Length && char.IsWhiteSpace(attributeValue[trimmedStart]))
        {
            trimmedStart++;
        }

        if (trimmedStart + 1 >= attributeValue.Length ||
            attributeValue[trimmedStart] != '{' ||
            attributeValue[trimmedStart + 1] != '=')
        {
            return false;
        }

        expressionStartInValue = trimmedStart + 2;
        while (expressionStartInValue < attributeValue.Length && char.IsWhiteSpace(attributeValue[expressionStartInValue]))
        {
            expressionStartInValue++;
        }

        var trimmedEnd = attributeValue.Length;
        while (trimmedEnd > expressionStartInValue && char.IsWhiteSpace(attributeValue[trimmedEnd - 1]))
        {
            trimmedEnd--;
        }

        if (trimmedEnd > expressionStartInValue && attributeValue[trimmedEnd - 1] == '}')
        {
            trimmedEnd--;
        }

        if (caretOffsetInValue < expressionStartInValue || caretOffsetInValue > Math.Max(expressionStartInValue, trimmedEnd))
        {
            return false;
        }

        expressionText = attributeValue.Substring(expressionStartInValue, Math.Max(0, trimmedEnd - expressionStartInValue));
        caretOffsetInExpression = Math.Clamp(caretOffsetInValue - expressionStartInValue, 0, expressionText.Length);
        return true;
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
