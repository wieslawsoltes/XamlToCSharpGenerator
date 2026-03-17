using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Templates;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class SourceGeneratedRuntimeXamlLoader
{
    [Flags]
    private enum RootOverlayCollections
    {
        None = 0,
        Resources = 1 << 0,
        Styles = 1 << 1,
        DataTemplates = 1 << 2
    }

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
        var localAssembly = configuration.LocalAssembly ?? Assembly.GetEntryAssembly();
        PreviewRootDataContextHydrator.TryHydrate(document.RootInstance, xamlText, localAssembly);

        var baseline = baselineLoader(document, configuration, xamlText);
        PreviewRootDataContextHydrator.TryHydrate(
            baseline,
            xamlText,
            localAssembly ?? baseline.GetType().Assembly);
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
            PreviewRootDataContextHydrator.TryHydrate(
                overlaidRoot,
                xamlText,
                localAssembly ?? overlaidRoot.GetType().Assembly);
            LastGoodOverlayByDocument[cacheKey] = xamlText;
            return overlaidRoot;
        }

        var fallbackBaseline = baselineLoader(document, configuration, xamlText);
        PreviewRootDataContextHydrator.TryHydrate(
            fallbackBaseline,
            xamlText,
            localAssembly ?? fallbackBaseline.GetType().Assembly);
        if (LastGoodOverlayByDocument.TryGetValue(cacheKey, out var lastGoodXaml) &&
            !string.Equals(lastGoodXaml, xamlText, StringComparison.Ordinal))
        {
            if (overlayApplier(document, configuration, fallbackBaseline, lastGoodXaml, out var lastGoodRoot))
            {
                PreviewRootDataContextHydrator.TryHydrate(
                    lastGoodRoot,
                    lastGoodXaml,
                    localAssembly ?? lastGoodRoot.GetType().Assembly);
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
            CreatePreviewConfiguration(configuration));
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
            PrepareBaselineForOverlay(baselineRoot, xamlText);
            var rewrittenXaml = SourceGeneratedPreviewMarkupRuntimeInstaller.IsInstalled
                ? SourceGeneratedPreviewXamlPreprocessor.Rewrite(xamlText, localAssembly)
                : xamlText;
            result = AvaloniaRuntimeXamlLoader.Load(
                CloneDocument(document, baselineRoot, rewrittenXaml),
                CreatePreviewConfiguration(configuration));
            return true;
        }
        catch (Exception ex)
        {
            Log("Live preview overlay failed: " + ex.Message);
            result = baselineRoot;
            return false;
        }
    }

    private static void PrepareBaselineForOverlay(object baselineRoot, string xamlText)
    {
        switch (baselineRoot)
        {
            case null:
                return;
            case ResourceDictionary resourceDictionary:
                XamlSourceGenHotReloadStateTracker.TryClearCollection(resourceDictionary);
                return;
            case Styles styles:
                XamlSourceGenHotReloadStateTracker.TryClearCollection(styles);
                return;
            case DataTemplates dataTemplates:
                XamlSourceGenHotReloadStateTracker.TryClearCollection(dataTemplates);
                return;
            case StyleBase styleBase:
                XamlSourceGenHotReloadStateTracker.TryClearCollection(styleBase.Setters);
                XamlSourceGenHotReloadStateTracker.TryClearCollection(styleBase.Children);
                XamlSourceGenHotReloadStateTracker.TryClearCollection(styleBase.Resources);
                XamlSourceGenHotReloadStateTracker.TryClearCollection(styleBase.Animations);
                return;
            case Application application:
                PrepareRootHostCollections(application, xamlText);
                return;
            case Control control:
                PrepareRootHostCollections(control, xamlText);
                return;
        }
    }

    private static void PrepareRootHostCollections(Application application, string xamlText)
    {
        var declarations = GetRootOverlayCollections(xamlText, application.GetType());
        if ((declarations & RootOverlayCollections.Resources) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(application.Resources);
        }

        if ((declarations & RootOverlayCollections.Styles) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(application.Styles);
        }

        if ((declarations & RootOverlayCollections.DataTemplates) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(application.DataTemplates);
        }
    }

    private static void PrepareRootHostCollections(Control control, string xamlText)
    {
        var declarations = GetRootOverlayCollections(xamlText, control.GetType());
        if ((declarations & RootOverlayCollections.Resources) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(control.Resources);
        }

        if ((declarations & RootOverlayCollections.Styles) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(control.Styles);
        }

        if ((declarations & RootOverlayCollections.DataTemplates) != 0)
        {
            XamlSourceGenHotReloadStateTracker.TryClearCollection(control.DataTemplates);
        }
    }

    private static RootOverlayCollections GetRootOverlayCollections(string xamlText, Type rootType)
    {
        if (string.IsNullOrWhiteSpace(xamlText))
        {
            return RootOverlayCollections.None;
        }

        try
        {
            var document = XDocument.Parse(xamlText, LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                return RootOverlayCollections.None;
            }

            var recognizedOwners = GetRecognizedRootOwnerNames(rootType);
            var declarations = RootOverlayCollections.None;
            foreach (var childElement in document.Root.Elements())
            {
                var localName = childElement.Name.LocalName;
                var separatorIndex = localName.IndexOf('.');
                if (separatorIndex <= 0 || separatorIndex >= localName.Length - 1)
                {
                    continue;
                }

                var ownerToken = localName[..separatorIndex];
                if (!recognizedOwners.Contains(ownerToken))
                {
                    continue;
                }

                var propertyToken = localName[(separatorIndex + 1)..];
                declarations |= propertyToken switch
                {
                    nameof(StyledElement.Resources) => RootOverlayCollections.Resources,
                    nameof(StyledElement.Styles) => RootOverlayCollections.Styles,
                    nameof(Control.DataTemplates) => RootOverlayCollections.DataTemplates,
                    _ => RootOverlayCollections.None
                };
            }

            return declarations;
        }
        catch
        {
            return RootOverlayCollections.None;
        }
    }

    private static HashSet<string> GetRecognizedRootOwnerNames(Type rootType)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        for (var current = rootType; current is not null && current != typeof(object); current = current.BaseType)
        {
            owners.Add(current.Name);
        }

        return owners;
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

    private static RuntimeXamlLoaderConfiguration CreatePreviewConfiguration(RuntimeXamlLoaderConfiguration configuration)
    {
        return new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = configuration.LocalAssembly,
            UseCompiledBindingsByDefault = configuration.UseCompiledBindingsByDefault,
            // VS Code preview must materialize design-only constructs such as Design.PreviewWith.
            DesignMode = true,
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
