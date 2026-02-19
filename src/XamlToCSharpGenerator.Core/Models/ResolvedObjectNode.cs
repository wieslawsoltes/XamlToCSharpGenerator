using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedObjectNode(
    string? KeyExpression,
    string? Name,
    string TypeName,
    string? FactoryExpression,
    bool UseServiceProviderConstructor,
    bool UseTopDownInitialization,
    ImmutableArray<ResolvedPropertyAssignment> PropertyAssignments,
    ImmutableArray<ResolvedPropertyElementAssignment> PropertyElementAssignments,
    ImmutableArray<ResolvedEventSubscription> EventSubscriptions,
    ImmutableArray<ResolvedObjectNode> Children,
    ResolvedChildAttachmentMode ChildAttachmentMode,
    string? ContentPropertyName,
    int Line,
    int Column);
