using System;
using System.Threading;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Workspace;
using XamlToCSharpGenerator.LanguageServer.Protocol;
using XamlToCSharpGenerator.LanguageServer.Server;

var workspaceRoot = ParseWorkspaceRoot(args);

// ------------------------------------------------------------------
// Tier-1: Attempt to build/load a fast compilation snapshot from the
// project's Avalonia build artifacts so IntelliSense is available
// immediately — before MSBuild finishes loading.
//
//   Cache hit  → snapshot loaded in < 1 s from disk, served as Tier-1
//   Cache miss → snapshot built from the Avalonia/references artifact,
//                persisted to disk in the background for next startup
//   No artifact → no snapshot, prewarm-only (same as before)
// ------------------------------------------------------------------
CompilationSnapshot? fastSnapshot = null;
string? avaloniaVersion = null;

if (workspaceRoot is not null)
{
    var referencesFile = AvaloniaFastCompilationProvider.FindReferencesFile(workspaceRoot);
    if (referencesFile is not null)
    {
        Console.Error.WriteLine($"[AXSG-LS] Avalonia references file found: {referencesFile}");

        avaloniaVersion = AvaloniaFastCompilationProvider.GetAvaloniaVersionFromReferencesFile(referencesFile);
        if (avaloniaVersion is not null)
        {
            Console.Error.WriteLine($"[AXSG-LS] Detected Avalonia version: {avaloniaVersion}");

            // Try cache first (fast path — just JSON deserialization)
            fastSnapshot = AvaloniaFastCompilationProvider.TryLoadFromCache(avaloniaVersion);

            if (fastSnapshot is not null)
            {
                Console.Error.WriteLine(
                    $"[AXSG-LS] Tier-1 cache hit for Avalonia {avaloniaVersion} — fast snapshot ready.");
            }
            else
            {
                // Cache miss: build from references file (may take a few seconds)
                Console.Error.WriteLine(
                    $"[AXSG-LS] Tier-1 cache miss for Avalonia {avaloniaVersion} — building fast snapshot...");

                fastSnapshot = AvaloniaFastCompilationProvider.BuildFastSnapshot(referencesFile);

                if (fastSnapshot?.Compilation is not null)
                {
                    // Persist to disk in the background so we don't block server startup
                    var snapshotCompilation = fastSnapshot.Compilation;
                    var versionForCache = avaloniaVersion;
                    _ = System.Threading.Tasks.Task.Run(() =>
                        AvaloniaFastCompilationProvider.PersistToDisk(snapshotCompilation, versionForCache));
                }
            }
        }
        else
        {
            Console.Error.WriteLine("[AXSG-LS] Could not determine Avalonia version from references file.");
        }
    }
    else
    {
        Console.Error.WriteLine("[AXSG-LS] No Avalonia references file found — project may not have been built yet.");
    }
}

var options = new XamlLanguageServiceOptions(workspaceRoot, AvaloniaVersion: avaloniaVersion);

// Use a TieredCompilationProvider so MSBuild loading starts at server startup
// rather than on the first completion keystroke.
var tieredProvider = new TieredCompilationProvider(new MsBuildCompilationProvider(), fastSnapshot);

using var engine = new XamlLanguageServiceEngine(tieredProvider);
using var server = new AxsgLanguageServer(
    new LspMessageReader(Console.OpenStandardInput()),
    new LspMessageWriter(Console.OpenStandardOutput()),
    engine,
    options);

// Wire prewarm completion → notify the client that Tier-2 is ready
tieredProvider.OnPrewarmCompleted = () =>
{
    if (avaloniaVersion is not null)
    {
        _ = server.NotifyCacheReadyAsync(avaloniaVersion);
    }
};

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
