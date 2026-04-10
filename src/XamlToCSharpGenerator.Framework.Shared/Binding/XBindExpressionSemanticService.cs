using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class XBindExpressionSemanticService
{
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string typeToken,
        string? fallbackClrNamespace);

    public delegate INamedTypeSymbol? ResolveTypeSymbolDelegate(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName);

    public delegate bool IsNameScopeBoundaryDelegate(XamlObjectNode node);

    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly ResolveTypeSymbolDelegate _resolveTypeSymbol;
    private readonly IsNameScopeBoundaryDelegate _isNameScopeBoundary;
    private readonly Func<string, string> _escape;

    public XBindExpressionSemanticService(
        ResolveTypeTokenDelegate resolveTypeToken,
        ResolveTypeSymbolDelegate resolveTypeSymbol,
        IsNameScopeBoundaryDelegate isNameScopeBoundary,
        Func<string, string> escape)
    {
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _resolveTypeSymbol = resolveTypeSymbol ?? throw new ArgumentNullException(nameof(resolveTypeSymbol));
        _isNameScopeBoundary = isNameScopeBoundary ?? throw new ArgumentNullException(nameof(isNameScopeBoundary));
        _escape = escape ?? throw new ArgumentNullException(nameof(escape));
    }

    public bool TryLowerExpression(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        return TryLowerExpressionCore(
            expression,
            loweringContext,
            allowAssignmentTarget: false,
            out loweredExpression,
            out errorMessage);
    }

    public bool TryBuildAssignmentExpression(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        string valueExpression,
        out string assignmentExpression,
        out string errorMessage)
    {
        assignmentExpression = string.Empty;
        errorMessage = string.Empty;

        if (!CanAssignTo(expression))
        {
            errorMessage = "x:Bind expression does not resolve to an assignable target.";
            return false;
        }

        if (!TryLowerExpressionCore(
                expression,
                loweringContext,
                allowAssignmentTarget: true,
                out var loweredExpression,
                out errorMessage))
        {
            return false;
        }

        if (loweredExpression.IsTypeReference)
        {
            errorMessage = "x:Bind expression resolves to a type reference and cannot be assigned.";
            return false;
        }

        assignmentExpression = loweredExpression.Expression + " = " + valueExpression;
        return true;
    }

    public ImmutableArray<XBindPathReference> CollectDependencies(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext)
    {
        var builder = ImmutableHashSet.CreateBuilder<XBindPathReference>();
        CollectDependenciesCore(expression, loweringContext, builder);
        return builder.ToImmutableArray();
    }

    public bool IsMainSourceReference(XBindPathReference candidate, XBindPathReference mainSourceReference)
    {
        return candidate.Equals(mainSourceReference);
    }

    public string BuildPathReferenceExpression(XBindPathReference sourceReference)
    {
        return "new global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency(" +
               BuildSourceKindExpression(sourceReference.Kind) +
               ", " +
               BuildNullableStringLiteral(sourceReference.Path) +
               ", " +
               BuildNullableStringLiteral(sourceReference.ElementName) +
               ", " +
               (string.IsNullOrWhiteSpace(sourceReference.RelativeSourceExpression)
                   ? "null"
                   : sourceReference.RelativeSourceExpression) +
               ", " +
               (string.IsNullOrWhiteSpace(sourceReference.SourceExpression)
                   ? "null"
                   : sourceReference.SourceExpression) +
               ")";
    }

    public string BuildPathReferenceArrayLiteral(IEnumerable<XBindPathReference> dependencies)
    {
        var items = dependencies.ToImmutableArray();
        if (items.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenBindingDependency[] { " +
               string.Join(", ", items.Select(BuildPathReferenceExpression)) +
               " }";
    }

    public IEnumerable<string> BuildEventCandidateBodies(
        XBindExpressionNode expression,
        string loweredTargetExpression,
        ImmutableArray<string> lambdaParameterNames)
    {
        var candidates = new List<string>();
        void AddCandidate(string candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !candidates.Contains(candidate, StringComparer.Ordinal))
            {
                candidates.Add(candidate);
            }
        }

        AddCandidate(loweredTargetExpression + "()");
        if (lambdaParameterNames.Length > 0)
        {
            AddCandidate(loweredTargetExpression + "(" + string.Join(", ", lambdaParameterNames) + ")");
            for (var count = 1; count <= lambdaParameterNames.Length; count++)
            {
                AddCandidate(loweredTargetExpression + "(" + string.Join(", ", lambdaParameterNames.Take(count)) + ")");
            }
        }

        if (expression is XBindInvocationExpression)
        {
            AddCandidate(loweredTargetExpression);
        }

        return candidates;
    }

    public bool TryResolveNamedElementType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        string elementName,
        out INamedTypeSymbol? typeSymbol)
    {
        typeSymbol = null;
        if (string.IsNullOrWhiteSpace(elementName))
        {
            return false;
        }

        var scopeRoot = FindVisibleNameScopeRoot(document.RootObject, currentNode) ?? document.RootObject;
        if (TryResolveNamedElementTypeInScopeCore(compilation, scopeRoot, elementName, out typeSymbol))
        {
            return true;
        }

        return !Equals(scopeRoot, document.RootObject) &&
               TryResolveNamedElementTypeInScopeCore(compilation, document.RootObject, elementName, out typeSymbol);
    }

    private bool TryResolveNamedElementTypeInScopeCore(
        Compilation compilation,
        XamlObjectNode scopeRoot,
        string elementName,
        out INamedTypeSymbol? typeSymbol)
    {
        foreach (var candidate in EnumerateVisibleNameScopeElements(scopeRoot, scopeRoot))
        {
            if (!string.Equals(candidate.Name, elementName, StringComparison.Ordinal))
            {
                continue;
            }

            typeSymbol = _resolveTypeSymbol(compilation, candidate.XmlNamespace, candidate.XmlTypeName);
            return typeSymbol is not null;
        }

        typeSymbol = null;
        return false;
    }

    private bool TryLowerExpressionCore(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        bool allowAssignmentTarget,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        switch (expression)
        {
            case XBindIdentifierExpression identifierExpression:
                return TryLowerIdentifierExpression(
                    identifierExpression,
                    loweringContext,
                    out loweredExpression,
                    out errorMessage);

            case XBindTypeReferenceExpression typeReferenceExpression:
                return TryLowerTypeReferenceExpression(
                    typeReferenceExpression,
                    loweringContext,
                    out loweredExpression,
                    out errorMessage);

            case XBindLiteralExpression literalExpression:
                loweredExpression = new XBindLoweredExpression(BuildLiteralExpression(literalExpression), IsTypeReference: false);
                errorMessage = string.Empty;
                return true;

            case XBindCastExpression castExpression:
                if (!TryLowerTypeToken(castExpression.TypeToken, loweringContext, out var castTypeExpression, out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                if (castExpression.Operand is null)
                {
                    loweredExpression = default;
                    errorMessage = "x:Bind cast expression requires an operand.";
                    return false;
                }

                if (!TryLowerExpressionCore(
                        castExpression.Operand,
                        loweringContext,
                        allowAssignmentTarget: false,
                        out var castOperand,
                        out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                loweredExpression = new XBindLoweredExpression(
                    "((" + castTypeExpression + ")" + castOperand.Expression + ")",
                    IsTypeReference: false);
                return true;

            case XBindMemberAccessExpression memberAccessExpression:
                if (!TryLowerExpressionCore(
                        memberAccessExpression.Target,
                        loweringContext,
                        allowAssignmentTarget: false,
                        out var loweredTarget,
                        out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                loweredExpression = new XBindLoweredExpression(
                    loweredTarget.Expression +
                    (memberAccessExpression.IsConditional ? "?." : ".") +
                    memberAccessExpression.MemberName,
                    IsTypeReference: false);
                return true;

            case XBindAttachedPropertyAccessExpression attachedPropertyAccessExpression:
                if (!TryLowerExpressionCore(
                        attachedPropertyAccessExpression.Target,
                        loweringContext,
                        allowAssignmentTarget: false,
                        out var attachedTarget,
                        out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                loweredExpression = new XBindLoweredExpression(
                    attachedTarget.Expression +
                    (attachedPropertyAccessExpression.IsConditional ? "?." : ".") +
                    attachedPropertyAccessExpression.PropertyName,
                    IsTypeReference: false);
                return true;

            case XBindIndexerExpression indexerExpression:
                if (!TryLowerExpressionCore(
                        indexerExpression.Target,
                        loweringContext,
                        allowAssignmentTarget: false,
                        out var indexerTarget,
                        out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                var loweredArguments = new string[indexerExpression.Arguments.Length];
                for (var index = 0; index < indexerExpression.Arguments.Length; index++)
                {
                    if (!TryLowerExpressionCore(
                            indexerExpression.Arguments[index],
                            loweringContext,
                            allowAssignmentTarget: false,
                            out var loweredArgument,
                            out errorMessage))
                    {
                        loweredExpression = default;
                        return false;
                    }

                    loweredArguments[index] = loweredArgument.Expression;
                }

                loweredExpression = new XBindLoweredExpression(
                    indexerTarget.Expression + "[" + string.Join(", ", loweredArguments) + "]",
                    IsTypeReference: false);
                return true;

            case XBindInvocationExpression invocationExpression:
                if (!TryLowerExpressionCore(
                        invocationExpression.Target,
                        loweringContext,
                        allowAssignmentTarget: false,
                        out var invocationTarget,
                        out errorMessage))
                {
                    loweredExpression = default;
                    return false;
                }

                var loweredInvocationArguments = new string[invocationExpression.Arguments.Length];
                for (var index = 0; index < invocationExpression.Arguments.Length; index++)
                {
                    if (!TryLowerExpressionCore(
                            invocationExpression.Arguments[index],
                            loweringContext,
                            allowAssignmentTarget: false,
                            out var loweredArgument,
                            out errorMessage))
                    {
                        loweredExpression = default;
                        return false;
                    }

                    loweredInvocationArguments[index] = loweredArgument.Expression;
                }

                loweredExpression = new XBindLoweredExpression(
                    invocationTarget.Expression + "(" + string.Join(", ", loweredInvocationArguments) + ")",
                    IsTypeReference: false);
                return true;

            default:
                loweredExpression = default;
                errorMessage = "Unsupported x:Bind expression node '" + expression.GetType().Name + "'.";
                return false;
        }
    }

    private bool TryLowerIdentifierExpression(
        XBindIdentifierExpression identifierExpression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var identifierToken = identifierExpression.Identifier.Trim();

        if (string.Equals(identifierToken, "source", StringComparison.Ordinal))
        {
            loweredExpression = new XBindLoweredExpression("source", IsTypeReference: false);
            return true;
        }

        if (string.Equals(identifierToken, "root", StringComparison.Ordinal))
        {
            loweredExpression = new XBindLoweredExpression("root", IsTypeReference: false);
            return true;
        }

        if (string.Equals(identifierToken, "target", StringComparison.Ordinal))
        {
            loweredExpression = new XBindLoweredExpression("target", IsTypeReference: false);
            return true;
        }

        if (HasInstanceMember(loweringContext.SourceType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("source." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (!SymbolEqualityComparer.Default.Equals(loweringContext.SourceType, loweringContext.RootType) &&
            HasInstanceMember(loweringContext.RootType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("root." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (loweringContext.TargetType is not null &&
            HasInstanceMember(loweringContext.TargetType, identifierToken))
        {
            loweredExpression = new XBindLoweredExpression("target." + identifierToken, IsTypeReference: false);
            return true;
        }

        if (TryResolveNamedElementType(
                loweringContext.Compilation,
                loweringContext.Document,
                loweringContext.CurrentNode,
                identifierToken,
                out var namedElementType) &&
            namedElementType is not null)
        {
            loweredExpression = new XBindLoweredExpression(
                "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResolveNamedElement<" +
                GetTypeName(namedElementType) +
                ">(target, root, \"" +
                _escape(identifierToken) +
                "\")",
                IsTypeReference: false);
            return true;
        }

        if (TryLowerTypeToken(identifierToken, loweringContext, out var typeExpression, out errorMessage))
        {
            loweredExpression = new XBindLoweredExpression(typeExpression, IsTypeReference: true);
            return true;
        }

        loweredExpression = default;
        errorMessage =
            "Identifier '" +
            identifierToken +
            "' could not be resolved against the x:Bind source, root, target, named elements, or known types.";
        return false;
    }

    private bool TryLowerTypeReferenceExpression(
        XBindTypeReferenceExpression typeReferenceExpression,
        XBindLoweringContext loweringContext,
        out XBindLoweredExpression loweredExpression,
        out string errorMessage)
    {
        if (!TryLowerTypeToken(typeReferenceExpression.TypeToken, loweringContext, out var typeExpression, out errorMessage))
        {
            loweredExpression = default;
            return false;
        }

        loweredExpression = new XBindLoweredExpression(typeExpression, IsTypeReference: true);
        return true;
    }

    private bool TryLowerTypeToken(
        string rawTypeToken,
        XBindLoweringContext loweringContext,
        out string typeExpression,
        out string errorMessage)
    {
        typeExpression = string.Empty;
        errorMessage = string.Empty;

        var resolvedType = _resolveTypeToken(
            loweringContext.Compilation,
            loweringContext.Document,
            rawTypeToken,
            loweringContext.Document.ClassNamespace);
        if (resolvedType is null)
        {
            errorMessage = "Could not resolve x:Bind type token '" + rawTypeToken + "'.";
            return false;
        }

        typeExpression = GetTypeName(resolvedType);
        return true;
    }

    private void CollectDependenciesCore(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        ImmutableHashSet<XBindPathReference>.Builder dependencies)
    {
        if (TryBuildPathReference(expression, loweringContext, out var dependencyReference))
        {
            dependencies.Add(dependencyReference);
            if (expression is not XBindIndexerExpression and not XBindInvocationExpression)
            {
                return;
            }
        }

        switch (expression)
        {
            case XBindCastExpression { Operand: not null } castExpression:
                CollectDependenciesCore(castExpression.Operand!, loweringContext, dependencies);
                break;
            case XBindMemberAccessExpression memberAccessExpression:
                CollectDependenciesCore(memberAccessExpression.Target, loweringContext, dependencies);
                break;
            case XBindAttachedPropertyAccessExpression attachedPropertyAccessExpression:
                CollectDependenciesCore(attachedPropertyAccessExpression.Target, loweringContext, dependencies);
                break;
            case XBindIndexerExpression indexerExpression:
                if (!TryBuildPathReference(indexerExpression.Target, loweringContext, out _))
                {
                    CollectDependenciesCore(indexerExpression.Target, loweringContext, dependencies);
                }

                foreach (var argument in indexerExpression.Arguments)
                {
                    CollectDependenciesCore(argument, loweringContext, dependencies);
                }

                break;
            case XBindInvocationExpression invocationExpression:
                if (!TryBuildPathReference(invocationExpression.Target, loweringContext, out _))
                {
                    CollectDependenciesCore(invocationExpression.Target, loweringContext, dependencies);
                }

                foreach (var argument in invocationExpression.Arguments)
                {
                    CollectDependenciesCore(argument, loweringContext, dependencies);
                }

                break;
        }
    }

    private bool TryBuildPathReference(
        XBindExpressionNode expression,
        XBindLoweringContext loweringContext,
        out XBindPathReference pathReference)
    {
        switch (expression)
        {
            case XBindIdentifierExpression identifierExpression:
                return TryResolveIdentifierPathReference(identifierExpression.Identifier, loweringContext, out pathReference);

            case XBindCastExpression { Operand: not null } castExpression:
                return TryBuildPathReference(castExpression.Operand!, loweringContext, out pathReference);

            case XBindMemberAccessExpression memberAccessExpression:
                if (TryBuildPathReference(memberAccessExpression.Target, loweringContext, out var memberTargetReference))
                {
                    pathReference = memberTargetReference with
                    {
                        Path = AppendPathSegment(memberTargetReference.Path, memberAccessExpression.MemberName)
                    };
                    return true;
                }

                break;

            case XBindAttachedPropertyAccessExpression attachedPropertyAccessExpression:
                if (TryBuildPathReference(attachedPropertyAccessExpression.Target, loweringContext, out var attachedTargetReference))
                {
                    pathReference = attachedTargetReference with
                    {
                        Path = AppendAttachedPropertyPath(
                            attachedTargetReference.Path,
                            attachedPropertyAccessExpression.OwnerTypeToken,
                            attachedPropertyAccessExpression.PropertyName)
                    };
                    return true;
                }

                break;

            case XBindIndexerExpression indexerExpression when
                TryBuildPathReference(indexerExpression.Target, loweringContext, out var indexerTargetReference) &&
                TryRenderIndexerArguments(indexerExpression.Arguments, out var indexerSuffix):
                pathReference = indexerTargetReference with
                {
                    Path = indexerTargetReference.Path + indexerSuffix
                };
                return true;
        }

        pathReference = default;
        return false;
    }

    private static bool CanAssignTo(XBindExpressionNode expression)
    {
        return expression switch
        {
            XBindIdentifierExpression => true,
            XBindMemberAccessExpression memberAccessExpression => CanAssignTo(memberAccessExpression.Target),
            XBindAttachedPropertyAccessExpression attachedPropertyAccessExpression => CanAssignTo(attachedPropertyAccessExpression.Target),
            XBindIndexerExpression => true,
            XBindCastExpression { Operand: not null } castExpression => CanAssignTo(castExpression.Operand!),
            _ => false
        };
    }

    private static string AppendPathSegment(string path, string segment)
    {
        if (string.IsNullOrWhiteSpace(path) || path == ".")
        {
            return segment;
        }

        return path + "." + segment;
    }

    private static string AppendAttachedPropertyPath(
        string existingPath,
        string ownerTypeToken,
        string propertyName)
    {
        var attachedSegment = "(" + ownerTypeToken + "." + propertyName + ")";
        if (string.IsNullOrWhiteSpace(existingPath) || existingPath == ".")
        {
            return attachedSegment;
        }

        return existingPath + "." + attachedSegment;
    }

    private static bool TryRenderIndexerArguments(
        ImmutableArray<XBindExpressionNode> arguments,
        out string suffix)
    {
        suffix = string.Empty;
        if (arguments.IsDefaultOrEmpty)
        {
            return false;
        }

        var parts = new string[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            if (!TryRenderIndexerArgument(arguments[index], out parts[index]))
            {
                return false;
            }
        }

        suffix = "[" + string.Join(", ", parts) + "]";
        return true;
    }

    private static bool TryRenderIndexerArgument(
        XBindExpressionNode argument,
        out string renderedArgument)
    {
        renderedArgument = string.Empty;
        switch (argument)
        {
            case XBindLiteralExpression literal when literal.Kind == XBindLiteralKind.String:
                renderedArgument = "\"" + literal.RawValue.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                return true;
            case XBindLiteralExpression literal:
                renderedArgument = literal.RawValue;
                return true;
        }

        return false;
    }

    private bool TryResolveIdentifierPathReference(
        string identifier,
        XBindLoweringContext loweringContext,
        out XBindPathReference pathReference)
    {
        if (HasInstanceMember(loweringContext.SourceType, identifier))
        {
            pathReference = loweringContext.DefaultSourceReference with
            {
                Path = identifier
            };
            return true;
        }

        if (!SymbolEqualityComparer.Default.Equals(loweringContext.SourceType, loweringContext.RootType) &&
            HasInstanceMember(loweringContext.RootType, identifier))
        {
            pathReference = new XBindPathReference(XBindSourceReferenceKind.Root, identifier, null, null, null);
            return true;
        }

        if (loweringContext.TargetType is not null &&
            HasInstanceMember(loweringContext.TargetType, identifier))
        {
            pathReference = new XBindPathReference(XBindSourceReferenceKind.Target, identifier, null, null, null);
            return true;
        }

        if (TryResolveNamedElementType(
                loweringContext.Compilation,
                loweringContext.Document,
                loweringContext.CurrentNode,
                identifier,
                out _))
        {
            pathReference = new XBindPathReference(XBindSourceReferenceKind.ElementName, ".", identifier, null, null);
            return true;
        }

        pathReference = default;
        return false;
    }

    private static string BuildLiteralExpression(XBindLiteralExpression literalExpression)
    {
        return literalExpression.Kind switch
        {
            XBindLiteralKind.Null => "null",
            XBindLiteralKind.Boolean => string.Equals(literalExpression.RawValue, "true", StringComparison.OrdinalIgnoreCase)
                ? "true"
                : "false",
            XBindLiteralKind.Number => literalExpression.RawValue,
            XBindLiteralKind.String => "@\"" + literalExpression.RawValue.Replace("\"", "\"\"") + "\"",
            _ => literalExpression.RawValue
        };
    }

    private string BuildSourceKindExpression(XBindSourceReferenceKind kind)
    {
        return kind switch
        {
            XBindSourceReferenceKind.DataContext => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.DataContext",
            XBindSourceReferenceKind.Root => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.Root",
            XBindSourceReferenceKind.Target => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.Target",
            XBindSourceReferenceKind.ElementName => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.ElementName",
            XBindSourceReferenceKind.TemplatedParent => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.TemplatedParent",
            XBindSourceReferenceKind.FindAncestor => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.FindAncestor",
            XBindSourceReferenceKind.ExplicitSource => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.ExplicitSource",
            _ => "global::XamlToCSharpGenerator.Runtime.SourceGenBindingSourceKind.DataContext"
        };
    }

    private string BuildNullableStringLiteral(string? value)
    {
        return value is null
            ? "null"
            : "\"" + _escape(value) + "\"";
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool HasInstanceMember(INamedTypeSymbol? typeSymbol, string memberName)
    {
        if (typeSymbol is null || string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(typeSymbol))
        {
            foreach (var member in current.GetMembers(memberName))
            {
                switch (member)
                {
                    case IPropertySymbol property when !property.IsStatic && property.GetMethod is not null:
                    case IFieldSymbol field when !field.IsStatic:
                    case IMethodSymbol method when !method.IsStatic &&
                                                   method.MethodKind == MethodKind.Ordinary &&
                                                   !method.IsImplicitlyDeclared:
                        return true;
                }
            }
        }

        return false;
    }

    private XamlObjectNode? FindVisibleNameScopeRoot(XamlObjectNode documentRoot, XamlObjectNode currentNode)
    {
        if (!TryBuildPathToNode(documentRoot, currentNode, ImmutableArray<XamlObjectNode>.Empty, out var path))
        {
            return documentRoot;
        }

        for (var index = path.Length - 1; index >= 0; index--)
        {
            var candidate = path[index];
            if (Equals(candidate, documentRoot) || _isNameScopeBoundary(candidate))
            {
                return candidate;
            }
        }

        return documentRoot;
    }

    private static bool TryBuildPathToNode(
        XamlObjectNode candidate,
        XamlObjectNode target,
        ImmutableArray<XamlObjectNode> path,
        out ImmutableArray<XamlObjectNode> result)
    {
        var currentPath = path.Add(candidate);
        if (ReferenceEquals(candidate, target) || Equals(candidate, target))
        {
            result = currentPath;
            return true;
        }

        foreach (var child in candidate.ChildObjects)
        {
            if (TryBuildPathToNode(child, target, currentPath, out result))
            {
                return true;
            }
        }

        foreach (var propertyElement in candidate.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                if (TryBuildPathToNode(objectValue, target, currentPath, out result))
                {
                    return true;
                }
            }
        }

        foreach (var constructorArgument in candidate.ConstructorArguments)
        {
            if (TryBuildPathToNode(constructorArgument, target, currentPath, out result))
            {
                return true;
            }
        }

        result = default;
        return false;
    }

    private IEnumerable<XamlObjectNode> EnumerateVisibleNameScopeElements(XamlObjectNode scopeRoot, XamlObjectNode candidate)
    {
        yield return candidate;

        foreach (var child in candidate.ChildObjects)
        {
            if (!Equals(child, scopeRoot) && _isNameScopeBoundary(child))
            {
                continue;
            }

            foreach (var descendant in EnumerateVisibleNameScopeElements(scopeRoot, child))
            {
                yield return descendant;
            }
        }

        foreach (var propertyElement in candidate.PropertyElements)
        {
            foreach (var objectValue in propertyElement.ObjectValues)
            {
                if (!Equals(objectValue, scopeRoot) && _isNameScopeBoundary(objectValue))
                {
                    continue;
                }

                foreach (var descendant in EnumerateVisibleNameScopeElements(scopeRoot, objectValue))
                {
                    yield return descendant;
                }
            }
        }
    }
}
