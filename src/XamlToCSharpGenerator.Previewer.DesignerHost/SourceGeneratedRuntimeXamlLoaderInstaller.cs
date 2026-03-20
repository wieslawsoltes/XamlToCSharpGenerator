using System.Linq;
using System.Reflection;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedRuntimeXamlLoaderInstaller
{
    public static void Install()
        => Install(PreviewCompilerMode.SourceGenerated, null, null);

    public static void Install(
        PreviewCompilerMode compilerMode,
        double? previewWidth,
        double? previewHeight)
    {
        PreviewSizingRootDecorator.Configure(previewWidth, previewHeight);

        var loaderContractType = typeof(AvaloniaXamlLoader).Assembly.GetType(
            "Avalonia.Markup.Xaml.AvaloniaXamlLoader+IRuntimeXamlLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia runtime XAML loader contract was not found.");
        Func<RuntimeXamlLoaderDocument, RuntimeXamlLoaderConfiguration, object> loadHandler = compilerMode == PreviewCompilerMode.SourceGenerated
            ? new SourceGeneratedRuntimeXamlLoader().Load
            : AvaloniaRuntimeXamlLoader.Load;

        var proxy = RuntimeXamlLoaderProxyFactory.Create(
            loaderContractType,
            (document, configuration) =>
            {
                var xamlText = SourceGeneratedRuntimeXamlLoader.ReadXamlText(document);
                var preparedDocument = PreparePreviewDocument(document, xamlText, configuration, out var preparedConfiguration);
                var decorated = PreviewSizingRootDecorator.Apply(
                    loadHandler(preparedDocument, preparedConfiguration),
                    xamlText);
                UpdatePreviewHotDesignDocument(decorated, xamlText, preparedConfiguration.LocalAssembly);
                return decorated;
            });
        var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base", throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia locator type was not found.");
        var currentMutableProperty = locatorType.GetProperty(
            "CurrentMutable",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AvaloniaLocator.CurrentMutable was not found.");
        var currentMutable = currentMutableProperty.GetValue(null)
            ?? throw new InvalidOperationException("AvaloniaLocator.CurrentMutable returned null.");
        var bindMethod = currentMutable.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Bind" &&
                                      method.IsGenericMethodDefinition &&
                                      method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("AvaloniaLocator.Bind<T>() was not found.");
        var binding = bindMethod.MakeGenericMethod(loaderContractType).Invoke(currentMutable, null)
            ?? throw new InvalidOperationException("Failed to create Avalonia locator binding.");
        var toConstantMethod = binding.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "ToConstant" &&
                                      method.GetParameters().Length == 1)
            ?? throw new InvalidOperationException("Avalonia locator binding does not expose ToConstant.");
        var closedToConstantMethod = toConstantMethod.IsGenericMethodDefinition
            ? toConstantMethod.MakeGenericMethod(proxy.GetType())
            : toConstantMethod;
        closedToConstantMethod.Invoke(binding, [proxy]);
    }

    internal static RuntimeXamlLoaderDocument PreparePreviewDocument(
        RuntimeXamlLoaderDocument document,
        string xamlText,
        RuntimeXamlLoaderConfiguration configuration,
        out RuntimeXamlLoaderConfiguration preparedConfiguration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);

        Assembly? localAssembly = ResolvePreviewLocalAssembly(configuration.LocalAssembly);
        preparedConfiguration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly,
            UseCompiledBindingsByDefault = ResolveUseCompiledBindingsByDefault(configuration.UseCompiledBindingsByDefault, localAssembly),
            DesignMode = configuration.DesignMode,
            CreateSourceInfo = configuration.CreateSourceInfo,
            DiagnosticHandler = configuration.DiagnosticHandler
        };

        Uri? baseUri = ResolvePreviewBaseUri(document.BaseUri, localAssembly, PreviewHostRuntimeState.Current.XamlFileProjectPath);
        if (Equals(baseUri, document.BaseUri) && ReferenceEquals(preparedConfiguration.LocalAssembly, configuration.LocalAssembly))
        {
            return document;
        }

        return new RuntimeXamlLoaderDocument(baseUri, document.RootInstance, xamlText)
        {
            Document = document.Document,
            ServiceProvider = document.ServiceProvider
        };
    }

    internal static Assembly? ResolvePreviewLocalAssembly(Assembly? localAssembly)
    {
        string? sourceAssemblyPath = PreviewHostRuntimeState.Current.SourceAssemblyPath;
        if (string.IsNullOrWhiteSpace(sourceAssemblyPath))
        {
            return localAssembly ?? Assembly.GetEntryAssembly();
        }

        string assemblyName = Path.GetFileNameWithoutExtension(sourceAssemblyPath);
        if (localAssembly is not null &&
            AssemblyMatchesPreviewSource(localAssembly, sourceAssemblyPath, assemblyName))
        {
            return localAssembly;
        }

        if (!string.IsNullOrWhiteSpace(assemblyName) &&
            TryResolveLoadedAssembly(sourceAssemblyPath, assemblyName, out Assembly? loadedAssembly))
        {
            return loadedAssembly;
        }

        try
        {
            return Assembly.LoadFrom(Path.GetFullPath(sourceAssemblyPath));
        }
        catch
        {
            return localAssembly ?? Assembly.GetEntryAssembly();
        }
    }

    internal static Uri? BuildPreviewBaseUri(Assembly? localAssembly, string? xamlFileProjectPath)
    {
        if (localAssembly is null || string.IsNullOrWhiteSpace(xamlFileProjectPath))
        {
            return null;
        }

        string assemblyName = localAssembly.GetName().Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return null;
        }

        string normalizedPath = xamlFileProjectPath.Replace('\\', '/').Trim();
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
        {
            normalizedPath = "/" + normalizedPath;
        }

        return new Uri("avares://" + assemblyName + normalizedPath, UriKind.Absolute);
    }

    private static Uri? ResolvePreviewBaseUri(Uri? baseUri, Assembly? localAssembly, string? xamlFileProjectPath)
    {
        Uri? previewBaseUri = BuildPreviewBaseUri(localAssembly, xamlFileProjectPath);
        if (previewBaseUri is null)
        {
            return baseUri;
        }

        if (baseUri is null ||
            !baseUri.IsAbsoluteUri ||
            !string.Equals(baseUri.Scheme, previewBaseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(baseUri.Host, previewBaseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return previewBaseUri;
        }

        return baseUri;
    }

    private static bool AssemblyMatchesPreviewSource(Assembly assembly, string sourceAssemblyPath, string sourceAssemblyName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        try
        {
            string? assemblyLocation = assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyLocation))
            {
                return !string.IsNullOrWhiteSpace(sourceAssemblyName) &&
                       string.Equals(assembly.GetName().Name, sourceAssemblyName, StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(
                Path.GetFullPath(assemblyLocation),
                Path.GetFullPath(sourceAssemblyPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveLoadedAssembly(string sourceAssemblyPath, string assemblyName, out Assembly? assembly)
    {
        foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                if (AssemblyMatchesPreviewSource(loadedAssembly, sourceAssemblyPath, assemblyName))
                {
                    assembly = loadedAssembly;
                    return true;
                }
            }
            catch
            {
                // Best effort match.
            }
        }

        assembly = null;
        return false;
    }

    private static bool ResolveUseCompiledBindingsByDefault(bool configuredValue, Assembly? localAssembly)
    {
        if (localAssembly is null)
        {
            return configuredValue;
        }

        string? metadataValue = localAssembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "AvaloniaUseCompiledBindingsByDefault", StringComparison.Ordinal))
            ?.Value;
        return bool.TryParse(metadataValue, out bool parsedValue)
            ? parsedValue
            : configuredValue;
    }

    private static void UpdatePreviewHotDesignDocument(object? root, string xamlText, Assembly? localAssembly)
    {
        PreviewHostOptions options = PreviewHostRuntimeState.Current;
        string? sourceFilePath = options.SourceFilePath;
        string? buildUri = BuildPreviewBaseUri(localAssembly, options.XamlFileProjectPath)?.ToString();
        global::XamlToCSharpGenerator.Runtime.AxsgPreviewHotDesignSessionBridge.UpdateCurrentDocument(
            root,
            xamlText,
            buildUri ?? sourceFilePath,
            sourceFilePath);
    }
}
