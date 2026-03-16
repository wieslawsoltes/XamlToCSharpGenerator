using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class SourceGeneratedRuntimeXamlLoader
{
    private static readonly ConcurrentDictionary<string, string> LastGoodOverlayByDocument = new(StringComparer.OrdinalIgnoreCase);

    public object Load(RuntimeXamlLoaderDocument document, RuntimeXamlLoaderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);

        var localAssembly = configuration.LocalAssembly ?? Assembly.GetEntryAssembly();
        PreloadDepsAssemblies(localAssembly);

        var xamlText = ReadXamlText(document);
        return LoadCore(
            document,
            configuration,
            xamlText,
            PreviewHostRuntimeState.Current.SourceFilePath,
            localAssembly?.Location,
            LoadGeneratedBaseline,
            TryApplyLiveOverlay);
    }

    internal object LoadCore(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        string xamlText,
        string? sourceFilePath,
        string? assemblyPath,
        Func<RuntimeXamlLoaderDocument, RuntimeXamlLoaderConfiguration, string, object> baselineLoader,
        TryApplyLiveOverlayDelegate overlayApplier)
    {
        var baseline = baselineLoader(document, configuration, xamlText);
        var cacheKey = GetOverlayCacheKey(document, sourceFilePath);
        if (!ShouldApplyPreviewOverlay(
                baseline,
                xamlText,
                sourceFilePath,
                assemblyPath))
        {
            LastGoodOverlayByDocument.TryRemove(cacheKey, out _);
            return baseline;
        }

        if (overlayApplier(document, configuration, baseline, xamlText, out var overlaidRoot))
        {
            LastGoodOverlayByDocument[cacheKey] = xamlText;
            return overlaidRoot;
        }

        var fallbackBaseline = baselineLoader(document, configuration, xamlText);
        if (LastGoodOverlayByDocument.TryGetValue(cacheKey, out var lastGoodXaml) &&
            !string.Equals(lastGoodXaml, xamlText, StringComparison.Ordinal))
        {
            if (overlayApplier(document, configuration, fallbackBaseline, lastGoodXaml, out var lastGoodRoot))
            {
                Log("Live preview XAML was invalid. Reverted to the last known good unsaved preview.");
                return lastGoodRoot;
            }
        }

        Log("Live preview XAML was invalid. Falling back to the last successful build output.");
        return fallbackBaseline;
    }

    internal static bool ShouldApplyPreviewOverlay(
        object baselineRoot,
        string xamlText,
        string? sourceFilePath,
        string? assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(baselineRoot);
        ArgumentNullException.ThrowIfNull(xamlText);

        return RequiresDesignOverlay(baselineRoot) ||
               ShouldApplyLiveOverlay(xamlText, sourceFilePath, assemblyPath);
    }

    internal static bool ShouldApplyLiveOverlay(string xamlText, string? sourceFilePath, string? assemblyPath)
    {
        ArgumentNullException.ThrowIfNull(xamlText);

        if (string.IsNullOrWhiteSpace(sourceFilePath) ||
            !File.Exists(sourceFilePath))
        {
            return true;
        }

        string persistedText;
        try
        {
            persistedText = File.ReadAllText(sourceFilePath);
        }
        catch
        {
            return true;
        }

        if (!string.Equals(persistedText, xamlText, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(assemblyPath) ||
            !File.Exists(assemblyPath))
        {
            return true;
        }

        try
        {
            var sourceWriteTimeUtc = File.GetLastWriteTimeUtc(sourceFilePath);
            var assemblyWriteTimeUtc = File.GetLastWriteTimeUtc(assemblyPath);
            return sourceWriteTimeUtc > assemblyWriteTimeUtc;
        }
        catch
        {
            return true;
        }
    }

    internal static bool RequiresDesignOverlay(object baselineRoot)
    {
        ArgumentNullException.ThrowIfNull(baselineRoot);

        return baselineRoot switch
        {
            Application => true,
            Control => false,
            ResourceDictionary => true,
            IStyle => true,
            IDataTemplate => true,
            AvaloniaObject => true,
            _ => false
        };
    }

    internal static string ReadXamlText(RuntimeXamlLoaderDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var stream = document.XamlStream;
        if (stream is null)
        {
            return string.Empty;
        }

        long? originalPosition = null;
        if (stream.CanSeek)
        {
            originalPosition = stream.Position;
            stream.Position = 0;
        }

        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            return reader.ReadToEnd();
        }
        finally
        {
            if (originalPosition.HasValue && stream.CanSeek)
            {
                stream.Position = originalPosition.Value;
            }
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine("[AXSG preview] " + message);
    }

    private static string GetOverlayCacheKey(RuntimeXamlLoaderDocument document, string? sourceFilePath)
    {
        if (!string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return Path.GetFullPath(sourceFilePath);
        }

        if (document.BaseUri is not null)
        {
            return document.BaseUri.ToString();
        }

        return document.Document ?? "<inline>";
    }

    internal static void ClearLastGoodOverlayCacheForTests()
    {
        LastGoodOverlayByDocument.Clear();
    }

    internal static void SetLastGoodOverlayForTests(RuntimeXamlLoaderDocument document, string? sourceFilePath, string xamlText)
    {
        LastGoodOverlayByDocument[GetOverlayCacheKey(document, sourceFilePath)] = xamlText;
    }

    internal static bool TryGetLastGoodOverlayForTests(RuntimeXamlLoaderDocument document, string? sourceFilePath, out string? xamlText)
    {
        if (LastGoodOverlayByDocument.TryGetValue(GetOverlayCacheKey(document, sourceFilePath), out var value))
        {
            xamlText = value;
            return true;
        }

        xamlText = null;
        return false;
    }

    private static object LoadGeneratedBaseline(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        string xamlText)
    {
        AvaloniaSourceGeneratedXamlLoader.Enable();
        AvaloniaSourceGeneratedXamlLoader.ConfigureRuntimeCompilation(options =>
        {
            options.EnableRuntimeCompilationFallback = false;
            options.TraceDiagnostics = false;
        });

        return AvaloniaSourceGeneratedXamlLoader.Load(
            CloneDocument(document, rootInstance: document.RootInstance, xamlText),
            CloneConfiguration(configuration));
    }

    private static bool TryApplyLiveOverlay(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        object baselineRoot,
        string xamlText,
        out object result)
    {
        ArgumentNullException.ThrowIfNull(baselineRoot);

        try
        {
            var localAssembly = configuration.LocalAssembly ?? baselineRoot.GetType().Assembly;
            var rewrittenXaml = SourceGeneratedPreviewMarkupRuntimeInstaller.IsInstalled
                ? SourceGeneratedPreviewXamlPreprocessor.Rewrite(xamlText, localAssembly)
                : xamlText;
            result = AvaloniaRuntimeXamlLoader.Load(
                CloneDocument(document, baselineRoot, rewrittenXaml),
                CloneConfiguration(configuration));
            return true;
        }
        catch (Exception ex)
        {
            Log("Live preview overlay failed: " + ex.Message);
            result = baselineRoot;
            return false;
        }
    }

    internal delegate bool TryApplyLiveOverlayDelegate(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration,
        object baselineRoot,
        string xamlText,
        out object result);

    private static RuntimeXamlLoaderDocument CloneDocument(
        RuntimeXamlLoaderDocument document,
        object? rootInstance,
        string xamlText)
    {
        var clone = new RuntimeXamlLoaderDocument(document.BaseUri, rootInstance, xamlText)
        {
            Document = document.Document,
            ServiceProvider = document.ServiceProvider
        };
        return clone;
    }

    private static RuntimeXamlLoaderConfiguration CloneConfiguration(RuntimeXamlLoaderConfiguration configuration)
    {
        return new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = configuration.LocalAssembly,
            UseCompiledBindingsByDefault = configuration.UseCompiledBindingsByDefault,
            DesignMode = configuration.DesignMode,
            CreateSourceInfo = configuration.CreateSourceInfo,
            DiagnosticHandler = configuration.DiagnosticHandler
        };
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
            using var depsDocument = JsonDocument.Parse(stream);
            if (!depsDocument.RootElement.TryGetProperty("targets", out var targetsElement) ||
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
