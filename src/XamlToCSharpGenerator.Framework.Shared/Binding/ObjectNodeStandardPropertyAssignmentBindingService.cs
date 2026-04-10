using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record ObjectNodeStandardPropertyAssignmentBindingRequest(
    INamedTypeSymbol ObjectType,
    string ObjectTypeName,
    XamlPropertyAssignment Assignment,
    string NormalizedPropertyName,
    IPropertySymbol? Property,
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
    ITypeSymbol? InferredSetterValueType,
    INamedTypeSymbol? SelectorNestingTypeHint);

public sealed record ObjectNodeStandardPropertyAssignmentBindingResult(
    ResolvedPropertyAssignment? PropertyAssignment,
    ResolvedPropertyElementAssignment? PropertyElementAssignment,
    ResolvedEventSubscription? EventSubscription,
    DiagnosticInfo? Diagnostic);

public sealed class ObjectNodeStandardPropertyAssignmentBindingService
{
    public delegate bool TryBindCollectionLiteralPropertyAssignmentDelegate(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        out ResolvedPropertyElementAssignment? resolvedAssignment);

    public delegate bool TryBindClrPropertyAssignmentDelegate(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment);

    public delegate bool TryBindEventSubscriptionDelegate(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedEventSubscription? subscription);

    public delegate bool TryBindFrameworkPropertyAssignmentDelegate(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyAssignment? resolvedAssignment);

    private readonly TryBindCollectionLiteralPropertyAssignmentDelegate _tryBindCollectionLiteralPropertyAssignment;
    private readonly TryBindClrPropertyAssignmentDelegate _tryBindClrPropertyAssignment;
    private readonly TryBindEventSubscriptionDelegate _tryBindEventSubscription;
    private readonly TryBindFrameworkPropertyAssignmentDelegate _tryBindFrameworkPropertyAssignment;

    public ObjectNodeStandardPropertyAssignmentBindingService(
        TryBindCollectionLiteralPropertyAssignmentDelegate tryBindCollectionLiteralPropertyAssignment,
        TryBindClrPropertyAssignmentDelegate tryBindClrPropertyAssignment,
        TryBindEventSubscriptionDelegate tryBindEventSubscription,
        TryBindFrameworkPropertyAssignmentDelegate tryBindFrameworkPropertyAssignment)
    {
        _tryBindCollectionLiteralPropertyAssignment = tryBindCollectionLiteralPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindCollectionLiteralPropertyAssignment));
        _tryBindClrPropertyAssignment = tryBindClrPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindClrPropertyAssignment));
        _tryBindEventSubscription = tryBindEventSubscription ?? throw new ArgumentNullException(nameof(tryBindEventSubscription));
        _tryBindFrameworkPropertyAssignment = tryBindFrameworkPropertyAssignment ?? throw new ArgumentNullException(nameof(tryBindFrameworkPropertyAssignment));
    }

    public ObjectNodeStandardPropertyAssignmentBindingResult Bind(
        ObjectNodeStandardPropertyAssignmentBindingRequest request,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (request.Property is not null &&
            request.Property.SetMethod is null &&
            _tryBindCollectionLiteralPropertyAssignment(
                request.ObjectType,
                request.Property,
                request.Assignment,
                request.Compilation,
                out var collectionLiteralAssignment))
        {
            return new ObjectNodeStandardPropertyAssignmentBindingResult(
                PropertyAssignment: null,
                PropertyElementAssignment: collectionLiteralAssignment,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (_tryBindFrameworkPropertyAssignment(request, diagnostics, out var frameworkAssignment))
        {
            return new ObjectNodeStandardPropertyAssignmentBindingResult(
                PropertyAssignment: frameworkAssignment,
                PropertyElementAssignment: null,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (request.Property is not null && request.Property.SetMethod is not null)
        {
            _ = _tryBindClrPropertyAssignment(request, diagnostics, out var clrAssignment);
            return new ObjectNodeStandardPropertyAssignmentBindingResult(
                PropertyAssignment: clrAssignment,
                PropertyElementAssignment: null,
                EventSubscription: null,
                Diagnostic: null);
        }

        if (_tryBindEventSubscription(request, diagnostics, out var eventSubscription))
        {
            return new ObjectNodeStandardPropertyAssignmentBindingResult(
                PropertyAssignment: null,
                PropertyElementAssignment: null,
                EventSubscription: eventSubscription,
                Diagnostic: null);
        }

        return new ObjectNodeStandardPropertyAssignmentBindingResult(
            PropertyAssignment: null,
            PropertyElementAssignment: null,
            EventSubscription: null,
            Diagnostic: new DiagnosticInfo(
                "AXSG0101",
                $"Property '{request.Assignment.PropertyName}' was not found on '{request.ObjectType.ToDisplayString()}'.",
                request.Document.FilePath,
                request.Assignment.Line,
                request.Assignment.Column,
                request.Options.StrictMode));
    }
}
