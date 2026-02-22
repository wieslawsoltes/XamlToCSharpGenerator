using System;

namespace XamlToCSharpGenerator.Runtime;

public interface IXamlSourceGenUriMapper
{
    string Normalize(string? uri);

    bool UriEquals(string? left, string? right);
}

public sealed class XamlSourceGenUriMapper : IXamlSourceGenUriMapper
{
    public static XamlSourceGenUriMapper Default { get; } = new();

    public string Normalize(string? uri)
    {
        return string.IsNullOrWhiteSpace(uri) ? string.Empty : uri.Trim();
    }

    public bool UriEquals(string? left, string? right)
    {
        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }
}
