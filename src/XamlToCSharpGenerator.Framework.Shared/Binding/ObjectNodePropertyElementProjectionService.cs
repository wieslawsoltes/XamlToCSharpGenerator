using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ObjectNodePropertyElementResolutionResult(
    ResolvedPropertyElementAssignment? Assignment,
    string? DiagnosticId = null,
    string? DiagnosticMessage = null);

public sealed class ObjectNodePropertyElementProjectionService
{
    public delegate bool TryResolveAliasedFrameworkPropertyElementAssignmentDelegate(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result);

    public delegate bool TryResolveOwnerQualifiedFrameworkPropertyElementAssignmentDelegate(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result);

    public delegate ObjectNodePropertyElementAssignmentPlan BuildGenericPropertyElementAssignmentPlanDelegate(
        INamedTypeSymbol ownerType,
        string normalizedPropertyName,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition);

    public delegate void ValidatePropertyElementTargetPropertyDelegate(
        INamedTypeSymbol ownerType,
        IPropertySymbol property,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options);

    public delegate bool TryResolveFrameworkPropertyElementAssignmentDelegate(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result);

    private readonly TryResolveAliasedFrameworkPropertyElementAssignmentDelegate _tryResolveAliasedFrameworkPropertyElementAssignment;
    private readonly TryResolveOwnerQualifiedFrameworkPropertyElementAssignmentDelegate _tryResolveOwnerQualifiedFrameworkPropertyElementAssignment;
    private readonly BuildGenericPropertyElementAssignmentPlanDelegate _buildGenericPropertyElementAssignmentPlan;
    private readonly ValidatePropertyElementTargetPropertyDelegate _validatePropertyElementTargetProperty;
    private readonly TryResolveFrameworkPropertyElementAssignmentDelegate _tryResolveFrameworkPropertyElementAssignment;

    public ObjectNodePropertyElementProjectionService(
        TryResolveAliasedFrameworkPropertyElementAssignmentDelegate tryResolveAliasedFrameworkPropertyElementAssignment,
        TryResolveOwnerQualifiedFrameworkPropertyElementAssignmentDelegate tryResolveOwnerQualifiedFrameworkPropertyElementAssignment,
        BuildGenericPropertyElementAssignmentPlanDelegate buildGenericPropertyElementAssignmentPlan,
        ValidatePropertyElementTargetPropertyDelegate validatePropertyElementTargetProperty,
        TryResolveFrameworkPropertyElementAssignmentDelegate tryResolveFrameworkPropertyElementAssignment)
    {
        _tryResolveAliasedFrameworkPropertyElementAssignment = tryResolveAliasedFrameworkPropertyElementAssignment ??
            throw new ArgumentNullException(nameof(tryResolveAliasedFrameworkPropertyElementAssignment));
        _tryResolveOwnerQualifiedFrameworkPropertyElementAssignment = tryResolveOwnerQualifiedFrameworkPropertyElementAssignment ??
            throw new ArgumentNullException(nameof(tryResolveOwnerQualifiedFrameworkPropertyElementAssignment));
        _buildGenericPropertyElementAssignmentPlan = buildGenericPropertyElementAssignmentPlan ??
            throw new ArgumentNullException(nameof(buildGenericPropertyElementAssignmentPlan));
        _validatePropertyElementTargetProperty = validatePropertyElementTargetProperty ??
            throw new ArgumentNullException(nameof(validatePropertyElementTargetProperty));
        _tryResolveFrameworkPropertyElementAssignment = tryResolveFrameworkPropertyElementAssignment ??
            throw new ArgumentNullException(nameof(tryResolveFrameworkPropertyElementAssignment));
    }

    public void Project(
        INamedTypeSymbol targetType,
        ImmutableArray<BoundObjectNodePropertyElementPlan> propertyElementPlans,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        int bindingPriorityScope,
        ImmutableArray<ResolvedPropertyElementAssignment>.Builder propertyElementAssignments)
    {
        foreach (var propertyElementPlan in propertyElementPlans)
        {
            if (_tryResolveAliasedFrameworkPropertyElementAssignment(
                    propertyElementPlan,
                    targetType,
                    compilation,
                    document,
                    bindingPriorityScope,
                    out var aliasedResult))
            {
                ApplyResult(
                    aliasedResult,
                    propertyElementPlan.PropertyElement,
                    diagnostics,
                    document.FilePath,
                    options.StrictMode,
                    propertyElementAssignments);
                continue;
            }

            if (_tryResolveOwnerQualifiedFrameworkPropertyElementAssignment(
                    propertyElementPlan,
                    targetType,
                    compilation,
                    document,
                    bindingPriorityScope,
                    out var ownerQualifiedResult))
            {
                ApplyResult(
                    ownerQualifiedResult,
                    propertyElementPlan.PropertyElement,
                    diagnostics,
                    document.FilePath,
                    options.StrictMode,
                    propertyElementAssignments);
                continue;
            }

            var propertyElement = propertyElementPlan.PropertyElement;
            var genericPropertyElementPlan = _buildGenericPropertyElementAssignmentPlan(
                targetType,
                propertyElementPlan.NormalizedPropertyName,
                propertyElementPlan.ObjectValues,
                compilation,
                document,
                propertyElement.Line,
                propertyElement.Column,
                propertyElement.Condition);
            var property = genericPropertyElementPlan.Property;
            if (property is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Property element '{propertyElement.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                    document.FilePath,
                    propertyElement.Line,
                    propertyElement.Column,
                    options.StrictMode));
                continue;
            }

            _validatePropertyElementTargetProperty(
                targetType,
                property,
                propertyElement,
                compilation,
                diagnostics,
                document,
                options);

            if (_tryResolveFrameworkPropertyElementAssignment(
                    propertyElementPlan,
                    targetType,
                    property,
                    compilation,
                    document,
                    bindingPriorityScope,
                    out var frameworkPropertyResult))
            {
                ApplyResult(
                    frameworkPropertyResult,
                    propertyElement,
                    diagnostics,
                    document.FilePath,
                    options.StrictMode,
                    propertyElementAssignments);
                continue;
            }

            if (genericPropertyElementPlan.Assignment is not null)
            {
                propertyElementAssignments.Add(genericPropertyElementPlan.Assignment);
                continue;
            }

            switch (genericPropertyElementPlan.IssueKind)
            {
                case ObjectNodePropertyElementAssignmentIssueKind.RequiresSingleValue:
                    var cardinalityMessage =
                        genericPropertyElementPlan.SingleValueRequirementKind ==
                        ObjectNodePropertyElementSingleValueRequirementKind.DictionaryPropertyElement
                            ? $"Dictionary property element '{propertyElement.PropertyName}' requires exactly one object value."
                            : $"Property element '{propertyElement.PropertyName}' requires exactly one object value.";
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        cardinalityMessage,
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    break;

                case ObjectNodePropertyElementAssignmentIssueKind.Unsupported:
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0101",
                        $"Property element '{propertyElement.PropertyName}' is not supported on '{targetType.ToDisplayString()}'.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    break;
            }
        }
    }

    private static void ApplyResult(
        ObjectNodePropertyElementResolutionResult result,
        XamlPropertyElement propertyElement,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath,
        bool strictMode,
        ImmutableArray<ResolvedPropertyElementAssignment>.Builder propertyElementAssignments)
    {
        if (result.Assignment is not null)
        {
            propertyElementAssignments.Add(result.Assignment);
        }

        if (!string.IsNullOrWhiteSpace(result.DiagnosticMessage))
        {
            var diagnosticId = string.IsNullOrWhiteSpace(result.DiagnosticId) ? "AXSG0101" : result.DiagnosticId!;
            var diagnosticMessage = result.DiagnosticMessage!;
            diagnostics.Add(new DiagnosticInfo(
                diagnosticId,
                diagnosticMessage,
                filePath,
                propertyElement.Line,
                propertyElement.Column,
                strictMode));
        }
    }
}
