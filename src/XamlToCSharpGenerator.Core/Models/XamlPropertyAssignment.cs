namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlPropertyAssignment(
    string PropertyName,
    string XmlNamespace,
    string Value,
    bool IsAttached,
    int Line,
    int Column);
