using System;
using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlClrSymbolResolver
{
    public static bool TryResolveTypeInfo(
        AvaloniaTypeIndex typeIndex,
        ImmutableDictionary<string, string> prefixMap,
        string? qualifiedTypeName,
        out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (string.IsNullOrWhiteSpace(qualifiedTypeName))
        {
            return false;
        }

        if (!XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                prefixMap,
                qualifiedTypeName,
                out var xmlNamespace,
                out var xmlTypeName))
        {
            return false;
        }

        if (TryResolveClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return typeIndex.TryGetTypeByClrNamespace(clrNamespace, xmlTypeName, out typeInfo) && typeInfo is not null;
        }

        return typeIndex.TryGetType(xmlNamespace, xmlTypeName, out typeInfo) && typeInfo is not null;
    }

    public static bool TryResolvePropertyInfo(
        AvaloniaTypeIndex typeIndex,
        ImmutableDictionary<string, string> prefixMap,
        string? currentElementName,
        string? propertyToken,
        out AvaloniaPropertyInfo? propertyInfo,
        out AvaloniaTypeInfo? ownerTypeInfo)
    {
        propertyInfo = null;
        ownerTypeInfo = null;

        if (string.IsNullOrWhiteSpace(propertyToken))
        {
            return false;
        }

        if (propertyToken.Contains(':', StringComparison.Ordinal) &&
            !propertyToken.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        if (propertyToken.Contains('.', StringComparison.Ordinal))
        {
            var separator = propertyToken.IndexOf('.');
            var ownerToken = separator > 0 ? propertyToken.Substring(0, separator) : string.Empty;
            var attachedPropertyName = separator >= 0 && separator + 1 < propertyToken.Length
                ? propertyToken.Substring(separator + 1)
                : string.Empty;
            if (string.IsNullOrWhiteSpace(ownerToken) || string.IsNullOrWhiteSpace(attachedPropertyName))
            {
                return false;
            }

            if (!TryResolveTypeInfo(typeIndex, prefixMap, ownerToken, out ownerTypeInfo) || ownerTypeInfo is null)
            {
                return false;
            }

            propertyInfo = ownerTypeInfo.Properties.FirstOrDefault(property =>
                string.Equals(property.Name, attachedPropertyName, StringComparison.Ordinal));
            return propertyInfo is not null;
        }

        if (!TryResolveTypeInfo(typeIndex, prefixMap, currentElementName, out ownerTypeInfo) || ownerTypeInfo is null)
        {
            return false;
        }

        propertyInfo = ownerTypeInfo.Properties.FirstOrDefault(property =>
            string.Equals(property.Name, propertyToken, StringComparison.Ordinal));
        return propertyInfo is not null;
    }

    internal static bool TryResolveClrNamespace(string xmlNamespace, out string clrNamespace)
    {
        clrNamespace = string.Empty;
        if (string.IsNullOrWhiteSpace(xmlNamespace))
        {
            return false;
        }

        if (xmlNamespace.StartsWith("using:", StringComparison.Ordinal))
        {
            clrNamespace = xmlNamespace.Substring("using:".Length).Trim();
            return !string.IsNullOrWhiteSpace(clrNamespace);
        }

        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.OrdinalIgnoreCase))
        {
            var value = xmlNamespace.Substring("clr-namespace:".Length);
            var separator = value.IndexOf(';');
            clrNamespace = (separator >= 0 ? value.Substring(0, separator) : value).Trim();
            return !string.IsNullOrWhiteSpace(clrNamespace);
        }

        return false;
    }
}
