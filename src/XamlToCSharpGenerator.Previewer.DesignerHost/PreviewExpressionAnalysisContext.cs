using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class PreviewExpressionAnalysisContext
{
    private static readonly ConcurrentDictionary<Assembly, PreviewExpressionAnalysisContext> Cache = new();

    private readonly ConcurrentDictionary<Type, ImmutableHashSet<string>> _memberNamesCache = new();

    private PreviewExpressionAnalysisContext(Assembly localAssembly)
    {
    }

    public static PreviewExpressionAnalysisContext ForAssembly(Assembly localAssembly)
    {
        ArgumentNullException.ThrowIfNull(localAssembly);
        return Cache.GetOrAdd(localAssembly, static assembly => new PreviewExpressionAnalysisContext(assembly));
    }

    public bool TryRewriteSourceContextExpression(
        Type sourceType,
        string rawExpression,
        out string rewrittenExpression,
        out IReadOnlyList<string> dependencyNames,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        return TryRewritePreviewExpression(
            sourceType,
            rootType: null,
            targetType: null,
            rawExpression,
            out rewrittenExpression,
            out dependencyNames,
            out errorMessage);
    }

    public bool TryRewritePreviewExpression(
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        string rawExpression,
        out string rewrittenExpression,
        out IReadOnlyList<string> dependencyNames,
        out string errorMessage)
    {
        rewrittenExpression = string.Empty;
        dependencyNames = Array.Empty<string>();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawExpression))
        {
            errorMessage = "Expression must not be empty.";
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(rawExpression.Trim());
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        if (!TryRewriteSyntax(
                parsedExpression,
                sourceType,
                rootType,
                targetType,
                out ExpressionSyntax rewrittenSyntax,
                out var dependencies))
        {
            errorMessage = "Expression rewrite failed.";
            return false;
        }

        rewrittenExpression = rewrittenSyntax.ToFullString().Trim();
        dependencyNames = ToOrderedDependencyArray(dependencies);
        return true;
    }

    public bool TryRewritePreviewLambda(
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        string rawLambdaExpression,
        out string rewrittenExpression,
        out IReadOnlyList<string> dependencyNames,
        out string errorMessage)
    {
        rewrittenExpression = string.Empty;
        dependencyNames = Array.Empty<string>();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawLambdaExpression))
        {
            errorMessage = "Lambda must not be empty.";
            return false;
        }

        var normalizedLambdaExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawLambdaExpression.Trim());
        var parsedExpression = SyntaxFactory.ParseExpression(normalizedLambdaExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        if (parsedExpression is not AnonymousFunctionExpressionSyntax lambdaSyntax)
        {
            errorMessage = "Expression is not a lambda expression.";
            return false;
        }

        if (!TryRewriteSyntax(
                lambdaSyntax,
                sourceType,
                rootType,
                targetType,
                out AnonymousFunctionExpressionSyntax rewrittenSyntax,
                out var dependencies))
        {
            errorMessage = "Lambda rewrite failed.";
            return false;
        }

        rewrittenExpression = rewrittenSyntax.ToFullString().Trim();
        dependencyNames = ToOrderedDependencyArray(dependencies);
        return true;
    }

    public bool TryRewritePreviewStatements(
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        string rawStatements,
        out string rewrittenStatements,
        out IReadOnlyList<string> dependencyNames,
        out string errorMessage)
    {
        rewrittenStatements = string.Empty;
        dependencyNames = Array.Empty<string>();
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawStatements))
        {
            errorMessage = "Event code block must not be empty.";
            return false;
        }

        var normalizedStatements = rawStatements.Trim();
        var wrapperPrefix = "{" + Environment.NewLine;
        var parsedStatement = SyntaxFactory.ParseStatement(wrapperPrefix + normalizedStatements + Environment.NewLine + "}");
        var parseDiagnostic = parsedStatement.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        if (parseDiagnostic is not null || parsedStatement is not BlockSyntax blockSyntax)
        {
            errorMessage = parseDiagnostic?.GetMessage(CultureInfo.InvariantCulture) ?? "Event code block parse failed.";
            return false;
        }

        if (!TryRewriteSyntax(
                blockSyntax,
                sourceType,
                rootType,
                targetType,
                out BlockSyntax rewrittenSyntax,
                out var dependencies))
        {
            errorMessage = "Event code block rewrite failed.";
            return false;
        }

        rewrittenStatements = string.Concat(rewrittenSyntax.Statements.Select(static statement => statement.ToFullString())).Trim();
        dependencyNames = ToOrderedDependencyArray(dependencies);
        return true;
    }

    private bool TryRewriteSyntax<TSyntax>(
        TSyntax syntax,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        out TSyntax rewrittenSyntax,
        out HashSet<string> dependencies)
        where TSyntax : SyntaxNode
    {
        rewrittenSyntax = null!;
        dependencies = new HashSet<string>(StringComparer.Ordinal);

        var rewriter = new PreviewContextExpressionRewriter(
            GetMemberNames(sourceType),
            GetMemberNames(rootType),
            GetMemberNames(targetType),
            GetLocalNames(syntax));
        if (rewriter.Visit(syntax) is not TSyntax rewritten)
        {
            return false;
        }

        rewrittenSyntax = rewritten;
        dependencies = rewriter.Dependencies;
        return true;
    }

    private ImmutableHashSet<string> GetMemberNames(Type? type)
    {
        if (type is null)
        {
            return ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
        }

        return _memberNamesCache.GetOrAdd(type, static candidate =>
        {
            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            for (var current = candidate; current is not null; current = current.BaseType)
            {
                AddMemberNames(current, builder);
            }

            var interfaces = candidate.GetInterfaces();
            for (var index = 0; index < interfaces.Length; index++)
            {
                AddMemberNames(interfaces[index], builder);
            }

            return builder.ToImmutable();
        });
    }

    private static void AddMemberNames(Type type, ImmutableHashSet<string>.Builder builder)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var propertyInfo in type.GetProperties(flags))
        {
            if (propertyInfo.GetMethod is not null &&
                !propertyInfo.GetMethod.IsStatic)
            {
                builder.Add(propertyInfo.Name);
            }
        }

        foreach (var fieldInfo in type.GetFields(flags))
        {
            if (!fieldInfo.IsStatic)
            {
                builder.Add(fieldInfo.Name);
            }
        }

        foreach (var methodInfo in type.GetMethods(flags))
        {
            if (!methodInfo.IsStatic &&
                !methodInfo.IsSpecialName)
            {
                builder.Add(methodInfo.Name);
            }
        }
    }

    private static ImmutableHashSet<string> GetLocalNames(SyntaxNode node)
    {
        var collector = new PreviewLocalNameCollector();
        collector.Visit(node);
        return collector.Names.ToImmutableHashSet(StringComparer.Ordinal);
    }

    private static IReadOnlyList<string> ToOrderedDependencyArray(HashSet<string> dependencies)
    {
        return dependencies.Count == 0
            ? Array.Empty<string>()
            : dependencies.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    private sealed class PreviewLocalNameCollector : CSharpSyntaxWalker
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);

        public override void VisitParameter(ParameterSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitParameter(node);
        }

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

        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            AddName(node.Identifier.ValueText);
            base.VisitLocalFunctionStatement(node);
        }

        private void AddName(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                Names.Add(name!);
            }
        }
    }

    private sealed class PreviewContextExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly ImmutableHashSet<string> _sourceMemberNames;
        private readonly ImmutableHashSet<string> _rootMemberNames;
        private readonly ImmutableHashSet<string> _targetMemberNames;
        private readonly ImmutableHashSet<string> _localNames;
        private readonly Stack<HashSet<string>> _scopes = new();

        public PreviewContextExpressionRewriter(
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

            var rewrittenName = SyntaxFactory.IdentifierName(name);
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
            var scope = new HashSet<string>(names.Where(static name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
            _scopes.Push(scope);
        }

        private void PushScope(string? name)
        {
            PushScope(name is null ? Enumerable.Empty<string>() : [name]);
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
