using System.IO;
using System.Text.Json;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class JsonRpcMessageReaderTests
{
    [Fact]
    public async Task ReadMessageAsync_PreservesPreviouslyReadDocumentAfterSubsequentReads()
    {
        using var stream = new MemoryStream();
        var writer = new JsonRpcMessageWriter(stream);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await writer.WriteAsync(new { jsonrpc = "2.0", method = "alpha", value = 1 }, cancellation.Token);
        await writer.WriteAsync(new { jsonrpc = "2.0", method = "bravo", value = 2 }, cancellation.Token);

        stream.Position = 0;

        var reader = new JsonRpcMessageReader(stream);

        using JsonDocument first = await reader.ReadMessageAsync(cancellation.Token)
            ?? throw new InvalidOperationException("Expected the first JSON-RPC message.");
        using JsonDocument second = await reader.ReadMessageAsync(cancellation.Token)
            ?? throw new InvalidOperationException("Expected the second JSON-RPC message.");

        Assert.Equal("alpha", first.RootElement.GetProperty("method").GetString());
        Assert.Equal(1, first.RootElement.GetProperty("value").GetInt32());
        Assert.Equal("bravo", second.RootElement.GetProperty("method").GetString());
        Assert.Equal(2, second.RootElement.GetProperty("value").GetInt32());
    }
}
