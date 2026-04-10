using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record AttachedObjectNodePropertyAssignmentBindingRequest(
    INamedTypeSymbol TargetType,
    string TargetTypeName,
    XamlPropertyAssignment Assignment,
    Compilation Compilation,
    XamlDocumentModel Document,
    GeneratorOptions Options,
    ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings,
    ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder UnsafeAccessors,
    bool CompileBindingsEnabled,
    INamedTypeSymbol? AssignmentDataType,
    INamedTypeSymbol? CurrentSetterTargetType,
    int CurrentBindingPriorityScope,
    INamedTypeSymbol? RootTypeSymbol,
    bool IsInsideDataTemplate,
    string XBindDefaultMode,
    XamlObjectNode CurrentNode,
    INamedTypeSymbol? ExplicitOwnerType,
    string? ExplicitPropertyName,
    string? ExplicitPropertyFieldName);

public sealed record AttachedObjectNodePropertyAssignmentBindingResult(
    ResolvedPropertyAssignment? PropertyAssignment,
    ResolvedEventSubscription? EventSubscription,
    DiagnosticInfo? Diagnostic);

public sealed class ObjectNodeAttachedPropertyAssignmentBindingService
{
    public delegate bool TryBindAttachedPropertyAssignmentDelegate(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment);

    public delegate bool TryBindAttachedStaticSetterAssignmentDelegate(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment);

    public delegate bool TryBindAttachedClassPropertyAssignmentDelegate(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment);

    public delegate bool TryBindAttachedEventSubscriptionDelegate(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedEventSubscription? subscription);

    private readonly TryBindAttachedPropertyAssignmentDelegate _tryBindAttachedPropertyAssignment;
    private readonly TryBindAttachedStaticSetterAssignmentDelegate _tryBindAttachedStaticSetterAssignment;
    private readonly TryBindAttachedClassPropertyAssignmentDelegate _tryBindAttachedClassPropertyAssignment;
    private readonly TryBindAttachedEventSubscriptionDelegate _tryBindAttachedEventSubscription;

    public ObjectNodeAttachedPropertyAssignmentBindingService(
        TryBindAttachedPropertyAssignmentDelegate tryBindAttachedPropertyAssignment,
        TryBindAttachedStaticSetterAssignmentDelegate tryBindAttachedStaticSetterAssignment,
        TryBindAttachedClassPropertyAssignmentDelegate tryBindAttachedClassPropertyAssignment,
        TryBindAttachedEventSubscriptionDelegate tryBindAttachedEventSubscription)
    {
        _tryBindAttachedPropertyAssignment = tryBindAttachedPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindAttachedPropertyAssignment));
        _tryBindAttachedStaticSetterAssignment = tryBindAttachedStaticSetterAssignment ?? throw new ArgumentNullException(nameof(tryBindAttachedStaticSetterAssignment));
        _tryBindAttachedClassPropertyAssignment = tryBindAttachedClassPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindAttachedClassPropertyAssignment));
        _tryBindAttachedEventSubscription = tryBindAttachedEventSubscription ?? throw new ArgumentNullException(nameof(tryBindAttachedEventSubscription));
    }

    public AttachedObjectNodePropertyAssignmentBindingResult Bind(
        AttachedObjectNodePropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (_tryBindAttachedPropertyAssignment(request, diagnostics, out var attachedAssignment))
        {
            return new AttachedObjectNodePropertyAssignmentBindingResult(
                PropertyAssignment: attachedAssignment,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (_tryBindAttachedStaticSetterAssignment(request, diagnostics, out var staticSetterAssignment))
        {
            return new AttachedObjectNodePropertyAssignmentBindingResult(
                PropertyAssignment: staticSetterAssignment,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (_tryBindAttachedClassPropertyAssignment(request, diagnostics, out var classPropertyAssignment))
        {
            return new AttachedObjectNodePropertyAssignmentBindingResult(
                PropertyAssignment: classPropertyAssignment,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (_tryBindAttachedEventSubscription(request, diagnostics, out var attachedEventSubscription))
        {
            return new AttachedObjectNodePropertyAssignmentBindingResult(
                PropertyAssignment: null,
                EventSubscription: attachedEventSubscription,
                Diagnostic: null);
        }

        return new AttachedObjectNodePropertyAssignmentBindingResult(
            PropertyAssignment: null,
            EventSubscription: null,
            Diagnostic: new DiagnosticInfo(
                "AXSG0101",
                $"Attached property '{request.Assignment.PropertyName}' could not be resolved on this scope.",
                request.Document.FilePath,
                request.Assignment.Line,
                request.Assignment.Column,
                request.Options.StrictMode));
    }
}
