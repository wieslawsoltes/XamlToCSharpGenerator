namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlNamedElement(
    string Name,
    string XmlNamespace,
    string XmlTypeName,
    string? FieldModifier,
    int Line,
    int Column);
