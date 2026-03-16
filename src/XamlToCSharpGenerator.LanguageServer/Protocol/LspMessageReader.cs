using System.IO;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

internal sealed class LspMessageReader : JsonRpcMessageReader
{
    public LspMessageReader(Stream stream)
        : base(stream)
    {
    }
}
