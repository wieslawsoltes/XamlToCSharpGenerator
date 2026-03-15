using System.Reflection;
using System.Text.Json;
using global::Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class SourceGeneratedRuntimeXamlLoader
{
    public object Load(RuntimeXamlLoaderDocument document, RuntimeXamlLoaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);

        PreloadDepsAssemblies(configuration.LocalAssembly ?? Assembly.GetEntryAssembly());
        AvaloniaSourceGeneratedXamlLoader.Enable();
        AvaloniaSourceGeneratedXamlLoader.ConfigureRuntimeCompilation(options =>
        {
            options.EnableRuntimeCompilationFallback = false;
            options.TraceDiagnostics = false;
        });

        return AvaloniaSourceGeneratedXamlLoader.Load(document, configuration);
    }

    private static void PreloadDepsAssemblies(Assembly? targetAssembly)
    {
        var assemblyLocation = targetAssembly?.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return;
        }

        var depsJsonFile = Path.ChangeExtension(assemblyLocation, ".deps.json");
        if (!File.Exists(depsJsonFile))
        {
            var sameDirectory = Path.GetDirectoryName(depsJsonFile);
            if (string.IsNullOrWhiteSpace(sameDirectory) || !Directory.Exists(sameDirectory))
            {
                return;
            }

            var fallbackDepsFiles = Directory.GetFiles(sameDirectory, "*.deps.json");
            if (fallbackDepsFiles.Length != 1)
            {
                return;
            }

            depsJsonFile = fallbackDepsFiles[0];
        }

        try
        {
            using var stream = File.OpenRead(depsJsonFile);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("targets", out var targetsElement) ||
                targetsElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var target in targetsElement.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var library in target.Value.EnumerateObject())
                {
                    if (!library.Value.TryGetProperty("runtime", out var runtimeElement) ||
                        runtimeElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var runtimeAsset in runtimeElement.EnumerateObject())
                    {
                        var assemblyName = Path.GetFileNameWithoutExtension(runtimeAsset.Name);
                        if (string.IsNullOrWhiteSpace(assemblyName))
                        {
                            continue;
                        }

                        try
                        {
                            _ = Assembly.Load(new AssemblyName(assemblyName));
                        }
                        catch
                        {
                            // Best effort preload.
                        }
                    }
                }
            }
        }
        catch
        {
            // Best effort preload.
        }
    }
}
