using System.Text.Json;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

internal static class Program
{
    private static readonly SemaphoreSlim OutputGate = new(1, 1);

    public static async Task<int> Main(string[] args)
    {
        if (args.Any(static arg => string.Equals(arg, "--mcp", StringComparison.Ordinal)))
        {
            await using var mcpServer = new PreviewHostMcpServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
            return await mcpServer.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using var router = new PreviewHostCommandRouter();
        router.EventPublished += message => _ = WriteMessageAsync(message);

        string? line;
        while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            AxsgPreviewHostResponseEnvelope response;
            try
            {
                AxsgPreviewHostCommandEnvelope command = AxsgPreviewHostProtocol.ParseCommandLine(line);
                response = await router.HandleCommandAsync(command, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                response = AxsgPreviewHostProtocol.CreateFailureResponse(
                    requestId: null,
                    "Invalid JSON command: " + ex.Message);
            }

            await WriteMessageAsync(response).ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task WriteMessageAsync(object message)
    {
        string json = JsonSerializer.Serialize(message, JsonRpcSerializer.DefaultOptions);
        await OutputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Console.Out.WriteLineAsync(json).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            OutputGate.Release();
        }
    }
}
