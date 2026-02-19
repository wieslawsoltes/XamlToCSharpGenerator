namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedNamedElement(
    string Name,
    string TypeName,
    string FieldModifier,
    int Line,
    int Column);
