namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedCompiledBindingDefinition(
    string TargetTypeName,
    string TargetPropertyName,
    string Path,
    string SourceTypeName,
    string? ResultTypeName,
    string AccessorExpression,
    bool IsSetterBinding,
    int Line,
    int Column);
