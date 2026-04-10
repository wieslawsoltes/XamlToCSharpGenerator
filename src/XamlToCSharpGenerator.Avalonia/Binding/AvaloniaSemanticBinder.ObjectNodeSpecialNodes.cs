using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool ShouldTreatOwnerQualifiedAssignmentAsAttached(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (!assignment.IsAttached ||
            !TrySplitOwnerQualifiedPropertyToken(
                assignment.PropertyName,
                out var ownerToken,
                out var propertyName))
        {
            return false;
        }

        var ownerType = ResolveTypeSymbol(compilation, assignment.XmlNamespace, ownerToken) ??
                        ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null ||
            !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(targetType, ownerType))
        {
            return true;
        }

        return FindBindableProperty(ownerType, propertyName, compilation, document) is null;
    }

    private static void TryAddTemplateDataTypeDirectiveAssignment(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        if (symbol is null ||
            !IsDataTemplateNode(node) ||
            string.IsNullOrWhiteSpace(node.DataType))
        {
            return;
        }

        if (node.PropertyAssignments.Any(static assignment =>
                !assignment.IsAttached &&
                NormalizePropertyName(assignment.PropertyName).Equals("DataType", StringComparison.Ordinal)))
        {
            return;
        }

        var dataTypeProperty = TypeSymbolLookupSemanticsService.FindProperty(symbol, "DataType");
        if (dataTypeProperty is null || dataTypeProperty.SetMethod is null)
        {
            return;
        }

        var resolvedDataType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.DataType,
            document.ClassNamespace);
        if (resolvedDataType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Template x:DataType '{node.DataType}' could not be resolved for runtime DataType assignment.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return;
        }

        assignments.Add(CreateClrPropertyAssignment(
            dataTypeProperty,
            "typeof(" + resolvedDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            compilation,
            unsafeAccessors,
            node.Line,
            node.Column,
            node.Condition));
    }

    private static void TryAddControlThemeTargetTypeDirectiveAssignment(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        if (symbol is null ||
            !IsControlThemeType(symbol, compilation))
        {
            return;
        }

        if (assignments.Any(static assignment =>
                NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal)))
        {
            return;
        }

        var targetTypeAssignment = node.PropertyAssignments.FirstOrDefault(static assignment =>
            !assignment.IsAttached &&
            NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal));
        if (targetTypeAssignment.PropertyName is null ||
            string.IsNullOrWhiteSpace(targetTypeAssignment.Value))
        {
            return;
        }

        var targetTypeProperty = TypeSymbolLookupSemanticsService.FindProperty(symbol, "TargetType");
        if (targetTypeProperty?.SetMethod is null)
        {
            return;
        }

        var resolvedTargetType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            targetTypeAssignment.Value,
            document.ClassNamespace);
        if (resolvedTargetType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"ControlTheme TargetType '{targetTypeAssignment.Value}' could not be resolved for runtime assignment.",
                document.FilePath,
                targetTypeAssignment.Line,
                targetTypeAssignment.Column,
                options.StrictMode));
            return;
        }

        assignments.Add(CreateClrPropertyAssignment(
            targetTypeProperty,
            "typeof(" + resolvedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            compilation,
            unsafeAccessors,
            targetTypeAssignment.Line,
            targetTypeAssignment.Column,
            targetTypeAssignment.Condition));
    }

    private static ResolvedPropertyAssignment CreateClrPropertyAssignment(
        IPropertySymbol property,
        string valueExpression,
        Compilation compilation,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        int line,
        int column,
        ConditionalXamlExpression? condition = null,
        ResolvedValueKind valueKind = ResolvedValueKind.Unknown,
        bool requiresStaticResourceResolver = false,
        ResolvedValueRequirements valueRequirements = default,
        bool preserveBindingValue = false)
    {
        return ClrPropertyAssignmentCreationService.Create(
            property,
            valueExpression,
            line,
            column,
            condition,
            valueKind,
            requiresStaticResourceResolver,
            valueRequirements,
            preserveBindingValue,
            ResolveInitOnlySetterUnsafeAccessorMethodName(
                property,
                compilation,
                unsafeAccessors));
    }

    private static string? ResolveInitOnlySetterUnsafeAccessorMethodName(
        IPropertySymbol property,
        Compilation compilation,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors)
    {
        var setter = property.SetMethod;
        if (setter?.IsInitOnly != true || !SupportsUnsafeAccessor(compilation))
        {
            return null;
        }

        return RegisterUnsafeAccessorDefinition(unsafeAccessors, setter);
    }

    private static ResolvedObjectNode BindXamlArrayNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol)
    {
        var elementType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.ArrayItemType,
            document.ClassNamespace);
        if (elementType is null && node.TypeArguments.Length > 0)
        {
            elementType = ResolveTypeToken(compilation, document, node.TypeArguments[0], document.ClassNamespace);
        }

        var elementTypeName = elementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
        var valueExpressions = new List<string>(node.ChildObjects.Length);

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

            var boundChild = BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!ExplicitConstructionBindingService.TryBuildInlineResolvedObjectExpression(boundChild, out var inlineChildExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0108",
                    "x:Array values must be inline-constructable objects when used in source-generated construction.",
                    document.FilePath,
                    child.Line,
                    child.Column,
                    options.StrictMode));
                continue;
            }

            valueExpressions.Add(inlineChildExpression);
        }

        var factoryExpression = valueExpressions.Count == 0
            ? "global::System.Array.Empty<" + elementTypeName + ">()"
            : "new " + elementTypeName + "[] { " + string.Join(", ", valueExpressions) + " }";
        var normalizedNodeName = NormalizeObjectNodeName(node.Name);

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: normalizedNodeName,
            TypeName: "global::System.Array",
            IsBindingObjectNode: false,
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition);
    }

    private static string? BuildObjectNodeKeyExpression(
        string? rawKey,
        Compilation compilation,
        XamlDocumentModel document)
    {
        return ObjectNodeKeyExpressionService.BuildObjectNodeKeyExpression(rawKey, compilation, document);
    }

    private static INamedTypeSymbol? ResolvePropertyElementSetterTargetType(
        INamedTypeSymbol? objectType,
        string propertyName,
        INamedTypeSymbol? inheritedSetterTargetType)
    {
        if (inheritedSetterTargetType is not null || objectType is null)
        {
            return inheritedSetterTargetType;
        }

        var property = TypeSymbolLookupSemanticsService.FindProperty(objectType, propertyName);
        if (property is not null && IsTransitionsCollectionType(property.Type))
        {
            return objectType;
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveAmbientSetterTargetType(
        BindingScopeContext? scopeContext,
        Compilation compilation,
        XamlDocumentModel document)
    {
        for (var current = scopeContext; current is not null; current = current.Parent)
        {
            if (current.SetterTargetType is not null)
            {
                return current.SetterTargetType;
            }

            var resolved = ResolveCurrentSetterTargetType(
                current.NodeType,
                current.Node,
                compilation,
                document,
                inheritedSetterTargetType: null);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool IsTransitionsCollectionType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "Transitions" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Animation")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryExtractInlineCSharpObjectNodeCode(
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        out string code)
    {
        const string avaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
        const string runtimeUsingNamespace = "using:XamlToCSharpGenerator.Runtime";
        const string runtimeClrNamespace = "clr-namespace:XamlToCSharpGenerator.Runtime";
        const string markupUsingNamespace = "using:XamlToCSharpGenerator.Runtime.Markup";
        const string markupClrNamespace = "clr-namespace:XamlToCSharpGenerator.Runtime.Markup";

        code = string.Empty;
        var symbol = ResolveObjectTypeSymbol(compilation, document, node);
        var isInlineCSharpNode =
            string.Equals(
                symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "global::XamlToCSharpGenerator.Runtime.CSharp",
                StringComparison.Ordinal) ||
            string.Equals(
                symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                "global::XamlToCSharpGenerator.Runtime.Markup.CSharp",
                StringComparison.Ordinal) ||
            (string.Equals(node.XmlTypeName, "CSharp", StringComparison.Ordinal) &&
             (string.Equals(node.XmlNamespace, runtimeUsingNamespace, StringComparison.Ordinal) ||
              string.Equals(node.XmlNamespace, runtimeClrNamespace, StringComparison.Ordinal) ||
              string.Equals(node.XmlNamespace, markupUsingNamespace, StringComparison.Ordinal) ||
              string.Equals(node.XmlNamespace, markupClrNamespace, StringComparison.Ordinal) ||
              string.Equals(node.XmlNamespace, avaloniaDefaultXmlNamespace, StringComparison.Ordinal)));
        if (!isInlineCSharpNode)
        {
            return false;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (NormalizePropertyName(assignment.PropertyName).Equals("Code", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(assignment.Value))
            {
                code = Unquote(assignment.Value);
                return code.Trim().Length > 0;
            }
        }

        var rawTextContent = node.RawTextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(rawTextContent))
        {
            code = rawTextContent!;
            return true;
        }

        var textContent = node.TextContent?.Trim();
        if (!string.IsNullOrWhiteSpace(textContent))
        {
            code = textContent!;
            return true;
        }

        return false;
    }

    private static bool TryBindInlineCSharpObjectNode(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedObjectNode resolvedNode)
    {
        resolvedNode = default!;
        if (!TryExtractInlineCSharpObjectNodeCode(node, compilation, document, out var rawCode))
        {
            return false;
        }

        var bindingTypeName = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface)?
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
        var normalizedNodeName = NormalizeObjectNodeName(node.Name);

        if (!TryBuildInlineCodeBindingExpression(
                compilation,
                nodeDataType,
                rootTypeSymbol,
                targetType,
                rawCode,
                out var bindingExpression,
                out _,
                out _,
                out var errorMessage))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0112",
                $"Inline C# is invalid: {errorMessage}",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));

            resolvedNode = new ResolvedObjectNode(
                KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
                Name: normalizedNodeName,
                TypeName: bindingTypeName,
                IsBindingObjectNode: true,
                FactoryExpression: "default(" + bindingTypeName + ")",
                FactoryValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                UseServiceProviderConstructor: false,
                UseTopDownInitialization: false,
                PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
                PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
                EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
                Children: ImmutableArray<ResolvedObjectNode>.Empty,
                ChildAttachmentMode: ResolvedChildAttachmentMode.None,
                ContentPropertyName: null,
                Line: node.Line,
                Column: node.Column,
                Condition: node.Condition);
            return true;
        }

        resolvedNode = new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: normalizedNodeName,
            TypeName: bindingTypeName,
            IsBindingObjectNode: true,
            FactoryExpression: bindingExpression,
            FactoryValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition);
        return true;
    }
}
