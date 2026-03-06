using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace XamlToCSharpGenerator.ExpressionSemantics;

public readonly struct SourceContextExpressionBuildResult
{
    public SourceContextExpressionBuildResult(
        string accessorExpression,
        ImmutableArray<string> dependencyNames)
    {
        AccessorExpression = accessorExpression ?? string.Empty;
        DependencyNames = dependencyNames.IsDefault ? ImmutableArray<string>.Empty : dependencyNames;
    }

    public string AccessorExpression { get; }

    public ImmutableArray<string> DependencyNames { get; }
}

internal readonly struct SourceContextExpressionRewriteResult
{
    public SourceContextExpressionRewriteResult(
        ExpressionSyntax rewrittenExpressionSyntax,
        ImmutableArray<string> dependencyNames)
    {
        RewrittenExpressionSyntax = rewrittenExpressionSyntax;
        DependencyNames = dependencyNames.IsDefault ? ImmutableArray<string>.Empty : dependencyNames;
    }

    public ExpressionSyntax RewrittenExpressionSyntax { get; }

    public ImmutableArray<string> DependencyNames { get; }
}

public static class CSharpSourceContextExpressionBuilder
{
    internal const string RawSpanAnnotationKind = "AXSGExpressionRawSpan";

    public static bool TryBuildAccessorExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string rawExpression,
        string sourceParameterName,
        out SourceContextExpressionBuildResult result,
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

        if (string.IsNullOrWhiteSpace(sourceParameterName))
        {
            errorMessage = "source parameter name is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "expression text is empty";
            return false;
        }

        if (!TryRewriteAccessorExpression(
                sourceType,
                rawExpression,
                sourceParameterName,
                out var rewriteResult,
                out errorMessage))
        {
            return false;
        }

        var rewrittenExpression = rewriteResult.RewrittenExpressionSyntax.ToFullString().Trim();
        if (!TryValidateGeneratedExpression(compilation, sourceType, sourceParameterName, rewrittenExpression, out errorMessage))
        {
            return false;
        }

        result = new SourceContextExpressionBuildResult(
            rewrittenExpression,
            rewriteResult.DependencyNames);
        return true;
    }

    internal static bool TryRewriteAccessorExpression(
        INamedTypeSymbol sourceType,
        string rawExpression,
        string sourceParameterName,
        out SourceContextExpressionRewriteResult result,
        out string errorMessage)
    {
        if (sourceType is null)
        {
            throw new ArgumentNullException(nameof(sourceType));
        }

        result = default;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(sourceParameterName))
        {
            errorMessage = "source parameter name is empty";
            return false;
        }

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "expression text is empty";
            return false;
        }

        var normalizedExpression = rawExpression.Trim();
        var parsedExpression = SyntaxFactory.ParseExpression(normalizedExpression);
        var parseDiagnostic = parsedExpression
            .GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var annotatedExpression = AnnotateSimpleNames(parsedExpression);
        var sourceMemberNames = GetExpressionSourceMemberNames(sourceType);
        var expressionLocalNames = GetExpressionLocalNames(annotatedExpression);
        var rewriter = new SourceContextExpressionRewriter(
            sourceMemberNames,
            expressionLocalNames,
            sourceParameterName);
        if (rewriter.Visit(annotatedExpression) is not ExpressionSyntax rewrittenExpressionSyntax)
        {
            errorMessage = "expression rewrite failed";
            return false;
        }

        var rewrittenExpression = rewrittenExpressionSyntax.ToFullString().Trim();
        if (rewrittenExpression.Length == 0)
        {
            errorMessage = "expression rewrite produced an empty expression";
            return false;
        }

        result = new SourceContextExpressionRewriteResult(
            rewrittenExpressionSyntax,
            rewriter.Dependencies
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToImmutableArray());
        return true;
    }

    private static ImmutableHashSet<string> GetExpressionLocalNames(ExpressionSyntax expression)
    {
        var collector = new ExpressionLocalNameCollector();
        collector.Visit(expression);
        return collector.Names.ToImmutableHashSet(StringComparer.Ordinal);
    }

    internal static SyntaxAnnotation CreateRawSpanAnnotation(int start, int length)
    {
        return new SyntaxAnnotation(
            RawSpanAnnotationKind,
            start.ToString(CultureInfo.InvariantCulture) + ":" + length.ToString(CultureInfo.InvariantCulture));
    }

    internal static bool TryParseRawSpanAnnotation(
        SyntaxAnnotation annotation,
        out int start,
        out int length)
    {
        start = 0;
        length = 0;
        if (!string.Equals(annotation.Kind, RawSpanAnnotationKind, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(annotation.Data))
        {
            return false;
        }

        var annotationData = annotation.Data!;
        var separatorIndex = annotationData.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= annotationData.Length - 1)
        {
            return false;
        }

        return int.TryParse(annotationData.Substring(0, separatorIndex), NumberStyles.Integer, CultureInfo.InvariantCulture, out start) &&
               int.TryParse(annotationData.Substring(separatorIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out length);
    }

    private static ExpressionSyntax AnnotateSimpleNames(ExpressionSyntax expression)
    {
        return expression.ReplaceNodes(
            expression.DescendantNodesAndSelf().OfType<SimpleNameSyntax>(),
            static (originalNode, _) => originalNode.WithAdditionalAnnotations(
                CreateRawSpanAnnotation(originalNode.SpanStart, originalNode.Span.Length)));
    }

    private static ImmutableHashSet<string> GetExpressionSourceMemberNames(INamedTypeSymbol sourceType)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        for (INamedTypeSymbol? current = sourceType; current is not null; current = current.BaseType)
        {
            AddExpressionSourceMemberNames(current, builder);
        }

        foreach (var interfaceType in sourceType.AllInterfaces)
        {
            AddExpressionSourceMemberNames(interfaceType, builder);
        }

        return builder.ToImmutable();
    }

    private static void AddExpressionSourceMemberNames(
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

    private static bool TryValidateGeneratedExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string sourceParameterName,
        string expression,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_ExpressionValidation",
            "{",
            "    internal static class __ExpressionValidator",
            "    {",
            "        internal static object? __Evaluate(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " " +
            sourceParameterName +
            ") => (object?)(" +
            expression +
            ");",
            "    }",
            "}");

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationCompilation = compilation.AddSyntaxTrees(validationTree);
        var validationDiagnostic = validationCompilation.GetDiagnostics()
            .FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == validationTree);
        if (validationDiagnostic is null)
        {
            return true;
        }

        errorMessage = validationDiagnostic.GetMessage(CultureInfo.InvariantCulture);
        return false;
    }

    private sealed class ExpressionLocalNameCollector : CSharpSyntaxWalker
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

        public override void VisitFromClause(FromClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitFromClause(node);
        }

        public override void VisitLetClause(LetClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitLetClause(node);
        }

        public override void VisitJoinClause(JoinClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitJoinClause(node);
        }

        public override void VisitJoinIntoClause(JoinIntoClauseSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitJoinIntoClause(node);
        }

        public override void VisitQueryContinuation(QueryContinuationSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitQueryContinuation(node);
        }

        public override void VisitDeclarationExpression(DeclarationExpressionSyntax node)
        {
            AddVariableDesignation(node.Designation);
            base.VisitDeclarationExpression(node);
        }

        public override void VisitDeclarationPattern(DeclarationPatternSyntax node)
        {
            AddVariableDesignation(node.Designation);
            base.VisitDeclarationPattern(node);
        }

        private void AddVariableDesignation(VariableDesignationSyntax designation)
        {
            switch (designation)
            {
                case SingleVariableDesignationSyntax single:
                    AddName(single.Identifier.ValueText);
                    break;
                case ParenthesizedVariableDesignationSyntax parenthesized:
                {
                    foreach (var variable in parenthesized.Variables)
                    {
                        AddVariableDesignation(variable);
                    }

                    break;
                }
            }
        }

        private void AddName(string? name)
        {
            if (name is null)
            {
                return;
            }

            if (name.Trim().Length == 0)
            {
                return;
            }

            Names.Add(name);
        }
    }

    private sealed class SourceContextExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly ImmutableHashSet<string> _sourceMemberNames;
        private readonly ImmutableHashSet<string> _localNames;
        private readonly string _sourceParameterName;
        private readonly Stack<HashSet<string>> _scopes = new();

        public SourceContextExpressionRewriter(
            ImmutableHashSet<string> sourceMemberNames,
            ImmutableHashSet<string> localNames,
            string sourceParameterName)
        {
            _sourceMemberNames = sourceMemberNames;
            _localNames = localNames;
            _sourceParameterName = sourceParameterName;
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
                name.Equals(_sourceParameterName, StringComparison.Ordinal) ||
                _localNames.Contains(name) ||
                IsScopedName(name) ||
                !_sourceMemberNames.Contains(name))
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name == node)
            {
                return base.VisitIdentifierName(node);
            }

            if (node.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax or NameColonSyntax)
            {
                return base.VisitIdentifierName(node);
            }

            Dependencies.Add(name);
            var rewrittenName = SyntaxFactory.IdentifierName(name)
                .WithTriviaFrom(node)
                .WithAdditionalAnnotations(node.GetAnnotations(RawSpanAnnotationKind));
            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(_sourceParameterName),
                    rewrittenName)
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText.Equals(_sourceParameterName, StringComparison.Ordinal))
            {
                Dependencies.Add(node.Name.Identifier.ValueText);
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
        {
            if (node.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.ValueText.Equals(_sourceParameterName, StringComparison.Ordinal) &&
                node.WhenNotNull is MemberBindingExpressionSyntax memberBinding &&
                memberBinding.Name is SimpleNameSyntax memberName)
            {
                Dependencies.Add(memberName.Identifier.ValueText);
            }

            return base.VisitConditionalAccessExpression(node);
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
            if (singleName is null)
            {
                PushScope(Enumerable.Empty<string>());
                return;
            }

            var name = singleName.Trim();
            if (name.Length == 0)
            {
                PushScope(Enumerable.Empty<string>());
                return;
            }

            PushScope(new[] { name });
        }

        private void PopScope()
        {
            if (_scopes.Count > 0)
            {
                _scopes.Pop();
            }
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
    }
}
