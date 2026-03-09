using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly record struct CSharpInlineExpressionAnalysisResult(
    string NormalizedExpression,
    ImmutableArray<string> DependencyNames,
    ITypeSymbol? ResultTypeSymbol,
    ImmutableArray<SourceContextExpressionSymbolReference> SymbolReferences,
    ImmutableArray<SourceContextSymbolOccurrence> SymbolOccurrences);

public enum SourceContextSymbolTokenKind
{
    Type,
    Method,
    Property,
    Parameter,
    Variable
}

public readonly record struct SourceContextSymbolOccurrence(
    ISymbol Symbol,
    int Start,
    int Length,
    bool IsDeclaration,
    SourceContextSymbolTokenKind TokenKind);

internal readonly record struct InlineContextRewriteResult<TSyntax>(
    TSyntax RewrittenSyntax,
    ImmutableArray<string> DependencyNames);

public static class CSharpInlineCodeAnalysisService
{
    public static bool TryAnalyzeExpression(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawExpression,
        out CSharpInlineExpressionAnalysisResult result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "expression must not be empty";
            return false;
        }

        if (!TryRewriteExpression(
                sourceType,
                rootType,
                targetType,
                rawExpression,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_InlineCode",
            "{",
            "    internal static class __Context",
            "    {",
            "        internal static object? __Evaluate(" +
            GetTypeName(sourceType, compilation) +
            " source, " +
            GetTypeName(rootType, compilation) +
            " root, " +
            GetTypeName(targetType, compilation) +
            " target) => default;",
            "    }",
            "}");

        var tree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var rootNode = tree.GetRoot();
        var placeholderExpression = rootNode.DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static clause => clause.Expression)
            .FirstOrDefault();
        if (placeholderExpression is null)
        {
            errorMessage = "inline expression analysis tree did not contain the expression";
            return false;
        }

        var updatedRoot = rootNode.ReplaceNode(
            placeholderExpression,
            rewriteResult.RewrittenSyntax.WithoutTrivia());
        tree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var compilationWithTree = compilation.AddSyntaxTrees(tree);
        var diagnostic = compilationWithTree.GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree == tree);
        if (diagnostic is not null)
        {
            errorMessage = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var semanticModel = compilationWithTree.GetSemanticModel(tree, ignoreAccessibility: true);
        var analyzedExpression = tree.GetRoot().DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static clause => clause.Expression)
            .FirstOrDefault();
        if (analyzedExpression is null)
        {
            errorMessage = "inline expression analysis tree did not contain the expression";
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(analyzedExpression);
        result = new CSharpInlineExpressionAnalysisResult(
            NormalizedExpression: rewriteResult.RewrittenSyntax.ToFullString().Trim(),
            DependencyNames: rewriteResult.DependencyNames,
            ResultTypeSymbol: typeInfo.Type ?? typeInfo.ConvertedType,
            SymbolReferences: CollectSymbolReferences(semanticModel, analyzedExpression),
            SymbolOccurrences: CollectSymbolOccurrences(semanticModel, analyzedExpression));
        return true;
    }

    public static bool TryAnalyzeLambda(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol delegateType,
        string rawLambdaExpression,
        out SourceContextLambdaAnalysisResult result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawLambdaExpression))
        {
            errorMessage = "lambda must not be empty";
            return false;
        }

        if (!TryRewriteLambda(
                sourceType,
                rootType,
                targetType,
                rawLambdaExpression,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_InlineCode",
            "{",
            "    internal static class __Context",
            "    {",
            "        internal static void __Bind(" +
            GetTypeName(sourceType, compilation) +
            " source, " +
            GetTypeName(rootType, compilation) +
            " root, " +
            GetTypeName(targetType, compilation) +
            " target)",
            "        {",
            "            " +
            delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " __handler = default!;",
            "        }",
            "    }",
            "}");

        var tree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var rootNode = tree.GetRoot();
        var declarator = rootNode.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(static candidate => string.Equals(candidate.Identifier.ValueText, "__handler", StringComparison.Ordinal));
        if (declarator?.Initializer is null)
        {
            errorMessage = "inline lambda analysis tree did not contain the handler initializer";
            return false;
        }

        var updatedRoot = rootNode.ReplaceNode(
            declarator.Initializer.Value,
            rewriteResult.RewrittenSyntax.WithoutTrivia());
        tree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var compilationWithTree = compilation.AddSyntaxTrees(tree);
        var diagnostic = compilationWithTree.GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree == tree);
        if (diagnostic is not null)
        {
            errorMessage = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var semanticModel = compilationWithTree.GetSemanticModel(tree, ignoreAccessibility: true);
        var analyzedLambda = tree.GetRoot().DescendantNodes()
            .OfType<AnonymousFunctionExpressionSyntax>()
            .FirstOrDefault();
        if (analyzedLambda is null)
        {
            errorMessage = "inline lambda analysis tree did not contain the lambda";
            return false;
        }

        result = new SourceContextLambdaAnalysisResult(
            rewriteResult.RewrittenSyntax.ToFullString().Trim(),
            rewriteResult.DependencyNames,
            CollectSymbolReferences(semanticModel, analyzedLambda),
            CollectSymbolOccurrences(semanticModel, analyzedLambda));
        return true;
    }

    public static bool TryAnalyzeEventStatements(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol delegateType,
        string rawStatements,
        out SourceContextLambdaAnalysisResult result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawStatements))
        {
            errorMessage = "event code block must not be empty";
            return false;
        }

        if (delegateType.DelegateInvokeMethod is not IMethodSymbol invokeMethod)
        {
            errorMessage = "event delegate type does not expose Invoke";
            return false;
        }

        if (!TryRewriteStatements(
                sourceType,
                rootType,
                targetType,
                rawStatements,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var parameterBuilder = new List<string>(invokeMethod.Parameters.Length);
        var aliasBuilder = new List<string>(Math.Max(2, invokeMethod.Parameters.Length));
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            var parameter = invokeMethod.Parameters[index];
            var parameterName = "arg" + index.ToString(CultureInfo.InvariantCulture);
            parameterBuilder.Add(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " " + parameterName);
        }

        if (invokeMethod.Parameters.Length > 0)
        {
            aliasBuilder.Add("            var sender = arg0;");
        }
        else
        {
            aliasBuilder.Add("            object? sender = null;");
        }

        if (invokeMethod.Parameters.Length > 1)
        {
            aliasBuilder.Add("            var e = arg1;");
        }
        else
        {
            aliasBuilder.Add("            object? e = null;");
        }

        var methodParameters = string.Join(", ", new[]
        {
            GetTypeName(sourceType, compilation) + " source",
            GetTypeName(rootType, compilation) + " root",
            GetTypeName(targetType, compilation) + " target"
        }.Concat(parameterBuilder));

        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_InlineCode",
            "{",
            "    internal static class __Context",
            "    {",
            "        internal static void __Execute(" + methodParameters + ")",
            "        {",
            string.Join(Environment.NewLine, aliasBuilder),
            "        }",
            "    }",
            "}");

        var tree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var rootNode = tree.GetRoot();
        var methodBody = rootNode.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(static method => string.Equals(method.Identifier.ValueText, "__Execute", StringComparison.Ordinal))
            ?.Body;
        if (methodBody is null)
        {
            errorMessage = "inline event analysis tree did not contain the execution body";
            return false;
        }

        var replacementStatements = new SyntaxList<StatementSyntax>(
            methodBody.Statements.AddRange(rewriteResult.RewrittenSyntax.Statements));
        var updatedRoot = rootNode.ReplaceNode(methodBody, methodBody.WithStatements(replacementStatements));
        tree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var compilationWithTree = compilation.AddSyntaxTrees(tree);
        var diagnostic = compilationWithTree.GetDiagnostics()
            .FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree == tree);
        if (diagnostic is not null)
        {
            errorMessage = diagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var semanticModel = compilationWithTree.GetSemanticModel(tree, ignoreAccessibility: true);
        var analyzedMethod = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(static method => string.Equals(method.Identifier.ValueText, "__Execute", StringComparison.Ordinal));
        if (analyzedMethod?.Body is null)
        {
            errorMessage = "inline event analysis tree did not contain the execution body";
            return false;
        }

        var lambdaParameters = BuildLambdaParameterList(invokeMethod.Parameters);
        var lambdaExpression = "(" + lambdaParameters + ") => { " +
                               BuildInlineEventLambdaBody(invokeMethod.Parameters.Length, rewriteResult.RewrittenSyntax) +
                               " }";
        result = new SourceContextLambdaAnalysisResult(
            lambdaExpression,
            rewriteResult.DependencyNames,
            CollectSymbolReferences(semanticModel, analyzedMethod.Body),
            CollectSymbolOccurrences(semanticModel, analyzedMethod.Body));
        return true;
    }

    private static bool TryRewriteExpression(
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawExpression,
        out InlineContextRewriteResult<ExpressionSyntax> result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        var normalizedExpression = rawExpression.Trim();
        var parsedExpression = SyntaxFactory.ParseExpression(normalizedExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var annotatedExpression = AnnotateIdentifiers(parsedExpression);
        var localNames = GetLocalNames(annotatedExpression);
        var rewriter = new InlineContextExpressionRewriter(
            GetExpressionMemberNames(sourceType),
            GetExpressionMemberNames(rootType),
            GetExpressionMemberNames(targetType),
            localNames);
        if (rewriter.Visit(annotatedExpression) is not ExpressionSyntax rewrittenExpression)
        {
            errorMessage = "expression rewrite failed";
            return false;
        }

        result = new InlineContextRewriteResult<ExpressionSyntax>(
            rewrittenExpression,
            ToOrderedDependencyArray(rewriter.Dependencies));
        return true;
    }

    private static bool TryRewriteLambda(
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawLambdaExpression,
        out InlineContextRewriteResult<AnonymousFunctionExpressionSyntax> result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        var normalizedLambdaExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawLambdaExpression.Trim());
        var parsedExpression = SyntaxFactory.ParseExpression(normalizedLambdaExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        if (parsedExpression is not AnonymousFunctionExpressionSyntax lambdaSyntax)
        {
            errorMessage = "expression is not a lambda expression";
            return false;
        }

        var annotatedLambda = (AnonymousFunctionExpressionSyntax)AnnotateIdentifiers(lambdaSyntax);
        var localNames = GetLocalNames(annotatedLambda);
        var rewriter = new InlineContextExpressionRewriter(
            GetExpressionMemberNames(sourceType),
            GetExpressionMemberNames(rootType),
            GetExpressionMemberNames(targetType),
            localNames);
        if (rewriter.Visit(annotatedLambda) is not AnonymousFunctionExpressionSyntax rewrittenLambda)
        {
            errorMessage = "lambda rewrite failed";
            return false;
        }

        result = new InlineContextRewriteResult<AnonymousFunctionExpressionSyntax>(
            rewrittenLambda,
            ToOrderedDependencyArray(rewriter.Dependencies));
        return true;
    }

    private static bool TryRewriteStatements(
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        string rawStatements,
        out InlineContextRewriteResult<BlockSyntax> result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        var normalizedStatements = rawStatements.Trim();
        var wrapperPrefix = "{" + Environment.NewLine;
        var parsedStatement = SyntaxFactory.ParseStatement(wrapperPrefix + normalizedStatements + Environment.NewLine + "}");
        var parseDiagnostic = parsedStatement.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null || parsedStatement is not BlockSyntax block)
        {
            errorMessage = parseDiagnostic?.GetMessage(CultureInfo.InvariantCulture) ?? "event code block parse failed";
            return false;
        }

        var annotatedBlock = (BlockSyntax)AnnotateIdentifiers(block, -wrapperPrefix.Length);
        var localNames = GetLocalNames(annotatedBlock);
        var rewriter = new InlineContextExpressionRewriter(
            GetExpressionMemberNames(sourceType),
            GetExpressionMemberNames(rootType),
            GetExpressionMemberNames(targetType),
            localNames);
        if (rewriter.Visit(annotatedBlock) is not BlockSyntax rewrittenBlock)
        {
            errorMessage = "event code block rewrite failed";
            return false;
        }

        result = new InlineContextRewriteResult<BlockSyntax>(
            rewrittenBlock,
            ToOrderedDependencyArray(rewriter.Dependencies));
        return true;
    }

    private static SyntaxNode AnnotateIdentifiers(SyntaxNode node, int rawSpanOffset = 0)
    {
        var annotatedNode = node.ReplaceNodes(
            node.DescendantNodesAndSelf().OfType<SimpleNameSyntax>(),
            (originalNode, _) => originalNode.WithAdditionalAnnotations(
                CSharpSourceContextExpressionBuilder.CreateRawSpanAnnotation(
                    originalNode.SpanStart + rawSpanOffset,
                    originalNode.Span.Length)));

        return annotatedNode.ReplaceTokens(
            annotatedNode.DescendantTokens().Where(static token => ShouldAnnotateDeclarationToken(token)),
            (originalToken, _) => originalToken.WithAdditionalAnnotations(
                CSharpSourceContextExpressionBuilder.CreateRawSpanAnnotation(
                    originalToken.SpanStart + rawSpanOffset,
                    originalToken.Span.Length)));
    }

    private static bool ShouldAnnotateDeclarationToken(SyntaxToken token)
    {
        return token.Parent switch
        {
            ParameterSyntax parameterSyntax when token == parameterSyntax.Identifier => true,
            VariableDeclaratorSyntax variableDeclaratorSyntax when token == variableDeclaratorSyntax.Identifier => true,
            SingleVariableDesignationSyntax variableDesignationSyntax when token == variableDesignationSyntax.Identifier => true,
            ForEachStatementSyntax forEachStatementSyntax when token == forEachStatementSyntax.Identifier => true,
            CatchDeclarationSyntax catchDeclarationSyntax when token == catchDeclarationSyntax.Identifier => true,
            LocalFunctionStatementSyntax localFunctionStatementSyntax when token == localFunctionStatementSyntax.Identifier => true,
            _ => false
        };
    }

    private static ImmutableHashSet<string> GetLocalNames(SyntaxNode node)
    {
        var collector = new InlineCodeLocalNameCollector();
        collector.Visit(node);
        return collector.Names.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static ImmutableHashSet<string> GetExpressionMemberNames(INamedTypeSymbol? typeSymbol)
    {
        if (typeSymbol is null)
        {
            return ImmutableHashSet.Create<string>(StringComparer.Ordinal);
        }

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            AddExpressionMemberNames(current, builder);
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            AddExpressionMemberNames(interfaceType, builder);
        }

        return builder.ToImmutable();
    }

    private static void AddExpressionMemberNames(
        INamedTypeSymbol type,
        ImmutableHashSet<string>.Builder builder)
    {
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IPropertySymbol property when !property.IsStatic && property.GetMethod is not null:
                    builder.Add(property.Name);
                    break;
                case IFieldSymbol field when !field.IsStatic:
                    builder.Add(field.Name);
                    break;
                case IMethodSymbol method when
                    !method.IsStatic &&
                    method.MethodKind == MethodKind.Ordinary &&
                    !method.IsImplicitlyDeclared:
                    builder.Add(method.Name);
                    break;
            }
        }
    }

    private static ImmutableArray<string> ToOrderedDependencyArray(HashSet<string> dependencies)
    {
        return dependencies.Count == 0
            ? ImmutableArray<string>.Empty
            : dependencies.OrderBy(static name => name, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<SourceContextExpressionSymbolReference> CollectSymbolReferences(
        SemanticModel semanticModel,
        SyntaxNode rootNode)
    {
        var builder = ImmutableArray.CreateBuilder<SourceContextExpressionSymbolReference>();
        foreach (var occurrence in CollectSymbolOccurrences(semanticModel, rootNode))
        {
            if (occurrence.IsDeclaration || !ShouldTrackReferenceSymbol(occurrence.Symbol))
            {
                continue;
            }

            builder.Add(new SourceContextExpressionSymbolReference(
                NormalizeSymbol(occurrence.Symbol),
                occurrence.Start,
                occurrence.Length));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<SourceContextSymbolOccurrence> CollectSymbolOccurrences(
        SemanticModel semanticModel,
        SyntaxNode rootNode)
    {
        var builder = ImmutableArray.CreateBuilder<SourceContextSymbolOccurrence>();
        foreach (var nameSyntax in rootNode.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
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
            if (!TryMapTrackedSymbol(symbol, out var tokenKind))
            {
                continue;
            }

            builder.Add(new SourceContextSymbolOccurrence(
                NormalizeSymbol(symbol!),
                rawStart,
                rawLength,
                IsDeclaration: false,
                tokenKind));
        }

        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<ParameterSyntax>(),
            static parameterSyntax => parameterSyntax.Identifier,
            SourceContextSymbolTokenKind.Parameter,
            builder);
        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>(),
            static variableDeclaratorSyntax => variableDeclaratorSyntax.Identifier,
            SourceContextSymbolTokenKind.Variable,
            builder);
        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<SingleVariableDesignationSyntax>(),
            static variableDesignationSyntax => variableDesignationSyntax.Identifier,
            SourceContextSymbolTokenKind.Variable,
            builder);
        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<ForEachStatementSyntax>(),
            static forEachStatementSyntax => forEachStatementSyntax.Identifier,
            SourceContextSymbolTokenKind.Variable,
            builder);
        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<CatchDeclarationSyntax>(),
            static catchDeclarationSyntax => catchDeclarationSyntax.Identifier,
            SourceContextSymbolTokenKind.Variable,
            builder);
        AddDeclaredSymbolOccurrences(
            semanticModel,
            rootNode.DescendantNodesAndSelf().OfType<LocalFunctionStatementSyntax>(),
            static localFunctionStatementSyntax => localFunctionStatementSyntax.Identifier,
            SourceContextSymbolTokenKind.Method,
            builder);

        return builder.ToImmutable();
    }

    private static void AddDeclaredSymbolOccurrences<TSyntax>(
        SemanticModel semanticModel,
        IEnumerable<TSyntax> syntaxNodes,
        Func<TSyntax, SyntaxToken> tokenSelector,
        SourceContextSymbolTokenKind tokenKind,
        ImmutableArray<SourceContextSymbolOccurrence>.Builder builder)
        where TSyntax : SyntaxNode
    {
        foreach (var syntaxNode in syntaxNodes)
        {
            var token = tokenSelector(syntaxNode);
            if (!TryGetRawSpan(token, out var rawStart, out var rawLength))
            {
                continue;
            }

            var declaredSymbol = semanticModel.GetDeclaredSymbol(syntaxNode);
            if (declaredSymbol is null)
            {
                continue;
            }

            builder.Add(new SourceContextSymbolOccurrence(
                NormalizeSymbol(declaredSymbol),
                rawStart,
                rawLength,
                IsDeclaration: true,
                tokenKind));
        }
    }

    private static bool TryGetRawSpan(SyntaxToken token, out int rawStart, out int rawLength)
    {
        rawStart = 0;
        rawLength = 0;
        var rawSpanAnnotation = token.GetAnnotations(CSharpSourceContextExpressionBuilder.RawSpanAnnotationKind)
            .FirstOrDefault();
        if (rawSpanAnnotation is null ||
            !CSharpSourceContextExpressionBuilder.TryParseRawSpanAnnotation(rawSpanAnnotation, out rawStart, out rawLength))
        {
            return false;
        }

        return rawLength > 0;
    }

    private static bool ShouldTrackReferenceSymbol(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        if (symbol.IsStatic && symbol is not INamedTypeSymbol)
        {
            return false;
        }

        return symbol is IPropertySymbol or IFieldSymbol or IMethodSymbol or INamedTypeSymbol;
    }

    private static bool TryMapTrackedSymbol(
        ISymbol? symbol,
        out SourceContextSymbolTokenKind tokenKind)
    {
        tokenKind = default;
        if (symbol is null)
        {
            return false;
        }

        if (symbol.IsStatic && symbol is not INamedTypeSymbol)
        {
            return false;
        }

        switch (symbol)
        {
            case IPropertySymbol:
            case IFieldSymbol:
                tokenKind = SourceContextSymbolTokenKind.Property;
                return true;
            case IMethodSymbol:
                tokenKind = SourceContextSymbolTokenKind.Method;
                return true;
            case INamedTypeSymbol:
                tokenKind = SourceContextSymbolTokenKind.Type;
                return true;
            case IParameterSymbol:
                tokenKind = SourceContextSymbolTokenKind.Parameter;
                return true;
            case ILocalSymbol:
            case IRangeVariableSymbol:
                tokenKind = SourceContextSymbolTokenKind.Variable;
                return true;
            default:
                return false;
        }
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

    private static string GetTypeName(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        return (typeSymbol ?? compilation.ObjectType).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string BuildLambdaParameterList(ImmutableArray<IParameterSymbol> parameters)
    {
        if (parameters.Length == 0)
        {
            return string.Empty;
        }

        if (parameters.Length == 1)
        {
            return "sender";
        }

        if (parameters.Length == 2)
        {
            return "sender, e";
        }

        var parts = new string[parameters.Length];
        for (var index = 0; index < parameters.Length; index++)
        {
            parts[index] = "arg" + index.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(", ", parts);
    }

    private static string BuildInlineEventLambdaBody(int parameterCount, BlockSyntax statements)
    {
        var builder = new StringBuilder();
        foreach (var statement in statements.Statements)
        {
            builder.Append(statement.ToFullString());
        }

        var trimmedStatements = builder.ToString().Trim();
        return parameterCount switch
        {
            0 => "object? sender = null; object? e = null; " + trimmedStatements,
            1 => "object? e = null; " + trimmedStatements,
            2 => trimmedStatements,
            _ => "var sender = arg0; var e = arg1; " + trimmedStatements
        };
    }

    private sealed class InlineCodeLocalNameCollector : CSharpSyntaxWalker
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            AddName(node.Parameter.Identifier.ValueText);
            base.VisitSimpleLambdaExpression(node);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            foreach (var parameter in node.ParameterList.Parameters)
            {
                AddName(parameter.Identifier.ValueText);
            }

            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            if (node.ParameterList is not null)
            {
                foreach (var parameter in node.ParameterList.Parameters)
                {
                    AddName(parameter.Identifier.ValueText);
                }
            }

            base.VisitAnonymousMethodExpression(node);
        }

        public override void VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitVariableDeclarator(node);
        }

        public override void VisitForEachStatement(ForEachStatementSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitForEachStatement(node);
        }

        public override void VisitCatchDeclaration(CatchDeclarationSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitCatchDeclaration(node);
        }

        public override void VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitSingleVariableDesignation(node);
        }

        private void AddName(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Names.Add(name!);
            }
        }
    }

    private sealed class InlineContextExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly ImmutableHashSet<string> _sourceMemberNames;
        private readonly ImmutableHashSet<string> _rootMemberNames;
        private readonly ImmutableHashSet<string> _targetMemberNames;
        private readonly ImmutableHashSet<string> _localNames;
        private readonly Stack<HashSet<string>> _scopes = new();

        public InlineContextExpressionRewriter(
            ImmutableHashSet<string> sourceMemberNames,
            ImmutableHashSet<string> rootMemberNames,
            ImmutableHashSet<string> targetMemberNames,
            ImmutableHashSet<string> localNames)
        {
            _sourceMemberNames = sourceMemberNames;
            _rootMemberNames = rootMemberNames;
            _targetMemberNames = targetMemberNames;
            _localNames = localNames;
        }

        public HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            PushScope(node.Parameter.Identifier.ValueText);
            var rewritten = base.VisitSimpleLambdaExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            PushScope(node.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText));
            var rewritten = base.VisitParenthesizedLambdaExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            var parameters = node.ParameterList is null
                ? Enumerable.Empty<string>()
                : node.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText);
            PushScope(parameters);
            var rewritten = base.VisitAnonymousMethodExpression(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            if (name.Length == 0 ||
                name.Equals("source", StringComparison.Ordinal) ||
                name.Equals("root", StringComparison.Ordinal) ||
                name.Equals("target", StringComparison.Ordinal) ||
                _localNames.Contains(name) ||
                IsScopedName(name) ||
                node.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NameColonSyntax)
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            var rewriteTarget = ResolveTarget(name);
            if (rewriteTarget is null)
            {
                return base.VisitIdentifierName(node);
            }

            if (string.Equals(rewriteTarget, "source", StringComparison.Ordinal))
            {
                Dependencies.Add(name);
            }

            var rewrittenName = SyntaxFactory.IdentifierName(name)
                .WithTriviaFrom(node)
                .WithAdditionalAnnotations(node.GetAnnotations(CSharpSourceContextExpressionBuilder.RawSpanAnnotationKind));
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(rewriteTarget),
                    rewrittenName)
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                string.Equals(identifier.Identifier.ValueText, "source", StringComparison.Ordinal))
            {
                Dependencies.Add(node.Name.Identifier.ValueText);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                string.Equals(identifier.Identifier.ValueText, "source", StringComparison.Ordinal) &&
                node.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                memberBinding.Name is SimpleNameSyntax memberName)
            {
                Dependencies.Add(memberName.Identifier.ValueText);
            }

            return base.VisitConditionalAccessExpression(node);
        }

        private string? ResolveTarget(string name)
        {
            if (_sourceMemberNames.Contains(name))
            {
                return "source";
            }

            if (_rootMemberNames.Contains(name))
            {
                return "root";
            }

            if (_targetMemberNames.Contains(name))
            {
                return "target";
            }

            return null;
        }

        private void PushScope(IEnumerable<string> names)
        {
            var scope = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    scope.Add(name);
                }
            }

            _scopes.Push(scope);
        }

        private void PushScope(string? singleName)
        {
            PushScope(singleName is null ? Enumerable.Empty<string>() : new[] { singleName });
        }

        private bool IsScopedName(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }

        private void PopScope()
        {
            if (_scopes.Count > 0)
            {
                _scopes.Pop();
            }
        }
    }
}
