using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupObjectElementTypeResolutionService
{
    public delegate bool IsFrameworkDefaultXmlNamespaceDelegate(string xmlNamespace);

    private readonly IsFrameworkDefaultXmlNamespaceDelegate _isFrameworkDefaultXmlNamespace;
    private readonly string _xaml2006Namespace;
    private readonly ImmutableDictionary<string, TypeContractId> _markupObjectElementTypeContracts;

    public MarkupObjectElementTypeResolutionService(
        IsFrameworkDefaultXmlNamespaceDelegate isFrameworkDefaultXmlNamespace,
        string xaml2006Namespace,
        ImmutableDictionary<string, TypeContractId> markupObjectElementTypeContracts)
    {
        _isFrameworkDefaultXmlNamespace = isFrameworkDefaultXmlNamespace ?? throw new ArgumentNullException(nameof(isFrameworkDefaultXmlNamespace));
        _xaml2006Namespace = xaml2006Namespace ?? throw new ArgumentNullException(nameof(xaml2006Namespace));
        _markupObjectElementTypeContracts = markupObjectElementTypeContracts;
    }

    public INamedTypeSymbol? TryResolve(
        ITypeSymbolCatalog? typeSymbolCatalog,
        string xmlNamespace,
        string xmlTypeName)
    {
        if (!ShouldResolveFromMarkupObjectElement(xmlNamespace))
        {
            return null;
        }

        var normalizedToken = XamlTypeTokenSemantics.TrimXamlDirectivePrefix(xmlTypeName).Trim();
        if (normalizedToken.Length == 0 ||
            !_markupObjectElementTypeContracts.TryGetValue(normalizedToken, out var typeContractId))
        {
            return null;
        }

        return typeSymbolCatalog?.GetOrDefault(typeContractId);
    }

    private bool ShouldResolveFromMarkupObjectElement(string xmlNamespace)
    {
        return _isFrameworkDefaultXmlNamespace(xmlNamespace) ||
               string.Equals(xmlNamespace, _xaml2006Namespace, StringComparison.Ordinal);
    }
}
