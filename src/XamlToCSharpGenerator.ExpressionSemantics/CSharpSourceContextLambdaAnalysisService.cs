using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly record struct SourceContextLambdaAnalysisResult(
    string RewrittenLambdaExpression,
    ImmutableArray<string> DependencyNames,
    ImmutableArray<SourceContextExpressionSymbolReference> SymbolReferences,
    ImmutableArray<SourceContextSymbolOccurrence> SymbolOccurrences);

public static class CSharpSourceContextLambdaAnalysisService
{
    public static bool TryAnalyze(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol delegateType,
        string rawLambdaExpression,
        string sourceParameterName,
        out SourceContextLambdaAnalysisResult result,
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

        if (delegateType is null)
        {
            throw new ArgumentNullException(nameof(delegateType));
        }

        result = default;
        errorMessage = string.Empty;

        if (!CSharpSourceContextExpressionBuilder.TryRewriteLambdaExpression(
                sourceType,
                rawLambdaExpression,
                sourceParameterName,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        if (!TryAnalyzeRewrittenLambda(
                compilation,
                sourceType,
                delegateType,
                sourceParameterName,
                rewriteResult.RewrittenLambdaExpressionSyntax,
                out var symbolReferences,
                out errorMessage))
        {
            return false;
        }

        result = new SourceContextLambdaAnalysisResult(
            rewriteResult.RewrittenLambdaExpressionSyntax.ToFullString().Trim(),
            rewriteResult.DependencyNames,
            symbolReferences,
            ImmutableArray<SourceContextSymbolOccurrence>.Empty);
        return true;
    }

    private static bool TryAnalyzeRewrittenLambda(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol delegateType,
        string sourceParameterName,
        AnonymousFunctionExpressionSyntax rewrittenLambdaExpression,
        out ImmutableArray<SourceContextExpressionSymbolReference> symbolReferences,
        out string errorMessage)
    {
        symbolReferences = ImmutableArray<SourceContextExpressionSymbolReference>.Empty;
        errorMessage = string.Empty;

        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_LambdaAnalysis",
            "{",
            "    internal static class __ExpressionContext",
            "    {",
            "        internal static void __Bind(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " " +
            sourceParameterName +
            ")",
            "        {",
            "            " +
            delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " __handler = default!;",
            "        }",
            "    }",
            "}");

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationRoot = validationTree.GetRoot();
        var declarator = validationRoot.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(static candidate => string.Equals(candidate.Identifier.ValueText, "__handler", StringComparison.Ordinal));
        if (declarator?.Initializer is null)
        {
            errorMessage = "lambda analysis tree did not contain the handler initializer";
            return false;
        }

        var updatedRoot = validationRoot.ReplaceNode(
            declarator.Initializer.Value,
            rewrittenLambdaExpression.WithoutTrivia());
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

        var lambdaSyntax = validationTree.GetRoot().DescendantNodes()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        if (lambdaSyntax is null)
        {
            errorMessage = "lambda analysis tree did not contain the handler expression";
            return false;
        }

        var semanticModel = validationCompilation.GetSemanticModel(validationTree, ignoreAccessibility: true);
        var builder = ImmutableArray.CreateBuilder<SourceContextExpressionSymbolReference>();
        foreach (var nameSyntax in lambdaSyntax.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            var rawSpanAnnotation = nameSyntax.GetAnnotations(CSharpSourceContextExpressionBuilder.RawSpanAnnotationKind)
                .FirstOrDefault();
            if (rawSpanAnnotation is null ||
                !CSharpSourceContextExpressionBuilder.TryParseRawSpanAnnotation(rawSpanAnnotation, out var rawStart, out var rawLength))
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(nameSyntax);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
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
