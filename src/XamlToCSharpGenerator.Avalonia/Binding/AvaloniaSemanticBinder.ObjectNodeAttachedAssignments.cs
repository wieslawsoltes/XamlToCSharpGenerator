using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static AttachedObjectNodePropertyAssignmentBindingResult BindAttachedObjectNodePropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        string targetTypeName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? assignmentDataType,
        INamedTypeSymbol? currentSetterTargetType,
        BindingPriorityScope currentBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        string xBindDefaultMode,
        XamlObjectNode currentNode,
        INamedTypeSymbol? explicitOwnerType,
        string? explicitPropertyName,
        string? explicitPropertyFieldName)
    {
        var request = new AttachedObjectNodePropertyAssignmentBindingRequest(
            targetType,
            targetTypeName,
            assignment,
            compilation,
            document,
            options,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            assignmentDataType,
            currentSetterTargetType,
            (int)currentBindingPriorityScope,
            rootTypeSymbol,
            isInsideDataTemplate,
            xBindDefaultMode,
            currentNode,
            explicitOwnerType,
            explicitPropertyName,
            explicitPropertyFieldName);
        return ObjectNodeAttachedPropertyAssignmentBindingService.Bind(request, diagnostics);
    }

    private static bool TryBindAttachedPropertyAssignment(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        return TryBindAttachedPropertyAssignment(
            request.Assignment,
            request.TargetType,
            request.TargetTypeName,
            request.Compilation,
            request.Document,
            request.Options,
            diagnostics,
            request.CompiledBindings,
            request.UnsafeAccessors,
            request.CompileBindingsEnabled,
            request.AssignmentDataType,
            request.CurrentSetterTargetType,
            (BindingPriorityScope)request.CurrentBindingPriorityScope,
            request.RootTypeSymbol,
            request.ExplicitOwnerType,
            request.ExplicitPropertyName,
            request.ExplicitPropertyFieldName,
            out resolvedAssignment,
            request.IsInsideDataTemplate,
            request.XBindDefaultMode,
            request.CurrentNode);
    }

    private static bool TryBindAttachedStaticSetterAssignment(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        return TryBindAttachedStaticSetterAssignment(
            request.Assignment,
            request.TargetType,
            request.Compilation,
            request.Document,
            request.Options,
            diagnostics,
            request.CurrentSetterTargetType,
            (BindingPriorityScope)request.CurrentBindingPriorityScope,
            out resolvedAssignment);
    }

    private static bool TryBindAttachedClassPropertyAssignment(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        return TryBindAttachedClassPropertyAssignment(
            request.Assignment,
            request.TargetType,
            request.Compilation,
            request.Document,
            request.Options,
            diagnostics,
            request.CurrentSetterTargetType,
            (BindingPriorityScope)request.CurrentBindingPriorityScope,
            out resolvedAssignment);
    }

    private static bool TryBindAttachedEventSubscription(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedEventSubscription? subscription)
    {
        return TryBindAttachedEventSubscription(
            request.Assignment,
            request.Compilation,
            request.AssignmentDataType,
            request.RootTypeSymbol,
            request.IsInsideDataTemplate,
            diagnostics,
            request.Document,
            request.Options,
            request.CurrentNode,
            out subscription);
    }
}
