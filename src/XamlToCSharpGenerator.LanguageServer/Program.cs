using System;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;

var workspaceRoot = ParseWorkspaceRoot(args);
var options = new XamlLanguageServiceOptions(workspaceRoot);

using var engine = new XamlLanguageServiceEngine();
using var server = new AxsgLanguageServer(
    new LspMessageReader(Console.OpenStandardInput()),
    new LspMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

var exitCode = await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
Environment.ExitCode = exitCode;

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
