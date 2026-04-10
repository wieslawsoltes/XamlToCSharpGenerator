using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ResolvedObjectNodeConstructionPlan(
    string? FactoryExpression,
    ResolvedValueRequirements FactoryValueRequirements,
    ImmutableArray<ResolvedPropertyAssignment> PropertyAssignments,
    ImmutableArray<ResolvedPropertyElementAssignment> PropertyElementAssignments,
    ImmutableArray<ResolvedObjectNode> Children);

public sealed class ObjectNodeConstructionPlanningService
{
    public delegate bool TryBuildExplicitConstructionExpressionDelegate(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression,
        out ResolvedValueRequirements requirements);

    public delegate IPropertySymbol? FindPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);

    public delegate bool TryBuildInlineTextContentPropertyAssignmentDelegate(
        XamlObjectNode node,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedPropertyAssignment assignment);

    public delegate bool TryBuildInlineTextContentCollectionAssignmentDelegate(
        XamlObjectNode node,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedPropertyElementAssignment assignment);

    public delegate bool TryBuildInlineTextFactoryExpressionDelegate(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression,
        out ResolvedValueRequirements requirements);

    private readonly TryBuildExplicitConstructionExpressionDelegate _tryBuildExplicitConstructionExpression;
    private readonly FindPropertyDelegate _findProperty;
    private readonly TryBuildInlineTextContentPropertyAssignmentDelegate _tryBuildInlineTextContentPropertyAssignment;
    private readonly TryBuildInlineTextContentCollectionAssignmentDelegate _tryBuildInlineTextContentCollectionAssignment;
    private readonly TryBuildInlineTextFactoryExpressionDelegate _tryBuildInlineTextFactoryExpression;

    public ObjectNodeConstructionPlanningService(
        TryBuildExplicitConstructionExpressionDelegate tryBuildExplicitConstructionExpression,
        FindPropertyDelegate findProperty,
        TryBuildInlineTextContentPropertyAssignmentDelegate tryBuildInlineTextContentPropertyAssignment,
        TryBuildInlineTextContentCollectionAssignmentDelegate tryBuildInlineTextContentCollectionAssignment,
        TryBuildInlineTextFactoryExpressionDelegate tryBuildInlineTextFactoryExpression)
    {
        _tryBuildExplicitConstructionExpression = tryBuildExplicitConstructionExpression ?? throw new ArgumentNullException(nameof(tryBuildExplicitConstructionExpression));
        _findProperty = findProperty ?? throw new ArgumentNullException(nameof(findProperty));
        _tryBuildInlineTextContentPropertyAssignment = tryBuildInlineTextContentPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBuildInlineTextContentPropertyAssignment));
        _tryBuildInlineTextContentCollectionAssignment = tryBuildInlineTextContentCollectionAssignment ?? throw new ArgumentNullException(nameof(tryBuildInlineTextContentCollectionAssignment));
        _tryBuildInlineTextFactoryExpression = tryBuildInlineTextFactoryExpression ?? throw new ArgumentNullException(nameof(tryBuildInlineTextFactoryExpression));
    }

    public ResolvedObjectNodeConstructionPlan BuildPlan(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        string? contentPropertyName,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedObjectNode> children)
    {
        var resolvedAssignments = propertyAssignments.ToBuilder();
        var resolvedPropertyElementAssignments = propertyElementAssignments.ToBuilder();

        if (_tryBuildExplicitConstructionExpression(
                node,
                objectType,
                compilation,
                document,
                out var explicitFactoryExpression,
                out var explicitFactoryRequirements))
        {
            return new ResolvedObjectNodeConstructionPlan(
                explicitFactoryExpression,
                explicitFactoryRequirements,
                resolvedAssignments.ToImmutable(),
                resolvedPropertyElementAssignments.ToImmutable(),
                children);
        }

        if (!string.IsNullOrWhiteSpace(node.TextContent) &&
            objectType is not null &&
            !string.IsNullOrWhiteSpace(contentPropertyName))
        {
            var contentProperty = _findProperty(objectType, contentPropertyName!);
            if (contentProperty is not null)
            {
                if (_tryBuildInlineTextContentPropertyAssignment(
                        node,
                        contentProperty,
                        compilation,
                        document,
                        out var propertyAssignment))
                {
                    resolvedAssignments.Add(propertyAssignment);
                }
                else if (_tryBuildInlineTextContentCollectionAssignment(
                             node,
                             contentProperty,
                             compilation,
                             document,
                             out var propertyElementAssignment))
                {
                    resolvedPropertyElementAssignments.Add(propertyElementAssignment);
                }
            }
        }

        if (_tryBuildInlineTextFactoryExpression(
                node,
                objectType,
                compilation,
                document,
                out var inlineFactoryExpression,
                out var inlineFactoryRequirements))
        {
            return new ResolvedObjectNodeConstructionPlan(
                inlineFactoryExpression,
                inlineFactoryRequirements,
                resolvedAssignments.ToImmutable(),
                resolvedPropertyElementAssignments.ToImmutable(),
                children);
        }

        return new ResolvedObjectNodeConstructionPlan(
            null,
            ResolvedValueRequirements.None,
            resolvedAssignments.ToImmutable(),
            resolvedPropertyElementAssignments.ToImmutable(),
            children);
    }

    private static bool HasResolvedPropertyAssignment(
        ImmutableArray<ResolvedPropertyAssignment> assignments,
        string propertyName)
    {
        foreach (var assignment in assignments)
        {
            if (assignment.PropertyName == propertyName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResolvedPropertyElementAssignment(
        ImmutableArray<ResolvedPropertyElementAssignment> assignments,
        string propertyName)
    {
        foreach (var assignment in assignments)
        {
            if (assignment.PropertyName == propertyName)
            {
                return true;
            }
        }

        return false;
    }
}
