using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed record AvaloniaTypeInfo(
    string XmlTypeName,
    string FullTypeName,
    string XmlNamespace,
    string ClrNamespace,
    ImmutableArray<AvaloniaPropertyInfo> Properties,
    string Summary);
