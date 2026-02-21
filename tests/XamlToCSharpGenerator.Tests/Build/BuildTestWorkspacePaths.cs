using System;
using System.IO;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class BuildTestWorkspacePaths
{
    public static string CreateTemporaryDirectory(string repositoryRoot, string scenario)
    {
        var normalizedScenario = NormalizeScenarioName(scenario);
        var directory = Path.Combine(
            repositoryRoot,
            ".tmp-tests",
            normalizedScenario,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void TryDeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for build-test workspaces.
        }
    }

    private static string NormalizeScenarioName(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario))
        {
            return "default";
        }

        var trimmed = scenario.Trim();
        var invalidChars = Path.GetInvalidFileNameChars();
        var result = new char[trimmed.Length];
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            result[index] = Array.IndexOf(invalidChars, current) >= 0 ? '_' : current;
        }

        return new string(result);
    }
}
