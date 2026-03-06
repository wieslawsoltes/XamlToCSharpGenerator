using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

internal static class XamlClrMemberSymbolResolver
{
    public static IPropertySymbol? ResolveInstanceProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var exact = current.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null);
            if (exact is not null)
            {
                return exact;
            }

            var fallback = current.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property =>
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null &&
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null)
            {
                return fallback;
            }
        }

        return null;
    }

    public static IMethodSymbol? ResolveParameterlessMethod(INamedTypeSymbol? typeSymbol, string methodName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var exact = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method =>
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid);
            if (exact is not null)
            {
                return exact;
            }

            var fallback = current.GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid &&
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null)
            {
                return fallback;
            }
        }

        return null;
    }

    public static ITypeSymbol? ResolveIndexedElementType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var indexer = namedType.GetMembers().OfType<IPropertySymbol>()
            .FirstOrDefault(static property => property.IsIndexer && property.Parameters.Length > 0);
        if (indexer?.Type is not null)
        {
            return indexer.Type;
        }

        var listInterface = namedType.AllInterfaces
            .FirstOrDefault(static candidate =>
                candidate is { Name: "IList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IReadOnlyList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IEnumerable", TypeArguments.Length: 1 });
        return listInterface?.TypeArguments.FirstOrDefault();
    }
}
