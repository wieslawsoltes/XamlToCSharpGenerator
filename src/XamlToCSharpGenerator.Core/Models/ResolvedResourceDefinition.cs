namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedResourceDefinition(
    string Key,
    string TypeName,
    string RawXaml,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
