using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public sealed class XamlFrameworkParserSettings
{
    public XamlFrameworkParserSettings(
        ImmutableDictionary<string, string> globalXmlnsPrefixes,
        bool allowImplicitDefaultXmlns,
        string implicitDefaultXmlns)
    {
        GlobalXmlnsPrefixes = globalXmlnsPrefixes;
        AllowImplicitDefaultXmlns = allowImplicitDefaultXmlns;
        ImplicitDefaultXmlns = implicitDefaultXmlns;
    }

    public ImmutableDictionary<string, string> GlobalXmlnsPrefixes { get; }

    public bool AllowImplicitDefaultXmlns { get; }

    public string ImplicitDefaultXmlns { get; }
}
