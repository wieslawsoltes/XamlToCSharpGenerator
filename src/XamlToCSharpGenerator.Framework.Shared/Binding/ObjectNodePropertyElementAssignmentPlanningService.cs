using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public enum ObjectNodePropertyElementAssignmentIssueKind
{
    None = 0,
    RequiresSingleValue,
    Unsupported
}

public enum ObjectNodePropertyElementSingleValueRequirementKind
{
    None = 0,
    PropertyElement,
    DictionaryPropertyElement
}

public sealed record ObjectNodePropertyElementAssignmentPlan(
    IPropertySymbol? Property,
    ResolvedPropertyElementAssignment? Assignment,
    ObjectNodePropertyElementAssignmentIssueKind IssueKind,
    ObjectNodePropertyElementSingleValueRequirementKind SingleValueRequirementKind);

public sealed class ObjectNodePropertyElementAssignmentPlanningService
{
    public delegate IPropertySymbol? FindPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);
    public delegate bool CanMergeDictionaryPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);
    public delegate bool TryBuildSpecialPropertyElementPlanDelegate(
        IPropertySymbol property,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        out ResolvedPropertyElementSpecialAssignmentPlan specialPlan);
    public delegate ImmutableArray<ResolvedObjectNode> MaterializePropertyElementValuesDelegate(
        ITypeSymbol? targetType,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column);
    public delegate bool HasAssignBindingAttributeDelegate(IPropertySymbol? property);

    private readonly FindPropertyDelegate _findProperty;
    private readonly CanMergeDictionaryPropertyDelegate _canMergeDictionaryProperty;
    private readonly TryBuildSpecialPropertyElementPlanDelegate _tryBuildSpecialPropertyElementPlan;
    private readonly MaterializePropertyElementValuesDelegate _materializePropertyElementValues;
    private readonly HasAssignBindingAttributeDelegate _hasAssignBindingAttribute;

    public ObjectNodePropertyElementAssignmentPlanningService(
        FindPropertyDelegate findProperty,
        CanMergeDictionaryPropertyDelegate canMergeDictionaryProperty,
        TryBuildSpecialPropertyElementPlanDelegate tryBuildSpecialPropertyElementPlan,
        MaterializePropertyElementValuesDelegate materializePropertyElementValues,
        HasAssignBindingAttributeDelegate hasAssignBindingAttribute)
    {
        _findProperty = findProperty ?? throw new ArgumentNullException(nameof(findProperty));
        _canMergeDictionaryProperty = canMergeDictionaryProperty ?? throw new ArgumentNullException(nameof(canMergeDictionaryProperty));
        _tryBuildSpecialPropertyElementPlan = tryBuildSpecialPropertyElementPlan ?? throw new ArgumentNullException(nameof(tryBuildSpecialPropertyElementPlan));
        _materializePropertyElementValues = materializePropertyElementValues ?? throw new ArgumentNullException(nameof(materializePropertyElementValues));
        _hasAssignBindingAttribute = hasAssignBindingAttribute ?? throw new ArgumentNullException(nameof(hasAssignBindingAttribute));
    }

    public ObjectNodePropertyElementAssignmentPlan BuildPlan(
        INamedTypeSymbol ownerType,
        string normalizedPropertyName,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition)
    {
        var property = _findProperty(ownerType, normalizedPropertyName);
        if (property is null)
        {
            return new ObjectNodePropertyElementAssignmentPlan(
                null,
                null,
                ObjectNodePropertyElementAssignmentIssueKind.None,
                ObjectNodePropertyElementSingleValueRequirementKind.None);
        }

        if (_tryBuildSpecialPropertyElementPlan(
                property,
                objectValues,
                compilation,
                document,
                line,
                column,
                condition,
                out var specialPlan))
        {
            return new ObjectNodePropertyElementAssignmentPlan(
                property,
                CreateSpecialPropertyElementAssignment(specialPlan.Assignment),
                specialPlan.IssueKind,
                specialPlan.SingleValueRequirementKind);
        }

        var assignmentValues = _materializePropertyElementValues(
            property.Type,
            objectValues,
            compilation,
            document,
            line,
            column);
        if (assignmentValues.Length != 1)
        {
            return new ObjectNodePropertyElementAssignmentPlan(
                property,
                null,
                ObjectNodePropertyElementAssignmentIssueKind.RequiresSingleValue,
                _canMergeDictionaryProperty(ownerType, property.Name)
                    ? ObjectNodePropertyElementSingleValueRequirementKind.DictionaryPropertyElement
                    : ObjectNodePropertyElementSingleValueRequirementKind.PropertyElement);
        }

        if (property.SetMethod is null)
        {
            return new ObjectNodePropertyElementAssignmentPlan(
                property,
                null,
                ObjectNodePropertyElementAssignmentIssueKind.Unsupported,
                ObjectNodePropertyElementSingleValueRequirementKind.None);
        }

        return new ObjectNodePropertyElementAssignmentPlan(
            property,
            new ResolvedPropertyElementAssignment(
                PropertyName: property.Name,
                ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsCollectionAdd: false,
                IsDictionaryMerge: false,
                ObjectValues: assignmentValues,
                Line: line,
                Column: column,
                Condition: condition,
                PreserveBindingValue: _hasAssignBindingAttribute(property)),
            ObjectNodePropertyElementAssignmentIssueKind.None,
            ObjectNodePropertyElementSingleValueRequirementKind.None);
    }

    private static ResolvedPropertyElementAssignment? CreateSpecialPropertyElementAssignment(
        ResolvedPropertyElementAssignment? assignment)
    {
        return assignment;
    }
}
