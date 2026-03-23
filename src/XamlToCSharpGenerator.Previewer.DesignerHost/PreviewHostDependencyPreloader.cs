using System.Reflection;
using System.Text.Json;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewHostDependencyPreloader
{
    public static void PreloadManagedDependencies(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return;
        }

        string normalizedAssemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(normalizedAssemblyPath))
        {
            return;
        }

        string rootAssemblySimpleName = Path.GetFileNameWithoutExtension(normalizedAssemblyPath);
        if (string.IsNullOrWhiteSpace(rootAssemblySimpleName))
        {
            return;
        }

        string depsJsonFile = Path.ChangeExtension(normalizedAssemblyPath, ".deps.json")
            ?? string.Empty;
        if (!File.Exists(depsJsonFile))
        {
            string? assemblyDirectory = Path.GetDirectoryName(normalizedAssemblyPath);
            if (string.IsNullOrWhiteSpace(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
            {
                return;
            }

            string[] fallbackDepsFiles = Directory.GetFiles(assemblyDirectory, "*.deps.json");
            if (fallbackDepsFiles.Length != 1)
            {
                return;
            }

            depsJsonFile = fallbackDepsFiles[0];
        }

        try
        {
            using FileStream stream = File.OpenRead(depsJsonFile);
            using JsonDocument depsDocument = JsonDocument.Parse(stream);
            if (!depsDocument.RootElement.TryGetProperty("targets", out JsonElement targetsElement) ||
                targetsElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (JsonProperty target in targetsElement.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty library in target.Value.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("runtime", out JsonElement runtimeElement) ||
                        runtimeElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (JsonProperty runtimeAsset in runtimeElement.EnumerateObject())
                    {
                        string assemblyName = Path.GetFileNameWithoutExtension(runtimeAsset.Name);
                        if (string.IsNullOrWhiteSpace(assemblyName) ||
                            string.Equals(assemblyName, rootAssemblySimpleName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            _ = Assembly.Load(new AssemblyName(assemblyName));
                        }
                        catch
                        {
                            // Best effort preload for preview/design-time startup only.
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort preload for preview/design-time startup only.
        }
    }
}
