using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static BoundObjectNodePropertyElementSet BindObjectNodePropertyElements(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        string? contentPropertyName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        ImmutableArray<ResolvedEventSubscription>.Builder eventSubscriptions,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        BindingScopeContext scopeContext)
    {
        _ = contentPropertyName;
        _ = assignments;
        _ = eventSubscriptions;

        if (objectType is not null && !node.PropertyElements.IsDefaultOrEmpty)
        {
            var filteredPropertyElements = ImmutableArray.CreateBuilder<XamlPropertyElement>(node.PropertyElements.Length);
            foreach (var propertyElement in node.PropertyElements)
            {
                var propertyAlias = ResolvePropertyAlias(objectType, propertyElement.PropertyName);
                if (ShouldDeferDocumentOverlayPropertyElementBinding(objectType, propertyAlias.ResolvedPropertyName))
                {
                    continue;
                }

                filteredPropertyElements.Add(propertyElement);
            }

            node = node with { PropertyElements = filteredPropertyElements.ToImmutable() };
        }

        return ObjectNodePropertyElementBindingService.Bind(
            node,
            objectType,
            compilation,
            diagnostics,
            document,
            options,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            inheritedSetterTargetType,
            (int)bindingPriorityScope,
            rootTypeSymbol,
            eventSubscriptions,
            scopeContext,
            static (
                boundNode,
                boundCompilation,
                boundDiagnostics,
                boundDocument,
                boundOptions,
                boundCompiledBindings,
                boundUnsafeAccessors,
                boundCompileBindingsEnabled,
                boundInheritedDataType,
                boundInheritedSetterTargetType,
                boundBindingPriorityScope,
                forcedType,
                boundRootTypeSymbol,
                boundParentScopeContext,
                boundParentPropertyName) =>
                BindObjectNode(
                    boundNode,
                    boundCompilation,
                    boundDiagnostics,
                    boundDocument,
                    boundOptions,
                    boundCompiledBindings,
                    boundUnsafeAccessors,
                    boundCompileBindingsEnabled,
                    boundInheritedDataType,
                    boundInheritedSetterTargetType,
                    (BindingPriorityScope)boundBindingPriorityScope,
                    forcedType,
                    boundRootTypeSymbol,
                    boundParentScopeContext,
                    boundParentPropertyName));
    }

    private static bool ShouldDeferDocumentOverlayPropertyElementBinding(
        INamedTypeSymbol objectType,
        string normalizedPropertyName)
    {
        if (!normalizedPropertyName.Equals("Resources", StringComparison.Ordinal) &&
            !normalizedPropertyName.Equals("Styles", StringComparison.Ordinal))
        {
            return false;
        }

        return TypeSymbolLookupSemanticsService.FindProperty(objectType, normalizedPropertyName) is null &&
               !TryFindAvaloniaPropertyField(objectType, normalizedPropertyName, out _, out _);
    }

    private static ObjectNodePropertyElementAssignmentPlan BuildGenericPropertyElementAssignmentPlan(
        INamedTypeSymbol ownerType,
        string normalizedPropertyName,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition)
    {
        return ObjectNodePropertyElementAssignmentPlanningService.BuildPlan(
            ownerType,
            normalizedPropertyName,
            objectValues,
            compilation,
            document,
            line,
            column,
            condition);
    }

    private static ResolvedObjectNodeConstructionPlan PlanObjectNodeConstruction(
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
        BindingPriorityScope currentBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedObjectNode> children)
    {
        _ = typeName;
        _ = diagnostics;
        _ = options;
        _ = compiledBindings;
        _ = unsafeAccessors;
        _ = compileBindingsEnabled;
        _ = nodeDataType;
        _ = currentSetterTargetType;
        _ = currentBindingPriorityScope;
        _ = rootTypeSymbol;

        return ObjectNodeConstructionPlanningService.BuildPlan(
            node,
            objectType,
            contentPropertyName,
            compilation,
            document,
            propertyAssignments,
            propertyElementAssignments,
            children);
    }

    private static ResolvedObjectNodeAttachmentFinalizationPlan FinalizeObjectNodeAttachmentPlan(
        INamedTypeSymbol? objectType,
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
        return ObjectNodeAttachmentPlanningService.FinalizeAttachmentPlan(
            objectType,
            explicitAttachmentMode,
            explicitContentPropertyName,
            propertyElementAssignments,
            children,
            compilation,
            document,
            line,
            column,
            condition);
    }

    private static void ReportObjectNodeAttachmentValidationIssues(
        ImmutableArray<ResolvedObjectNodeAttachmentValidationIssue> validationIssues,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string filePath,
        string typeName,
        int line,
        int column,
        bool strictMode)
    {
        foreach (var issue in validationIssues)
        {
            switch (issue.Kind)
            {
                case ResolvedObjectNodeAttachmentValidationIssueKind.MultipleContentChildren:
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        "More than one child object was found for a Content attachment target.",
                        filePath,
                        issue.Line == 0 ? line : issue.Line,
                        issue.Column == 0 ? column : issue.Column,
                        strictMode));
                    break;

                case ResolvedObjectNodeAttachmentValidationIssueKind.DictionaryChildMissingKey:
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0101",
                        $"'{typeName}' requires x:Key for child objects added via dictionary Add(key, value).",
                        filePath,
                        issue.Line == 0 ? line : issue.Line,
                        issue.Column == 0 ? column : issue.Column,
                        strictMode));
                    break;
            }
        }
    }

    private static ResolvedObjectNode FinalizeObjectNode(
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
        string? contentPropertyTypeName)
    {
        return ObjectNodeFinalizationService.Finalize(
            symbol,
            compilation,
            document,
            node,
            keyExpression,
            name,
            typeName,
            isBindingObjectNode,
            factoryExpression,
            factoryValueRequirements,
            propertyAssignments,
            propertyElementAssignments,
            eventSubscriptions,
            children,
            childAttachmentMode,
            contentPropertyName,
            contentPropertyTypeName);
    }

    private static IPropertySymbol? FindBindableProperty(
        INamedTypeSymbol ownerType,
        string propertyName,
        Compilation compilation,
        XamlDocumentModel document)
    {
        _ = compilation;
        _ = document;
        return TypeSymbolLookupSemanticsService.FindProperty(ownerType, propertyName);
    }

    private static bool IsStyleBaseType(ITypeSymbol? type, Compilation compilation)
    {
        if (type is null)
        {
            return false;
        }

        var namedType = type as INamedTypeSymbol;
        var stylesType = ResolveContractType(compilation, TypeContractId.Styles);
        return namedType is not null &&
               stylesType is not null &&
               TypeSymbolLookupSemanticsService.IsTypeAssignableTo(namedType, stylesType);
    }

    private static bool CanMergeDictionaryProperty(INamedTypeSymbol ownerType, string propertyName)
    {
        return TypeSymbolLookupSemanticsService.FindProperty(ownerType, propertyName)?.Type is INamedTypeSymbol namedType &&
               CollectionAddService.HasDictionaryAddMethod(namedType);
    }

    private static bool TryBuildPropertyElementSpecialAssignmentPlan(
        IPropertySymbol property,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        out ResolvedPropertyElementSpecialAssignmentPlan specialPlan)
    {
        if (CanMergeDictionaryProperty(property.ContainingType, property.Name) &&
            objectValues.Length == 1 &&
            string.IsNullOrWhiteSpace(objectValues[0].KeyExpression))
        {
            specialPlan = new ResolvedPropertyElementSpecialAssignmentPlan(
                new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: objectValues,
                    Line: line,
                    Column: column,
                    Condition: condition,
                    PreserveBindingValue: HasAssignBindingAttribute(property)));
            return true;
        }

        if (property.Type is INamedTypeSymbol collectionType)
        {
            var addInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValues(
                collectionType,
                objectValues,
                compilation,
                document);
            if (!addInstructions.IsDefaultOrEmpty)
            {
                specialPlan = new ResolvedPropertyElementSpecialAssignmentPlan(
                    new ResolvedPropertyElementAssignment(
                        PropertyName: property.Name,
                        ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: objectValues,
                        Line: line,
                        Column: column,
                        Condition: condition,
                        PreserveBindingValue: HasAssignBindingAttribute(property),
                        CollectionAddInstructions: addInstructions));
                return true;
            }
        }

        if (CanMergeDictionaryProperty(property.ContainingType, property.Name))
        {
            if (objectValues.Length != 1)
            {
                specialPlan = new ResolvedPropertyElementSpecialAssignmentPlan(
                    null,
                    ObjectNodePropertyElementAssignmentIssueKind.RequiresSingleValue,
                    ObjectNodePropertyElementSingleValueRequirementKind.DictionaryPropertyElement);
                return true;
            }

            specialPlan = new ResolvedPropertyElementSpecialAssignmentPlan(
                new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: objectValues,
                    Line: line,
                    Column: column,
                    Condition: condition,
                    PreserveBindingValue: HasAssignBindingAttribute(property)));
            return true;
        }

        specialPlan = default;
        return false;
    }

    private static ImmutableArray<ResolvedObjectNode> MaterializePropertyElementValuesForTargetTypeIfNeeded(
        ITypeSymbol? targetType,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column)
    {
        if (targetType is not INamedTypeSymbol namedTargetType ||
            objectValues.IsDefaultOrEmpty)
        {
            return objectValues;
        }

        var targetTypeName = namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (objectValues.Length == 1 &&
            string.Equals(objectValues[0].TypeName, targetTypeName, StringComparison.Ordinal))
        {
            return objectValues;
        }

        if (!CanImplicitlyConstructPropertyElementValueContainer(namedTargetType))
        {
            return objectValues;
        }

        var contentPropertyName = FindContentPropertyName(namedTargetType);
        var attachmentPlan = ObjectNodeAttachmentPlanningService.FinalizeAttachmentPlan(
            namedTargetType,
            ResolvedChildAttachmentMode.None,
            contentPropertyName,
            ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            objectValues,
            compilation,
            document,
            line,
            column,
            condition: null);

        if (attachmentPlan.AttachmentMode == ResolvedChildAttachmentMode.None ||
            !attachmentPlan.ValidationIssues.IsDefaultOrEmpty)
        {
            return objectValues;
        }

        return ImmutableArray.Create(new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: targetTypeName,
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: ShouldUseServiceProviderConstructor(namedTargetType),
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: attachmentPlan.PropertyElementAssignments,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: attachmentPlan.Children,
            ChildAttachmentMode: attachmentPlan.AttachmentMode,
            ContentPropertyName: attachmentPlan.ContentPropertyName,
            Line: line,
            Column: column,
            Condition: null,
            ChildAddInstructions: attachmentPlan.ChildAddInstructions,
            SemanticFlags: ResolvedObjectNodeSemanticFlags.None,
            ContentPropertyTypeName: attachmentPlan.ContentPropertyTypeName));
    }

    private static bool CanImplicitlyConstructPropertyElementValueContainer(INamedTypeSymbol targetType)
    {
        if (targetType.IsAbstract)
        {
            return false;
        }

        return targetType.InstanceConstructors.Any(static constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public &&
            (constructor.Parameters.Length == 0 || IsSingleServiceProviderConstructor(constructor)));
    }

    private static bool HasAssignBindingAttribute(IPropertySymbol? property)
    {
        if (property is null)
        {
            return false;
        }

        return property.GetAttributes().Any(attribute =>
        {
            var attributeTypeName = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.Equals(attributeTypeName, "global::Avalonia.Data.AssignBindingAttribute", StringComparison.Ordinal) ||
                   string.Equals(attribute.AttributeClass?.Name, "AssignBindingAttribute", StringComparison.Ordinal);
        });
    }

    private static bool TryBuildExplicitConstructionExpressionForObjectNode(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression,
        out ResolvedValueRequirements requirements)
    {
        expression = string.Empty;
        requirements = ResolvedValueRequirements.None;
        var hasConstructionDirectives = node.ConstructorArguments.Length > 0 ||
                                        !string.IsNullOrWhiteSpace(node.FactoryMethod);
        if (!hasConstructionDirectives || objectType is null)
        {
            return false;
        }

        var arguments = new List<string>(node.ConstructorArguments.Length);
        foreach (var argumentNode in node.ConstructorArguments)
        {
            if (!TryBuildInlineConstructionArgumentExpression(
                    argumentNode,
                    compilation,
                    document,
                    out var argumentExpression,
                    out var argumentRequirements))
            {
                return false;
            }

            arguments.Add(argumentExpression);
            requirements = CombineValueRequirements(requirements, argumentRequirements);
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryMethod))
        {
            var factoryMethod = TryFindMatchingFactoryMethod(objectType, node.FactoryMethod!, arguments.Count);
            if (factoryMethod is null)
            {
                return false;
            }

            expression = factoryMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         factoryMethod.Name +
                         "(" +
                         string.Join(", ", arguments) +
                         ")";
            return true;
        }

        var constructor = TryFindMatchingConstructor(objectType, arguments.Count);
        if (constructor is null)
        {
            return false;
        }

        expression = "new " +
                     objectType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     string.Join(", ", arguments) +
                     ")";
        return true;
    }

    private static bool TryBuildInlineConstructionArgumentExpression(
        XamlObjectNode argumentNode,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression,
        out ResolvedValueRequirements requirements)
    {
        expression = string.Empty;
        requirements = ResolvedValueRequirements.None;

        var argumentType = ResolveObjectTypeSymbol(compilation, document, argumentNode);
        if (argumentType is null)
        {
            return false;
        }

        if (TryBuildExplicitConstructionExpressionForObjectNode(
                argumentNode,
                argumentType,
                compilation,
                document,
                out expression,
                out requirements))
        {
            return true;
        }

        if (IsXamlTypeNode(argumentNode))
        {
            var xamlTypeNode = XamlTypeNodeBindingService.BindXamlTypeNode(
                argumentNode,
                compilation,
                document,
                strictMode: false,
                static _ => { });
            if (ExplicitConstructionBindingService.TryBuildInlineResolvedObjectExpression(xamlTypeNode, out expression))
            {
                requirements = xamlTypeNode.FactoryValueRequirements;
                return true;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(argumentNode.TextContent) ||
            argumentNode.PropertyAssignments.Length > 0 ||
            argumentNode.PropertyElements.Length > 0 ||
            argumentNode.ChildObjects.Length > 0)
        {
            return false;
        }

        if (!TryConvertValueConversion(
                argumentNode.TextContent!,
                argumentType,
                compilation,
                document,
                setterTargetType: null,
                BindingPriorityScope.None,
                out var conversion))
        {
            return false;
        }

        expression = conversion.Expression;
        requirements = conversion.EffectiveRequirements;
        return true;
    }

    private static ResolvedValueRequirements CombineValueRequirements(
        ResolvedValueRequirements current,
        ResolvedValueRequirements next)
    {
        return new ResolvedValueRequirements(
            NeedsServiceProvider: current.NeedsServiceProvider || next.NeedsServiceProvider,
            NeedsParentStack: current.NeedsParentStack || next.NeedsParentStack,
            NeedsProvideValueTarget: current.NeedsProvideValueTarget || next.NeedsProvideValueTarget,
            NeedsRootObject: current.NeedsRootObject || next.NeedsRootObject,
            NeedsBaseUri: current.NeedsBaseUri || next.NeedsBaseUri);
    }

    private static IMethodSymbol? TryFindMatchingFactoryMethod(INamedTypeSymbol type, string methodName, int argumentCount)
    {
        return type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsGenericMethod &&
                !method.ReturnsVoid)
            .Where(method => method.Parameters.Length == argumentCount)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IMethodSymbol? TryFindMatchingConstructor(INamedTypeSymbol type, int argumentCount)
    {
        return type.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic)
            .Where(constructor => constructor.Parameters.Length == argumentCount)
            .OrderBy(static constructor => constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool TryNormalizePlatformMarkupExtensionChildren(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment> existingAssignments,
        ImmutableArray<ResolvedObjectNode> existingChildren,
        out ImmutableArray<ResolvedPropertyAssignment> normalizedAssignments,
        out ImmutableArray<ResolvedObjectNode> normalizedChildren)
    {
        normalizedAssignments = existingAssignments;
        normalizedChildren = existingChildren;

        if (!TryClassifyPlatformMarkupExtension(objectType, compilation, out var optionKind) ||
            node.ChildObjects.Length == 0)
        {
            return false;
        }

        var synthesizedAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>();
        var matchedAnyOnChild = false;
        foreach (var child in node.ChildObjects)
        {
            if (ConditionalXamlEvaluationService.ShouldSkipBranch(
                    child.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (!TryBuildPlatformOptionAssignmentsFromChild(
                    child,
                    objectType!,
                    optionKind,
                    compilation,
                    document,
                    out var childAssignments))
            {
                return false;
            }

            matchedAnyOnChild = true;
            synthesizedAssignments.AddRange(childAssignments);
        }

        if (!matchedAnyOnChild)
        {
            return false;
        }

        var overriddenPropertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in synthesizedAssignments)
        {
            overriddenPropertyNames.Add(assignment.PropertyName);
        }

        var mergedAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>(
            existingAssignments.Length + synthesizedAssignments.Count);
        foreach (var assignment in existingAssignments)
        {
            if (!overriddenPropertyNames.Contains(assignment.PropertyName))
            {
                mergedAssignments.Add(assignment);
            }
        }

        mergedAssignments.AddRange(synthesizedAssignments);
        normalizedAssignments = mergedAssignments.ToImmutable();
        normalizedChildren = ImmutableArray<ResolvedObjectNode>.Empty;
        return true;
    }

    private static bool TryBuildInlineTextContentPropertyAssignment(
        XamlObjectNode node,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedPropertyAssignment assignment)
    {
        assignment = default!;
        if (string.IsNullOrWhiteSpace(node.TextContent) ||
            !TryConvertValueExpression(
                node.TextContent!,
                property.Type,
                compilation,
                document,
                setterTargetType: null,
                BindingPriorityScope.None,
                out var expression))
        {
            return false;
        }

        assignment = new ResolvedPropertyAssignment(
            PropertyName: property.Name,
            ValueExpression: expression,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            PreserveBindingValue: HasAssignBindingAttribute(property));
        return true;
    }

    private static bool TryBuildInlineTextContentCollectionAssignment(
        XamlObjectNode node,
        IPropertySymbol property,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedPropertyElementAssignment assignment)
    {
        assignment = default!;
        if (string.IsNullOrWhiteSpace(node.TextContent) ||
            !CollectionAddService.TryCreateCollectionContentValue(
                node.TextContent!,
                property.Type,
                compilation,
                document,
                setterTargetType: null,
                bindingPriorityScope: (int)BindingPriorityScope.None,
                allowObjectStringLiteralFallback: true,
                line: node.Line,
                column: node.Column,
                out var valueNode,
                out var addInstruction))
        {
            return false;
        }

        assignment = new ResolvedPropertyElementAssignment(
            PropertyName: property.Name,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsCollectionAdd: true,
            IsDictionaryMerge: false,
            ObjectValues: ImmutableArray.Create(valueNode),
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            PreserveBindingValue: HasAssignBindingAttribute(property),
            CollectionAddInstructions: ImmutableArray.Create(addInstruction));
        return true;
    }

    private static bool TryBuildInlineTextFactoryExpression(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression,
        out ResolvedValueRequirements requirements)
    {
        expression = string.Empty;
        requirements = ResolvedValueRequirements.None;
        if (objectType is null ||
            string.IsNullOrWhiteSpace(node.TextContent) ||
            node.PropertyAssignments.Length > 0 ||
            node.PropertyElements.Length > 0 ||
            node.ChildObjects.Length > 0)
        {
            return false;
        }

        if (!TryConvertValueExpression(
                node.TextContent!,
                objectType,
                compilation,
                document,
                setterTargetType: null,
                BindingPriorityScope.None,
                out expression))
        {
            return false;
        }

        requirements = ResolvedValueRequirements.None;
        return true;
    }

    private static bool TryClassifyPlatformMarkupExtension(
        INamedTypeSymbol? objectType,
        Compilation compilation,
        out PlatformMarkupOptionKind optionKind)
    {
        optionKind = PlatformMarkupOptionKind.None;
        if (objectType is null)
        {
            return false;
        }

        var onPlatformType = ResolveContractType(compilation, TypeContractId.OnPlatformExtension);
        if (onPlatformType is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(objectType, onPlatformType))
        {
            optionKind = PlatformMarkupOptionKind.Platform;
            return true;
        }

        var onFormFactorType = ResolveContractType(compilation, TypeContractId.OnFormFactorExtension);
        if (onFormFactorType is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(objectType, onFormFactorType))
        {
            optionKind = PlatformMarkupOptionKind.FormFactor;
            return true;
        }

        return false;
    }

    private static bool TryBuildPlatformOptionAssignmentsFromChild(
        XamlObjectNode child,
        INamedTypeSymbol markupExtensionType,
        PlatformMarkupOptionKind optionKind,
        Compilation compilation,
        XamlDocumentModel document,
        out ImmutableArray<ResolvedPropertyAssignment> assignments)
    {
        assignments = ImmutableArray<ResolvedPropertyAssignment>.Empty;
        var onMarkupType = ResolveContractType(compilation, TypeContractId.OnMarkupExtension);
        var childType = ResolveObjectTypeSymbol(compilation, document, child);
        if (childType is null ||
            onMarkupType is null ||
            !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(childType, onMarkupType))
        {
            return false;
        }

        if (!TryGetOnMarkupContentToken(child, out var contentToken) ||
            !TryGetOnMarkupOptions(child, optionKind, out var optionNames))
        {
            return false;
        }

        var resolvedAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>(optionNames.Length);
        foreach (var optionName in optionNames)
        {
            var optionProperty = TypeSymbolLookupSemanticsService.FindProperty(markupExtensionType, optionName);
            if (optionProperty is null)
            {
                return false;
            }

            if (!MarkupOptionValueExpressionService.TryConvert(
                    contentToken,
                    optionProperty.Type,
                    compilation,
                    document,
                    setterTargetType: markupExtensionType,
                    bindingPriorityScope: (int)BindingPriorityScope.None,
                    out var valueExpression))
            {
                return false;
            }

            resolvedAssignments.Add(new ResolvedPropertyAssignment(
                PropertyName: optionProperty.Name,
                ValueExpression: valueExpression,
                ClrPropertyOwnerTypeName: markupExtensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ClrPropertyTypeName: optionProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: child.Line,
                Column: child.Column,
                Condition: child.Condition,
                PreserveBindingValue: HasAssignBindingAttribute(optionProperty)));
        }

        assignments = resolvedAssignments.ToImmutable();
        return true;
    }

    private static bool TryGetOnMarkupContentToken(
        XamlObjectNode child,
        out string contentToken)
    {
        foreach (var assignment in child.PropertyAssignments)
        {
            var propertyName = NormalizePropertyName(assignment.PropertyName);
            if (propertyName.Equals("Content", StringComparison.Ordinal) ||
                propertyName.Equals("Value", StringComparison.Ordinal))
            {
                contentToken = assignment.Value;
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(child.TextContent))
        {
            contentToken = child.TextContent!;
            return true;
        }

        contentToken = string.Empty;
        return false;
    }

    private static bool TryGetOnMarkupOptions(
        XamlObjectNode child,
        PlatformMarkupOptionKind optionKind,
        out ImmutableArray<string> optionNames)
    {
        optionNames = ImmutableArray<string>.Empty;
        var optionsAssignment = child.PropertyAssignments.FirstOrDefault(
            static assignment => NormalizePropertyName(assignment.PropertyName).Equals("Options", StringComparison.Ordinal));
        if (optionsAssignment is null || string.IsNullOrWhiteSpace(optionsAssignment.Value))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var token in optionsAssignment.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryMapPlatformOptionName(Unquote(token), optionKind, out var optionName))
            {
                builder.Add(optionName);
            }
        }

        if (builder.Count == 0)
        {
            return false;
        }

        optionNames = builder.ToImmutable();
        return true;
    }

    private static bool TryMapPlatformOptionName(
        string rawOptionName,
        PlatformMarkupOptionKind optionKind,
        out string optionName)
    {
        optionName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawOptionName))
        {
            return false;
        }

        switch (optionKind)
        {
            case PlatformMarkupOptionKind.Platform:
                switch (rawOptionName.Trim())
                {
                    case "Default":
                        optionName = "Default";
                        return true;
                    case "Windows":
                        optionName = "Windows";
                        return true;
                    case "macOS":
                    case "MacOS":
                        optionName = "macOS";
                        return true;
                    case "Linux":
                        optionName = "Linux";
                        return true;
                    case "Android":
                        optionName = "Android";
                        return true;
                    case "iOS":
                    case "IOS":
                        optionName = "iOS";
                        return true;
                    case "Browser":
                        optionName = "Browser";
                        return true;
                }

                break;

            case PlatformMarkupOptionKind.FormFactor:
                switch (rawOptionName.Trim())
                {
                    case "Default":
                        optionName = "Default";
                        return true;
                    case "Desktop":
                        optionName = "Desktop";
                        return true;
                    case "Mobile":
                        optionName = "Mobile";
                        return true;
                    case "TV":
                        optionName = "TV";
                        return true;
                }

                break;
        }

        return false;
    }

    private enum PlatformMarkupOptionKind
    {
        None = 0,
        Platform,
        FormFactor
    }
}
