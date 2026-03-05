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

    public static string CreateMetadataDocumentUri(string fullTypeName, string documentId, string? memberName = null)
    {
        var typeName = GetLeafTypeName(fullTypeName);
        var fileName = string.IsNullOrWhiteSpace(memberName)
            ? typeName + ".cs"
            : typeName + "." + memberName + ".cs";
        var encodedDocumentId = Uri.EscapeDataString(documentId);
        var encodedTypeName = Uri.EscapeDataString(fullTypeName);
        var encodedMemberName = Uri.EscapeDataString(memberName ?? string.Empty);
        return $"{Scheme}:///{fileName}?id={encodedDocumentId}&type={encodedTypeName}&member={encodedMemberName}";
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

    private static string GetLeafTypeName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return "symbol";
        }

        var separatorIndex = fullTypeName.LastIndexOf('.');
        return separatorIndex >= 0 && separatorIndex + 1 < fullTypeName.Length
            ? fullTypeName.Substring(separatorIndex + 1)
            : fullTypeName;
    }
}
