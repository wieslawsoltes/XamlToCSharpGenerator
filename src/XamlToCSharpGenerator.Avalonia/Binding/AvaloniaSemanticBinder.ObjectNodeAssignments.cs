using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static ObjectNodeStandardPropertyAssignmentBindingResult BindStandardObjectNodePropertyAssignment(
        INamedTypeSymbol symbol,
        string typeName,
        XamlPropertyAssignment assignment,
        string normalizedPropertyName,
        IPropertySymbol? property,
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
        ITypeSymbol? inferredSetterValueType,
        INamedTypeSymbol? selectorNestingTypeHint)
    {
        var request = new ObjectNodeStandardPropertyAssignmentBindingRequest(
            symbol,
            typeName,
            assignment,
            normalizedPropertyName,
            property,
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
            inferredSetterValueType,
            selectorNestingTypeHint);
        return ObjectNodeStandardPropertyAssignmentBindingService.Bind(request, diagnostics);
    }

    private static bool TryBindClrPropertyAssignment(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        var clrRequest = new ClrPropertyAssignmentBindingRequest(
            request.ObjectType,
            request.ObjectTypeName,
            request.Property!,
            request.Assignment,
            request.Compilation,
            request.Document,
            request.Options,
            request.CompiledBindings,
            request.UnsafeAccessors,
            request.CompileBindingsEnabled,
            request.AssignmentDataType,
            request.CurrentSetterTargetType,
            request.CurrentBindingPriorityScope,
            request.CurrentBindingPriorityScope == (int)BindingPriorityScope.Template,
            request.RootTypeSymbol,
            request.IsInsideDataTemplate,
            request.XBindDefaultMode,
            request.CurrentNode,
            request.InferredSetterValueType,
            request.SelectorNestingTypeHint,
            AvaloniaPropertyMetadataName);
        return TryBindClrPropertyAssignment(clrRequest, diagnostics, out resolvedAssignment);
    }

    private static bool TryBindEventSubscription(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedEventSubscription? subscription)
    {
        return TryBindEventSubscription(
            request.ObjectType,
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

    private static bool TryBindFrameworkPropertyAssignment(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        return TryBindAvaloniaPropertyAssignment(
            request.ObjectType,
            request.ObjectTypeName,
            request.NormalizedPropertyName,
            request.Assignment,
            request.Compilation,
            request.Document,
            request.Options,
            diagnostics,
            request.CompiledBindings,
            request.UnsafeAccessors,
            request.CompileBindingsEnabled,
            request.AssignmentDataType,
            request.Property?.Type,
            (BindingPriorityScope)request.CurrentBindingPriorityScope,
            request.CurrentSetterTargetType,
            request.RootTypeSymbol,
            out resolvedAssignment,
            allowCompiledBindingRegistration: true,
            compiledBindingAccessorPlaceholderToken: null,
            explicitOwnerType: null,
            explicitFrameworkPropertyFieldName: null,
            isInsideDataTemplate: request.IsInsideDataTemplate,
            xBindDefaultMode: request.XBindDefaultMode,
            currentNode: request.CurrentNode);
    }
}
