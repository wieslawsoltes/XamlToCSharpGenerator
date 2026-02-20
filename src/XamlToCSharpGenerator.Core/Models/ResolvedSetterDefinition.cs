namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedSetterDefinition(
    string PropertyName,
    string ValueExpression,
    bool IsCompiledBinding,
    string? CompiledBindingPath,
    string? CompiledBindingSourceTypeName,
    string? AvaloniaPropertyOwnerTypeName,
    string? AvaloniaPropertyFieldName,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
