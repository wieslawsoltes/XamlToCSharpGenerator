using System;
using System.IO;

namespace XamlToCSharpGenerator.LanguageService.Text;

internal static class UriPathHelper
{
    public static string ToFilePath(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            return parsed.LocalPath;
        }

        return uri;
    }

    public static string ToDocumentUri(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(filePath, UriKind.Absolute, out var parsed) && parsed.IsFile)
        {
            return parsed.AbsoluteUri;
        }

        var absolute = Path.GetFullPath(filePath);
        return new Uri(absolute).AbsoluteUri;
    }
}
