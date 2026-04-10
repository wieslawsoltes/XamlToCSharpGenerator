using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static bool TryParseBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseBindingMarkup(value, TryParseMarkupExtension, out bindingMarkup);
    }

    private static bool TryParseXBindMarkup(string value, out XBindMarkup xBindMarkup)
    {
        return BindingEventMarkupParser.TryParseXBindMarkup(value, TryParseMarkupExtension, out xBindMarkup);
    }

    private static bool TryParseReflectionBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseReflectionBindingMarkup(value, TryParseMarkupExtension, out bindingMarkup);
    }

    private static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        XamlMarkupExtensionKind extensionKind,
        out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseBindingMarkupCore(
            markup,
            extensionKind,
            TryParseMarkupExtension,
            out bindingMarkup);
    }

    private static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, params string[] argumentNames)
    {
        return BindingEventMarkupParser.TryGetNamedMarkupArgument(markup, argumentNames);
    }

    private static BindingMarkup NormalizeBindingQuerySyntax(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.NormalizeBindingQuerySyntax(bindingMarkup, TryParseMarkupExtension);
    }

    private static bool HasExplicitBindingSource(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.HasExplicitBindingSource(bindingMarkup);
    }

    private static int CountExplicitBindingSources(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.CountExplicitBindingSources(bindingMarkup);
    }

    private static BindingMarkup CreateBindingSourceConflict(BindingMarkup bindingMarkup, string message)
    {
        return BindingEventMarkupParser.CreateBindingSourceConflict(bindingMarkup, message);
    }

    private static bool TryExtractReferenceElementName(string? sourceValue, out string elementName)
    {
        return BindingEventMarkupParser.TryExtractReferenceElementName(
            sourceValue,
            TryParseMarkupExtension,
            out elementName);
    }

    private static bool TryNormalizeReferenceName(string? rawName, out string normalizedName)
    {
        return BindingEventMarkupParser.TryNormalizeReferenceName(rawName, out normalizedName);
    }

    private static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseElementNameQuery(path, out elementName, out normalizedPath);
    }

    private static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseSelfQuery(path, out relativeSource, out normalizedPath);
    }

    private static bool TryParseParentQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseParentQuery(path, out relativeSource, out normalizedPath);
    }

    private static bool CanUseCompiledBinding(BindingMarkup bindingMarkup)
    {
        return !bindingMarkup.HasSourceConflict &&
               string.IsNullOrWhiteSpace(bindingMarkup.ElementName) &&
               bindingMarkup.RelativeSource is null &&
               string.IsNullOrWhiteSpace(bindingMarkup.Source);
    }

    private readonly record struct CompiledBindingAccessorResolution(
        string AccessorExpression,
        string NormalizedPath,
        string? ResultTypeName,
        ITypeSymbol? ResultTypeSymbol,
        ImmutableArray<string> DependencyNames);

    private static bool TryBuildCompiledBindingAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolution resolution,
        out string errorMessage)
    {
        if (CompiledBindingAccessorResolutionService.TryBuildAccessorExpression(
                compilation,
                document,
                sourceType,
                rawPath,
                targetPropertyType,
                unsafeAccessors,
                out var sharedResolution,
                out errorMessage))
        {
            resolution = new CompiledBindingAccessorResolution(
                sharedResolution.AccessorExpression,
                sharedResolution.NormalizedPath,
                sharedResolution.ResultTypeName,
                sharedResolution.ResultTypeSymbol,
                sharedResolution.DependencyNames);
            return true;
        }

        resolution = default;
        return false;
    }

    private static bool SupportsUnsafeAccessor(Compilation compilation)
    {
        return CompiledBindingAccessorResolutionService.SupportsUnsafeAccessor(compilation);
    }

    private static string RegisterUnsafeAccessorDefinition(
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        IMethodSymbol method)
    {
        return CompiledBindingAccessorResolutionService.RegisterUnsafeAccessorDefinition(unsafeAccessors, method);
    }

    private static ITypeSymbol? TryResolveSetterValueType(
        INamedTypeSymbol? objectType,
        ImmutableArray<XamlPropertyAssignment> propertyAssignments,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType)
    {
        if (!IsSetterType(objectType))
        {
            return null;
        }

        foreach (var assignment in propertyAssignments)
        {
            if (assignment.IsAttached)
            {
                continue;
            }

            if (!NormalizePropertyName(assignment.PropertyName).Equals("Property", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryResolveAvaloniaPropertyValueTypeFromToken(
                    assignment.Value,
                    compilation,
                    document,
                    defaultOwnerType,
                    out var valueType))
            {
                return valueType;
            }
        }

        return null;
    }

    private static bool IsSetterType(INamedTypeSymbol? objectType)
    {
        return objectType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               "global::Avalonia.Styling.Setter";
    }

    private static bool IsBindingObjectType(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (IsTypeByMetadataName(typeSymbol, "Avalonia.Data.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.MultiBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.InstancedBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.MultiBinding"))
        {
            return true;
        }

        var bindingBaseType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingBase);
        if (bindingBaseType is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(typeSymbol, bindingBaseType))
        {
            return true;
        }

        var bindingInterfaceType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface);
        if (bindingInterfaceType is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(typeSymbol, bindingInterfaceType))
        {
            return true;
        }

        var bindingInterface2Type = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface2);
        if (bindingInterface2Type is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(typeSymbol, bindingInterface2Type))
        {
            return true;
        }

        return false;
    }

    private static bool IsTypeByMetadataName(INamedTypeSymbol symbol, string metadataName)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Equals("global::" + metadataName, StringComparison.Ordinal);
    }

    private static BindingPriorityScope ResolveCurrentBindingPriorityScope(
        INamedTypeSymbol? nodeType,
        Compilation compilation,
        BindingPriorityScope inheritedScope)
    {
        if (nodeType is null)
        {
            return inheritedScope;
        }

        if (IsStyleType(nodeType, compilation) || IsControlThemeType(nodeType, compilation))
        {
            return BindingPriorityScope.Style;
        }

        if (IsTemplateScopeType(nodeType, compilation))
        {
            return BindingPriorityScope.Template;
        }

        return inheritedScope;
    }

    private static bool TryBindCollectionLiteralPropertyAssignment(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        out ResolvedPropertyElementAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (TryParseMarkupExtension(assignment.Value, out _))
        {
            return false;
        }

        if (property.SetMethod is not null ||
            property.GetMethod is null ||
            property.Type is not INamedTypeSymbol propertyType)
        {
            return false;
        }

        var isClassesLikeProperty =
            property.Name.Equals("Classes", StringComparison.Ordinal) ||
            propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Avalonia.Controls.Classes", StringComparison.Ordinal);

        ITypeSymbol? elementType = null;
        string[] literalItems;
        if (isClassesLikeProperty)
        {
            var classTokens = XamlListValueSemantics.SplitWhitespaceAndCommaTokens(assignment.Value);
            literalItems = classTokens.ToArray();
            elementType = compilation.GetSpecialType(SpecialType.System_String);
        }
        else
        {
            if (!TryGetCollectionElementType(
                    propertyType,
                    out var resolvedElementType,
                    out _,
                    out _))
            {
                return false;
            }

            if (resolvedElementType.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            elementType = resolvedElementType;
            literalItems = XamlListValueSemantics.SplitCommaSeparatedTokens(assignment.Value).ToArray();
        }

        if (elementType is null || elementType.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var values = ImmutableArray.CreateBuilder<ResolvedObjectNode>(literalItems.Length);
        foreach (var literalItem in literalItems)
        {
            values.Add(new ResolvedObjectNode(
                KeyExpression: null,
                Name: null,
                TypeName: elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsBindingObjectNode: false,
                FactoryExpression: "\"" + Escape(literalItem) + "\"",
                FactoryValueRequirements: ResolvedValueRequirements.None,
                UseServiceProviderConstructor: false,
                UseTopDownInitialization: false,
                PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
                PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
                EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
                Children: ImmutableArray<ResolvedObjectNode>.Empty,
                ChildAttachmentMode: ResolvedChildAttachmentMode.None,
                ContentPropertyName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition));
        }

        var resolvedValues = values.ToImmutable();
        var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValueType(
            propertyType,
            elementType,
            resolvedValues.Length);

        resolvedAssignment = new ResolvedPropertyElementAssignment(
            PropertyName: property.Name,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsCollectionAdd: true,
            IsDictionaryMerge: false,
            ObjectValues: resolvedValues,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            CollectionAddInstructions: collectionAddInstructions);
        return true;
    }

    private static bool TryBindAttachedPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        string targetTypeName,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? explicitOwnerType,
        string? explicitPropertyName,
        string? explicitPropertyFieldName,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool isInsideDataTemplate = false,
        string? xBindDefaultMode = null,
        XamlObjectNode? currentNode = null)
    {
        resolvedAssignment = null;

        var attachedPropertyName = explicitPropertyName;
        var ownerType = explicitOwnerType;
        if (ownerType is null || string.IsNullOrWhiteSpace(attachedPropertyName))
        {
            if (!TrySplitOwnerQualifiedPropertyToken(
                    assignment.PropertyName,
                    out var ownerToken,
                    out var normalizedPropertyName))
            {
                return false;
            }

            attachedPropertyName = normalizedPropertyName;
            ownerType = ResolveOwnerQualifiedMemberOwnerType(
                compilation,
                document,
                ownerToken,
                assignment.XmlNamespace);
        }

        if (ownerType is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(explicitPropertyFieldName) &&
            !TryFindAvaloniaPropertyField(
                ownerType,
                attachedPropertyName!,
                out _,
                out _,
                explicitPropertyFieldName))
        {
            return false;
        }

        return TryBindAvaloniaPropertyAssignment(
            targetType,
            targetTypeName,
            attachedPropertyName!,
            assignment,
            compilation,
            document,
            options,
            diagnostics,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            fallbackValueType: null,
            bindingPriorityScope,
            setterTargetType,
            rootTypeSymbol,
            out resolvedAssignment,
            allowCompiledBindingRegistration: true,
            explicitOwnerType: ownerType,
            explicitFrameworkPropertyFieldName: explicitPropertyFieldName,
            isInsideDataTemplate: isInsideDataTemplate,
            xBindDefaultMode: xBindDefaultMode,
            currentNode: currentNode);
    }

    private static bool TryBindAttachedStaticSetterAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var attachedPropertyName))
        {
            return false;
        }

        if (!TryFindAttachedSetterMethod(
                ownerType!,
                attachedPropertyName,
                targetType,
                out var resolvedOwnerType,
                out var setterMethod))
        {
            return false;
        }

        var valueType = setterMethod.Parameters[1].Type;
        if (!TryConvertValueConversion(
                assignment.Value,
                valueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedValue,
                allowObjectStringLiteralFallback: !options.StrictMode))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{assignment.Value}' for attached setter '{resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{setterMethod.Name}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: setterMethod.Name,
            ValueExpression: convertedValue.Expression,
            ClrPropertyOwnerTypeName: resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: null,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: convertedValue.ValueKind,
            RequiresStaticResourceResolver: convertedValue.RequiresStaticResourceResolver,
            ValueRequirements: convertedValue.EffectiveRequirements);
        return true;
    }

    private static bool TryBindAttachedClassPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var className))
        {
            return false;
        }

        var ownerTypeName = ownerType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!ownerTypeName.Equals("global::Avalonia.Controls.Classes", StringComparison.Ordinal))
        {
            return false;
        }

        var styledElementType = ResolveContractType(compilation, TypeContractId.StyledElement);
        if (styledElementType is null || !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(targetType, styledElementType))
        {
            return false;
        }

        if (TryParseBindingMarkup(assignment.Value, out var classBindingMarkup))
        {
            if (TryReportBindingSourceConflict(
                    classBindingMarkup,
                    diagnostics,
                    document,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode))
            {
                return true;
            }

            var normalizedBindingMarkup = NormalizeBindingQuerySyntax(classBindingMarkup);
            if (!TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    normalizedBindingMarkup,
                    setterTargetType,
                    bindingPriorityScope,
                    out var classBindingExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0102",
                    $"Could not convert class binding literal '{assignment.Value}' for '{assignment.PropertyName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: "SetClass:" + className,
                ValueExpression: classBindingExpression,
                ClrPropertyOwnerTypeName: "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime",
                ClrPropertyTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        if (!TryConvertValueConversion(
                assignment.Value,
                boolType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedValue,
                allowObjectStringLiteralFallback: false))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert class binding literal '{assignment.Value}' for '{assignment.PropertyName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: "SetClass:" + className,
            ValueExpression: convertedValue.Expression,
            ClrPropertyOwnerTypeName: "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime",
            ClrPropertyTypeName: null,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: convertedValue.ValueKind,
            RequiresStaticResourceResolver: convertedValue.RequiresStaticResourceResolver,
            ValueRequirements: convertedValue.EffectiveRequirements);
        return true;
    }

    private static bool TryBindAttachedEventSubscription(
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        XamlObjectNode? currentNode,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var eventName))
        {
            return false;
        }

        var eventAssignment = new XamlPropertyAssignment(
            PropertyName: eventName,
            XmlNamespace: assignment.XmlNamespace,
            Value: assignment.Value,
            IsAttached: false,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition);

        return TryBindEventSubscription(
            ownerType!,
            eventAssignment,
            compilation,
            nodeDataType,
            rootTypeSymbol,
            isInsideDataTemplate,
            diagnostics,
            document,
            options,
            currentNode,
            out subscription);
    }

    private static bool TryResolveAttachedOwnerAndMember(
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        out INamedTypeSymbol? ownerType,
        out string memberName)
    {
        ownerType = null;
        memberName = string.Empty;
        if (!TrySplitOwnerQualifiedPropertyToken(
                assignment.PropertyName,
                out var ownerToken,
                out var normalizedMemberName))
        {
            return false;
        }

        ownerType = ResolveOwnerQualifiedMemberOwnerType(
            compilation,
            document,
            ownerToken,
            assignment.XmlNamespace);
        if (ownerType is null)
        {
            return false;
        }

        memberName = normalizedMemberName;
        return true;
    }

    private static INamedTypeSymbol? ResolveOwnerQualifiedMemberOwnerType(
        Compilation compilation,
        XamlDocumentModel document,
        string ownerToken,
        string? xmlNamespace)
    {
        if (!string.IsNullOrWhiteSpace(xmlNamespace))
        {
            var namespacedType = ResolveTypeSymbol(compilation, xmlNamespace!, ownerToken);
            if (namespacedType is not null)
            {
                return namespacedType;
            }
        }

        return ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
    }

    private static bool TryFindAttachedSetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        INamedTypeSymbol targetType,
        out INamedTypeSymbol resolvedOwnerType,
        out IMethodSymbol setterMethod)
    {
        var methodName = "Set" + NormalizePropertyName(propertyName);
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    candidate.IsStatic &&
                    candidate.MethodKind == MethodKind.Ordinary &&
                    !candidate.IsGenericMethod &&
                    candidate.Parameters.Length == 2 &&
                    candidate.RefKind == RefKind.None &&
                    candidate.Parameters[0].RefKind == RefKind.None &&
                    candidate.Parameters[1].RefKind == RefKind.None &&
                    TypeSymbolLookupSemanticsService.IsTypeAssignableTo(targetType, candidate.Parameters[0].Type));
            if (method is null)
            {
                continue;
            }

            resolvedOwnerType = current;
            setterMethod = method;
            return true;
        }

        resolvedOwnerType = ownerType;
        setterMethod = null!;
        return false;
    }

    private static bool TryBindEventSubscription(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        XamlObjectNode? currentNode,
        out ResolvedEventSubscription? subscription)
    {
        return EventSubscriptionBindingService.TryBindAssignment(
            targetType,
            assignment,
            compilation,
            nodeDataType,
            rootTypeSymbol,
            isInsideDataTemplate,
            diagnostics,
            document,
            options,
            currentNode,
            out subscription);
    }

    private static bool TryBindInlineEventLambda(
        XamlPropertyAssignment assignment,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out bool handled)
    {
        eventBindingDefinition = null;
        handled = false;

        if (!TryParseInlineEventLambdaExpression(assignment.Value, out var lambdaExpression))
        {
            return false;
        }

        handled = true;

        if (CSharpMarkupExpressionSemantics.IsAsyncLambdaExpression(lambdaExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Inline event lambda on '{eventName}' does not support async lambdas.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (EventBindingDefinitionService.TryBuildInlineCodeDefinition(
                rawCode: lambdaExpression,
                isLambdaExpression: true,
                eventName: eventName,
                eventHandlerType: eventHandlerType,
                compilation: compilation,
                nodeDataType: nodeDataType,
                targetType: targetType,
                rootTypeSymbol: rootTypeSymbol,
                documentClassFullName: document.IsClassBacked ? document.ClassFullName : null,
                line: assignment.Line,
                column: assignment.Column,
                out eventBindingDefinition,
                out var errorMessage))
        {
            return true;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0600",
            errorMessage,
            document.FilePath,
            assignment.Line,
            assignment.Column,
            options.StrictMode));
        return false;
    }

    private static bool TryBindInlineEventCodeSubscription(
        INamedTypeSymbol targetType,
        string propertyName,
        string rawCode,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        return EventSubscriptionBindingService.TryBindInlineCode(
            targetType,
            propertyName,
            rawCode,
            line,
            column,
            condition,
            compilation,
            nodeDataType,
            rootTypeSymbol,
            diagnostics,
            document,
            options,
            out subscription);
    }

    private static string BuildCompiledBindingAccessorPlaceholderToken(int line, int column)
    {
        return "__AXSG_CompiledBindingAccessor_" +
               line.ToString(CultureInfo.InvariantCulture) +
               "_" +
               column.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryReportBindingSourceConflict(
        BindingMarkup bindingMarkup,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        bool strictMode)
    {
        if (!bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0111",
            bindingMarkup.SourceConflictMessage ?? "Binding source configuration is invalid.",
            document.FilePath,
            line,
            column,
            strictMode));
        return true;
    }

    private static string NormalizeRuntimeBindingPath(string path)
    {
        return XamlRuntimeBindingPathSemantics.NormalizePath(path);
    }

    private static bool TryMapBindingMode(string modeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapBindingModeToken(modeToken, out expression);
    }

    private static bool TryMapRelativeSourceMode(string modeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapRelativeSourceModeToken(modeToken, out expression);
    }

    private static bool TryMapTreeType(string treeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapTreeTypeToken(treeToken, out expression);
    }

    private static string? GetDefaultBindingPriorityToken(BindingPriorityScope scope)
    {
        return scope switch
        {
            BindingPriorityScope.Style => "Style",
            BindingPriorityScope.Template => "Template",
            _ => null
        };
    }

    private static bool TryConvertUntypedValueExpression(string value, out string expression)
    {
        var trimmed = value.Trim();

        if (XamlScalarLiteralSemantics.TryParseBoolean(trimmed, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (XamlScalarLiteralSemantics.TryParseInt32(trimmed, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (XamlScalarLiteralSemantics.TryParseDouble(trimmed, out var doubleValue))
        {
            expression = FormatDoubleLiteral(doubleValue);
            return true;
        }

        expression = "\"" + Escape(trimmed) + "\"";
        return true;
    }

    private static bool TryParseInlineEventLambdaExpression(string value, out string lambdaExpression)
    {
        lambdaExpression = string.Empty;
        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                implicitExpressionsEnabled: true,
                looksLikeMarkupExtensionStart: static _ => false,
                out var rawExpression,
                out _,
                out var isLambdaExpression) ||
            !isLambdaExpression)
        {
            return false;
        }

        lambdaExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawExpression);
        return lambdaExpression.Length > 0;
    }

    private static bool IsCommandTargetType(ITypeSymbol? targetPropertyType, INamedTypeSymbol? commandType)
    {
        if (targetPropertyType is null)
        {
            return false;
        }

        if (commandType is not null)
        {
            if (TypeSymbolLookupSemanticsService.AreEquivalentTypesIgnoringNullable(targetPropertyType, commandType))
            {
                return true;
            }
        }

        return IsCommandMetadataType(targetPropertyType);
    }

    private static bool IsCommandMetadataType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.Name.Equals("ICommand", StringComparison.Ordinal) &&
               namedType.ContainingNamespace.ToDisplayString().Equals("System.Windows.Input", StringComparison.Ordinal);
    }

    private static bool IsCommandLikeType(ITypeSymbol type)
    {
        if (IsCommandMetadataType(type))
        {
            return true;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (namedType.AllInterfaces.Any(IsCommandMetadataType))
        {
            return true;
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (IsCommandMetadataType(current))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        if (TrySplitOwnerQualifiedPropertyToken(propertyName, out _, out var normalized))
        {
            return normalized;
        }

        return propertyName;
    }

    private static bool IsDesignTimePropertyToken(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var trimmed = propertyName.Trim();
        return trimmed.StartsWith("Design.", StringComparison.Ordinal);
    }

    private static bool TrySplitOwnerQualifiedPropertyToken(
        string propertyToken,
        out string ownerToken,
        out string propertyName)
    {
        return XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
            propertyToken,
            out ownerToken,
            out propertyName);
    }

    private static bool HasResolvedPropertyAssignment(
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResolvedPropertyElementAssignment(
        ImmutableArray<ResolvedPropertyElementAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValueExpressionForLiteralPrimitives(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out string expression)
    {
        return TryConvertValueExpression(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            (BindingPriorityScope)bindingPriorityScope,
            out expression);
    }

    private static bool TryConvertValueExpression(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null)
    {
        return ValueConversionSemanticService.TryConvertExpression(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out expression,
            preferTypedStaticResourceCoercion,
            allowObjectStringLiteralFallback,
            selectorNestingTypeHint);
    }

    private static bool TryConvertValueConversion(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        bool allowStaticParseMethodFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        return ValueConversionSemanticService.TryConvert(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out conversion,
            preferTypedStaticResourceCoercion,
            allowObjectStringLiteralFallback,
            allowStaticParseMethodFallback,
            selectorNestingTypeHint,
            converterAttributes);
    }

    private static bool TryConvertValueForCollectionAdd(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback)
    {
        return ValueConversionSemanticService.TryConvertForCollectionAdd(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out conversion,
            allowObjectStringLiteralFallback);
    }

    private static bool TryConvertEnumValueExpression(
        INamedTypeSymbol enumType,
        string value,
        out string expression)
    {
        return LiteralConversionPrimitivesService.TryConvertEnumValueExpression(
            enumType,
            value,
            out expression);
    }

    private static bool TryConvertStaticPropertyValueExpression(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        return LiteralConversionPrimitivesService.TryConvertStaticPropertyValueExpression(
            type,
            value,
            out expression);
    }

    private static bool TryConvertTimeSpanLiteralExpression(string value, out string expression)
    {
        return LiteralConversionPrimitivesService.TryConvertTimeSpanLiteralExpression(
            value,
            out expression);
    }

    private static bool TryResolveAvaloniaNamedColorExpression(
        INamedTypeSymbol colorType,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                token,
                out var ownerToken,
                out var memberToken) &&
            ownerToken.Equals("Colors", StringComparison.OrdinalIgnoreCase))
        {
            token = memberToken.Trim();
        }

        if (token.Length == 0 ||
            !XamlIdentifierSemantics.IsIdentifier(token))
        {
            return false;
        }

        var colorsType = colorType.ContainingNamespace.GetTypeMembers("Colors").FirstOrDefault();
        if (colorsType is null)
        {
            return false;
        }

        var property = colorsType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(member =>
                member.IsStatic &&
                SymbolEqualityComparer.Default.Equals(member.Type, colorType) &&
                member.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (property is null)
        {
            return false;
        }

        expression = "global::Avalonia.Media.Colors." + property.Name;
        return true;
    }

    private static string FormatDoubleLiteral(double value)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }

    private static string FormatSingleLiteral(float value)
    {
        if (float.IsNaN(value))
        {
            return "global::System.Single.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "global::System.Single.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "global::System.Single.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    private static string FormatHexUInt32Literal(uint value)
    {
        return "0x" + value.ToString("X8", CultureInfo.InvariantCulture) + "u";
    }

    private static bool HasPublicConstructorWithParameterTypes(
        INamedTypeSymbol type,
        params string[] parameterTypeNames)
    {
        return SymbolConstructionSemanticsService.HasPublicConstructorWithParameterTypes(
            type,
            parameterTypeNames);
    }

    private static bool HasPublicStaticMethod(
        INamedTypeSymbol type,
        string methodName,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames)
    {
        return SymbolConstructionSemanticsService.HasPublicStaticMethod(
            type,
            methodName,
            returnTypeName,
            parameterTypeNames);
    }

    private static bool TryFindPublicMethod(
        INamedTypeSymbol type,
        string methodName,
        bool isStatic,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames,
        out IMethodSymbol method)
    {
        return SymbolConstructionSemanticsService.TryFindPublicMethod(
            type,
            methodName,
            isStatic,
            returnTypeName,
            parameterTypeNames,
            out method);
    }

    private static bool HasPublicStaticProperty(
        INamedTypeSymbol type,
        string propertyName,
        string returnTypeName)
    {
        return SymbolConstructionSemanticsService.HasPublicStaticProperty(
            type,
            propertyName,
            returnTypeName);
    }

    private static bool TypeNameMatches(string actualTypeName, string expectedTypeName)
    {
        return SymbolConstructionSemanticsService.TypeNameMatches(actualTypeName, expectedTypeName);
    }

    private static string NormalizeTypeNameForComparison(string typeName)
    {
        return SymbolConstructionSemanticsService.NormalizeTypeNameForComparison(typeName);
    }

    private static bool TryConvertCollectionLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        return LiteralConversionPrimitivesService.TryConvertCollectionLiteralExpression(
            targetType,
            value,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out expression);
    }

    private static bool TryGetCollectionElementType(
        ITypeSymbol targetType,
        out ITypeSymbol elementType,
        out bool isArrayTarget,
        out INamedTypeSymbol? collectionTypeForSplitConfig)
    {
        return LiteralConversionPrimitivesService.TryGetCollectionElementType(
            targetType,
            out elementType,
            out isArrayTarget,
            out collectionTypeForSplitConfig);
    }

    private static void TryGetCollectionSplitConfiguration(
        INamedTypeSymbol collectionType,
        ref string[] separators,
        ref StringSplitOptions splitOptions,
        bool trimEntries)
    {
        var listAttribute = collectionType.GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Metadata.AvaloniaListAttribute");
        if (listAttribute is null)
        {
            return;
        }

        foreach (var namedArgument in listAttribute.NamedArguments)
        {
            var key = namedArgument.Key;
            var value = namedArgument.Value;
            if (key.Equals("Separators", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Array &&
                !value.IsNull)
            {
                var configuredSeparators = value.Values
                    .Where(item => item.Kind == TypedConstantKind.Primitive && item.Value is string)
                    .Select(item => (string)item.Value!)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (configuredSeparators.Length > 0)
                {
                    separators = configuredSeparators;
                }

                continue;
            }

            if (key.Equals("SplitOptions", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Enum &&
                value.Value is int configuredSplitOptions)
            {
                splitOptions = (StringSplitOptions)configuredSplitOptions;
            }
        }

        _ = trimEntries;
    }

    private static bool TryConvertMarkupExtensionExpression(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true)
    {
        return ValueConversionSemanticService.TryConvertMarkupExtensionExpression(
            value,
            targetType,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out expression,
            preferTypedStaticResourceCoercion);
    }

    private static bool TryConvertMarkupExtensionConversion(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true)
    {
        return ValueConversionSemanticService.TryConvertMarkupExtension(
            value,
            targetType,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out conversion,
            preferTypedStaticResourceCoercion);
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return GetActiveMarkupExpressionParser().TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryParseRelativeSourceMarkup(string value, out RelativeSourceMarkup relativeSourceMarkup)
    {
        return BindingEventMarkupParser.TryParseRelativeSourceMarkup(
            value,
            TryParseMarkupExtension,
            out relativeSourceMarkup);
    }

    private static int IndexOfTopLevel(string value, char token)
    {
        return TopLevelTextParser.IndexOfTopLevel(value, token);
    }

    private static string Unquote(string value)
    {
        return XamlQuotedValueSemantics.TrimAndUnquote(value);
    }

    private static bool IsQuotedLiteral(string value)
    {
        return XamlQuotedValueSemantics.IsWrapped(value.Trim());
    }
}
