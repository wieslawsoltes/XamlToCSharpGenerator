namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedPropertyAssignment(
    string PropertyName,
    string ValueExpression,
    string? AvaloniaPropertyOwnerTypeName,
    string? AvaloniaPropertyFieldName,
    string? ClrPropertyOwnerTypeName,
    string? ClrPropertyTypeName,
    string? BindingPriorityExpression,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
