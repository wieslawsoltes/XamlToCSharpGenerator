using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewHostDependencyPreloader
{
    public static void PreloadManagedDependencies(string? assemblyPath)
    {
        IReadOnlyList<string> dependencyPaths = GetManagedDependencyPaths(assemblyPath);
        for (var index = 0; index < dependencyPaths.Count; index += 1)
        {
            string normalizedRuntimeAssetPath = dependencyPaths[index];
            try
            {
                if (IsAssemblyLoadedFromPath(normalizedRuntimeAssetPath))
                {
                    continue;
                }

                _ = AssemblyLoadContext.Default.LoadFromAssemblyPath(normalizedRuntimeAssetPath);
            }
            catch
            {
                // Best effort preload for preview/design-time startup only.
            }
        }
    }

    public static IReadOnlyList<string> GetManagedDependencyPaths(string? assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return [];
        }

        string normalizedAssemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(normalizedAssemblyPath))
        {
            return [];
        }

        string rootAssemblySimpleName = Path.GetFileNameWithoutExtension(normalizedAssemblyPath);
        if (string.IsNullOrWhiteSpace(rootAssemblySimpleName))
        {
            return [];
        }

        AssemblyDependencyResolver? dependencyResolver = TryCreateDependencyResolver(normalizedAssemblyPath);

        string? assemblyDirectory = Path.GetDirectoryName(normalizedAssemblyPath);
        if (string.IsNullOrWhiteSpace(assemblyDirectory) || !Directory.Exists(assemblyDirectory))
        {
            return [];
        }

        string depsJsonFile = Path.ChangeExtension(normalizedAssemblyPath, ".deps.json")
            ?? string.Empty;
        if (!File.Exists(depsJsonFile))
        {
            string[] fallbackDepsFiles = Directory.GetFiles(assemblyDirectory, "*.deps.json");
            if (fallbackDepsFiles.Length != 1)
            {
                return [];
            }

            depsJsonFile = fallbackDepsFiles[0];
        }

        try
        {
            var dependencyPaths = new List<string>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using FileStream stream = File.OpenRead(depsJsonFile);
            using JsonDocument depsDocument = JsonDocument.Parse(stream);
            if (!depsDocument.RootElement.TryGetProperty("targets", out JsonElement targetsElement) ||
                targetsElement.ValueKind != JsonValueKind.Object)
            {
                return [];
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

                        string? normalizedRuntimeAssetPath = ResolveRuntimeAssetPath(
                            dependencyResolver,
                            assemblyDirectory,
                            assemblyName,
                            runtimeAsset.Name);
                        if (string.IsNullOrWhiteSpace(normalizedRuntimeAssetPath) ||
                            !File.Exists(normalizedRuntimeAssetPath) ||
                            !seenPaths.Add(normalizedRuntimeAssetPath))
                        {
                            continue;
                        }

                        dependencyPaths.Add(normalizedRuntimeAssetPath);
                    }
                }
            }

            return dependencyPaths;
        }
        catch
        {
            return [];
        }
    }

    private static AssemblyDependencyResolver? TryCreateDependencyResolver(string assemblyPath)
    {
        try
        {
            return new AssemblyDependencyResolver(assemblyPath);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveRuntimeAssetPath(
        AssemblyDependencyResolver? dependencyResolver,
        string assemblyDirectory,
        string assemblyName,
        string runtimeAssetPath)
    {
        if (dependencyResolver is not null)
        {
            try
            {
                string? resolvedPath = dependencyResolver.ResolveAssemblyToPath(new AssemblyName(assemblyName));
                if (!string.IsNullOrWhiteSpace(resolvedPath) &&
                    File.Exists(resolvedPath))
                {
                    return Path.GetFullPath(resolvedPath);
                }
            }
            catch
            {
                // Fall back to local runtime asset probing below.
            }
        }

        string normalizedRuntimeAssetPath = NormalizeRuntimeAssetPath(assemblyDirectory, runtimeAssetPath);
        return File.Exists(normalizedRuntimeAssetPath)
            ? normalizedRuntimeAssetPath
            : null;
    }

    private static string NormalizeRuntimeAssetPath(string assemblyDirectory, string runtimeAssetPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyDirectory);
        ArgumentException.ThrowIfNullOrEmpty(runtimeAssetPath);

        string normalizedRelativePath = runtimeAssetPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);

        return Path.GetFullPath(Path.Combine(assemblyDirectory, normalizedRelativePath));
    }

    private static bool IsAssemblyLoadedFromPath(string assemblyPath)
    {
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            Assembly assembly = loadedAssemblies[index];
            if (assembly.IsDynamic ||
                string.IsNullOrWhiteSpace(assembly.Location))
            {
                continue;
            }

            if (string.Equals(
                Path.GetFullPath(assembly.Location),
                assemblyPath,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
