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
            GetMemberNames(targetType));
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

    private static IReadOnlyList<string> ToOrderedDependencyArray(HashSet<string> dependencies)
    {
        return dependencies.Count == 0
            ? Array.Empty<string>()
            : dependencies.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    private sealed class PreviewContextExpressionRewriter : CSharpSyntaxRewriter
    {
        private readonly ImmutableHashSet<string> _sourceMemberNames;
        private readonly ImmutableHashSet<string> _rootMemberNames;
        private readonly ImmutableHashSet<string> _targetMemberNames;
        private readonly Stack<HashSet<string>> _scopes = new();

        public PreviewContextExpressionRewriter(
            ImmutableHashSet<string> sourceMemberNames,
            ImmutableHashSet<string> rootMemberNames,
            ImmutableHashSet<string> targetMemberNames)
        {
            _sourceMemberNames = sourceMemberNames;
            _rootMemberNames = rootMemberNames;
            _targetMemberNames = targetMemberNames;
            _scopes.Push(new HashSet<string>(StringComparer.Ordinal));
        }

        public HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            PushScope(GetLocalFunctionNames(node));
            var rewritten = base.VisitBlock(node);
            PopScope();
            return rewritten;
        }

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

        public override SyntaxNode? VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
        {
            PushScope(node.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText));
            var rewritten = base.VisitLocalFunctionStatement(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitVariableDeclarator(VariableDeclaratorSyntax node)
        {
            AddNameToCurrentScope(node.Identifier.ValueText);
            return base.VisitVariableDeclarator(node);
        }

        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            PushScope(node.Declaration?.Variables.Select(static variable => variable.Identifier.ValueText) ?? []);
            var rewritten = base.VisitForStatement(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            ExpressionSyntax rewrittenExpression = (ExpressionSyntax)Visit(node.Expression)!;
            PushScope(node.Identifier.ValueText);
            StatementSyntax rewrittenStatement = (StatementSyntax)Visit(node.Statement)!;
            PopScope();

            return node
                .WithExpression(rewrittenExpression)
                .WithStatement(rewrittenStatement);
        }

        public override SyntaxNode? VisitUsingStatement(UsingStatementSyntax node)
        {
            PushScope(node.Declaration?.Variables.Select(static variable => variable.Identifier.ValueText) ?? []);
            var rewritten = base.VisitUsingStatement(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitFixedStatement(FixedStatementSyntax node)
        {
            PushScope(node.Declaration.Variables.Select(static variable => variable.Identifier.ValueText));
            var rewritten = base.VisitFixedStatement(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitCatchClause(CatchClauseSyntax node)
        {
            PushScope(node.Declaration?.Identifier.ValueText);
            var rewritten = base.VisitCatchClause(node);
            PopScope();
            return rewritten;
        }

        public override SyntaxNode? VisitQueryExpression(QueryExpressionSyntax node)
        {
            FromClauseSyntax rewrittenFromClause = node.FromClause
                .WithExpression((ExpressionSyntax)Visit(node.FromClause.Expression)!);

            PushScope(node.FromClause.Identifier.ValueText);
            QueryBodySyntax rewrittenBody = RewriteQueryBody(node.Body);
            PopScope();

            return node
                .WithFromClause(rewrittenFromClause)
                .WithBody(rewrittenBody);
        }

        public override SyntaxNode? VisitSingleVariableDesignation(SingleVariableDesignationSyntax node)
        {
            AddNameToCurrentScope(node.Identifier.ValueText);
            return base.VisitSingleVariableDesignation(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.ValueText;
            if (name.Length == 0 ||
                name.Equals("source", StringComparison.Ordinal) ||
                name.Equals("root", StringComparison.Ordinal) ||
                name.Equals("target", StringComparison.Ordinal) ||
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
            if (_scopes.Count > 1)
            {
                _scopes.Pop();
            }
        }

        private HashSet<string> DetachCurrentScope()
        {
            return _scopes.Pop();
        }

        private void RestoreScope(HashSet<string> scope)
        {
            _scopes.Push(scope);
        }

        private void AddNameToCurrentScope(string? name)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                _scopes.Peek().Add(name!);
            }
        }

        private static IEnumerable<string> GetLocalFunctionNames(BlockSyntax block)
        {
            return block.Statements
                .OfType<LocalFunctionStatementSyntax>()
                .Select(static statement => statement.Identifier.ValueText);
        }

        private QueryBodySyntax RewriteQueryBody(QueryBodySyntax node)
        {
            var rewrittenClauses = new SyntaxList<QueryClauseSyntax>();
            for (var index = 0; index < node.Clauses.Count; index++)
            {
                rewrittenClauses = rewrittenClauses.Add(RewriteQueryClause(node.Clauses[index]));
            }

            SelectOrGroupClauseSyntax rewrittenSelectOrGroup = (SelectOrGroupClauseSyntax)Visit(node.SelectOrGroup)!;
            QueryContinuationSyntax? rewrittenContinuation = null;
            if (node.Continuation is not null)
            {
                var currentScope = DetachCurrentScope();
                PushScope(node.Continuation.Identifier.ValueText);
                QueryBodySyntax rewrittenContinuationBody = RewriteQueryBody(node.Continuation.Body);
                PopScope();
                RestoreScope(currentScope);

                rewrittenContinuation = node.Continuation.WithBody(rewrittenContinuationBody);
            }

            return node
                .WithClauses(rewrittenClauses)
                .WithSelectOrGroup(rewrittenSelectOrGroup)
                .WithContinuation(rewrittenContinuation);
        }

        private QueryClauseSyntax RewriteQueryClause(QueryClauseSyntax clause)
        {
            return clause switch
            {
                FromClauseSyntax fromClause => RewriteFromClause(fromClause),
                LetClauseSyntax letClause => RewriteLetClause(letClause),
                JoinClauseSyntax joinClause => RewriteJoinClause(joinClause),
                _ => (QueryClauseSyntax)Visit(clause)!
            };
        }

        private FromClauseSyntax RewriteFromClause(FromClauseSyntax node)
        {
            FromClauseSyntax rewritten = node
                .WithExpression((ExpressionSyntax)Visit(node.Expression)!);
            AddNameToCurrentScope(node.Identifier.ValueText);
            return rewritten;
        }

        private LetClauseSyntax RewriteLetClause(LetClauseSyntax node)
        {
            LetClauseSyntax rewritten = node
                .WithExpression((ExpressionSyntax)Visit(node.Expression)!);
            AddNameToCurrentScope(node.Identifier.ValueText);
            return rewritten;
        }

        private JoinClauseSyntax RewriteJoinClause(JoinClauseSyntax node)
        {
            ExpressionSyntax rewrittenInExpression = (ExpressionSyntax)Visit(node.InExpression)!;
            ExpressionSyntax rewrittenLeftExpression = (ExpressionSyntax)Visit(node.LeftExpression)!;

            PushScope(node.Identifier.ValueText);
            ExpressionSyntax rewrittenRightExpression = (ExpressionSyntax)Visit(node.RightExpression)!;
            PopScope();

            if (node.Into is null)
            {
                AddNameToCurrentScope(node.Identifier.ValueText);
            }
            else
            {
                AddNameToCurrentScope(node.Into.Identifier.ValueText);
            }

            return node
                .WithInExpression(rewrittenInExpression)
                .WithLeftExpression(rewrittenLeftExpression)
                .WithRightExpression(rewrittenRightExpression);
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
