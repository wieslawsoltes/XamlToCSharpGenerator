using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

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

internal readonly record struct XamlMarkupExpressionContext(
    XamlAnalysisResult Analysis,
    string SourceText,
    XElement Element,
    XAttribute Attribute,
    SourceRange AttributeValueRange,
    INamedTypeSymbol SourceType,
    string RawExpression,
    string NormalizedExpression,
    bool IsLambda,
    int ExpressionStartOffset,
    int ExpressionLength,
    ImmutableArray<MappedExpressionSymbolReference> SymbolReferences,
    ITypeSymbol? ResultTypeSymbol);

internal static class XamlExpressionBindingNavigationService
{
    public static bool IsCSharpMarkupExpression(string attributeValue)
    {
        return XamlCSharpMarkupExpressionService.IsCSharpMarkupExpression(attributeValue);
    }

    public static bool TryResolveNavigationTarget(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlExpressionNavigationTarget target)
    {
        target = default;
        var documentOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        if (!XamlCSharpMarkupExpressionService.TryFindMarkupExpressionAttributeContext(
                analysis,
                position,
                out var element,
                out var attribute,
                out var attributeValueRange,
                out var expressionInfo,
                out _) ||
            !TryResolveExpressionContext(analysis, analysis.Document.Text, element, attribute, attributeValueRange, expressionInfo, out var context))
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
        if (!TryResolveExpressionContext(analysis, sourceText, element, attribute, out var context))
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
        if (!TryResolveExpressionContext(analysis, sourceText, element, attribute, out var context) ||
            context.IsLambda ||
            context.ResultTypeSymbol is null)
        {
            return false;
        }

        target = new XamlExpressionInlayHintTarget(
            context.AttributeValueRange,
            context.RawExpression,
            context.SourceType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            context.ResultTypeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, context.ResultTypeSymbol));
        return true;
    }

    internal static bool TryResolveExpressionContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        out XamlMarkupExpressionContext context)
    {
        context = default;
        if (!XamlXmlSourceRangeService.TryCreateAttributeValueRange(sourceText, attribute, out var attributeValueRange))
        {
            return false;
        }

        if (!XamlCSharpMarkupExpressionService.TryParseMarkupExpression(
                analysis,
                element,
                attribute,
                TextCoordinateHelper.GetOffset(sourceText, attributeValueRange.Start),
                out var expressionInfo))
        {
            return false;
        }

        return TryResolveExpressionContext(analysis, sourceText, element, attribute, attributeValueRange, expressionInfo, out context);
    }

    internal static bool TryResolveExpressionContext(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        SourceRange attributeValueRange,
        XamlCSharpMarkupExpressionInfo expressionInfo,
        out XamlMarkupExpressionContext context)
    {
        context = default;
        if (analysis.Compilation is null)
        {
            return false;
        }

        if (TryResolveAmbientDataType(analysis, element, out var ambientSourceType) &&
            TryCreateExpressionContextForSourceType(
                analysis,
                sourceText,
                element,
                attribute,
                attributeValueRange,
                expressionInfo,
                ambientSourceType,
                out context))
        {
            return true;
        }

        return TryResolveRootType(analysis, out var rootSourceType) &&
               TryCreateExpressionContextForSourceType(
                   analysis,
                   sourceText,
                   element,
                   attribute,
                   attributeValueRange,
                   expressionInfo,
                   rootSourceType,
                   out context);
    }

    private static bool TryCreateExpressionContextForSourceType(
        XamlAnalysisResult analysis,
        string sourceText,
        XElement element,
        XAttribute attribute,
        SourceRange attributeValueRange,
        XamlCSharpMarkupExpressionInfo expressionInfo,
        INamedTypeSymbol sourceType,
        out XamlMarkupExpressionContext context)
    {
        context = default;

        ImmutableArray<MappedExpressionSymbolReference> mappedReferences;
        ITypeSymbol? resultTypeSymbol = null;
        if (expressionInfo.Kind == XamlCSharpMarkupExpressionKind.Lambda)
        {
            var eventHandlerType = TryResolveEventHandlerType(analysis, element, attribute);
            if (eventHandlerType is null ||
                !CSharpSourceContextLambdaAnalysisService.TryAnalyze(
                    analysis.Compilation!,
                    sourceType,
                    eventHandlerType,
                    expressionInfo.NormalizedExpression,
                    "__axsgLambdaSource",
                    out var lambdaAnalysis,
                    out _) ||
                !TryMapSymbolReferencesToRawExpression(
                    expressionInfo.RawExpression,
                    expressionInfo.NormalizedExpression,
                    lambdaAnalysis.SymbolReferences,
                    out mappedReferences))
            {
                return false;
            }
        }
        else
        {
            if (!CSharpSourceContextExpressionAnalysisService.TryAnalyze(
                    analysis.Compilation!,
                    sourceType,
                    expressionInfo.NormalizedExpression,
                    "source",
                    out var expressionAnalysis,
                    out _) ||
                !TryMapSymbolReferencesToRawExpression(
                    expressionInfo.RawExpression,
                    expressionInfo.NormalizedExpression,
                    expressionAnalysis.SymbolReferences,
                    out mappedReferences))
            {
                return false;
            }

            resultTypeSymbol = expressionAnalysis.ResultTypeSymbol;
        }

        context = new XamlMarkupExpressionContext(
            analysis,
            sourceText,
            element,
            attribute,
            attributeValueRange,
            sourceType,
            expressionInfo.RawExpression,
            expressionInfo.NormalizedExpression,
            expressionInfo.Kind == XamlCSharpMarkupExpressionKind.Lambda,
            expressionInfo.ExpressionStartOffset,
            expressionInfo.ExpressionLength,
            mappedReferences,
            resultTypeSymbol);
        return true;
    }

    private static bool TryResolveAmbientDataType(
        XamlAnalysisResult analysis,
        XElement element,
        out INamedTypeSymbol sourceType)
    {
        sourceType = null!;
        return XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(
            analysis,
            element,
            out sourceType,
            out _);
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
        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (current == '$' &&
                index + 1 < expression.Length &&
                expression[index + 1] is '"' or '\'')
            {
                index = TokenizeInterpolatedStringTokens(expression, index, builder);
                continue;
            }

            if (current == '\'' &&
                !IsEscapedChar(expression, index))
            {
                index = SkipQuotedLiteral(expression, index, '\'');
                continue;
            }

            if (current == '"' &&
                !IsEscapedChar(expression, index))
            {
                index = SkipQuotedLiteral(expression, index, '"');
                continue;
            }

            if (!SyntaxFacts.IsIdentifierStartCharacter(current))
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

    private static int SkipQuotedLiteral(string expression, int startIndex, char quote)
    {
        var index = startIndex + 1;
        while (index < expression.Length)
        {
            if (expression[index] == quote &&
                !IsEscapedChar(expression, index))
            {
                return index;
            }

            index++;
        }

        return expression.Length - 1;
    }

    private static int TokenizeInterpolatedStringTokens(
        string expression,
        int startIndex,
        ImmutableArray<ExpressionIdentifierToken>.Builder builder)
    {
        var quote = expression[startIndex + 1];
        var index = startIndex + 2;
        var interpolationDepth = 0;

        while (index < expression.Length)
        {
            var current = expression[index];
            if (interpolationDepth == 0)
            {
                if (current == quote &&
                    !IsEscapedChar(expression, index))
                {
                    return index;
                }

                if (current == '{')
                {
                    if (index + 1 < expression.Length && expression[index + 1] == '{')
                    {
                        index += 2;
                        continue;
                    }

                    interpolationDepth = 1;
                    index++;
                    continue;
                }

                index++;
                continue;
            }

            if (current == '{')
            {
                interpolationDepth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                interpolationDepth--;
                index++;
                continue;
            }

            if (current == '\'' &&
                !IsEscapedChar(expression, index))
            {
                index = SkipQuotedLiteral(expression, index, '\'') + 1;
                continue;
            }

            if (current == '"' &&
                !IsEscapedChar(expression, index))
            {
                index = SkipQuotedLiteral(expression, index, '"') + 1;
                continue;
            }

            if (!SyntaxFacts.IsIdentifierStartCharacter(current))
            {
                index++;
                continue;
            }

            var tokenStart = index;
            index++;
            while (index < expression.Length && SyntaxFacts.IsIdentifierPartCharacter(expression[index]))
            {
                index++;
            }

            var length = index - tokenStart;
            builder.Add(new ExpressionIdentifierToken(
                tokenStart,
                length,
                expression.Substring(tokenStart, length)));
        }

        return expression.Length - 1;
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

    private readonly record struct ExpressionIdentifierToken(
        int Start,
        int Length,
        string Text);

    private static bool TryResolveRootType(XamlAnalysisResult analysis, out INamedTypeSymbol sourceType)
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

    private static INamedTypeSymbol? TryResolveEventHandlerType(
        XamlAnalysisResult analysis,
        XElement element,
        XAttribute attribute)
    {
        if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, element, out var elementType))
        {
            return null;
        }

        var eventName = attribute.Name.LocalName;
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        return FindEvent(elementType, eventName)?.Type as INamedTypeSymbol;
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol typeSymbol, string eventName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }
}
