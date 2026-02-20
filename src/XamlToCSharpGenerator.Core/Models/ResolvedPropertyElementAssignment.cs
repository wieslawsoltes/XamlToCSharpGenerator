using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedPropertyElementAssignment(
    string PropertyName,
    string? AvaloniaPropertyOwnerTypeName,
    string? AvaloniaPropertyFieldName,
    string? ClrPropertyOwnerTypeName,
    string? ClrPropertyTypeName,
    string? BindingPriorityExpression,
    bool IsCollectionAdd,
    bool IsDictionaryMerge,
    ImmutableArray<ResolvedObjectNode> ObjectValues,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
