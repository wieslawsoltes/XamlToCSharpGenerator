using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

internal static class CompilationTypeSymbolResolver
{
    public static INamedTypeSymbol? ResolveByFullTypeName(Compilation? compilation, string fullTypeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            var direct = assembly.GetTypeByMetadataName(fullTypeName);
            if (direct is not null)
            {
                return direct;
            }

            foreach (var candidate in EnumerateTypes(assembly.GlobalNamespace))
            {
                if (string.Equals(
                        candidate.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                        fullTypeName,
                        StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    public static INamedTypeSymbol? ResolveByNamespaceAndName(
        Compilation? compilation,
        string clrNamespace,
        string typeName)
    {
        if (compilation is null ||
            string.IsNullOrWhiteSpace(clrNamespace) ||
            string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            var namespaceSymbol = ResolveNamespace(assembly.GlobalNamespace, clrNamespace);
            if (namespaceSymbol is null)
            {
                continue;
            }

            var candidate = namespaceSymbol.GetTypeMembers(typeName)
                .OrderBy(static item => item.Arity)
                .FirstOrDefault();
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        yield return compilation.Assembly;

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols
                     .Where(static assembly => assembly is not null)
                     .OrderBy(static assembly => assembly.Identity.Name, StringComparer.Ordinal))
        {
            yield return assembly;
        }
    }

    private static INamespaceSymbol? ResolveNamespace(INamespaceSymbol root, string clrNamespace)
    {
        var current = root;
        foreach (var segment in clrNamespace.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = current.GetNamespaceMembers()
                .FirstOrDefault(candidate => string.Equals(candidate.Name, segment, StringComparison.Ordinal));
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol @namespace)
    {
        foreach (var namespaceMember in @namespace.GetNamespaceMembers())
        {
            foreach (var nested in EnumerateTypes(namespaceMember))
            {
                yield return nested;
            }
        }

        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in EnumerateNestedTypes(type))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;

            foreach (var deepNested in EnumerateNestedTypes(nested))
            {
                yield return deepNested;
            }
        }
    }
}
