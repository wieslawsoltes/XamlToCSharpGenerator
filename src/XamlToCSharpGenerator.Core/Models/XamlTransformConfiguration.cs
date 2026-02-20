using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlTransformConfiguration(
    ImmutableArray<XamlTypeAliasRule> TypeAliases,
    ImmutableArray<XamlPropertyAliasRule> PropertyAliases)
{
    public static XamlTransformConfiguration Empty { get; } = new(
        ImmutableArray<XamlTypeAliasRule>.Empty,
        ImmutableArray<XamlPropertyAliasRule>.Empty);
}

public sealed record XamlTypeAliasRule(
    string XmlNamespace,
    string XamlTypeName,
    string ClrTypeName,
    string Source,
    int Line,
    int Column);

public sealed record XamlPropertyAliasRule(
    string TargetTypeName,
    string XamlPropertyName,
    string? ClrPropertyName,
    string? AvaloniaPropertyOwnerTypeName,
    string? AvaloniaPropertyFieldName,
    string Source,
    int Line,
    int Column);
