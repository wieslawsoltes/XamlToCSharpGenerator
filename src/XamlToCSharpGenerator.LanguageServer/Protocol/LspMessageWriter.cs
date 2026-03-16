using System.IO;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

internal sealed class LspMessageWriter : JsonRpcMessageWriter
{
    public LspMessageWriter(Stream stream)
        : base(stream)
    {
    }
}
