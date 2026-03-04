using System;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlMetadataSymbolUri
{
    public const string Scheme = "axsg-metadata";
    public const string SourceLinkScheme = "axsg-sourcelink";

    public static string CreateTypeUri(string fullTypeName)
    {
        var encodedTypeName = Uri.EscapeDataString(fullTypeName);
        return $"{Scheme}:///symbol.cs?kind=type&type={encodedTypeName}";
    }

    public static string CreatePropertyUri(
        string ownerTypeName,
        string propertyName,
        string propertyTypeName,
        bool isAttached,
        bool isSettable)
    {
        var encodedOwner = Uri.EscapeDataString(ownerTypeName);
        var encodedName = Uri.EscapeDataString(propertyName);
        var encodedPropertyType = Uri.EscapeDataString(propertyTypeName);
        return $"{Scheme}:///symbol.cs?kind=property&owner={encodedOwner}&name={encodedName}&type={encodedPropertyType}&attached={(isAttached ? "true" : "false")}&settable={(isSettable ? "true" : "false")}";
    }

    public static string CreateSourceLinkUri(string sourceUrl)
    {
        var encodedSourceUrl = Uri.EscapeDataString(sourceUrl);
        return $"{SourceLinkScheme}:///source.cs?url={encodedSourceUrl}";
    }
}
