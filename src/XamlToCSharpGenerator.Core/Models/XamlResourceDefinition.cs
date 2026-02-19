namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlResourceDefinition(
    string Key,
    string XmlNamespace,
    string XmlTypeName,
    string RawXaml,
    int Line,
    int Column);
