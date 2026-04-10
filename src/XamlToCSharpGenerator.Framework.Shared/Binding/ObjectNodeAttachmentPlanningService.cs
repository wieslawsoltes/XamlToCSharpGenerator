using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ResolvedChildAttachmentPlan(
    ResolvedChildAttachmentMode AttachmentMode,
    string? ContentPropertyName,
    string? ContentPropertyTypeName,
    ImmutableArray<ResolvedCollectionAddInstruction> ChildAddInstructions);

public enum ResolvedObjectNodeAttachmentValidationIssueKind
{
    None = 0,
    MultipleContentChildren,
    DictionaryChildMissingKey
}

public sealed record ResolvedObjectNodeAttachmentValidationIssue(
    ResolvedObjectNodeAttachmentValidationIssueKind Kind,
    int Line,
    int Column);

public sealed record ResolvedObjectNodeAttachmentFinalizationPlan(
    ResolvedChildAttachmentMode AttachmentMode,
    string? ContentPropertyName,
    string? ContentPropertyTypeName,
    ImmutableArray<ResolvedPropertyElementAssignment> PropertyElementAssignments,
    ImmutableArray<ResolvedObjectNode> Children,
    ImmutableArray<ResolvedObjectNodeAttachmentValidationIssue> ValidationIssues,
    ImmutableArray<ResolvedCollectionAddInstruction> ChildAddInstructions);

public sealed record ResolvedPropertyElementSpecialAssignmentPlan(
    ResolvedPropertyElementAssignment? Assignment,
    ObjectNodePropertyElementAssignmentIssueKind IssueKind = ObjectNodePropertyElementAssignmentIssueKind.None,
    ObjectNodePropertyElementSingleValueRequirementKind SingleValueRequirementKind = ObjectNodePropertyElementSingleValueRequirementKind.None);

public sealed class ObjectNodeAttachmentPlanningService
{
    public delegate IPropertySymbol? FindBindablePropertyDelegate(
        INamedTypeSymbol ownerType,
        string propertyName,
        Compilation compilation,
        XamlDocumentModel document);

    public delegate IPropertySymbol? FindPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);
    public delegate bool HasCollectionAddMethodDelegate(INamedTypeSymbol type);
    public delegate bool IsStyleBaseTypeDelegate(ITypeSymbol? type, Compilation compilation);
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(Compilation compilation, XamlDocumentModel document, string token, string fallbackClrNamespace);
    public delegate bool IsTypeAssignableToDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);
    public delegate ImmutableArray<ResolvedCollectionAddInstruction> ResolveCollectionAddInstructionsForValuesDelegate(
        ITypeSymbol? collectionType,
        ImmutableArray<ResolvedObjectNode> values,
        Compilation compilation,
        XamlDocumentModel document);

    private readonly FindBindablePropertyDelegate _findBindableProperty;
    private readonly FindPropertyDelegate _findProperty;
    private readonly HasCollectionAddMethodDelegate _hasDirectAddMethod;
    private readonly HasCollectionAddMethodDelegate _hasDictionaryAddMethod;
    private readonly IsStyleBaseTypeDelegate _isStyleBaseType;
    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;
    private readonly ResolveCollectionAddInstructionsForValuesDelegate _resolveCollectionAddInstructionsForValues;

    public ObjectNodeAttachmentPlanningService(
        FindBindablePropertyDelegate findBindableProperty,
        FindPropertyDelegate findProperty,
        HasCollectionAddMethodDelegate hasDirectAddMethod,
        HasCollectionAddMethodDelegate hasDictionaryAddMethod,
        IsStyleBaseTypeDelegate isStyleBaseType,
        ResolveTypeTokenDelegate resolveTypeToken,
        IsTypeAssignableToDelegate isTypeAssignableTo,
        ResolveCollectionAddInstructionsForValuesDelegate resolveCollectionAddInstructionsForValues)
    {
        _findBindableProperty = findBindableProperty ?? throw new ArgumentNullException(nameof(findBindableProperty));
        _findProperty = findProperty ?? throw new ArgumentNullException(nameof(findProperty));
        _hasDirectAddMethod = hasDirectAddMethod ?? throw new ArgumentNullException(nameof(hasDirectAddMethod));
        _hasDictionaryAddMethod = hasDictionaryAddMethod ?? throw new ArgumentNullException(nameof(hasDictionaryAddMethod));
        _isStyleBaseType = isStyleBaseType ?? throw new ArgumentNullException(nameof(isStyleBaseType));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _resolveCollectionAddInstructionsForValues = resolveCollectionAddInstructionsForValues ?? throw new ArgumentNullException(nameof(resolveCollectionAddInstructionsForValues));
    }

    public ImmutableArray<ResolvedCollectionAddInstruction> ResolveChildAddInstructions(
        INamedTypeSymbol? targetType,
        string? contentPropertyName,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (targetType is null || children.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
        }

        if (!string.IsNullOrWhiteSpace(contentPropertyName))
        {
            var contentProperty = _findProperty(targetType, contentPropertyName!);
            if (contentProperty is not null)
            {
                return _resolveCollectionAddInstructionsForValues(contentProperty.Type, children, compilation, document);
            }
        }

        return _resolveCollectionAddInstructionsForValues(targetType, children, compilation, document);
    }

    public ResolvedObjectNodeAttachmentFinalizationPlan FinalizeAttachmentPlan(
        INamedTypeSymbol? targetType,
        ResolvedChildAttachmentMode explicitAttachmentMode,
        string? explicitContentPropertyName,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition)
    {
        _ = line;
        _ = column;
        _ = condition;

        var validationIssues = ImmutableArray.CreateBuilder<ResolvedObjectNodeAttachmentValidationIssue>();
        var attachmentPlan = TryBuildContentPropertySpecialAssignmentPlan(
            targetType,
            explicitAttachmentMode,
            explicitContentPropertyName,
            children,
            compilation,
            document);

        if (attachmentPlan.AttachmentMode == ResolvedChildAttachmentMode.Content &&
            children.Length > 1)
        {
            foreach (var child in children)
            {
                validationIssues.Add(new ResolvedObjectNodeAttachmentValidationIssue(
                    ResolvedObjectNodeAttachmentValidationIssueKind.MultipleContentChildren,
                    child.Line,
                    child.Column));
            }
        }

        if (attachmentPlan.AttachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            foreach (var child in children)
            {
                if (string.IsNullOrWhiteSpace(child.KeyExpression))
                {
                    validationIssues.Add(new ResolvedObjectNodeAttachmentValidationIssue(
                        ResolvedObjectNodeAttachmentValidationIssueKind.DictionaryChildMissingKey,
                        child.Line,
                        child.Column));
                }
            }
        }

        return new ResolvedObjectNodeAttachmentFinalizationPlan(
            attachmentPlan.AttachmentMode,
            attachmentPlan.ContentPropertyName,
            attachmentPlan.ContentPropertyTypeName,
            propertyElementAssignments,
            children,
            validationIssues.ToImmutable(),
            attachmentPlan.ChildAddInstructions);
    }

    private ResolvedChildAttachmentPlan TryBuildContentPropertySpecialAssignmentPlan(
        INamedTypeSymbol? targetType,
        ResolvedChildAttachmentMode explicitAttachmentMode,
        string? explicitContentPropertyName,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (explicitAttachmentMode != ResolvedChildAttachmentMode.None)
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                explicitAttachmentMode,
                explicitContentPropertyName,
                children,
                compilation,
                document);
        }

        if (targetType is null)
        {
            return new ResolvedChildAttachmentPlan(
                ResolvedChildAttachmentMode.None,
                null,
                null,
                ImmutableArray<ResolvedCollectionAddInstruction>.Empty);
        }

        if (children.IsDefaultOrEmpty)
        {
            var contentPropertyTypeName = !string.IsNullOrWhiteSpace(explicitContentPropertyName)
                ? _findProperty(targetType, explicitContentPropertyName!)?
                    .Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;

            return new ResolvedChildAttachmentPlan(
                ResolvedChildAttachmentMode.None,
                explicitContentPropertyName,
                contentPropertyTypeName,
                ImmutableArray<ResolvedCollectionAddInstruction>.Empty);
        }

        if (!string.IsNullOrWhiteSpace(explicitContentPropertyName))
        {
            var discoveredAttachmentMode = DetermineAttachmentModeForDiscoveredContentProperty(
                targetType,
                explicitContentPropertyName!,
                children,
                compilation);
            return BuildResolvedAttachmentPlan(
                targetType,
                discoveredAttachmentMode,
                explicitContentPropertyName,
                children,
                compilation,
                document);
        }

        if (_findProperty(targetType, "Content") is { DeclaredAccessibility: Accessibility.Public } contentProperty &&
            !_isStyleBaseType(targetType, compilation))
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                ResolvedChildAttachmentMode.Content,
                contentProperty.Name,
                children,
                compilation,
                document);
        }

        if (_findProperty(targetType, "Children") is { DeclaredAccessibility: Accessibility.Public } childrenProperty)
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                ResolvedChildAttachmentMode.ChildrenCollection,
                childrenProperty.Name,
                children,
                compilation,
                document);
        }

        if (_findProperty(targetType, "Items") is { DeclaredAccessibility: Accessibility.Public } itemsProperty)
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                ResolvedChildAttachmentMode.ItemsCollection,
                itemsProperty.Name,
                children,
                compilation,
                document);
        }

        if (_hasDictionaryAddMethod(targetType))
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                ResolvedChildAttachmentMode.DictionaryAdd,
                null,
                children,
                compilation,
                document);
        }

        if (_hasDirectAddMethod(targetType))
        {
            return BuildResolvedAttachmentPlan(
                targetType,
                ResolvedChildAttachmentMode.DirectAdd,
                null,
                children,
                compilation,
                document);
        }

        return new ResolvedChildAttachmentPlan(
            ResolvedChildAttachmentMode.None,
            null,
            null,
            ImmutableArray<ResolvedCollectionAddInstruction>.Empty);
    }

    private ResolvedChildAttachmentMode DetermineAttachmentModeForDiscoveredContentProperty(
        INamedTypeSymbol targetType,
        string contentPropertyName,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation)
    {
        var contentProperty = _findProperty(targetType, contentPropertyName);
        if (contentProperty is null ||
            contentProperty.DeclaredAccessibility != Accessibility.Public)
        {
            return ResolvedChildAttachmentMode.Content;
        }

        if (contentPropertyName.Equals("Children", StringComparison.Ordinal))
        {
            return ResolvedChildAttachmentMode.ChildrenCollection;
        }

        if (contentPropertyName.Equals("Items", StringComparison.Ordinal))
        {
            return ResolvedChildAttachmentMode.ItemsCollection;
        }

        if (children.Length == 1 &&
            ChildRepresentsPropertyValue(children[0], contentProperty.Type))
        {
            return ResolvedChildAttachmentMode.Content;
        }

        if (contentProperty.Type is INamedTypeSymbol contentPropertyType)
        {
            if (_hasDictionaryAddMethod(contentPropertyType))
            {
                return ResolvedChildAttachmentMode.DictionaryAdd;
            }

            if (_hasDirectAddMethod(contentPropertyType))
            {
                return ResolvedChildAttachmentMode.ChildrenCollection;
            }
        }

        if (contentPropertyName.Equals("Content", StringComparison.Ordinal) &&
            !_isStyleBaseType(targetType, compilation))
        {
            return ResolvedChildAttachmentMode.Content;
        }

        return ResolvedChildAttachmentMode.Content;
    }

    private static bool ChildRepresentsPropertyValue(
        ResolvedObjectNode child,
        ITypeSymbol propertyType)
    {
        var propertyTypeName = propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(child.TypeName, propertyTypeName, StringComparison.Ordinal);
    }

    private ResolvedChildAttachmentPlan BuildResolvedAttachmentPlan(
        INamedTypeSymbol? targetType,
        ResolvedChildAttachmentMode attachmentMode,
        string? contentPropertyName,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document)
    {
        var contentPropertyTypeName = default(string);
        if (targetType is not null && !string.IsNullOrWhiteSpace(contentPropertyName))
        {
            contentPropertyTypeName = _findProperty(targetType, contentPropertyName!)?
                .Type
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return new ResolvedChildAttachmentPlan(
            attachmentMode,
            contentPropertyName,
            contentPropertyTypeName,
            ResolveChildAddInstructions(targetType, contentPropertyName, children, compilation, document));
    }
}
