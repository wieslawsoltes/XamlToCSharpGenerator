using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static ResolvedObjectNode AssembleBoundObjectNode(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
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
        ImmutableArray<ResolvedPropertyAssignment> assignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        ImmutableArray<ResolvedObjectNode> children,
        BoundObjectNodePropertyElementSet propertyElementBindingPlan)
    {
        var request = new ObjectNodeAssemblyRequest(
            node,
            symbol,
            typeName,
            contentPropertyName,
            compilation,
            document,
            options,
            diagnostics,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            currentSetterTargetType,
            (int)currentBindingPriorityScope,
            rootTypeSymbol,
            assignments,
            propertyElementAssignments,
            eventSubscriptions,
            children,
            propertyElementBindingPlan);
        return ObjectNodeAssemblyService.Assemble(request);
    }

    private static ImmutableArray<ResolvedPropertyElementAssignment> ProjectObjectNodePropertyElementAssignmentsToImmutable(
        INamedTypeSymbol objectType,
        ImmutableArray<BoundObjectNodePropertyElementPlan> propertyElementPlans,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        int bindingPriorityScope)
    {
        var builder = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>(propertyElementPlans.Length);
        ProjectObjectNodePropertyElementAssignments(
            objectType,
            propertyElementPlans,
            compilation,
            diagnostics,
            document,
            options,
            (BindingPriorityScope)bindingPriorityScope,
            builder);
        return builder.ToImmutable();
    }
}
