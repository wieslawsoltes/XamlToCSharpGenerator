using System;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

internal static class XamlClrMemberSymbolResolver
{
    public static IPropertySymbol? ResolveInstanceProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol property &&
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null)
                {
                    return property;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null &&
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property;
                }
            }
        }

        return null;
    }

    public static IMethodSymbol? ResolveParameterlessMethod(INamedTypeSymbol? typeSymbol, string methodName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(methodName))
            {
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid)
                {
                    return method;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid &&
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    return method;
                }
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

        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.IsIndexer &&
                property.Parameters.Length > 0 &&
                property.Type is not null)
            {
                return property.Type;
            }
        }

        foreach (var candidate in namedType.AllInterfaces)
        {
            if (candidate is { Name: "IList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IReadOnlyList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IEnumerable", TypeArguments.Length: 1 })
            {
                return candidate.TypeArguments[0];
            }
        }

        return null;
    }
}
