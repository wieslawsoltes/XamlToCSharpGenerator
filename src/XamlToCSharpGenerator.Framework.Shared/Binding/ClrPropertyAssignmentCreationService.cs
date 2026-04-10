using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ClrPropertyAssignmentCreationService
{
    public delegate bool RequiresObjectInitializerDelegate(
        IPropertySymbol property,
        ResolvedValueRequirements valueRequirements);

    private readonly RequiresObjectInitializerDelegate _requiresObjectInitializer;

    public ClrPropertyAssignmentCreationService(
        RequiresObjectInitializerDelegate requiresObjectInitializer)
    {
        _requiresObjectInitializer = requiresObjectInitializer
            ?? throw new ArgumentNullException(nameof(requiresObjectInitializer));
    }

    public ResolvedPropertyAssignment Create(
        IPropertySymbol property,
        string valueExpression,
        int line,
        int column,
        ConditionalXamlExpression? condition = null,
        ResolvedValueKind valueKind = ResolvedValueKind.Unknown,
        bool requiresStaticResourceResolver = false,
        ResolvedValueRequirements valueRequirements = default,
        bool preserveBindingValue = false,
        string? clrSetterUnsafeAccessorMethodName = null)
    {
        return new ResolvedPropertyAssignment(
            PropertyName: property.Name,
            ValueExpression: valueExpression,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Line: line,
            Column: column,
            Condition: condition,
            ValueKind: valueKind,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            ValueRequirements: valueRequirements,
            PreserveBindingValue: preserveBindingValue,
            RequiresObjectInitializer: _requiresObjectInitializer(property, valueRequirements),
            ClrSetterUnsafeAccessorMethodName: clrSetterUnsafeAccessorMethodName,
            IsInitOnlyClrProperty: property.SetMethod?.IsInitOnly == true,
            IsRequiredClrProperty: property.IsRequired);
    }

    public ResolvedPropertyAssignment Create(
        IPropertySymbol property,
        ResolvedValueConversionResult conversion,
        int line,
        int column,
        ConditionalXamlExpression? condition = null,
        bool preserveBindingValue = false,
        string? clrSetterUnsafeAccessorMethodName = null)
    {
        return Create(
            property,
            conversion.Expression,
            line,
            column,
            condition,
            conversion.ValueKind,
            conversion.RequiresStaticResourceResolver,
            conversion.EffectiveRequirements,
            preserveBindingValue,
            clrSetterUnsafeAccessorMethodName);
    }
}
