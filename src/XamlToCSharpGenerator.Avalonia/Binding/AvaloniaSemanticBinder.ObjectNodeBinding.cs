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
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Avalonia.Binding.Services;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{
    private sealed record BindingScopeContext(
        XamlObjectNode Node,
        INamedTypeSymbol? NodeType,
        INamedTypeSymbol? NodeDataType,
        bool CompileBindingsEnabled,
        BindingScopeContext? Parent,
        string? ParentPropertyName);


    private static ResolvedObjectNode BindObjectNode(
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
        INamedTypeSymbol? forcedType = null,
        INamedTypeSymbol? rootTypeSymbol = null,
        BindingScopeContext? parentScopeContext = null,
        string? parentPropertyName = null)
    {
        var symbol = forcedType ?? ResolveObjectTypeSymbol(compilation, document, node);
        var typeName = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";

        if (IsXamlArrayNode(node))
        {
            return BindXamlArrayNode(
                node,
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
                rootTypeSymbol);
        }

        var compileBindingsEnabled = node.CompileBindings ?? inheritedCompileBindingsEnabled;
        var scopeContext = new BindingScopeContext(
            node,
            symbol,
            inheritedDataType,
            compileBindingsEnabled,
            parentScopeContext,
            parentPropertyName);
        var nodeDataType = ResolveNodeDataType(
            compilation,
            document,
            node,
            symbol,
            inheritedDataType,
            scopeContext,
            unsafeAccessors);
        scopeContext = scopeContext with { NodeDataType = nodeDataType };
        var currentSetterTargetType = ResolveCurrentSetterTargetType(
            symbol,
            node,
            compilation,
            document,
            inheritedSetterTargetType);
        var currentBindingPriorityScope = ResolveCurrentBindingPriorityScope(
            symbol,
            compilation,
            inheritedBindingPriorityScope);
        var contentPropertyName = FindContentPropertyName(symbol);
        var inferredSetterValueType = TryResolveSetterValueType(
            symbol,
            node.PropertyAssignments,
            compilation,
            document,
            currentSetterTargetType);

        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>();
        var propertyElementAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var eventSubscriptions = ImmutableArray.CreateBuilder<ResolvedEventSubscription>();
        foreach (var assignment in node.PropertyAssignments)
        {
            if (ShouldSkipConditionalBranch(
                    assignment.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (symbol is null)
            {
                continue;
            }

            if (IsDesignTimePropertyToken(assignment.PropertyName))
            {
                continue;
            }

            var propertyAlias = ResolvePropertyAlias(symbol, assignment.PropertyName);
            var assignmentDataType = ResolveAssignmentBindingDataType(
                assignment,
                symbol,
                compilation,
                document,
                inheritedDataType,
                nodeDataType,
                scopeContext,
                unsafeAccessors);

            if (assignment.IsAttached || propertyAlias.HasAvaloniaPropertyAlias)
            {
                if (TryBindAttachedPropertyAssignment(
                        assignment,
                        symbol,
                        typeName,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        assignmentDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol,
                        explicitOwnerType: propertyAlias.AvaloniaPropertyOwnerType,
                        explicitPropertyName: propertyAlias.ResolvedPropertyName,
                        explicitPropertyFieldName: propertyAlias.AvaloniaPropertyFieldName,
                        out var attachedAssignment))
                {
                    if (attachedAssignment is not null)
                    {
                        assignments.Add(attachedAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedStaticSetterAssignment(
                        assignment,
                        symbol,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var staticSetterAssignment))
                {
                    if (staticSetterAssignment is not null)
                    {
                        assignments.Add(staticSetterAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedClassPropertyAssignment(
                        assignment,
                        symbol,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var classPropertyAssignment))
                {
                    if (classPropertyAssignment is not null)
                    {
                        assignments.Add(classPropertyAssignment);
                    }

                    continue;
                }

                if (TryBindAttachedEventSubscription(
                        assignment,
                        compilation,
                        assignmentDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var attachedEventSubscription))
                {
                    if (attachedEventSubscription is not null)
                    {
                        eventSubscriptions.Add(attachedEventSubscription);
                    }

                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Attached property '{assignment.PropertyName}' could not be resolved on this scope.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is not null &&
                property.SetMethod is null &&
                TryBindCollectionLiteralPropertyAssignment(
                    symbol,
                    property,
                    assignment,
                    compilation,
                    out var collectionLiteralAssignment))
            {
                if (collectionLiteralAssignment is not null)
                {
                    propertyElementAssignments.Add(collectionLiteralAssignment);
                }

                continue;
            }

            if (property is not null && property.SetMethod is not null)
            {
                if (TryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var inlineCode))
                {
                    if (TryBindAvaloniaPropertyAssignment(
                            symbol,
                            typeName,
                            normalizedPropertyName,
                            assignment,
                            compilation,
                            document,
                            options,
                            diagnostics,
                            compiledBindings,
                            unsafeAccessors,
                            compileBindingsEnabled,
                            assignmentDataType,
                            property.Type,
                            currentBindingPriorityScope,
                            currentSetterTargetType,
                            rootTypeSymbol,
                            out var avaloniaInlineCodeAssignment,
                            allowCompiledBindingRegistration: false))
                    {
                        if (avaloniaInlineCodeAssignment is not null)
                        {
                            assignments.Add(avaloniaInlineCodeAssignment);
                        }

                        continue;
                    }

                    if (!TryBuildInlineCodeBindingExpression(
                            compilation,
                            assignmentDataType,
                            rootTypeSymbol,
                            symbol,
                            inlineCode,
                            out var inlineBindingExpression,
                            out _,
                            out _,
                            out var inlineErrorMessage))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0112",
                            $"Inline C# for '{property.Name}' is invalid: {inlineErrorMessage}",
                            document.FilePath,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode));
                        continue;
                    }

                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: property.Name,
                        ValueExpression: inlineBindingExpression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: assignment.Line,
                        Column: assignment.Column,
                        Condition: assignment.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                        PreserveBindingValue: HasAssignBindingAttribute(property)));
                    continue;
                }

                var isPotentialCSharpExpressionMarkup =
                    ExpressionClassificationService.TryParseCSharpExpressionMarkup(
                        assignment.Value,
                        compilation,
                        document,
                        options.CSharpExpressionsEnabled,
                        options.ImplicitCSharpExpressionsEnabled,
                        out _,
                        out _);

                if (isPotentialCSharpExpressionMarkup &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        assignmentDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        currentSetterTargetType,
                        rootTypeSymbol,
                        out var avaloniaShorthandAssignment,
                        allowCompiledBindingRegistration: true))
                {
                    if (avaloniaShorthandAssignment is not null)
                    {
                        assignments.Add(avaloniaShorthandAssignment);
                    }

                    continue;
                }

                if (isPotentialCSharpExpressionMarkup &&
                    TryResolveImplicitCSharpShorthandExpression(
                        assignment.Value,
                        compilation,
                        document,
                        options,
                        assignmentDataType,
                        rootTypeSymbol,
                        currentSetterTargetType ?? symbol,
                        out var isShorthandExpression,
                        out var shorthandResolution) &&
                    isShorthandExpression)
                {
                    if (!string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticId) &&
                        !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticMessage))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            shorthandResolution.DiagnosticId!,
                            shorthandResolution.DiagnosticMessage!,
                            document.FilePath,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode));
                    }

                    continue;
                }

                if (TryConvertCSharpExpressionMarkupToBindingExpression(
                        assignment.Value,
                        compilation,
                        document,
                        options,
                        assignmentDataType,
                        accessorPlaceholderToken: null,
                        out var isExpressionMarkup,
                        out var expressionBindingValueExpression,
                        out var expressionAccessorExpression,
                        out var normalizedExpression,
                        out var expressionResultTypeName,
                        out var expressionErrorCode,
                        out var expressionErrorMessage))
                {
                    if (TryBindAvaloniaPropertyAssignment(
                            symbol,
                            typeName,
                            normalizedPropertyName,
                            assignment,
                            compilation,
                            document,
                            options,
                            diagnostics,
                            compiledBindings,
                            unsafeAccessors,
                            compileBindingsEnabled,
                            assignmentDataType,
                            property.Type,
                            currentBindingPriorityScope,
                            currentSetterTargetType,
                            rootTypeSymbol,
                            out var avaloniaExpressionBindingAssignment,
                            allowCompiledBindingRegistration: true))
                    {
                        if (avaloniaExpressionBindingAssignment is not null)
                        {
                            assignments.Add(avaloniaExpressionBindingAssignment);
                        }

                        continue;
                    }

                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: property.Name,
                        ValueExpression: expressionBindingValueExpression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: assignment.Line,
                        Column: assignment.Column,
                        Condition: assignment.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                        PreserveBindingValue: HasAssignBindingAttribute(property)));
                    continue;
                }

                if (isExpressionMarkup)
                {
                    var message = expressionErrorCode == "AXSG0110"
                        ? $"Expression binding for '{property.Name}' requires x:DataType in scope."
                        : $"Expression binding for '{property.Name}' is invalid: {expressionErrorMessage}";
                    diagnostics.Add(new DiagnosticInfo(
                        expressionErrorCode,
                        message,
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    continue;
                }

                if (TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
                {
                    if (TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode))
                    {
                        continue;
                    }

                    var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || compileBindingsEnabled;
                    INamedTypeSymbol? compiledBindingSourceType = null;
                    var requiresAmbientDataType = false;
                    var shouldCompileBinding = wantsCompiledBinding &&
                                               TryResolveCompiledBindingSourceType(
                                                   compilation,
                                                   document,
                                                   bindingMarkup,
                                                   assignmentDataType,
                                                   currentSetterTargetType ?? symbol,
                                                   out compiledBindingSourceType,
                                                   out requiresAmbientDataType);
                    if (shouldCompileBinding)
                    {
                        if (!TryBuildCompiledBindingAccessorExpression(
                                compilation,
                                document,
                                compiledBindingSourceType!,
                                bindingMarkup.Path,
                                property.Type,
                                unsafeAccessors,
                                out var compiledBindingResolution,
                                out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{compiledBindingSourceType!.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                assignment.Line,
                                assignment.Column,
                                options.StrictMode));
                            continue;
                        }

                        var compiledBindingAccessorPlaceholderToken = BuildCompiledBindingAccessorPlaceholderToken(
                            assignment.Line,
                            assignment.Column);
                        compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                            TargetTypeName: typeName,
                            TargetPropertyName: property.Name,
                            Path: compiledBindingResolution.NormalizedPath,
                            SourceTypeName: compiledBindingSourceType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ResultTypeName: compiledBindingResolution.ResultTypeName,
                            AccessorExpression: compiledBindingResolution.AccessorExpression,
                            IsSetterBinding: false,
                            Line: assignment.Line,
                            Column: assignment.Column,
                            AccessorPlaceholderToken: compiledBindingAccessorPlaceholderToken));

                        if (TryBindAvaloniaPropertyAssignment(
                                symbol,
                                typeName,
                                normalizedPropertyName,
                                assignment,
                                compilation,
                                document,
                                options,
                                diagnostics,
                                compiledBindings,
                                unsafeAccessors,
                                compileBindingsEnabled,
                                assignmentDataType,
                                property.Type,
                                currentBindingPriorityScope,
                                currentSetterTargetType,
                                rootTypeSymbol,
                                out var compiledBindingAssignment,
                                allowCompiledBindingRegistration: false,
                                compiledBindingAccessorPlaceholderToken: compiledBindingAccessorPlaceholderToken))
                        {
                            if (compiledBindingAssignment is not null)
                            {
                                assignments.Add(compiledBindingAssignment);
                            }

                            continue;
                        }
                    }
                    else if (wantsCompiledBinding && requiresAmbientDataType)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0110",
                            $"Compiled binding for '{property.Name}' requires x:DataType in scope.",
                            document.FilePath,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode));
                        continue;
                    }

                    if (TryBindAvaloniaPropertyAssignment(
                            symbol,
                            typeName,
                            normalizedPropertyName,
                            assignment,
                            compilation,
                            document,
                            options,
                            diagnostics,
                            compiledBindings,
                            unsafeAccessors,
                            compileBindingsEnabled,
                            assignmentDataType,
                            property.Type,
                            currentBindingPriorityScope,
                            currentSetterTargetType,
                            rootTypeSymbol,
                            out var bindingAssignment,
                            allowCompiledBindingRegistration: false))
                    {
                        if (bindingAssignment is not null)
                        {
                            assignments.Add(bindingAssignment);
                        }

                        continue;
                    }

                    if (TryBuildBindingValueExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            property.Type,
                            currentSetterTargetType,
                            currentBindingPriorityScope,
                            out var runtimeBindingExpression))
                    {
                        assignments.Add(new ResolvedPropertyAssignment(
                            PropertyName: property.Name,
                            ValueExpression: runtimeBindingExpression,
                            AvaloniaPropertyOwnerTypeName: null,
                            AvaloniaPropertyFieldName: null,
                            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            BindingPriorityExpression: null,
                            Line: assignment.Line,
                            Column: assignment.Column,
                            Condition: assignment.Condition,
                            ValueKind: ResolvedValueKind.Binding,
                            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                            PreserveBindingValue: HasAssignBindingAttribute(property)));
                        continue;
                    }

                    if (shouldCompileBinding)
                    {
                        continue;
                    }
                }

                if (currentBindingPriorityScope == BindingPriorityScope.Template &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        assignmentDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        currentSetterTargetType,
                        rootTypeSymbol,
                        out var templatePriorityAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (templatePriorityAssignment is not null)
                    {
                        assignments.Add(templatePriorityAssignment);
                    }

                    continue;
                }

                if (TryParseMarkupExtension(assignment.Value, out _) &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        assignmentDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        currentSetterTargetType,
                        rootTypeSymbol,
                        out var markupExtensionAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (markupExtensionAssignment is not null)
                    {
                        assignments.Add(markupExtensionAssignment);
                    }

                    continue;
                }

                var isSetterValueProperty = property.Name.Equals("Value", StringComparison.Ordinal) &&
                                            IsSetterType(symbol);
                var conversionTargetType = property.Type;
                if (isSetterValueProperty &&
                    inferredSetterValueType is not null)
                {
                    if (conversionTargetType.SpecialType == SpecialType.System_Object)
                    {
                        conversionTargetType = inferredSetterValueType;
                    }
                }

                var valueExpression = string.Empty;
                var valueKind = ResolvedValueKind.Literal;
                var requiresStaticResourceResolver = false;
                var valueRequirements = ResolvedValueRequirements.None;
                if (isSetterValueProperty &&
                    TryBuildRuntimeXamlFragmentExpression(
                        assignment.Value,
                        conversionTargetType,
                        document,
                        out var runtimeXamlSetterValueExpression))
                {
                    valueExpression = runtimeXamlSetterValueExpression;
                    valueKind = ResolvedValueKind.RuntimeXamlFallback;
                    valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
                }

                var selectorNestingTypeHint =
                    IsStyleType(symbol, compilation) &&
                    property.Name.Equals("Selector", StringComparison.Ordinal)
                        ? inheritedSetterTargetType
                        : null;

                if (valueExpression.Length == 0 &&
                    HasResolveByNameSemantics(symbol, property.Name) &&
                    TryBuildResolveByNameLiteralExpression(
                        assignment.Value,
                        conversionTargetType,
                        out var resolveByNameValueExpression))
                {
                    valueExpression = resolveByNameValueExpression;
                    valueKind = ResolvedValueKind.MarkupExtension;
                }

                if (valueExpression.Length == 0 &&
                    property.Type is INamedTypeSymbol delegateType &&
                    delegateType.TypeKind == TypeKind.Delegate &&
                    TryBuildDelegateMethodGroupValueExpression(
                        assignment.Value,
                        delegateType,
                        rootTypeSymbol,
                        out var delegateMethodExpression))
                {
                    valueExpression = delegateMethodExpression;
                }

                if (valueExpression.Length == 0 && isSetterValueProperty)
                {
                    if (!TryResolveSetterValueWithPolicy(
                            rawValue: assignment.Value,
                            conversionTargetType: conversionTargetType,
                            compilation: compilation,
                            document: document,
                            setterTargetType: currentSetterTargetType,
                            bindingPriorityScope: currentBindingPriorityScope,
                            strictMode: options.StrictMode,
                            preferTypedStaticResourceCoercion: true,
                            allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode &&
                                                                            conversionTargetType.SpecialType == SpecialType.System_Object,
                            allowCompatibilityStringLiteralFallback: !options.StrictMode &&
                                                                     conversionTargetType.SpecialType == SpecialType.System_Object,
                            propertyName: property.Name,
                            ownerDisplayName: symbol.ToDisplayString(),
                            line: assignment.Line,
                            column: assignment.Column,
                            diagnostics: diagnostics,
                            resolution: out var setterResolution,
                            selectorNestingTypeHint: selectorNestingTypeHint,
                            setterContext: false))
                    {
                        continue;
                    }

                    valueExpression = setterResolution.Expression;
                    valueKind = setterResolution.ValueKind;
                    requiresStaticResourceResolver = setterResolution.RequiresStaticResourceResolver;
                    valueRequirements = setterResolution.ValueRequirements;
                }
                else if (valueExpression.Length == 0)
                {
                    if (!TryConvertValueConversion(
                            assignment.Value,
                            conversionTargetType,
                            compilation,
                            document,
                            currentSetterTargetType,
                            currentBindingPriorityScope,
                            out var convertedValue,
                            allowObjectStringLiteralFallback: !options.StrictMode &&
                                                              conversionTargetType.SpecialType == SpecialType.System_Object,
                            selectorNestingTypeHint: selectorNestingTypeHint))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0102",
                            $"Could not convert literal '{assignment.Value}' for '{property.Name}' on '{symbol.ToDisplayString()}'.",
                            document.FilePath,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode));
                        continue;
                    }

                    valueExpression = convertedValue.Expression;
                    valueKind = convertedValue.ValueKind;
                    requiresStaticResourceResolver = convertedValue.RequiresStaticResourceResolver;
                    valueRequirements = convertedValue.EffectiveRequirements;
                }

                assignments.Add(new ResolvedPropertyAssignment(
                    PropertyName: property.Name,
                    ValueExpression: valueExpression,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: valueKind,
                    RequiresStaticResourceResolver: requiresStaticResourceResolver,
                    ValueRequirements: valueRequirements));
                continue;
            }

            if (TryBindEventSubscription(
                    symbol,
                    assignment,
                    compilation,
                    assignmentDataType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out var eventSubscription))
            {
                if (eventSubscription is not null)
                {
                    eventSubscriptions.Add(eventSubscription);
                }

                continue;
            }

            if (TryBindAvaloniaPropertyAssignment(
                    symbol,
                    typeName,
                    normalizedPropertyName,
                    assignment,
                    compilation,
                    document,
                    options,
                    diagnostics,
                    compiledBindings,
                    unsafeAccessors,
                    compileBindingsEnabled,
                    assignmentDataType,
                    property?.Type,
                    currentBindingPriorityScope,
                    currentSetterTargetType,
                    rootTypeSymbol,
                    out var fallbackAssignment))
            {
                if (fallbackAssignment is not null)
                {
                    assignments.Add(fallbackAssignment);
                }

                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property '{assignment.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        TryAddTemplateDataTypeDirectiveAssignment(
            node,
            symbol,
            compilation,
            document,
            options,
            diagnostics,
            assignments);

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        foreach (var child in node.ChildObjects)
        {
            if (ShouldSkipConditionalBranch(
                    child.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            children.Add(BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol,
                parentScopeContext: scopeContext));
        }

        ResolvedChildAttachmentMode? explicitAttachment = null;
        string? explicitContentPropertyName = null;
        foreach (var propertyElement in node.PropertyElements)
        {
            if (ShouldSkipConditionalBranch(
                    propertyElement.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            if (IsDesignTimePropertyToken(propertyElement.PropertyName))
            {
                continue;
            }

            var propertyAlias = ResolvePropertyAlias(symbol, propertyElement.PropertyName);
            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            var propertyElementSetterTargetType = ResolvePropertyElementSetterTargetType(
                symbol,
                normalizedPropertyName,
                currentSetterTargetType);

            if (propertyElement.ObjectValues.Length == 1 &&
                TryExtractInlineCSharpObjectNodeCode(
                    propertyElement.ObjectValues[0],
                    compilation,
                    document,
                    out var inlinePropertyCode))
            {
                var inlineAssignment = new XamlPropertyAssignment(
                    PropertyName: propertyElement.PropertyName,
                    XmlNamespace: string.Empty,
                    Value: inlinePropertyCode,
                    IsAttached: false,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition);

                if (symbol is not null &&
                    TryBindInlineEventCodeSubscription(
                        symbol,
                        propertyElement.PropertyName,
                        inlinePropertyCode,
                        propertyElement.Line,
                        propertyElement.Column,
                        propertyElement.Condition,
                        compilation,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var inlineEventSubscription))
                {
                    if (inlineEventSubscription is not null)
                    {
                        eventSubscriptions.Add(inlineEventSubscription);
                    }

                    continue;
                }

                var inlineProperty = symbol is null
                    ? null
                    : FindProperty(symbol, normalizedPropertyName);
                if (inlineProperty is not null && inlineProperty.SetMethod is not null)
                {
                    if (!TryBuildInlineCodeBindingExpression(
                            compilation,
                            nodeDataType,
                            rootTypeSymbol,
                            symbol,
                            inlinePropertyCode,
                            out var inlineBindingExpression,
                            out _,
                            out _,
                            out var inlineErrorMessage))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0112",
                            $"Inline C# for '{inlineProperty.Name}' is invalid: {inlineErrorMessage}",
                            document.FilePath,
                            propertyElement.Line,
                            propertyElement.Column,
                            options.StrictMode));
                        continue;
                    }

                    if (TryFindAvaloniaPropertyField(
                            symbol!,
                            normalizedPropertyName,
                            out var inlineOwnerType,
                            out var inlinePropertyField))
                    {
                        assignments.Add(new ResolvedPropertyAssignment(
                            PropertyName: inlineProperty.Name,
                            ValueExpression: inlineBindingExpression,
                            AvaloniaPropertyOwnerTypeName: inlineOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            AvaloniaPropertyFieldName: inlinePropertyField.Name,
                            ClrPropertyOwnerTypeName: null,
                            ClrPropertyTypeName: null,
                            BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                                symbol!,
                                inlinePropertyField,
                                compilation,
                                currentBindingPriorityScope),
                            Line: propertyElement.Line,
                            Column: propertyElement.Column,
                            Condition: propertyElement.Condition,
                            ValueKind: ResolvedValueKind.Binding,
                            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                            PreserveBindingValue: HasAssignBindingAttribute(inlineProperty)));
                        continue;
                    }

                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: inlineProperty.Name,
                        ValueExpression: inlineBindingExpression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: inlineProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: inlineProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: propertyElement.Line,
                        Column: propertyElement.Column,
                        Condition: propertyElement.Condition,
                        ValueKind: ResolvedValueKind.Binding,
                        ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                        PreserveBindingValue: HasAssignBindingAttribute(inlineProperty)));
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                normalizedPropertyName.Equals(contentPropertyName, StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.Content;
                explicitContentPropertyName = contentPropertyName;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        nodeDataType,
                        propertyElementSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol,
                        parentScopeContext: scopeContext,
                        parentPropertyName: propertyElement.PropertyName));
                }

                continue;
            }

            if (normalizedPropertyName.Equals("Children", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ChildrenCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        nodeDataType,
                        propertyElementSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol,
                        parentScopeContext: scopeContext,
                        parentPropertyName: propertyElement.PropertyName));
                }

                continue;
            }

            if (normalizedPropertyName.Equals("Items", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ItemsCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    if (ShouldSkipConditionalBranch(
                            value.Condition,
                            compilation,
                            document,
                            diagnostics,
                            options))
                    {
                        continue;
                    }

                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        unsafeAccessors,
                        compileBindingsEnabled,
                        nodeDataType,
                        propertyElementSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol,
                        parentScopeContext: scopeContext,
                        parentPropertyName: propertyElement.PropertyName));
                }

                continue;
            }

            if (symbol is null)
            {
                continue;
            }

            if (propertyElement.ObjectValues.Length == 0)
            {
                continue;
            }

            var elementValues = ImmutableArray.CreateBuilder<ResolvedObjectNode>(propertyElement.ObjectValues.Length);
            foreach (var value in propertyElement.ObjectValues)
            {
                if (ShouldSkipConditionalBranch(
                        value.Condition,
                        compilation,
                        document,
                        diagnostics,
                        options))
                {
                    continue;
                }

                elementValues.Add(BindObjectNode(
                    value,
                    compilation,
                    diagnostics,
                    document,
                    options,
                    compiledBindings,
                    unsafeAccessors,
                    compileBindingsEnabled,
                    nodeDataType,
                    propertyElementSetterTargetType,
                    currentBindingPriorityScope,
                    rootTypeSymbol: rootTypeSymbol,
                    parentScopeContext: scopeContext,
                    parentPropertyName: propertyElement.PropertyName));
            }

            var elementValuesArray = elementValues.ToImmutable();

            if (propertyAlias.HasAvaloniaPropertyAlias &&
                propertyAlias.AvaloniaPropertyOwnerType is not null &&
                TryFindAvaloniaPropertyField(
                    propertyAlias.AvaloniaPropertyOwnerType,
                    normalizedPropertyName,
                    out var aliasedOwnerType,
                    out var aliasedPropertyField,
                    propertyAlias.AvaloniaPropertyFieldName))
            {
                var aliasedAssignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    TryGetAvaloniaPropertyValueType(aliasedPropertyField.Type),
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (aliasedAssignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Aliased Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: normalizedPropertyName,
                    AvaloniaPropertyOwnerTypeName: aliasedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: aliasedPropertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        symbol,
                        aliasedPropertyField,
                        compilation,
                        currentBindingPriorityScope),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: aliasedAssignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    PreserveBindingValue: HasAssignBindingAttribute(FindProperty(symbol, normalizedPropertyName))));
                continue;
            }

            if (TrySplitOwnerQualifiedPropertyToken(
                    propertyElement.PropertyName,
                    out var attachedOwnerToken,
                    out var attachedPropertyName))
            {
                var attachedOwnerType = ResolveTypeToken(
                    compilation,
                    document,
                    attachedOwnerToken,
                    document.ClassNamespace);
                if (attachedOwnerType is not null &&
                    TryFindAvaloniaPropertyField(
                        attachedOwnerType,
                        attachedPropertyName,
                        out var attachedResolvedOwnerType,
                        out var attachedPropertyField))
                {
                    var attachedAssignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                        TryGetAvaloniaPropertyValueType(attachedPropertyField.Type),
                        elementValuesArray,
                        compilation,
                        document,
                        propertyElement.Line,
                        propertyElement.Column);

                    if (attachedAssignmentValues.Length != 1)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0103",
                            $"Attached property element '{propertyElement.PropertyName}' requires exactly one object value.",
                            document.FilePath,
                            propertyElement.Line,
                            propertyElement.Column,
                            options.StrictMode));
                        continue;
                    }

                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: attachedPropertyName,
                        AvaloniaPropertyOwnerTypeName: attachedResolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        AvaloniaPropertyFieldName: attachedPropertyField.Name,
                        ClrPropertyOwnerTypeName: null,
                        ClrPropertyTypeName: null,
                        BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                            symbol,
                            attachedPropertyField,
                            compilation,
                            currentBindingPriorityScope),
                        IsCollectionAdd: false,
                        IsDictionaryMerge: false,
                        ObjectValues: attachedAssignmentValues,
                        Line: propertyElement.Line,
                        Column: propertyElement.Column,
                        Condition: propertyElement.Condition,
                        PreserveBindingValue: HasAssignBindingAttribute(FindProperty(attachedResolvedOwnerType, attachedPropertyName))));
                    continue;
                }

                if (attachedOwnerType is not null &&
                    TryBindAttachedSetterPropertyElementAssignment(
                        targetType: symbol,
                        ownerType: attachedOwnerType,
                        attachedPropertyName: attachedPropertyName,
                        propertyElement: propertyElement,
                        objectValues: elementValuesArray,
                        compilation: compilation,
                        document: document,
                        options: options,
                        diagnostics: diagnostics,
                        out var attachedSetterAssignment))
                {
                    if (attachedSetterAssignment is not null)
                    {
                        propertyElementAssignments.Add(attachedSetterAssignment);
                    }

                    continue;
                }
            }

            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Property element '{propertyElement.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    propertyElement.Line,
                    propertyElement.Column,
                    options.StrictMode));
                continue;
            }

            ValidateItemContainerInsideTemplateWarning(
                symbol,
                property,
                propertyElement,
                compilation,
                document,
                diagnostics,
                options);

            var canMergeDictionaryProperty = CanMergeDictionaryProperty(symbol, property.Name);
            if (canMergeDictionaryProperty &&
                TryBuildKeyedDictionaryMergeContainer(
                    property,
                    elementValuesArray,
                    propertyElement.Line,
                    propertyElement.Column,
                    out var dictionaryMergeContainer))
            {
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: ImmutableArray.Create(dictionaryMergeContainer),
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            if (CanAddToCollectionProperty(symbol, property.Name))
            {
                var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValues(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document);
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: true,
                    IsDictionaryMerge: false,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    CollectionAddInstructions: collectionAddInstructions));
                continue;
            }

            if (TryFindAvaloniaPropertyField(symbol, property.Name, out var ownerType, out var propertyField))
            {
                var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (assignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        symbol,
                        propertyField,
                        compilation,
                        currentBindingPriorityScope),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: assignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    PreserveBindingValue: HasAssignBindingAttribute(property)));
                continue;
            }

            if (property.SetMethod is not null)
            {
                var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
                    property.Type,
                    elementValuesArray,
                    compilation,
                    document,
                    propertyElement.Line,
                    propertyElement.Column);

                if (assignmentValues.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: assignmentValues,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition,
                    PreserveBindingValue: HasAssignBindingAttribute(property)));
                continue;
            }

            if (canMergeDictionaryProperty)
            {
                if (elementValuesArray.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Dictionary property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column,
                    Condition: propertyElement.Condition));
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property element '{propertyElement.PropertyName}' is not supported on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                propertyElement.Line,
                propertyElement.Column,
                options.StrictMode));
        }

        string? factoryExpression = null;
        var factoryValueRequirements = ResolvedValueRequirements.None;
        if (TryBuildExplicitConstructionExpression(
                node,
                symbol,
                typeName,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol,
                out var explicitConstructionExpression))
        {
            factoryExpression = explicitConstructionExpression;
            factoryValueRequirements = ResolvedValueRequirements.None;
        }

        if (symbol is not null &&
            string.IsNullOrWhiteSpace(factoryExpression) &&
            !string.IsNullOrWhiteSpace(node.TextContent) &&
            children.Count == 0)
        {
            var inlineTextContent = node.TextContent!.Trim();
            var handledAsContentProperty = false;
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                !HasResolvedPropertyAssignment(assignments, contentPropertyName!) &&
                !HasResolvedPropertyElementAssignment(propertyElementAssignments, contentPropertyName!))
            {
                var contentProperty = FindProperty(symbol, contentPropertyName!);
                if (contentProperty?.SetMethod is not null &&
                    TryConvertValueConversion(
                        inlineTextContent,
                        contentProperty.Type,
                        compilation,
                        document,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var inlineContentConversion,
                        allowObjectStringLiteralFallback: !options.StrictMode))
                {
                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: contentProperty.Name,
                        ValueExpression: inlineContentConversion.Expression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: contentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: contentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: null,
                        ValueKind: inlineContentConversion.ValueKind,
                        ValueRequirements: inlineContentConversion.EffectiveRequirements));
                    handledAsContentProperty = true;
                }
                else if (contentProperty is not null &&
                         CanAddToCollectionProperty(symbol, contentProperty.Name) &&
                         CollectionAddService.TryCreateCollectionContentValue(
                             inlineTextContent,
                             contentProperty.Type,
                             compilation,
                             document,
                             currentSetterTargetType,
                             (int)currentBindingPriorityScope,
                             !options.StrictMode,
                             node.Line,
                             node.Column,
                             out var inlineContentValue,
                             out var inlineContentAddInstruction))
                {
                    var inlineContentValues = ImmutableArray.Create(inlineContentValue);
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: contentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: contentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: contentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: inlineContentValues,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: null,
                        CollectionAddInstructions: ImmutableArray.Create(inlineContentAddInstruction)));
                    handledAsContentProperty = true;
                }
            }

            if (!handledAsContentProperty &&
                assignments.Count == 0 &&
                propertyElementAssignments.Count == 0 &&
                TryConvertValueConversion(
                    inlineTextContent,
                    symbol,
                    compilation,
                    document,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    out var inlineFactoryConversion))
            {
                factoryExpression = inlineFactoryConversion.Expression;
                factoryValueRequirements = inlineFactoryConversion.EffectiveRequirements;
            }
        }

        var (defaultAttachmentMode, defaultContentPropertyName) = DetermineChildAttachment(symbol);
        var attachmentMode = explicitAttachment ?? defaultAttachmentMode;
        var resolvedContentPropertyName = attachmentMode == ResolvedChildAttachmentMode.Content
            ? explicitContentPropertyName ?? defaultContentPropertyName
            : null;

        if (attachmentMode == ResolvedChildAttachmentMode.Content &&
            children.Count > 0 &&
            symbol is not null &&
            !string.IsNullOrWhiteSpace(resolvedContentPropertyName))
        {
            var resolvedContentProperty = FindProperty(symbol, resolvedContentPropertyName!);
            if (resolvedContentProperty is not null)
            {
                var contentChildrenValues = children.ToImmutableArray();
                var useCollectionAddForContent =
                    CanAddToCollectionProperty(symbol, resolvedContentProperty.Name) &&
                    ShouldUseCollectionAddForContentProperty(
                        resolvedContentProperty,
                        contentChildrenValues,
                        compilation,
                        document);
                if (useCollectionAddForContent)
                {
                    var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValues(
                        resolvedContentProperty.Type,
                        contentChildrenValues,
                        compilation,
                        document);
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: resolvedContentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: resolvedContentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: resolvedContentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: true,
                        IsDictionaryMerge: false,
                        ObjectValues: contentChildrenValues,
                        Line: node.Line,
                        Column: node.Column,
                        Condition: node.Condition,
                        CollectionAddInstructions: collectionAddInstructions));

                    children.Clear();
                    attachmentMode = ResolvedChildAttachmentMode.None;
                    resolvedContentPropertyName = null;
                }
                else if (CanMergeDictionaryProperty(symbol, resolvedContentProperty.Name) &&
                         ShouldUseDictionaryMergeForContentProperty(
                             resolvedContentProperty,
                             contentChildrenValues) &&
                         TryBuildKeyedDictionaryMergeContainer(
                             resolvedContentProperty,
                             contentChildrenValues,
                             node.Line,
                             node.Column,
                             out var contentDictionaryMergeContainer))
                {
                    propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                        PropertyName: resolvedContentProperty.Name,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: resolvedContentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: resolvedContentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        IsCollectionAdd: false,
                        IsDictionaryMerge: true,
                        ObjectValues: ImmutableArray.Create(contentDictionaryMergeContainer),
                        Line: node.Line,
                        Column: node.Column,
                        Condition: node.Condition));

                    children.Clear();
                    attachmentMode = ResolvedChildAttachmentMode.None;
                    resolvedContentPropertyName = null;
                }
            }
        }

        if (attachmentMode == ResolvedChildAttachmentMode.Content && children.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0103",
                "More than one child object was found for a Content attachment target. Only the first child will be attached.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
        }

        if (attachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            foreach (var child in children)
            {
                if (!string.IsNullOrWhiteSpace(child.KeyExpression))
                {
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Type '{typeName}' requires x:Key for child objects added via dictionary Add(key, value).",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                break;
            }
        }

        var useServiceProviderConstructor = ShouldUseServiceProviderConstructor(symbol);
        var useTopDownInitialization = IsUsableDuringInitialization(symbol);
        var normalizedNodeName = ResolveObjectNodeNameScopeRegistration(node, symbol, compilation);
        var resolvedAssignments = assignments.ToImmutable();
        var resolvedPropertyElementAssignments = propertyElementAssignments.ToImmutable();
        var resolvedEventSubscriptions = eventSubscriptions.ToImmutable();
        var resolvedChildren = children.ToImmutable();
        var childAddInstructions = ResolveChildAddInstructions(
            symbol,
            attachmentMode,
            resolvedChildren,
            compilation,
            document);
        var semanticFlags = ResolveObjectNodeSemanticFlags(
            symbol,
            compilation,
            node,
            normalizedNodeName,
            resolvedChildren,
            resolvedPropertyElementAssignments);

        return new ResolvedObjectNode(
            KeyExpression: BuildObjectNodeKeyExpression(node.Key, compilation, document),
            Name: normalizedNodeName,
            TypeName: typeName,
            IsBindingObjectNode: IsBindingObjectType(symbol, compilation),
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: factoryValueRequirements,
            UseServiceProviderConstructor: useServiceProviderConstructor,
            UseTopDownInitialization: useTopDownInitialization,
            PropertyAssignments: resolvedAssignments,
            PropertyElementAssignments: resolvedPropertyElementAssignments,
            EventSubscriptions: resolvedEventSubscriptions,
            Children: resolvedChildren,
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: resolvedContentPropertyName,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            ChildAddInstructions: childAddInstructions,
            SemanticFlags: semanticFlags);
    }

    private static void TryAddTemplateDataTypeDirectiveAssignment(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments)
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

        var dataTypeProperty = FindProperty(symbol, "DataType");
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

        assignments.Add(new ResolvedPropertyAssignment(
            PropertyName: dataTypeProperty.Name,
            ValueExpression: "typeof(" + resolvedDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: dataTypeProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: dataTypeProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BindingPriorityExpression: null,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition));
    }

    private static INamedTypeSymbol? ResolveNodeDataType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        INamedTypeSymbol? nodeType,
        INamedTypeSymbol? inheritedDataType,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var resolvedNodeDataType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.DataType,
            document.ClassNamespace);
        if (resolvedNodeDataType is not null)
        {
            return resolvedNodeDataType;
        }

        if (IsDataTemplateNode(node))
        {
            foreach (var assignment in node.PropertyAssignments)
            {
                if (assignment.IsAttached ||
                    !NormalizePropertyName(assignment.PropertyName).Equals("DataType", StringComparison.Ordinal))
                {
                    continue;
                }

                var resolvedTemplateDataType = ResolveTypeFromTypeExpression(
                    compilation,
                    document,
                    assignment.Value,
                    document.ClassNamespace);
                if (resolvedTemplateDataType is not null)
                {
                    return resolvedTemplateDataType;
                }
            }
        }

        var explicitDataContextType = ResolveExplicitDataContextType(
            compilation,
            document,
            node,
            nodeType,
            inheritedDataType,
            unsafeAccessors);
        if (explicitDataContextType is not null)
        {
            return explicitDataContextType;
        }

        if (scopeContext.ParentPropertyName is not null &&
            scopeContext.Parent is not null &&
            scopeContext.Parent.NodeType is not null)
        {
            var parentPropertyAlias = ResolvePropertyAlias(scopeContext.Parent.NodeType, scopeContext.ParentPropertyName);
            var inferredFromParentProperty = ResolvePresentedItemDataTypeFromItemsAttribute(
                compilation,
                document,
                scopeContext.Parent,
                scopeContext.Parent.NodeType,
                parentPropertyAlias.ResolvedPropertyName,
                unsafeAccessors);
            if (inferredFromParentProperty is not null)
            {
                return inferredFromParentProperty;
            }
        }

        return inheritedDataType;
    }

    private static INamedTypeSymbol? ResolveAssignmentBindingDataType(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol? ownerType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeDataType,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        if (IsNonAttachedDataContextProperty(assignment.PropertyName, assignment.IsAttached))
        {
            return inheritedDataType;
        }

        if (ownerType is not null)
        {
            var propertyAlias = ResolvePropertyAlias(ownerType, assignment.PropertyName);
            var inferredFromItemsProperty = ResolvePresentedItemDataTypeFromItemsAttribute(
                compilation,
                document,
                scopeContext,
                ownerType,
                propertyAlias.ResolvedPropertyName,
                unsafeAccessors);
            if (inferredFromItemsProperty is not null)
            {
                return inferredFromItemsProperty;
            }
        }

        return nodeDataType;
    }

    private static bool IsNonAttachedDataContextProperty(string propertyName, bool isAttached)
    {
        return !isAttached &&
               NormalizePropertyName(propertyName).Equals("DataContext", StringComparison.Ordinal);
    }

    private static INamedTypeSymbol? ResolveExplicitDataContextType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        INamedTypeSymbol? nodeType,
        INamedTypeSymbol? inheritedDataType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.IsAttached ||
                !NormalizePropertyName(assignment.PropertyName).Equals("DataContext", StringComparison.Ordinal) ||
                !TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
            {
                continue;
            }

            if (!TryResolveBindingSourceTypeForScopeInference(
                    compilation,
                    document,
                    bindingMarkup,
                    inheritedDataType,
                    nodeType,
                    out var sourceType,
                    out _)
                || sourceType is null)
            {
                continue;
            }

            if (!TryBuildCompiledBindingAccessorExpression(
                    compilation,
                    document,
                    sourceType,
                    bindingMarkup.Path,
                    targetPropertyType: null,
                    unsafeAccessors,
                    out var resolution,
                    out _))
            {
                continue;
            }

            if (resolution.ResultTypeSymbol is INamedTypeSymbol resolvedDataContextType)
            {
                return resolvedDataContextType;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolvePresentedItemDataTypeFromItemsAttribute(
        Compilation compilation,
        XamlDocumentModel document,
        BindingScopeContext propertyOwnerScopeContext,
        INamedTypeSymbol propertyOwnerType,
        string propertyName,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var property = FindProperty(propertyOwnerType, propertyName);
        if (property is null ||
            !TryGetInheritDataTypeFromItemsAttribute(property, out var ancestorItemsPropertyName, out var ancestorType))
        {
            return null;
        }

        var ancestorScopeContext = FindPresentedItemAncestorScope(propertyOwnerScopeContext, ancestorType);
        if (ancestorScopeContext is null)
        {
            return null;
        }

        var itemsCollectionType = ResolvePresentedItemsCollectionType(
            compilation,
            document,
            ancestorScopeContext,
            ancestorItemsPropertyName,
            unsafeAccessors);
        if (itemsCollectionType is null)
        {
            return null;
        }

        if (!TryGetCollectionElementType(
                itemsCollectionType,
                out var presentedItemType,
                out _,
                out _))
        {
            return null;
        }

        return presentedItemType as INamedTypeSymbol;
    }

    private static BindingScopeContext? FindPresentedItemAncestorScope(
        BindingScopeContext startingScopeContext,
        INamedTypeSymbol? ancestorType)
    {
        if (ancestorType is null)
        {
            return startingScopeContext;
        }

        for (var current = startingScopeContext; current is not null; current = current.Parent)
        {
            if (current.NodeType is not null &&
                IsTypeAssignableTo(current.NodeType, ancestorType))
            {
                return current;
            }
        }

        return null;
    }

    private static ITypeSymbol? ResolvePresentedItemsCollectionType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingScopeContext ancestorScopeContext,
        string ancestorItemsPropertyName,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        foreach (var assignment in ancestorScopeContext.Node.PropertyAssignments)
        {
            if (assignment.IsAttached ||
                !NormalizePropertyName(assignment.PropertyName).Equals(ancestorItemsPropertyName, StringComparison.Ordinal))
            {
                continue;
            }

            var bindingMarkupParsed = TryParseBindingMarkup(assignment.Value, out var bindingMarkup);
            if (!bindingMarkupParsed)
            {
                continue;
            }

            var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || ancestorScopeContext.CompileBindingsEnabled;
            if (!wantsCompiledBinding ||
                !TryResolveBindingSourceTypeForScopeInference(
                    compilation,
                    document,
                    bindingMarkup,
                    ancestorScopeContext.NodeDataType,
                    ancestorScopeContext.NodeType,
                    out var sourceType,
                    out _)
                || sourceType is null)
            {
                continue;
            }

            if (!TryBuildCompiledBindingAccessorExpression(
                    compilation,
                    document,
                    sourceType,
                    bindingMarkup.Path,
                    targetPropertyType: null,
                    unsafeAccessors,
                    out var resolution,
                    out _))
            {
                continue;
            }

            if (resolution.ResultTypeSymbol is not null)
            {
                return resolution.ResultTypeSymbol;
            }
        }

        foreach (var propertyElement in ancestorScopeContext.Node.PropertyElements)
        {
            if (!NormalizePropertyName(propertyElement.PropertyName).Equals(ancestorItemsPropertyName, StringComparison.Ordinal) ||
                propertyElement.ObjectValues.Length != 1)
            {
                continue;
            }

            var directCollectionType = ResolveObjectTypeSymbol(compilation, document, propertyElement.ObjectValues[0]);
            if (directCollectionType is not null)
            {
                return directCollectionType;
            }
        }

        return null;
    }

    private static bool TryGetInheritDataTypeFromItemsAttribute(
        IPropertySymbol property,
        out string ancestorItemsPropertyName,
        out INamedTypeSymbol? ancestorType)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            var isInheritDataTypeFromItemsAttribute =
                attributeType.Name.Equals("InheritDataTypeFromItemsAttribute", StringComparison.Ordinal) ||
                attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Equals("global::Avalonia.Metadata.InheritDataTypeFromItemsAttribute", StringComparison.Ordinal);
            if (!isInheritDataTypeFromItemsAttribute ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string propertyNameValue ||
                string.IsNullOrWhiteSpace(propertyNameValue))
            {
                continue;
            }

            ancestorItemsPropertyName = propertyNameValue.Trim();
            ancestorType = null;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (!namedArgument.Key.Equals("AncestorType", StringComparison.Ordinal) ||
                    namedArgument.Value.Value is not INamedTypeSymbol namedType)
                {
                    continue;
                }

                ancestorType = namedType;
                break;
            }

            return true;
        }

        ancestorItemsPropertyName = string.Empty;
        ancestorType = null;
        return false;
    }

    private static bool IsXamlArrayNode(XamlObjectNode node)
    {
        return node.XmlNamespace == Xaml2006.NamespaceName &&
               node.XmlTypeName.Equals("Array", StringComparison.Ordinal);
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
            if (ShouldSkipConditionalBranch(
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

            if (!TryBuildInlineResolvedObjectExpression(boundChild, out var inlineChildExpression))
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
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            return null;
        }

        if (TryBuildResourceKeyExpression(rawKey!, compilation, document, out var keyExpression))
        {
            return keyExpression.Expression;
        }

        return "\"" + Escape(rawKey!.Trim()) + "\"";
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

        var property = FindProperty(objectType, propertyName);
        if (property is not null && IsTransitionsCollectionType(property.Type))
        {
            return objectType;
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

    private static ResolvedObjectNodeSemanticFlags ResolveObjectNodeSemanticFlags(
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlObjectNode node,
        string? normalizedNodeName,
        ImmutableArray<ResolvedObjectNode> resolvedChildren,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments)
    {
        var flags = ObjectNodeSemanticContractService.Classify(
            symbol,
            GetActiveTypeSymbolCatalog(compilation),
            IsTypeAssignableTo);

        if (CanBeDeferredResourceNode(
                symbol,
                normalizedNodeName,
                resolvedChildren,
                propertyElementAssignments,
                flags,
                allowNameScopeRegistrations: node.IsShared == false))
        {
            flags |= ResolvedObjectNodeSemanticFlags.CanBeDeferredResource;
        }

        if (node.IsShared == false &&
            flags.HasFlag(ResolvedObjectNodeSemanticFlags.CanBeDeferredResource))
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource;
        }

        return flags;
    }

    private static bool CanBeDeferredResourceNode(
        INamedTypeSymbol? symbol,
        string? normalizedNodeName,
        ImmutableArray<ResolvedObjectNode> resolvedChildren,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ResolvedObjectNodeSemanticFlags semanticFlags,
        bool allowNameScopeRegistrations)
    {
        if (symbol is null ||
            symbol.IsValueType ||
            symbol.SpecialType == SpecialType.System_String ||
            semanticFlags.HasFlag(ResolvedObjectNodeSemanticFlags.IsResourceInclude) ||
            semanticFlags.HasFlag(ResolvedObjectNodeSemanticFlags.IsStyleInclude))
        {
            return false;
        }

        if (!allowNameScopeRegistrations &&
            !string.IsNullOrWhiteSpace(normalizedNodeName))
        {
            return false;
        }

        if (!allowNameScopeRegistrations)
        {
            foreach (var child in resolvedChildren)
            {
                if (ContainsNameScopeRegistration(child))
                {
                    return false;
                }
            }

            foreach (var propertyElementAssignment in propertyElementAssignments)
            {
                foreach (var objectValue in propertyElementAssignment.ObjectValues)
                {
                    if (ContainsNameScopeRegistration(objectValue))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool ContainsNameScopeRegistration(ResolvedObjectNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (ContainsNameScopeRegistration(child))
            {
                return true;
            }
        }

        foreach (var propertyElementAssignment in node.PropertyElementAssignments)
        {
            foreach (var objectValue in propertyElementAssignment.ObjectValues)
            {
                if (ContainsNameScopeRegistration(objectValue))
                {
                    return true;
                }
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
}
