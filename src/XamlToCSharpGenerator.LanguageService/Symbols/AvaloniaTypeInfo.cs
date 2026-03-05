using System.Collections.Immutable;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed record AvaloniaTypeInfo(
    string XmlTypeName,
    string FullTypeName,
    string XmlNamespace,
    string ClrNamespace,
    string AssemblyName,
    ImmutableArray<AvaloniaPropertyInfo> Properties,
    string Summary,
    AvaloniaSymbolSourceLocation? SourceLocation = null,
    ImmutableArray<AvaloniaPseudoClassInfo> PseudoClasses = default);
