using System;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Parsing;

internal enum XamlCSharpMarkupExpressionKind
{
    Expression = 0,
    Lambda = 1
}

internal readonly record struct XamlCSharpMarkupExpressionInfo(
    string RawExpression,
    string NormalizedExpression,
    int ExpressionStartOffset,
    int ExpressionLength,
    bool IsExplicitExpression,
    XamlCSharpMarkupExpressionKind Kind);

internal static class XamlCSharpMarkupExpressionService
{
    public static bool IsCSharpMarkupExpression(string attributeValue)
    {
        return TryParseMarkupExpression(attributeValue, 0, out _);
    }

    public static bool TryFindMarkupExpressionAttributeContext(
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

                if (!TryParseMarkupExpression(
                        analysis,
                        candidateElement,
                        candidateAttribute,
                        valueStart,
                        out var candidateExpressionInfo))
                {
                    continue;
                }

                var expressionEndOffset = candidateExpressionInfo.ExpressionStartOffset + candidateExpressionInfo.ExpressionLength;
                if (absoluteOffset < candidateExpressionInfo.ExpressionStartOffset ||
                    absoluteOffset > expressionEndOffset)
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                attributeValueRange = valueRange;
                expressionInfo = candidateExpressionInfo;
                caretOffsetInExpression = Math.Clamp(
                    absoluteOffset - candidateExpressionInfo.ExpressionStartOffset,
                    0,
                    candidateExpressionInfo.RawExpression.Length);
                return true;
            }
        }

        return false;
    }

    public static bool TryParseMarkupExpression(
        XamlAnalysisResult analysis,
        XElement element,
        XAttribute attribute,
        int valueStartOffset,
        out XamlCSharpMarkupExpressionInfo expressionInfo)
    {
        return TryParseMarkupExpression(
            attribute.Value,
            valueStartOffset,
            innerExpression => LooksLikeMarkupExtensionStart(analysis, element, attribute.Value, valueStartOffset),
            out expressionInfo);
    }

    public static bool TryParseMarkupExpression(
        string attributeValue,
        int valueStartOffset,
        out XamlCSharpMarkupExpressionInfo expressionInfo)
    {
        return TryParseMarkupExpression(
            attributeValue,
            valueStartOffset,
            _ => LooksLikeMarkupExtensionStart(attributeValue, valueStartOffset),
            out expressionInfo);
    }

    private static bool TryParseMarkupExpression(
        string attributeValue,
        int valueStartOffset,
        Func<string, bool> looksLikeMarkupExtensionStart,
        out XamlCSharpMarkupExpressionInfo expressionInfo)
    {
        expressionInfo = default;
        if (string.IsNullOrWhiteSpace(attributeValue))
        {
            return false;
        }

        if (!TryGetInnerSpan(attributeValue, out var innerStart, out var innerEnd))
        {
            return false;
        }

        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                attributeValue,
                implicitExpressionsEnabled: true,
                looksLikeMarkupExtensionStart,
                out var rawExpression,
                out var isExplicitExpression,
                out var isLambdaExpression))
        {
            return false;
        }

        var expressionStartInValue = innerStart;
        if (isExplicitExpression)
        {
            expressionStartInValue++;
            while (expressionStartInValue < innerEnd && char.IsWhiteSpace(attributeValue[expressionStartInValue]))
            {
                expressionStartInValue++;
            }
        }

        while (expressionStartInValue < innerEnd && char.IsWhiteSpace(attributeValue[expressionStartInValue]))
        {
            expressionStartInValue++;
        }

        if (expressionStartInValue >= innerEnd)
        {
            return false;
        }

        var expressionEndInValue = innerEnd;
        while (expressionEndInValue > expressionStartInValue && char.IsWhiteSpace(attributeValue[expressionEndInValue - 1]))
        {
            expressionEndInValue--;
        }

        if (expressionEndInValue <= expressionStartInValue)
        {
            return false;
        }

        expressionInfo = new XamlCSharpMarkupExpressionInfo(
            RawExpression: rawExpression,
            NormalizedExpression: CSharpExpressionTextSemantics.NormalizeExpressionCode(rawExpression),
            ExpressionStartOffset: valueStartOffset + expressionStartInValue,
            ExpressionLength: expressionEndInValue - expressionStartInValue,
            IsExplicitExpression: isExplicitExpression,
            Kind: isLambdaExpression ? XamlCSharpMarkupExpressionKind.Lambda : XamlCSharpMarkupExpressionKind.Expression);
        return true;
    }

    private static bool TryGetInnerSpan(string attributeValue, out int innerStart, out int innerEnd)
    {
        innerStart = 0;
        innerEnd = 0;

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

        innerStart = trimmedStart + 1;
        innerEnd = trimmedEnd - 1;
        while (innerStart < innerEnd && char.IsWhiteSpace(attributeValue[innerStart]))
        {
            innerStart++;
        }

        while (innerEnd > innerStart && char.IsWhiteSpace(attributeValue[innerEnd - 1]))
        {
            innerEnd--;
        }

        return innerEnd > innerStart;
    }

    private static bool LooksLikeMarkupExtensionStart(string attributeValue, int valueStartOffset)
    {
        if (!XamlMarkupExtensionSpanParser.TryParse(attributeValue, valueStartOffset, out var markupSpanInfo))
        {
            return false;
        }

        if (markupSpanInfo.ExtensionName.IndexOf(':') >= 0)
        {
            return true;
        }

        return XamlMarkupExtensionNameSemantics.Classify(markupSpanInfo.ExtensionName) != XamlMarkupExtensionKind.Unknown;
    }

    private static bool LooksLikeMarkupExtensionStart(
        XamlAnalysisResult analysis,
        XElement element,
        string attributeValue,
        int valueStartOffset)
    {
        if (!XamlMarkupExtensionSpanParser.TryParse(attributeValue, valueStartOffset, out var markupSpanInfo))
        {
            return false;
        }

        if (markupSpanInfo.ExtensionName.IndexOf(':') >= 0)
        {
            return true;
        }

        if (XamlMarkupExtensionNameSemantics.Classify(markupSpanInfo.ExtensionName) != XamlMarkupExtensionKind.Unknown)
        {
            return true;
        }

        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        return XamlMarkupExtensionNavigationSemantics.TryResolveExtensionTypeReference(
            analysis,
            prefixMap,
            markupSpanInfo.ExtensionName,
            out _);
    }
}
