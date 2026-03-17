using System.Reflection;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Runtime;
using global::Avalonia;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class PreviewRootDataContextHydrator
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace XamlNamespace = Xaml2006Namespace;

    public static bool TryHydrate(object? rootObject, string xamlText, Assembly? localAssembly)
    {
        if (rootObject is not StyledElement styledRoot ||
            styledRoot.IsSet(StyledElement.DataContextProperty) ||
            string.IsNullOrWhiteSpace(xamlText) ||
            localAssembly is null ||
            !TryResolveRootDataType(xamlText, localAssembly, out var dataType) ||
            dataType is null ||
            !TryCreateDataContextInstance(dataType, out var dataContext))
        {
            return false;
        }

        styledRoot.DataContext = dataContext;
        return true;
    }

    private static bool TryResolveRootDataType(string xamlText, Assembly localAssembly, out Type? dataType)
    {
        dataType = null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xamlText, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return false;
        }

        var root = document.Root;
        if (root is null)
        {
            return false;
        }

        var dataTypeAttribute = root.Attribute(XamlNamespace + "DataType");
        if (dataTypeAttribute is null)
        {
            return false;
        }

        if (!PreviewTypeExpressionParser.TryExtractTypeToken(dataTypeAttribute.Value, out var rawTypeToken))
        {
            return false;
        }

        dataType = ResolveTypeReference(root, rawTypeToken, localAssembly);
        return dataType is not null;
    }

    private static bool TryCreateDataContextInstance(Type dataType, out object? dataContext)
    {
        dataContext = null;

        if (dataType.IsAbstract ||
            dataType.IsInterface ||
            dataType.ContainsGenericParameters ||
            typeof(AvaloniaObject).IsAssignableFrom(dataType))
        {
            return false;
        }

        try
        {
            if (dataType.IsValueType)
            {
                dataContext = Activator.CreateInstance(dataType);
                return dataContext is not null;
            }

            var constructor = dataType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                Type.EmptyTypes,
                modifiers: null);
            if (constructor is null)
            {
                return false;
            }

            dataContext = constructor.Invoke(null);
            return dataContext is not null;
        }
        catch
        {
            dataContext = null;
            return false;
        }
    }

    private static Type? ResolveTypeReference(XElement element, string rawValue, Assembly localAssembly)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0 ||
            trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        SplitQualifiedName(trimmed, out var prefix, out var typeName);
        if (typeName.Length == 0)
        {
            return null;
        }

        XNamespace xmlNamespace;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            xmlNamespace = element.GetDefaultNamespace();
        }
        else
        {
            xmlNamespace = element.GetNamespaceOfPrefix(prefix!) ?? XNamespace.None;
        }

        if (SourceGenKnownTypeRegistry.TryResolve(xmlNamespace.NamespaceName, typeName, out var resolvedType))
        {
            return resolvedType;
        }

        if (TryResolveClrNamespaceType(xmlNamespace.NamespaceName, typeName, localAssembly, out resolvedType))
        {
            return resolvedType;
        }

        return typeName.Contains('.', StringComparison.Ordinal)
            ? ResolveClassType(localAssembly, typeName)
            : null;
    }

    private static bool TryResolveClrNamespaceType(
        string xmlNamespace,
        string typeName,
        Assembly localAssembly,
        out Type? resolvedType)
    {
        resolvedType = null;
        ParseClrNamespace(xmlNamespace, out var clrNamespace, out var assemblyName);
        if (string.IsNullOrWhiteSpace(clrNamespace))
        {
            return false;
        }

        var candidateFullName = clrNamespace + "." + typeName;
        foreach (var assembly in EnumerateCandidateAssemblies(localAssembly, assemblyName))
        {
            resolvedType = assembly.GetType(candidateFullName, throwOnError: false, ignoreCase: false);
            if (resolvedType is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Assembly> EnumerateCandidateAssemblies(Assembly localAssembly, string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var seenAssemblies = new HashSet<string>(StringComparer.Ordinal);
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var index = 0; index < loadedAssemblies.Length; index++)
            {
                var assembly = loadedAssemblies[index];
                if (AssemblyMatchesName(assembly, assemblyName) &&
                    seenAssemblies.Add(assembly.FullName ?? assemblyName))
                {
                    yield return assembly;
                }
            }

            if (TryLoadAssemblyByName(assemblyName, out var loadedAssembly) &&
                loadedAssembly is not null &&
                seenAssemblies.Add(loadedAssembly.FullName ?? assemblyName))
            {
                yield return loadedAssembly;
            }

            yield break;
        }

        yield return localAssembly;

        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < allAssemblies.Length; index++)
        {
            var assembly = allAssemblies[index];
            if (!ReferenceEquals(assembly, localAssembly))
            {
                yield return assembly;
            }
        }
    }

    private static bool AssemblyMatchesName(Assembly assembly, string requestedAssemblyName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(requestedAssemblyName);

        if (string.Equals(assembly.FullName, requestedAssemblyName, StringComparison.Ordinal))
        {
            return true;
        }

        var loadedName = assembly.GetName().Name;
        if (string.Equals(loadedName, requestedAssemblyName, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var requestedName = new AssemblyName(requestedAssemblyName).Name;
            return !string.IsNullOrWhiteSpace(requestedName) &&
                   string.Equals(loadedName, requestedName, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadAssemblyByName(string assemblyName, out Assembly? assembly)
    {
        assembly = null;

        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
            return assembly is not null;
        }
        catch
        {
            return false;
        }
    }

    private static Type? ResolveClassType(Assembly localAssembly, string fullTypeName)
    {
        var resolvedType = localAssembly.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            resolvedType = loadedAssemblies[index].GetType(fullTypeName, throwOnError: false, ignoreCase: false);
            if (resolvedType is not null)
            {
                return resolvedType;
            }
        }

        return null;
    }

    private static void ParseClrNamespace(string namespaceUri, out string? clrNamespace, out string? assemblyName)
    {
        clrNamespace = null;
        assemblyName = null;

        if (namespaceUri.StartsWith("using:", StringComparison.Ordinal))
        {
            clrNamespace = namespaceUri["using:".Length..].Trim();
            return;
        }

        if (!namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return;
        }

        var payload = namespaceUri["clr-namespace:".Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        foreach (var segment in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith("assembly=", StringComparison.OrdinalIgnoreCase))
            {
                assemblyName = segment["assembly=".Length..].Trim();
            }
            else if (clrNamespace is null)
            {
                clrNamespace = segment;
            }
        }
    }

    private static void SplitQualifiedName(string rawValue, out string? prefix, out string localName)
    {
        prefix = null;
        localName = rawValue;

        var separatorIndex = rawValue.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return;
        }

        prefix = rawValue[..separatorIndex];
        localName = rawValue[(separatorIndex + 1)..];
    }
}
