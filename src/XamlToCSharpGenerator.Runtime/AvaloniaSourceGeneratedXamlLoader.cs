using System;
using System.IO;
using System.Reflection;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class AvaloniaSourceGeneratedXamlLoader
{
    private static readonly object Sync = new();
    private static SourceGenRuntimeXamlCompilationOptions _runtimeCompilationOptions = new();

    public static bool IsEnabled { get; private set; }

    public static SourceGenRuntimeXamlCompilationOptions RuntimeCompilationOptions
    {
        get
        {
            lock (Sync)
            {
                return _runtimeCompilationOptions.Clone();
            }
        }
    }

    public static void Enable()
    {
        IsEnabled = true;
        SourceGenRuntimeXamlLoaderBridge.EnsureRegistered();
    }

    public static void ConfigureRuntimeCompilation(Action<SourceGenRuntimeXamlCompilationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        lock (Sync)
        {
            var clone = _runtimeCompilationOptions.Clone();
            configure(clone);
            _runtimeCompilationOptions = clone;
        }
    }

    public static bool TryLoad(IServiceProvider? serviceProvider, Uri uri, out object? value)
    {
        if (uri is null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        return XamlSourceGenRegistry.TryCreate(serviceProvider, uri.ToString(), out value);
    }

    public static object Load(RuntimeXamlLoaderDocument document, RuntimeXamlLoaderConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        configuration ??= new RuntimeXamlLoaderConfiguration();

        if (document.BaseUri is not null &&
            TryLoad(document.ServiceProvider, document.BaseUri, out var registeredValue) &&
            registeredValue is not null)
        {
            return registeredValue;
        }

        var options = RuntimeCompilationOptions;
        if (!options.EnableRuntimeCompilationFallback)
        {
            var missingUri = document.BaseUri?.ToString() ?? "<inline>";
            throw new XamlLoadException(
                "No source-generated runtime artifact found for '" + missingUri +
                "'. Enable runtime fallback with UseAvaloniaSourceGeneratedRuntimeXamlCompilation(enable: true).");
        }

        return SourceGenRuntimeXamlCompiler.Load(document, configuration, options);
    }

    public static object Load(
        string xaml,
        Assembly? localAssembly = null,
        object? rootInstance = null,
        Uri? baseUri = null,
        bool designMode = false)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            DesignMode = designMode
        };

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
        var document = new RuntimeXamlLoaderDocument(baseUri, rootInstance, stream);
        return Load(document, configuration);
    }
}
