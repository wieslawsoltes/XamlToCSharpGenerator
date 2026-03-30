using System.IO;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

public sealed class LspMessageReader : JsonRpcMessageReader
{
    public LspMessageReader(Stream stream)
        : base(stream)
    {
    }
}
