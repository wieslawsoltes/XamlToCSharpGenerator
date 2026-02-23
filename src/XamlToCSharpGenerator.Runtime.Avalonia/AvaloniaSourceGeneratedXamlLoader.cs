using System;
using System.IO;
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
        EnsureLocalAssemblyFromDocumentBaseUri(document, configuration);

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
        Type? localAssemblyAnchorType = null,
        string? localAssemblyName = null,
        object? rootInstance = null,
        Uri? baseUri = null,
        bool designMode = false)
    {
        ArgumentNullException.ThrowIfNull(xaml);

        var localAssembly = localAssemblyAnchorType?.Assembly ?? rootInstance?.GetType().Assembly;
        if (localAssembly is null &&
            !string.IsNullOrWhiteSpace(localAssemblyName))
        {
            var anchorType = ResolveAssemblyAnchorType(localAssemblyName.Trim());
            if (anchorType is not null)
            {
                localAssembly = anchorType.Assembly;
            }
        }

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            DesignMode = designMode
        };

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xaml));
        var document = new RuntimeXamlLoaderDocument(baseUri, rootInstance, stream);
        return Load(document, configuration);
    }

    private static void EnsureLocalAssemblyFromDocumentBaseUri(
        RuntimeXamlLoaderDocument document,
        RuntimeXamlLoaderConfiguration configuration)
    {
        if (configuration.LocalAssembly is not null ||
            document.BaseUri is null ||
            !document.BaseUri.IsAbsoluteUri ||
            !string.Equals(document.BaseUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var assemblyName = document.BaseUri.Host;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return;
        }

        var anchorType = ResolveAssemblyAnchorType(assemblyName);
        if (anchorType is not null)
        {
            configuration.LocalAssembly = anchorType.Assembly;
        }
    }

    private static Type? ResolveAssemblyAnchorType(string assemblyName)
    {
        var registeredTypes = SourceGenKnownTypeRegistry.GetRegisteredTypes();
        for (var index = 0; index < registeredTypes.Count; index++)
        {
            var candidate = registeredTypes[index];
            if (string.Equals(
                    candidate.Assembly.GetName().Name,
                    assemblyName,
                    StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
