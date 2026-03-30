using System.IO;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.LanguageServer.Protocol;

public sealed class LspMessageWriter : JsonRpcMessageWriter
{
    public LspMessageWriter(Stream stream)
        : base(stream)
    {
    }
}
