using System;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.McpServer.Server;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

var workspaceRoot = ParseWorkspaceRoot(args);
var options = new XamlLanguageServiceOptions(workspaceRoot);

using var engine = new XamlLanguageServiceEngine();
using var server = new AxsgMcpServer(
    new JsonRpcMessageReader(Console.OpenStandardInput()),
    new JsonRpcMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

Environment.ExitCode = await server.RunAsync(CancellationToken.None).ConfigureAwait(false);

static string? ParseWorkspaceRoot(string[] args)
{
    for (var index = 0; index < args.Length; index++)
    {
        if (!string.Equals(args[index], "--workspace", StringComparison.Ordinal))
        {
            continue;
        }

        if (index + 1 >= args.Length)
        {
            return null;
        }

        return args[index + 1];
    }

    return null;
}
