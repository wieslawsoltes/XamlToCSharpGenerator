using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal readonly record struct XamlExpressionNavigationTarget(
    SourceRange UsageRange,
    ISymbol Symbol);

internal readonly record struct MappedExpressionSymbolReference(
    ISymbol Symbol,
    int Start,
    int Length);

internal readonly record struct XamlExpressionInlayHintTarget(
    SourceRange HintAnchorRange,
    string Expression,
    string SourceTypeName,
    string ResultTypeName,
    AvaloniaSymbolSourceLocation? ResultTypeLocation);

internal static class XamlExpressionBindingNavigationService
{
    public static bool IsExplicitExpressionMarkup(string attributeValue)
    {
        return TryParseExplicitExpressionMarkup(attributeValue, 0, out _, out _, out _);
    }

    public static bool TryResolveNavigationTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlExpressionNavigationTarget target)
    {
        target = default;
        var documentOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (!TryFindExpressionAttributeAtPosition(analysis, position, out var element, out var attribute, out var attributeValueRange) ||
            !TryCreateExpressionContext(analysis, analysis.Document.Text, element, attribute, attributeValueRange, out var context))
        {
            return false;
        }

        foreach (var reference in context.SymbolReferences)
        {
            var start = context.ExpressionStartOffset + reference.Start;
            var end = start + reference.Length;
            if (documentOffset < start || documentOffset > end)
            {
                continue;
            }

            target = new XamlExpressionNavigationTarget(
                CreateRange(context.SourceText, start, reference.Length),
                reference.Symbol);
            return true;
        }

        return false;
    }

    public static ImmutableArray<SourceRange> FindReferenceRanges(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        ISymbol targetSymbol)
    {
        if (!TryCreateExpressionContext(analysis, sourceText, element, attribute, out var context))
        {
            return ImmutableArray<SourceRange>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<SourceRange>();
        var normalizedTarget = NormalizeSymbol(targetSymbol);
        foreach (var reference in context.SymbolReferences)
        {
            if (!AreEquivalentSymbols(reference.Symbol, normalizedTarget))
            {
                continue;
            }

            builder.Add(CreateRange(
                sourceText,
                context.ExpressionStartOffset + reference.Start,
                reference.Length));
        }

        return builder.ToImmutable();
    }

    public static bool TryResolveInlayHintTarget(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out XamlExpressionInlayHintTarget target)
    {
        target = default;
        if (!TryCreateExpressionContext(analysis, sourceText, element, attribute, out var context) ||
            context.ExpressionAnalysis.ResultTypeSymbol is null)
        {
            return false;
        }

        target = new XamlExpressionInlayHintTarget(
            context.AttributeValueRange,
            context.RawExpression,
            context.SourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            context.ExpressionAnalysis.ResultTypeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.ExpressionAnalysis.ResultTypeSymbol));
        return true;
    }

    private static bool TryFindExpressionAttributeAtPosition(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XElement element,
        out XAttribute attribute,
        out SourceRange attributeValueRange)
    {
        element = null!;
        attribute = null!;
        attributeValueRange = default;
        if (analysis.XmlDocument?.Root is null)
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
                        out var candidateValueRange) ||
                    !ContainsPosition(analysis.Document.Text, candidateValueRange, position))
                {
                    continue;
                }

                if (!TryParseExplicitExpressionMarkup(
                        candidateAttribute.Value,
                        TextCoordinateHelper.GetOffset(analysis.Document.Text, candidateValueRange.Start),
                        out _,
                        out _,
                        out _))
                {
                    continue;
                }

                element = candidateElement;
                attribute = candidateAttribute;
                attributeValueRange = candidateValueRange;
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateExpressionContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out ExpressionContext context)
    {
        context = default;
        if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(sourceText, attribute, out var attributeValueRange))
        {
            return false;
        }

        return TryCreateExpressionContext(analysis, sourceText, element, attribute, attributeValueRange, out context);
    }

    private static bool TryCreateExpressionContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        SourceRange attributeValueRange,
        out ExpressionContext context)
    {
        context = default;
        if (analysis.Compilation is null ||
            !TryResolveAmbientDataType(analysis, element, out var sourceType) ||
            !TryParseExplicitExpressionMarkup(
                attribute.Value,
                TextCoordinateHelper.GetOffset(sourceText, attributeValueRange.Start),
                out var expressionStartOffset,
                out var expressionLength,
                out var rawExpression))
        {
            return false;
        }

        var normalizedExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawExpression);
        if (string.IsNullOrWhiteSpace(normalizedExpression) ||
            !CSharpSourceContextExpressionAnalysisService.TryAnalyze(
                analysis.Compilation,
                sourceType,
                normalizedExpression,
                sourceParameterName: "source",
                out var expressionAnalysis,
                out _) ||
            !TryMapSymbolReferencesToRawExpression(
                rawExpression,
                normalizedExpression,
                expressionAnalysis.SymbolReferences,
                out var mappedReferences))
        {
            return false;
        }

        context = new ExpressionContext(
            analysis,
            sourceText,
            element,
            attribute,
            attributeValueRange,
            sourceType,
            rawExpression,
            expressionStartOffset,
            expressionLength,
            mappedReferences,
            expressionAnalysis);
        return true;
    }

    private static bool TryResolveAmbientDataType(
        XamlAnalysisResult analysis,
        XElement element,
        out INamedTypeSymbol sourceType)
    {
        sourceType = null!;
        if (analysis.Compilation is null)
        {
            return false;
        }

        var current = element;
        while (current is not null)
        {
            var dataTypeAttribute = current.Attributes()
                .FirstOrDefault(static attribute => string.Equals(attribute.Name.LocalName, "DataType", StringComparison.Ordinal));
            if (dataTypeAttribute is not null)
            {
                var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(current);
                if (XamlTypeReferenceNavigationResolver.TryResolve(
                        analysis,
                        prefixMap,
                        "DataType",
                        dataTypeAttribute.Value,
                        out var resolvedTypeReference))
                {
                    var candidate = analysis.Compilation.GetTypeByMetadataName(resolvedTypeReference.FullTypeName);
                    if (candidate is not null)
                    {
                        sourceType = candidate;
                        return true;
                    }
                }
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool TryParseExplicitExpressionMarkup(
        string attributeValue,
        int valueStartOffset,
        out int expressionStartOffset,
        out int expressionLength,
        out string rawExpression)
    {
        expressionStartOffset = 0;
        expressionLength = 0;
        rawExpression = string.Empty;

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

        if (trimmedEnd - trimmedStart < 3 ||
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

        if (innerEnd <= innerStart || attributeValue[innerStart] != '=')
        {
            return false;
        }

        var expressionStartInValue = innerStart + 1;
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

        expressionStartOffset = valueStartOffset + expressionStartInValue;
        expressionLength = expressionEndInValue - expressionStartInValue;
        rawExpression = attributeValue.Substring(expressionStartInValue, expressionLength);
        return rawExpression.Length > 0;
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static SourceRange CreateRange(string text, int startOffset, int length)
    {
        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset),
            TextCoordinateHelper.GetPosition(text, startOffset + length));
    }

    private static bool TryMapSymbolReferencesToRawExpression(
        string rawExpression,
        string normalizedExpression,
        ImmutableArray<SourceContextExpressionSymbolReference> normalizedReferences,
        out ImmutableArray<MappedExpressionSymbolReference> mappedReferences)
    {
        mappedReferences = ImmutableArray<MappedExpressionSymbolReference>.Empty;
        if (normalizedReferences.IsDefaultOrEmpty)
        {
            return true;
        }

        var rawTokens = TokenizeIdentifierTokens(rawExpression);
        var normalizedTokens = TokenizeIdentifierTokens(normalizedExpression);
        if (rawTokens.Length == 0 || normalizedTokens.Length == 0)
        {
            return false;
        }

        var rawTokenMap = new int[normalizedTokens.Length];
        var rawTokenIndex = 0;
        for (var normalizedTokenIndex = 0; normalizedTokenIndex < normalizedTokens.Length; normalizedTokenIndex++)
        {
            var normalizedToken = normalizedTokens[normalizedTokenIndex];
            while (rawTokenIndex < rawTokens.Length &&
                   !string.Equals(rawTokens[rawTokenIndex].Text, normalizedToken.Text, StringComparison.Ordinal))
            {
                rawTokenIndex++;
            }

            if (rawTokenIndex >= rawTokens.Length)
            {
                return false;
            }

            rawTokenMap[normalizedTokenIndex] = rawTokenIndex;
            rawTokenIndex++;
        }

        var builder = ImmutableArray.CreateBuilder<MappedExpressionSymbolReference>(normalizedReferences.Length);
        foreach (var reference in normalizedReferences)
        {
            var normalizedTokenIndex = FindIdentifierTokenIndex(normalizedTokens, reference.Start, reference.Length);
            if (normalizedTokenIndex < 0)
            {
                return false;
            }

            var rawToken = rawTokens[rawTokenMap[normalizedTokenIndex]];
            builder.Add(new MappedExpressionSymbolReference(
                reference.Symbol,
                rawToken.Start,
                rawToken.Length));
        }

        mappedReferences = builder.ToImmutable();
        return true;
    }

    private static int FindIdentifierTokenIndex(
        ImmutableArray<ExpressionIdentifierToken> tokens,
        int start,
        int length)
    {
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index];
            if (token.Start == start && token.Length == length)
            {
                return index;
            }
        }

        return -1;
    }

    private static ImmutableArray<ExpressionIdentifierToken> TokenizeIdentifierTokens(string expression)
    {
        var builder = ImmutableArray.CreateBuilder<ExpressionIdentifierToken>();
        var inSingleQuotedLiteral = false;
        var inDoubleQuotedLiteral = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (!inDoubleQuotedLiteral &&
                current == '\'' &&
                !IsEscapedChar(expression, index))
            {
                inSingleQuotedLiteral = !inSingleQuotedLiteral;
                continue;
            }

            if (!inSingleQuotedLiteral &&
                current == '"' &&
                !IsEscapedChar(expression, index))
            {
                inDoubleQuotedLiteral = !inDoubleQuotedLiteral;
                continue;
            }

            if (inSingleQuotedLiteral ||
                inDoubleQuotedLiteral ||
                !SyntaxFacts.IsIdentifierStartCharacter(current))
            {
                continue;
            }

            var start = index;
            index++;
            while (index < expression.Length && SyntaxFacts.IsIdentifierPartCharacter(expression[index]))
            {
                index++;
            }

            var length = index - start;
            builder.Add(new ExpressionIdentifierToken(
                start,
                length,
                expression.Substring(start, length)));
            index--;
        }

        return builder.ToImmutable();
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

    private static ISymbol NormalizeSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IMethodSymbol methodSymbol => methodSymbol.OriginalDefinition,
            IPropertySymbol propertySymbol => propertySymbol.OriginalDefinition,
            IFieldSymbol fieldSymbol => fieldSymbol.OriginalDefinition,
            INamedTypeSymbol typeSymbol => typeSymbol.OriginalDefinition,
            _ => symbol
        };
    }

    private static bool AreEquivalentSymbols(ISymbol left, ISymbol right)
    {
        var normalizedLeft = NormalizeSymbol(left);
        var normalizedRight = NormalizeSymbol(right);
        if (SymbolEqualityComparer.Default.Equals(normalizedLeft, normalizedRight))
        {
            return true;
        }

        if (normalizedLeft.Kind != normalizedRight.Kind)
        {
            return false;
        }

        return normalizedLeft switch
        {
            INamedTypeSymbol leftType when normalizedRight is INamedTypeSymbol rightType =>
                string.Equals(
                    leftType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IPropertySymbol leftProperty when normalizedRight is IPropertySymbol rightProperty =>
                string.Equals(leftProperty.Name, rightProperty.Name, StringComparison.Ordinal) &&
                string.Equals(
                    leftProperty.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightProperty.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IFieldSymbol leftField when normalizedRight is IFieldSymbol rightField =>
                string.Equals(leftField.Name, rightField.Name, StringComparison.Ordinal) &&
                string.Equals(
                    leftField.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightField.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            IMethodSymbol leftMethod when normalizedRight is IMethodSymbol rightMethod =>
                string.Equals(leftMethod.Name, rightMethod.Name, StringComparison.Ordinal) &&
                leftMethod.Parameters.Length == rightMethod.Parameters.Length &&
                string.Equals(
                    leftMethod.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    rightMethod.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    StringComparison.Ordinal),
            _ => false
        };
    }

    private readonly record struct ExpressionContext(
        XamlAnalysisResult Analysis,
        string SourceText,
        XElement Element,
        XAttribute Attribute,
        SourceRange AttributeValueRange,
        INamedTypeSymbol SourceType,
        string RawExpression,
        int ExpressionStartOffset,
        int ExpressionLength,
        ImmutableArray<MappedExpressionSymbolReference> SymbolReferences,
        SourceContextExpressionAnalysisResult ExpressionAnalysis);

    private readonly record struct ExpressionIdentifierToken(
        int Start,
        int Length,
        string Text);
}
