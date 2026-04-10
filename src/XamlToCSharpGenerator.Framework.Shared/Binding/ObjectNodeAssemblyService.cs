using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ObjectNodeAssemblyRequest(
    XamlObjectNode Node,
    INamedTypeSymbol? ObjectType,
    string TypeName,
    string? ContentPropertyName,
    Compilation Compilation,
    XamlDocumentModel Document,
    GeneratorOptions Options,
    ImmutableArray<DiagnosticInfo>.Builder Diagnostics,
    ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings,
    ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder UnsafeAccessors,
    bool CompileBindingsEnabled,
    INamedTypeSymbol? NodeDataType,
    INamedTypeSymbol? CurrentSetterTargetType,
    int CurrentBindingPriorityScope,
    INamedTypeSymbol? RootTypeSymbol,
    ImmutableArray<ResolvedPropertyAssignment> PropertyAssignments,
    ImmutableArray<ResolvedPropertyElementAssignment> PropertyElementAssignments,
    ImmutableArray<ResolvedEventSubscription> EventSubscriptions,
    ImmutableArray<ResolvedObjectNode> Children,
    BoundObjectNodePropertyElementSet PropertyElementBindingPlan);

public sealed class ObjectNodeAssemblyService
{
    public delegate bool TryNormalizePlatformMarkupExtensionChildrenDelegate(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment> existingAssignments,
        ImmutableArray<ResolvedObjectNode> existingChildren,
        out ImmutableArray<ResolvedPropertyAssignment> normalizedAssignments,
        out ImmutableArray<ResolvedObjectNode> normalizedChildren);

    public delegate ImmutableArray<ResolvedPropertyElementAssignment> ProjectObjectNodePropertyElementAssignmentsDelegate(
        INamedTypeSymbol objectType,
        ImmutableArray<BoundObjectNodePropertyElementPlan> propertyElementPlans,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        int bindingPriorityScope);

    public delegate ResolvedObjectNodeConstructionPlan PlanObjectNodeConstructionDelegate(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        string typeName,
        string? contentPropertyName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? currentSetterTargetType,
        int currentBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedObjectNode> children);

    public delegate ResolvedObjectNodeAttachmentFinalizationPlan FinalizeObjectNodeAttachmentPlanDelegate(
        INamedTypeSymbol? objectType,
        ResolvedChildAttachmentMode explicitAttachmentMode,
        string? explicitContentPropertyName,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition);

    public delegate void ReportObjectNodeAttachmentValidationIssuesDelegate(
        ImmutableArray<ResolvedObjectNodeAttachmentValidationIssue> validationIssues,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath,
        string typeName,
        int line,
        int column,
        bool strictMode);

    public delegate string? ResolveObjectNodeNameScopeRegistrationDelegate(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation);

    public delegate string? BuildObjectNodeKeyExpressionDelegate(
        string? rawKey,
        Compilation compilation,
        XamlDocumentModel document);

    public delegate ResolvedObjectNode FinalizeObjectNodeDelegate(
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        string? keyExpression,
        string? name,
        string typeName,
        bool isBindingObjectNode,
        string? factoryExpression,
        ResolvedValueRequirements factoryValueRequirements,
        ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        ImmutableArray<ResolvedObjectNode> children,
        ResolvedChildAttachmentMode childAttachmentMode,
        string? contentPropertyName,
        string? contentPropertyTypeName);

    public delegate bool IsBindingObjectTypeDelegate(
        INamedTypeSymbol type,
        Compilation compilation);

    private readonly TryNormalizePlatformMarkupExtensionChildrenDelegate _tryNormalizePlatformMarkupExtensionChildren;
    private readonly ProjectObjectNodePropertyElementAssignmentsDelegate _projectObjectNodePropertyElementAssignments;
    private readonly PlanObjectNodeConstructionDelegate _planObjectNodeConstruction;
    private readonly FinalizeObjectNodeAttachmentPlanDelegate _finalizeObjectNodeAttachmentPlan;
    private readonly ReportObjectNodeAttachmentValidationIssuesDelegate _reportObjectNodeAttachmentValidationIssues;
    private readonly ResolveObjectNodeNameScopeRegistrationDelegate _resolveObjectNodeNameScopeRegistration;
    private readonly BuildObjectNodeKeyExpressionDelegate _buildObjectNodeKeyExpression;
    private readonly FinalizeObjectNodeDelegate _finalizeObjectNode;
    private readonly IsBindingObjectTypeDelegate _isBindingObjectType;

    public ObjectNodeAssemblyService(
        TryNormalizePlatformMarkupExtensionChildrenDelegate tryNormalizePlatformMarkupExtensionChildren,
        ProjectObjectNodePropertyElementAssignmentsDelegate projectObjectNodePropertyElementAssignments,
        PlanObjectNodeConstructionDelegate planObjectNodeConstruction,
        FinalizeObjectNodeAttachmentPlanDelegate finalizeObjectNodeAttachmentPlan,
        ReportObjectNodeAttachmentValidationIssuesDelegate reportObjectNodeAttachmentValidationIssues,
        ResolveObjectNodeNameScopeRegistrationDelegate resolveObjectNodeNameScopeRegistration,
        BuildObjectNodeKeyExpressionDelegate buildObjectNodeKeyExpression,
        FinalizeObjectNodeDelegate finalizeObjectNode,
        IsBindingObjectTypeDelegate isBindingObjectType)
    {
        _tryNormalizePlatformMarkupExtensionChildren = tryNormalizePlatformMarkupExtensionChildren ?? throw new ArgumentNullException(nameof(tryNormalizePlatformMarkupExtensionChildren));
        _projectObjectNodePropertyElementAssignments = projectObjectNodePropertyElementAssignments ?? throw new ArgumentNullException(nameof(projectObjectNodePropertyElementAssignments));
        _planObjectNodeConstruction = planObjectNodeConstruction ?? throw new ArgumentNullException(nameof(planObjectNodeConstruction));
        _finalizeObjectNodeAttachmentPlan = finalizeObjectNodeAttachmentPlan ?? throw new ArgumentNullException(nameof(finalizeObjectNodeAttachmentPlan));
        _reportObjectNodeAttachmentValidationIssues = reportObjectNodeAttachmentValidationIssues ?? throw new ArgumentNullException(nameof(reportObjectNodeAttachmentValidationIssues));
        _resolveObjectNodeNameScopeRegistration = resolveObjectNodeNameScopeRegistration ?? throw new ArgumentNullException(nameof(resolveObjectNodeNameScopeRegistration));
        _buildObjectNodeKeyExpression = buildObjectNodeKeyExpression ?? throw new ArgumentNullException(nameof(buildObjectNodeKeyExpression));
        _finalizeObjectNode = finalizeObjectNode ?? throw new ArgumentNullException(nameof(finalizeObjectNode));
        _isBindingObjectType = isBindingObjectType ?? throw new ArgumentNullException(nameof(isBindingObjectType));
    }

    public ResolvedObjectNode Assemble(ObjectNodeAssemblyRequest request)
    {
        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>(
            request.Children.Length + request.PropertyElementBindingPlan.Children.Length);
        children.AddRange(request.Children);
        children.AddRange(request.PropertyElementBindingPlan.Children);

        var assignments = request.PropertyAssignments;
        var explicitAttachment = request.PropertyElementBindingPlan.ExplicitAttachmentMode;
        var explicitContentPropertyName = request.PropertyElementBindingPlan.ExplicitContentPropertyName;

        if (_tryNormalizePlatformMarkupExtensionChildren(
                request.Node,
                request.ObjectType,
                request.Compilation,
                request.Document,
                request.Options,
                request.Diagnostics,
                assignments,
                children.ToImmutable(),
                out var normalizedAssignments,
                out var normalizedChildren))
        {
            assignments = normalizedAssignments;
            children.Clear();
            children.AddRange(normalizedChildren);
        }

        var propertyElementAssignmentsBuilder = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>(
            request.PropertyElementAssignments.Length + request.PropertyElementBindingPlan.PropertyElementPlans.Length);
        propertyElementAssignmentsBuilder.AddRange(request.PropertyElementAssignments);
        if (request.ObjectType is not null)
        {
            propertyElementAssignmentsBuilder.AddRange(_projectObjectNodePropertyElementAssignments(
                request.ObjectType,
                request.PropertyElementBindingPlan.PropertyElementPlans,
                request.Compilation,
                request.Diagnostics,
                request.Document,
                request.Options,
                request.CurrentBindingPriorityScope));
        }

        var propertyElementAssignments = propertyElementAssignmentsBuilder.ToImmutable();

        var constructionPlan = _planObjectNodeConstruction(
            request.Node,
            request.ObjectType,
            request.TypeName,
            request.ContentPropertyName,
            request.Compilation,
            request.Diagnostics,
            request.Document,
            request.Options,
            request.CompiledBindings,
            request.UnsafeAccessors,
            request.CompileBindingsEnabled,
            request.NodeDataType,
            request.CurrentSetterTargetType,
            request.CurrentBindingPriorityScope,
            request.RootTypeSymbol,
            assignments,
            propertyElementAssignments,
            children.ToImmutable());

        var attachmentFinalizationPlan = _finalizeObjectNodeAttachmentPlan(
            request.ObjectType,
            explicitAttachment,
            explicitContentPropertyName ?? request.ContentPropertyName,
            constructionPlan.PropertyElementAssignments,
            children.ToImmutable(),
            request.Compilation,
            request.Document,
            request.Node.Line,
            request.Node.Column,
            request.Node.Condition);

        _reportObjectNodeAttachmentValidationIssues(
            attachmentFinalizationPlan.ValidationIssues,
            request.Diagnostics,
            request.Document.FilePath,
            request.TypeName,
            request.Node.Line,
            request.Node.Column,
            request.Options.StrictMode);

        var normalizedNodeName = _resolveObjectNodeNameScopeRegistration(
            request.Node,
            request.ObjectType,
            request.Compilation);

        return _finalizeObjectNode(
            request.ObjectType,
            request.Compilation,
            request.Document,
            request.Node,
            _buildObjectNodeKeyExpression(request.Node.Key, request.Compilation, request.Document),
            normalizedNodeName,
            request.TypeName,
            request.ObjectType is not null && _isBindingObjectType(request.ObjectType, request.Compilation),
            constructionPlan.FactoryExpression,
            constructionPlan.FactoryValueRequirements,
            constructionPlan.PropertyAssignments,
            attachmentFinalizationPlan.PropertyElementAssignments,
            request.EventSubscriptions,
            attachmentFinalizationPlan.Children,
            attachmentFinalizationPlan.AttachmentMode,
            attachmentFinalizationPlan.ContentPropertyName,
            attachmentFinalizationPlan.ContentPropertyTypeName);
    }
}
