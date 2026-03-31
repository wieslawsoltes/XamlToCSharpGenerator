using System;
using System.Threading;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Workspace;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;

var workspaceRoot = ParseWorkspaceRoot(args);
var options = new XamlLanguageServiceOptions(workspaceRoot);

// Use a TieredCompilationProvider so MSBuild loading starts at server startup
// rather than on the first completion keystroke.  For Avalonia, Tier-1 fast
// snapshots are not provided (Avalonia assemblies are project-specific NuGet
// packages, not a well-known SDK path); the prewarm alone cuts the cold-start
// wait significantly.
var tieredProvider = new TieredCompilationProvider(new MsBuildCompilationProvider());

using var engine = new XamlLanguageServiceEngine(tieredProvider);
using var server = new AxsgLanguageServer(
    new LspMessageReader(Console.OpenStandardInput()),
    new LspMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

// Kick off the background compilation load immediately.
if (workspaceRoot is not null)
{
    var projectFile = TieredCompilationProvider.FindFirstProjectFile(workspaceRoot);
    if (projectFile is not null)
    {
        Console.Error.WriteLine($"[AXSG-LS] Starting background prewarm for {projectFile}");
        _ = tieredProvider.PrewarmAsync(projectFile, workspaceRoot);
    }
    else
    {
        Console.Error.WriteLine("[AXSG-LS] No .csproj found in workspace — prewarm skipped.");
    }
}

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
