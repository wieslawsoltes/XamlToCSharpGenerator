using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class AvaloniaSourceGeneratedXamlLoader
{
    private const string TraceEnvVarName = "AXSG_HOTRELOAD_TRACE";
    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, bool> AttemptedAssemblyLoads = new(StringComparer.Ordinal);
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

        var lookupUris = BuildLookupUriCandidates(serviceProvider, uri);
        for (var index = 0; index < lookupUris.Length; index++)
        {
            EnsureAssemblyLoadedForUri(lookupUris[index]);
            if (XamlSourceGenRegistry.TryCreate(serviceProvider, lookupUris[index], out value))
            {
                return true;
            }
        }

        value = null;
        return false;
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

    private static string[] BuildLookupUriCandidates(IServiceProvider? serviceProvider, Uri uri)
    {
        var directCandidate = uri.ToString();
        if (uri.IsAbsoluteUri)
        {
            return [directCandidate];
        }

        if (serviceProvider?.GetService(typeof(IUriContext)) is not IUriContext uriContext ||
            uriContext.BaseUri is null ||
            !uriContext.BaseUri.IsAbsoluteUri)
        {
            return [directCandidate];
        }

        Uri resolvedUri;
        try
        {
            resolvedUri = new Uri(uriContext.BaseUri, uri);
        }
        catch
        {
            return [directCandidate];
        }

        var resolvedCandidate = resolvedUri.ToString();
        if (string.Equals(resolvedCandidate, directCandidate, StringComparison.OrdinalIgnoreCase))
        {
            return [directCandidate];
        }

        return [resolvedCandidate, directCandidate];
    }

    private static void EnsureAssemblyLoadedForUri(string candidateUri)
    {
        if (!Uri.TryCreate(candidateUri, UriKind.Absolute, out var parsedUri) ||
            !parsedUri.IsAbsoluteUri ||
            !string.Equals(parsedUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var assemblyName = parsedUri.Host;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return;
        }

        if (!AttemptedAssemblyLoads.TryAdd(assemblyName, true))
        {
            return;
        }

        if (IsAssemblyKnown(assemblyName))
        {
            Trace("Known types are already present for assembly '" + assemblyName + "'; ensuring module initializers have executed.");
        }

        Assembly? assembly = FindLoadedAssembly(assemblyName);
        try
        {
            assembly ??= Assembly.Load(new AssemblyName(assemblyName));
            Trace("Loaded assembly '" + assemblyName + "' for source-gen URI lookup.");
        }
        catch (Exception ex)
        {
            // Keep source-generated load path non-fatal; fallback remains available.
            Trace("Failed to load assembly '" + assemblyName + "' for source-gen URI lookup: " + ex.Message);
            return;
        }

        try
        {
            RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            Trace("Executed module initializers for assembly '" + assemblyName + "'.");
        }
        catch (Exception ex)
        {
            // Keep source-generated load path non-fatal; fallback remains available.
            Trace("Failed to execute module initializers for assembly '" + assemblyName + "': " + ex.Message);
            return;
        }

        if (!IsAssemblyKnown(assemblyName))
        {
            Trace("Assembly '" + assemblyName + "' loaded, but no source-gen known types were registered.");
        }
    }

    private static bool IsAssemblyKnown(string assemblyName)
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
                return true;
            }
        }

        return false;
    }

    private static Assembly? FindLoadedAssembly(string assemblyName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < assemblies.Length; index++)
        {
            var candidate = assemblies[index];
            if (string.Equals(candidate.GetName().Name, assemblyName, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void Trace(string message)
    {
        if (!IsTraceEnabled())
        {
            return;
        }

        try
        {
            Console.WriteLine("[AXSG.HotReload] " + message);
        }
        catch
        {
        }
    }

    private static bool IsTraceEnabled()
    {
        var value = Environment.GetEnvironmentVariable(TraceEnvVarName);
        return !string.IsNullOrWhiteSpace(value) &&
               (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "on", StringComparison.OrdinalIgnoreCase));
    }
}
