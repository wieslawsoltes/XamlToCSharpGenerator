namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedCompiledBindingDefinition(
    string TargetTypeName,
    string TargetPropertyName,
    string Path,
    string SourceTypeName,
    string AccessorExpression,
    bool IsSetterBinding,
    int Line,
    int Column);
