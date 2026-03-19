using System.Reflection;
using System.Runtime.Loader;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewHostAssemblyResolution
{
    private static readonly object Sync = new();
    private static bool _installed;
    private static PreviewHostAssemblyResolver? _resolver;

    public static void Configure(PreviewHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (Sync)
        {
            _resolver = new PreviewHostAssemblyResolver(options.SourceAssemblyPath, options.HostAssemblyPath);
            EnsureInstalledNoLock();
        }
    }

    private static void EnsureInstalledNoLock()
    {
        if (_installed)
        {
            return;
        }

        AssemblyLoadContext.Default.Resolving += ResolveManagedAssembly;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveManagedAssembly;
        _installed = true;
    }

    private static Assembly? ResolveManagedAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        PreviewHostAssemblyResolver? resolver;
        lock (Sync)
        {
            resolver = _resolver;
        }

        return resolver?.Resolve(context, assemblyName);
    }

    private static Assembly? ResolveManagedAssembly(object? sender, ResolveEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Name))
        {
            return null;
        }

        AssemblyName assemblyName;
        try
        {
            assemblyName = new AssemblyName(args.Name);
        }
        catch
        {
            return null;
        }

        return ResolveManagedAssembly(AssemblyLoadContext.Default, assemblyName);
    }
}

internal sealed class PreviewHostAssemblyResolver
{
    private readonly AssemblyDependencyResolver[] _dependencyResolvers;
    private readonly string[] _assemblyDirectories;

    public PreviewHostAssemblyResolver(string? sourceAssemblyPath, string? hostAssemblyPath)
    {
        List<AssemblyDependencyResolver> dependencyResolvers = [];
        HashSet<string> directories = new(StringComparer.Ordinal);

        AddAssemblyLocation(sourceAssemblyPath, dependencyResolvers, directories);
        AddAssemblyLocation(hostAssemblyPath, dependencyResolvers, directories);

        _dependencyResolvers = [.. dependencyResolvers];
        _assemblyDirectories = [.. directories];
    }

    public Assembly? Resolve(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(assemblyName);

        string? assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        if (TryGetLoadedAssembly(assemblySimpleName, out Assembly? loadedAssembly))
        {
            return loadedAssembly;
        }

        string? assemblyPath = ResolveAssemblyPath(assemblyName);
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return null;
        }

        try
        {
            return context.LoadFromAssemblyPath(assemblyPath);
        }
        catch
        {
            return null;
        }
    }

    internal string? ResolveAssemblyPath(AssemblyName assemblyName)
    {
        ArgumentNullException.ThrowIfNull(assemblyName);

        for (var index = 0; index < _dependencyResolvers.Length; index += 1)
        {
            string? fromDependencyResolver = _dependencyResolvers[index].ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(fromDependencyResolver) &&
                File.Exists(fromDependencyResolver))
            {
                return Path.GetFullPath(fromDependencyResolver);
            }
        }

        string? assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        for (var index = 0; index < _assemblyDirectories.Length; index += 1)
        {
            string assemblyDirectory = _assemblyDirectories[index];
            string dllCandidate = Path.Combine(assemblyDirectory, assemblySimpleName + ".dll");
            if (File.Exists(dllCandidate))
            {
                return dllCandidate;
            }

            string exeCandidate = Path.Combine(assemblyDirectory, assemblySimpleName + ".exe");
            if (File.Exists(exeCandidate))
            {
                return exeCandidate;
            }
        }

        return null;
    }

    private static void AddAssemblyLocation(
        string? assemblyPath,
        List<AssemblyDependencyResolver> dependencyResolvers,
        HashSet<string> directories)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return;
        }

        string normalizedPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(normalizedPath))
        {
            return;
        }

        string? assemblyDirectory = Path.GetDirectoryName(normalizedPath);
        if (!string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            directories.Add(assemblyDirectory);
        }

        try
        {
            dependencyResolvers.Add(new AssemblyDependencyResolver(normalizedPath));
        }
        catch
        {
            // Best effort only; plain directory probing still applies.
        }
    }

    private static bool TryGetLoadedAssembly(string assemblySimpleName, out Assembly? assembly)
    {
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            Assembly candidate = loadedAssemblies[index];
            if (string.Equals(candidate.GetName().Name, assemblySimpleName, StringComparison.Ordinal))
            {
                assembly = candidate;
                return true;
            }
        }

        assembly = null;
        return false;
    }
}
