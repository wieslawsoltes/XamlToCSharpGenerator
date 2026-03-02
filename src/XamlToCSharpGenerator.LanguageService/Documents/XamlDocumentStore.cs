using System.Collections.Concurrent;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Documents;

public sealed class XamlDocumentStore
{
    private readonly ConcurrentDictionary<string, LanguageServiceDocument> _documents =
        new(StringComparer.Ordinal);

    public LanguageServiceDocument Open(string uri, string text, int version)
    {
        var filePath = UriPathHelper.ToFilePath(uri);
        var document = new LanguageServiceDocument(uri, filePath, text, version);
        _documents[uri] = document;
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
        return updated;
    }

    public bool Close(string uri)
    {
        return _documents.TryRemove(uri, out _);
    }
}
