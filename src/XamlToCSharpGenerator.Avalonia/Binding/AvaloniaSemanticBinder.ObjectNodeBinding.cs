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
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{
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

        if (IsXamlTypeNode(node))
        {
            return BindXamlTypeNode(
                node,
                compilation,
                diagnostics,
                document,
                options);
        }

        var compileBindingsEnabled = node.CompileBindings ?? inheritedCompileBindingsEnabled;
        var inheritedXBindDefaultMode = parentScopeContext?.XBindDefaultMode ?? "OneTime";
        var xBindDefaultMode = ResolveXBindDefaultMode(node, inheritedXBindDefaultMode);
        var scopeContext = new BindingScopeContext(
            node,
            symbol,
            inheritedDataType,
            inheritedSetterTargetType ?? parentScopeContext?.SetterTargetType,
            compileBindingsEnabled,
            rootTypeSymbol,
            xBindDefaultMode,
            (parentScopeContext?.IsInsideDataTemplate ?? false) || IsDataTemplateNode(node),
            parentScopeContext,
            parentPropertyName);
        var nodeDataType = ResolveNodeDataType(
            compilation,
            document,
            node,
            symbol,
            inheritedDataType,
            options,
            scopeContext,
            unsafeAccessors);
        var currentSetterTargetType = ResolveCurrentSetterTargetType(
            symbol,
            node,
            compilation,
            document,
            inheritedSetterTargetType);
        currentSetterTargetType ??= ResolveAmbientSetterTargetType(
            parentScopeContext,
            compilation,
            document);
        scopeContext = scopeContext with
        {
            NodeDataType = nodeDataType,
            SetterTargetType = currentSetterTargetType
        };
        var currentBindingPriorityScope = ResolveCurrentBindingPriorityScope(
            symbol,
            compilation,
            inheritedBindingPriorityScope);

        if (TryBindInlineCSharpObjectNode(
                node,
                symbol,
                compilation,
                diagnostics,
                document,
                options,
                nodeDataType,
                currentSetterTargetType ?? parentScopeContext?.NodeType ?? symbol,
                rootTypeSymbol,
                out var inlineCSharpBindingNode))
        {
            return inlineCSharpBindingNode;
        }

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
            if (ConditionalXamlEvaluationService.ShouldSkipBranch(
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

            if (IsXBindDefaultModeDirective(assignment))
            {
                if (!IsSupportedXBindMode(assignment.Value))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0115",
                        $"x:DefaultBindMode '{assignment.Value}' is not supported. Use OneTime, OneWay, or TwoWay.",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                }

                continue;
            }

            var propertyAlias = ResolvePropertyAlias(symbol, assignment.PropertyName);
            var treatAsAttachedAssignment = propertyAlias.HasFrameworkPropertyAlias(FrameworkProfileIds.Avalonia) ||
                                            (assignment.IsAttached &&
                                             ShouldTreatOwnerQualifiedAssignmentAsAttached(
                                                 symbol,
                                                 assignment,
                                                 compilation,
                                                 document));
            var assignmentDataType = ResolveAssignmentBindingDataType(
                assignment,
                symbol,
                compilation,
                document,
                inheritedDataType,
                nodeDataType,
                options,
                scopeContext,
                unsafeAccessors);

            if (treatAsAttachedAssignment)
            {
                var attachedAssignmentResult = BindAttachedObjectNodePropertyAssignment(
                    assignment,
                    symbol,
                    typeName,
                    compilation,
                    diagnostics,
                    document,
                    options,
                    compiledBindings,
                    unsafeAccessors,
                    compileBindingsEnabled,
                    assignmentDataType,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    rootTypeSymbol,
                    scopeContext.IsInsideDataTemplate,
                    scopeContext.XBindDefaultMode,
                    node,
                    propertyAlias.GetFrameworkPropertyOwnerTypeSymbol(FrameworkProfileIds.Avalonia),
                    propertyAlias.ResolvedPropertyName,
                    propertyAlias.GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia));

                if (attachedAssignmentResult.PropertyAssignment is not null)
                {
                    assignments.Add(attachedAssignmentResult.PropertyAssignment);
                    continue;
                }

                if (attachedAssignmentResult.EventSubscription is not null)
                {
                    eventSubscriptions.Add(attachedAssignmentResult.EventSubscription);
                    continue;
                }

                if (attachedAssignmentResult.Diagnostic is not null)
                {
                    diagnostics.Add(attachedAssignmentResult.Diagnostic);
                }
                continue;
            }

            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            var property = TypeSymbolLookupSemanticsService.FindProperty(symbol, normalizedPropertyName);
            var selectorNestingTypeHint =
                property is not null &&
                IsStyleType(symbol, compilation) &&
                property.Name.Equals("Selector", StringComparison.Ordinal)
                    ? inheritedSetterTargetType
                    : null;
            var standardAssignmentResult = BindStandardObjectNodePropertyAssignment(
                symbol,
                typeName,
                assignment,
                normalizedPropertyName,
                property,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                assignmentDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol,
                scopeContext.IsInsideDataTemplate,
                scopeContext.XBindDefaultMode,
                node,
                inferredSetterValueType,
                selectorNestingTypeHint);
            if (standardAssignmentResult.PropertyElementAssignment is not null)
            {
                propertyElementAssignments.Add(standardAssignmentResult.PropertyElementAssignment);
            }

            if (standardAssignmentResult.PropertyAssignment is not null)
            {
                assignments.Add(standardAssignmentResult.PropertyAssignment);
            }

            if (standardAssignmentResult.EventSubscription is not null)
            {
                eventSubscriptions.Add(standardAssignmentResult.EventSubscription);
            }

            if (standardAssignmentResult.Diagnostic is not null)
            {
                diagnostics.Add(standardAssignmentResult.Diagnostic);
            }

            continue;
        }

        TryAddTemplateDataTypeDirectiveAssignment(
            node,
            symbol,
            compilation,
            document,
            options,
            diagnostics,
            assignments,
            unsafeAccessors);
        TryAddControlThemeTargetTypeDirectiveAssignment(
            node,
            symbol,
            compilation,
            document,
            options,
            diagnostics,
            assignments,
            unsafeAccessors);

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
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

        var propertyElementBindingPlan = BindObjectNodePropertyElements(
            node,
            symbol,
            contentPropertyName,
            compilation,
            diagnostics,
            document,
            options,
            assignments,
            eventSubscriptions,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            currentSetterTargetType,
            currentBindingPriorityScope,
            rootTypeSymbol,
            scopeContext);
        return AssembleBoundObjectNode(
            node,
            symbol,
            typeName,
            contentPropertyName,
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
            assignments.ToImmutable(),
            propertyElementAssignments.ToImmutable(),
            eventSubscriptions.ToImmutable(),
            children.ToImmutable(),
            propertyElementBindingPlan);
    }
}
