using System.Collections.Concurrent;
using System.IO;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Documents;

public sealed class XamlDocumentStore
{
    private readonly ConcurrentDictionary<string, LanguageServiceDocument> _documents =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _filePathToUri =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public LanguageServiceDocument Open(string uri, string text, int version)
    {
        var filePath = UriPathHelper.ToFilePath(uri);
        var document = new LanguageServiceDocument(uri, filePath, text, version);
        _documents[uri] = document;
        _filePathToUri[NormalizePath(filePath)] = uri;
        return document;
    }

    public LanguageServiceDocument? Get(string uri)
    {
        return _documents.TryGetValue(uri, out var document) ? document : null;
    }

    public LanguageServiceDocument? Update(string uri, string text, int version)
    {
        if (!_documents.TryGetValue(uri, out var document))
        {
            return null;
        }

        var updated = document with { Text = text, Version = version };
        _documents[uri] = updated;
        _filePathToUri[NormalizePath(updated.FilePath)] = uri;
        return updated;
    }

    public LanguageServiceDocument? GetByFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return _filePathToUri.TryGetValue(NormalizePath(filePath), out var uri)
            ? Get(uri)
            : null;
    }

    public bool Close(string uri)
    {
        if (!_documents.TryRemove(uri, out var document))
        {
            return false;
        }

        _filePathToUri.TryRemove(NormalizePath(document.FilePath), out _);
        return true;
    }

    private static string NormalizePath(string path)
    {
        return UriPathHelper.NormalizeFilePath(path);
    }
}
