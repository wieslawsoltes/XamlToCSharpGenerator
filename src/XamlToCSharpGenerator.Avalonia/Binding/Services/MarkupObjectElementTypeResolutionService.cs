using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class MarkupObjectElementTypeResolutionService
{
    internal delegate bool IsAvaloniaDefaultXmlNamespaceDelegate(string xmlNamespace);

    private readonly IsAvaloniaDefaultXmlNamespaceDelegate _isAvaloniaDefaultXmlNamespace;
    private readonly string _xaml2006Namespace;

    public MarkupObjectElementTypeResolutionService(
        IsAvaloniaDefaultXmlNamespaceDelegate isAvaloniaDefaultXmlNamespace,
        string xaml2006Namespace)
    {
        _isAvaloniaDefaultXmlNamespace = isAvaloniaDefaultXmlNamespace ??
                                         throw new ArgumentNullException(nameof(isAvaloniaDefaultXmlNamespace));
        _xaml2006Namespace = xaml2006Namespace ?? throw new ArgumentNullException(nameof(xaml2006Namespace));
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
        if (normalizedToken.Length == 0)
        {
            return null;
        }

        TypeContractId? typeContractId = normalizedToken switch
        {
            "StaticResource" or "StaticResourceExtension" => TypeContractId.StaticResourceExtension,
            "DynamicResource" or "DynamicResourceExtension" => TypeContractId.DynamicResourceExtension,
            "OnPlatform" or "OnPlatformExtension" => TypeContractId.OnPlatformExtension,
            "OnFormFactor" or "OnFormFactorExtension" => TypeContractId.OnFormFactorExtension,
            "On" => TypeContractId.OnMarkupExtension,
            _ => null
        };

        return typeContractId is null
            ? null
            : typeSymbolCatalog?.GetOrDefault(typeContractId.Value);
    }

    private bool ShouldResolveFromMarkupObjectElement(string xmlNamespace)
    {
        if (_isAvaloniaDefaultXmlNamespace(xmlNamespace))
        {
            return true;
        }

        return string.Equals(xmlNamespace, _xaml2006Namespace, StringComparison.Ordinal);
    }
}
