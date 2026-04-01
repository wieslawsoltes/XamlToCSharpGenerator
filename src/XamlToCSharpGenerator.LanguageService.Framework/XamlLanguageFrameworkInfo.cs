using System.Collections.Immutable;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed record XamlLanguageFrameworkInfo(
    string Id,
    IXamlFrameworkProfile Profile,
    string DefaultXmlNamespace,
    ImmutableArray<string> XmlnsDefinitionAttributeMetadataNames,
    ImmutableArray<string> XmlnsPrefixAttributeMetadataNames,
    ImmutableArray<string> MarkupExtensionNamespaces,
    string PreferredProjectXamlItemName,
    ImmutableArray<string> ProjectXamlItemNames,
    ImmutableArray<XamlLanguageFrameworkCompletion> DirectiveCompletions,
    ImmutableArray<XamlLanguageFrameworkCompletion> MarkupExtensionCompletions,
    bool SupportsPseudoClasses = false,
    string? PseudoClassesAttributeMetadataName = null,
    bool SupportsAssemblyResourceUris = false,
    bool IncludeSourceAssemblyClrNamespacesInDefaultXmlNamespace = false,
    bool UseCompiledBindingsByDefault = false);
