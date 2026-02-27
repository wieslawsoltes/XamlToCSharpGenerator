using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class AvaloniaSourceGeneratedXamlLoader
{
    private static readonly object Sync = new();
    private static readonly HashSet<string> AttemptedAssemblyLoads = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AttemptedModuleInitializers = new(StringComparer.OrdinalIgnoreCase);
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
            EnsureAssemblyLoadedForUriCandidate(serviceProvider, lookupUris[index]);
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

        var candidates = new List<string>(capacity: 3)
        {
            directCandidate
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            directCandidate
        };

        if (TryResolveUriAgainstContextBase(serviceProvider, uri, out var resolvedCandidate) &&
            seen.Add(resolvedCandidate))
        {
            candidates.Insert(0, resolvedCandidate);
        }

        if (TryResolveUriAgainstRootObjectAssembly(serviceProvider, uri, out var assemblyResolvedCandidate) &&
            seen.Add(assemblyResolvedCandidate))
        {
            candidates.Add(assemblyResolvedCandidate);
        }

        return candidates.ToArray();
    }

    private static bool TryResolveUriAgainstContextBase(
        IServiceProvider? serviceProvider,
        Uri relativeUri,
        out string resolvedCandidate)
    {
        resolvedCandidate = string.Empty;
        if (serviceProvider?.GetService(typeof(IUriContext)) is not IUriContext uriContext ||
            uriContext.BaseUri is null ||
            !uriContext.BaseUri.IsAbsoluteUri)
        {
            return false;
        }

        try
        {
            resolvedCandidate = new Uri(uriContext.BaseUri, relativeUri).ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveUriAgainstRootObjectAssembly(
        IServiceProvider? serviceProvider,
        Uri relativeUri,
        out string resolvedCandidate)
    {
        resolvedCandidate = string.Empty;
        if (!relativeUri.IsAbsoluteUri &&
            serviceProvider?.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider rootObjectProvider &&
            rootObjectProvider.RootObject is object rootObject)
        {
            var assemblyName = rootObject.GetType().Assembly.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                var normalizedPath = relativeUri.OriginalString.Replace('\\', '/').TrimStart('/');
                resolvedCandidate = normalizedPath.Length == 0
                    ? "avares://" + assemblyName + "/"
                    : "avares://" + assemblyName + "/" + normalizedPath;
                return true;
            }
        }

        return false;
    }

    private static void EnsureAssemblyLoadedForUriCandidate(IServiceProvider? serviceProvider, string uriCandidate)
    {
        if (!Uri.TryCreate(uriCandidate, UriKind.Absolute, out var parsedUri) ||
            !string.Equals(parsedUri.Scheme, "avares", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var assemblyName = parsedUri.Host;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return;
        }

        if (TryGetLoadedAssemblyDescriptor(assemblyName, out var assemblyKey, out var moduleHandle))
        {
            EnsureModuleInitializerExecuted(assemblyKey, moduleHandle);
            return;
        }

        lock (Sync)
        {
            if (!AttemptedAssemblyLoads.Add(assemblyName))
            {
                return;
            }
        }

        try
        {
            AppDomain.CurrentDomain.Load(assemblyName);
        }
        catch
        {
            // Keep lookup non-fatal. If loading fails, try a full assembly identity from known references.
        }

        if (TryGetLoadedAssemblyDescriptor(assemblyName, out assemblyKey, out moduleHandle))
        {
            EnsureModuleInitializerExecuted(assemblyKey, moduleHandle);
            return;
        }

        if (!TryResolveReferencedAssemblyFullName(serviceProvider, assemblyName, out var fullAssemblyName))
        {
            return;
        }

        try
        {
            AppDomain.CurrentDomain.Load(fullAssemblyName);
        }
        catch
        {
            // Keep lookup non-fatal. XamlSourceGenRegistry lookup remains the source of truth.
        }

        if (TryGetLoadedAssemblyDescriptor(assemblyName, out assemblyKey, out moduleHandle))
        {
            EnsureModuleInitializerExecuted(assemblyKey, moduleHandle);
        }
    }

    private static bool IsAssemblyLoaded(string assemblyName)
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var loadedName = loadedAssemblies[index].GetName().Name;
            if (string.Equals(loadedName, assemblyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetLoadedAssemblyDescriptor(
        string assemblyName,
        out string assemblyKey,
        out ModuleHandle moduleHandle)
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var candidate = loadedAssemblies[index];
            var loadedName = candidate.GetName().Name;
            if (string.Equals(loadedName, assemblyName, StringComparison.Ordinal))
            {
                assemblyKey = candidate.FullName ?? loadedName ?? string.Empty;
                moduleHandle = candidate.ManifestModule.ModuleHandle;
                return true;
            }
        }

        assemblyKey = string.Empty;
        moduleHandle = default;
        return false;
    }

    private static void EnsureModuleInitializerExecuted(string assemblyKey, ModuleHandle moduleHandle)
    {
        if (string.IsNullOrWhiteSpace(assemblyKey))
        {
            return;
        }

        lock (Sync)
        {
            if (!AttemptedModuleInitializers.Add(assemblyKey))
            {
                return;
            }
        }

        try
        {
            RuntimeHelpers.RunModuleConstructor(moduleHandle);
        }
        catch
        {
            // Keep lookup non-fatal. Registry lookup remains the source of truth.
        }
    }

    private static bool TryResolveReferencedAssemblyFullName(
        IServiceProvider? serviceProvider,
        string assemblyName,
        out string fullAssemblyName)
    {
        fullAssemblyName = string.Empty;

        if (serviceProvider?.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider rootObjectProvider &&
            rootObjectProvider.RootObject is object rootObject)
        {
            var rootReferencedAssemblies = rootObject.GetType().Assembly.GetReferencedAssemblies();
            for (var index = 0; index < rootReferencedAssemblies.Length; index++)
            {
                var referencedAssembly = rootReferencedAssemblies[index];
                if (!string.Equals(referencedAssembly.Name, assemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(referencedAssembly.FullName))
                {
                    fullAssemblyName = referencedAssembly.FullName!;
                    return true;
                }
            }
        }

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var referencedAssemblies = loadedAssemblies[index].GetReferencedAssemblies();
            for (var referencedIndex = 0; referencedIndex < referencedAssemblies.Length; referencedIndex++)
            {
                var referencedAssembly = referencedAssemblies[referencedIndex];
                if (!string.Equals(referencedAssembly.Name, assemblyName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(referencedAssembly.FullName))
                {
                    fullAssemblyName = referencedAssembly.FullName!;
                    return true;
                }
            }
        }

        return false;
    }

}
