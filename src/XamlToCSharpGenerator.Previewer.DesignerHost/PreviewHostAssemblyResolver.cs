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
            _resolver = new PreviewHostAssemblyResolver(options.SourceAssemblyPath);
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
    private readonly AssemblyDependencyResolver? _dependencyResolver;
    private readonly string? _sourceAssemblyDirectory;

    public PreviewHostAssemblyResolver(string? sourceAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(sourceAssemblyPath))
        {
            return;
        }

        string normalizedPath = Path.GetFullPath(sourceAssemblyPath);
        if (!File.Exists(normalizedPath))
        {
            return;
        }

        _sourceAssemblyDirectory = Path.GetDirectoryName(normalizedPath);
        try
        {
            _dependencyResolver = new AssemblyDependencyResolver(normalizedPath);
        }
        catch
        {
            _dependencyResolver = null;
        }
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

        string? fromDependencyResolver = _dependencyResolver?.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(fromDependencyResolver) &&
            File.Exists(fromDependencyResolver))
        {
            return Path.GetFullPath(fromDependencyResolver);
        }

        string? assemblySimpleName = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(_sourceAssemblyDirectory) ||
            string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            return null;
        }

        string dllCandidate = Path.Combine(_sourceAssemblyDirectory, assemblySimpleName + ".dll");
        if (File.Exists(dllCandidate))
        {
            return dllCandidate;
        }

        string exeCandidate = Path.Combine(_sourceAssemblyDirectory, assemblySimpleName + ".exe");
        return File.Exists(exeCandidate)
            ? exeCandidate
            : null;
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
