using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static void ProjectObjectNodePropertyElementAssignments(
        INamedTypeSymbol targetType,
        ImmutableArray<BoundObjectNodePropertyElementPlan> propertyElementPlans,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        BindingPriorityScope bindingPriorityScope,
        ImmutableArray<ResolvedPropertyElementAssignment>.Builder propertyElementAssignments)
    {
        ObjectNodePropertyElementProjectionService.Project(
            targetType,
            propertyElementPlans,
            compilation,
            diagnostics,
            document,
            options,
            (int)bindingPriorityScope,
            propertyElementAssignments);
    }

    private static bool TryResolveAliasedFrameworkPropertyElementAssignment(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result)
    {
        var propertyAlias = propertyElementPlan.PropertyAlias;
        var propertyElement = propertyElementPlan.PropertyElement;
        if (!propertyAlias.HasFrameworkPropertyAlias(FrameworkProfileIds.Avalonia) ||
            propertyAlias.GetFrameworkPropertyOwnerTypeSymbol(FrameworkProfileIds.Avalonia) is not { } frameworkPropertyOwnerType ||
            !TryFindAvaloniaPropertyField(
                frameworkPropertyOwnerType,
                propertyElementPlan.NormalizedPropertyName,
                out var resolvedOwnerType,
                out var propertyField,
                propertyAlias.GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia)))
        {
            result = new ObjectNodePropertyElementResolutionResult(null);
            return false;
        }

        var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
            TryGetAvaloniaPropertyValueType(propertyField.Type),
            propertyElementPlan.ObjectValues,
            compilation,
            document,
            propertyElement.Line,
            propertyElement.Column);
        if (assignmentValues.Length != 1)
        {
            result = new ObjectNodePropertyElementResolutionResult(
                null,
                "AXSG0103",
                $"Aliased Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.");
            return true;
        }

        result = new ObjectNodePropertyElementResolutionResult(
            new ResolvedPropertyElementAssignment(
                PropertyName: propertyElementPlan.NormalizedPropertyName,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    resolvedOwnerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                        (BindingPriorityScope)bindingPriorityScope)),
                IsCollectionAdd: false,
                IsDictionaryMerge: false,
                ObjectValues: assignmentValues,
                Line: propertyElement.Line,
                Column: propertyElement.Column,
                Condition: propertyElement.Condition,
                PreserveBindingValue: HasAssignBindingAttribute(
                    TypeSymbolLookupSemanticsService.FindProperty(targetType, propertyElementPlan.NormalizedPropertyName))));
        return true;
    }

    private static bool TryResolveOwnerQualifiedFrameworkPropertyElementAssignment(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result)
    {
        var propertyElement = propertyElementPlan.PropertyElement;
        if (!TrySplitOwnerQualifiedPropertyToken(
                propertyElement.PropertyName,
                out var attachedOwnerToken,
                out var attachedPropertyName))
        {
            result = new ObjectNodePropertyElementResolutionResult(null);
            return false;
        }

        var attachedOwnerType = ResolveOwnerQualifiedMemberOwnerType(
            compilation,
            document,
            attachedOwnerToken,
            propertyElement.XmlNamespace);
        if (attachedOwnerType is null)
        {
            result = new ObjectNodePropertyElementResolutionResult(null);
            return false;
        }

        if (TryFindAvaloniaPropertyField(
                attachedOwnerType,
                attachedPropertyName,
                out var attachedResolvedOwnerType,
                out var attachedPropertyField))
        {
            var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                TryGetAvaloniaPropertyValueType(attachedPropertyField.Type),
                propertyElementPlan.ObjectValues,
                compilation,
                document,
                propertyElement.Line,
                propertyElement.Column);
            if (assignmentValues.Length != 1)
            {
                result = new ObjectNodePropertyElementResolutionResult(
                    null,
                    "AXSG0103",
                    $"Attached property element '{propertyElement.PropertyName}' requires exactly one object value.");
                return true;
            }

            result = new ObjectNodePropertyElementResolutionResult(
                new ResolvedPropertyElementAssignment(
                    PropertyName: attachedPropertyName,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                        attachedResolvedOwnerType,
                        attachedPropertyField,
                        GetSetValueBindingPriorityExpression(
                            targetType,
                            attachedPropertyField,
                            compilation,
                            (BindingPriorityScope)bindingPriorityScope)),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: assignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    PreserveBindingValue: HasAssignBindingAttribute(
                        TypeSymbolLookupSemanticsService.FindProperty(attachedResolvedOwnerType, attachedPropertyName))));
            return true;
        }

        return TryResolveAttachedSetterPropertyElementAssignment(
            targetType,
            attachedOwnerType,
            attachedPropertyName,
            propertyElement,
            propertyElementPlan.ObjectValues,
            compilation,
            document,
            out result);
    }

    private static bool TryResolveFrameworkPropertyElementAssignment(
        BoundObjectNodePropertyElementPlan propertyElementPlan,
        INamedTypeSymbol targetType,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        int bindingPriorityScope,
        out ObjectNodePropertyElementResolutionResult result)
    {
        var propertyElement = propertyElementPlan.PropertyElement;
        if (!TryFindAvaloniaPropertyField(targetType, property.Name, out var ownerType, out var propertyField))
        {
            result = new ObjectNodePropertyElementResolutionResult(null);
            return false;
        }

        var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
            property.Type,
            propertyElementPlan.ObjectValues,
            compilation,
            document,
            propertyElement.Line,
            propertyElement.Column);
        if (assignmentValues.Length != 1)
        {
            result = new ObjectNodePropertyElementResolutionResult(
                null,
                "AXSG0103",
                $"Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.");
            return true;
        }

        result = new ObjectNodePropertyElementResolutionResult(
            new ResolvedPropertyElementAssignment(
                PropertyName: property.Name,
                ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    ownerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                        (BindingPriorityScope)bindingPriorityScope)),
                IsCollectionAdd: false,
                IsDictionaryMerge: false,
                ObjectValues: assignmentValues,
                Line: propertyElement.Line,
                Column: propertyElement.Column,
                Condition: propertyElement.Condition,
                PreserveBindingValue: HasAssignBindingAttribute(property)));
        return true;
    }

    private static void ValidatePropertyElementTargetProperty(
        INamedTypeSymbol targetType,
        IPropertySymbol property,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options)
    {
        ItemContainerTemplateWarningService.Validate(
            targetType,
            property,
            propertyElement,
            compilation,
            document,
            SemanticConventions.ItemContainerTypeMappings,
            diagnostics,
            options);
    }

    private static bool TryResolveAttachedSetterPropertyElementAssignment(
        INamedTypeSymbol targetType,
        INamedTypeSymbol ownerType,
        string attachedPropertyName,
        XamlPropertyElement propertyElement,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        out ObjectNodePropertyElementResolutionResult result)
    {
        if (!TryFindAttachedSetterMethod(
                ownerType,
                attachedPropertyName,
                targetType,
                out var resolvedOwnerType,
                out var setterMethod))
        {
            result = new ObjectNodePropertyElementResolutionResult(null);
            return false;
        }

        var parameterType = setterMethod.Parameters[1].Type;
        var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
            parameterType,
            objectValues,
            compilation,
            document,
            propertyElement.Line,
            propertyElement.Column);
        if (assignmentValues.Length != 1)
        {
            result = new ObjectNodePropertyElementResolutionResult(
                null,
                "AXSG0103",
                $"Attached property element '{propertyElement.PropertyName}' requires exactly one object value.");
            return true;
        }

        result = new ObjectNodePropertyElementResolutionResult(
            new ResolvedPropertyElementAssignment(
                PropertyName: setterMethod.Name,
                ClrPropertyOwnerTypeName: resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ClrPropertyTypeName: null,
                IsCollectionAdd: false,
                IsDictionaryMerge: false,
                ObjectValues: assignmentValues,
                Line: propertyElement.Line,
                Column: propertyElement.Column,
                Condition: propertyElement.Condition));
        return true;
    }
}
