using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly record struct SourceContextExpressionSymbolReference(
    ISymbol Symbol,
    int Start,
    int Length);

public readonly record struct SourceContextExpressionAnalysisResult(
    string AccessorExpression,
    ImmutableArray<string> DependencyNames,
    ITypeSymbol? ResultTypeSymbol,
    ImmutableArray<SourceContextExpressionSymbolReference> SymbolReferences);

public static class CSharpSourceContextExpressionAnalysisService
{
    public static bool TryAnalyze(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string rawExpression,
        string sourceParameterName,
        out SourceContextExpressionAnalysisResult result,
        out string errorMessage)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (sourceType is null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        result = default;
        errorMessage = string.Empty;

        if (!CSharpSourceContextExpressionBuilder.TryRewriteAccessorExpression(
                sourceType,
                rawExpression,
                sourceParameterName,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        if (!TryAnalyzeRewrittenExpression(
                compilation,
                sourceType,
                sourceParameterName,
                rewriteResult.RewrittenExpressionSyntax,
                out var resultTypeSymbol,
                out var symbolReferences,
                out errorMessage))
        {
            return false;
        }

        result = new SourceContextExpressionAnalysisResult(
            rewriteResult.RewrittenExpressionSyntax.ToFullString().Trim(),
            rewriteResult.DependencyNames,
            resultTypeSymbol,
            symbolReferences);
        return true;
    }

    private static bool TryAnalyzeRewrittenExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string sourceParameterName,
        ExpressionSyntax rewrittenExpressionSyntax,
        out ITypeSymbol? resultTypeSymbol,
        out ImmutableArray<SourceContextExpressionSymbolReference> symbolReferences,
        out string errorMessage)
    {
        resultTypeSymbol = null;
        symbolReferences = ImmutableArray<SourceContextExpressionSymbolReference>.Empty;
        errorMessage = string.Empty;

        var rewrittenExpression = rewrittenExpressionSyntax.ToFullString().Trim();
        if (rewrittenExpression.Length == 0)
        {
            errorMessage = "expression rewrite produced an empty expression";
            return false;
        }

        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_ExpressionAnalysis",
            "{",
            "    internal static class __ExpressionContext",
            "    {",
            "        internal static object? __Evaluate(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " " +
            sourceParameterName +
            ") => default;",
            "    }",
            "}");

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationRoot = validationTree.GetRoot();
        var placeholderExpression = validationRoot.DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static clause => clause.Expression)
            .FirstOrDefault();
        if (placeholderExpression is null)
        {
            errorMessage = "expression analysis tree did not contain the evaluation expression";
            return false;
        }

        var updatedRoot = validationRoot.ReplaceNode(
            placeholderExpression,
            rewrittenExpressionSyntax.WithoutTrivia());
        validationTree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var validationCompilation = compilation.AddSyntaxTrees(validationTree);
        var validationDiagnostic = validationCompilation.GetDiagnostics()
            .FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == validationTree);
        if (validationDiagnostic is not null)
        {
            errorMessage = validationDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var root = validationTree.GetRoot();
        var expressionSyntax = root.DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static clause => clause.Expression)
            .FirstOrDefault();
        if (expressionSyntax is null)
        {
            errorMessage = "expression analysis tree did not contain the evaluation expression";
            return false;
        }

        var analyzedExpression = UnwrapAnalyzedExpression(expressionSyntax);

        var semanticModel = validationCompilation.GetSemanticModel(validationTree, ignoreAccessibility: true);
        var expressionTypeInfo = semanticModel.GetTypeInfo(analyzedExpression);
        resultTypeSymbol = expressionTypeInfo.Type ?? expressionTypeInfo.ConvertedType;

        var builder = ImmutableArray.CreateBuilder<SourceContextExpressionSymbolReference>();
        foreach (var nameSyntax in analyzedExpression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            var rawSpanAnnotation = nameSyntax.GetAnnotations(CSharpSourceContextExpressionBuilder.RawSpanAnnotationKind)
                .FirstOrDefault();
            if (rawSpanAnnotation is null ||
                !CSharpSourceContextExpressionBuilder.TryParseRawSpanAnnotation(rawSpanAnnotation, out var rawStart, out var rawLength))
            {
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(nameSyntax).Symbol ??
                         semanticModel.GetSymbolInfo(nameSyntax).CandidateSymbols.FirstOrDefault();
            if (!ShouldTrackSymbol(symbol))
            {
                continue;
            }

            builder.Add(new SourceContextExpressionSymbolReference(
                NormalizeSymbol(symbol!),
                rawStart,
                rawLength));
        }

        symbolReferences = builder.ToImmutable();
        return true;
    }

    private static ExpressionSyntax UnwrapAnalyzedExpression(ExpressionSyntax expressionSyntax)
    {
        var current = expressionSyntax;
        while (true)
        {
            switch (current)
            {
                case ParenthesizedExpressionSyntax parenthesizedExpression:
                    current = parenthesizedExpression.Expression;
                    continue;
                case CastExpressionSyntax castExpression:
                    current = castExpression.Expression;
                    continue;
                default:
                    return current;
            }
        }
    }

    private static bool ShouldTrackSymbol(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        if (symbol.IsStatic && symbol is not INamedTypeSymbol)
        {
            return false;
        }

        return symbol switch
        {
            IPropertySymbol => true,
            IFieldSymbol => true,
            IMethodSymbol => true,
            INamedTypeSymbol => true,
            _ => false
        };
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
}
