using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class MarkupObjectElementTypeResolutionService
{
    private const string StaticResourceExtensionMetadataName = "Avalonia.Markup.Xaml.MarkupExtensions.StaticResourceExtension";
    private const string DynamicResourceExtensionMetadataName = "Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension";
    private const string OnPlatformExtensionMetadataName = "Avalonia.Markup.Xaml.MarkupExtensions.OnPlatformExtension";
    private const string OnFormFactorExtensionMetadataName = "Avalonia.Markup.Xaml.MarkupExtensions.OnFormFactorExtension";
    private const string OnMetadataName = "Avalonia.Markup.Xaml.MarkupExtensions.On";

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

    public INamedTypeSymbol? TryResolve(Compilation compilation, string xmlNamespace, string xmlTypeName)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (!ShouldResolveFromMarkupObjectElement(xmlNamespace))
        {
            return null;
        }

        var normalizedToken = XamlTypeTokenSemantics.TrimXamlDirectivePrefix(xmlTypeName).Trim();
        if (normalizedToken.Length == 0)
        {
            return null;
        }

        var metadataName = normalizedToken switch
        {
            "StaticResource" or "StaticResourceExtension" => StaticResourceExtensionMetadataName,
            "DynamicResource" or "DynamicResourceExtension" => DynamicResourceExtensionMetadataName,
            "OnPlatform" or "OnPlatformExtension" => OnPlatformExtensionMetadataName,
            "OnFormFactor" or "OnFormFactorExtension" => OnFormFactorExtensionMetadataName,
            "On" => OnMetadataName,
            _ => null
        };

        return metadataName is null
            ? null
            : compilation.GetTypeByMetadataName(metadataName);
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
