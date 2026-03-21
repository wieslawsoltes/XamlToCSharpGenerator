using System;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

internal static class XamlClrMemberSymbolResolver
{
    public static IPropertySymbol? ResolveInstanceProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        return ResolveProperty(typeSymbol, propertyName, isStatic: false);
    }

    public static IPropertySymbol? ResolveStaticProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        return ResolveProperty(typeSymbol, propertyName, isStatic: true);
    }

    public static IFieldSymbol? ResolveInstanceField(INamedTypeSymbol? typeSymbol, string fieldName)
    {
        return ResolveField(typeSymbol, fieldName, isStatic: false);
    }

    public static IFieldSymbol? ResolveStaticField(INamedTypeSymbol? typeSymbol, string fieldName)
    {
        return ResolveField(typeSymbol, fieldName, isStatic: true);
    }

    public static IMethodSymbol? ResolveParameterlessMethod(INamedTypeSymbol? typeSymbol, string methodName)
    {
        return ResolveMethod(typeSymbol, methodName, isStatic: false, parameterCount: 0, allowVoidReturn: false);
    }

    public static IMethodSymbol? ResolveMethod(
        INamedTypeSymbol? typeSymbol,
        string methodName,
        bool isStatic,
        int? parameterCount,
        bool allowVoidReturn)
    {
        if (typeSymbol is null || string.IsNullOrWhiteSpace(methodName))
        {
            return null;
        }

        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var member in current.GetMembers(methodName))
            {
                if (member is IMethodSymbol method &&
                    MatchesMethod(method, methodName, isStatic, parameterCount, allowVoidReturn))
                {
                    return method;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IMethodSymbol method &&
                    MatchesMethod(method, methodName, isStatic, parameterCount, allowVoidReturn, allowCaseInsensitiveName: true))
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

    private static IPropertySymbol? ResolveProperty(
        INamedTypeSymbol? typeSymbol,
        string propertyName,
        bool isStatic)
    {
        if (typeSymbol is null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol property &&
                    MatchesProperty(property, propertyName, isStatic))
                {
                    return property;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    MatchesProperty(property, propertyName, isStatic, allowCaseInsensitiveName: true))
                {
                    return property;
                }
            }
        }

        return null;
    }

    private static IFieldSymbol? ResolveField(
        INamedTypeSymbol? typeSymbol,
        string fieldName,
        bool isStatic)
    {
        if (typeSymbol is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        foreach (var current in EnumerateTypeHierarchy(typeSymbol))
        {
            foreach (var member in current.GetMembers(fieldName))
            {
                if (member is IFieldSymbol field &&
                    MatchesField(field, fieldName, isStatic))
                {
                    return field;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IFieldSymbol field &&
                    MatchesField(field, fieldName, isStatic, allowCaseInsensitiveName: true))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private static bool MatchesProperty(
        IPropertySymbol property,
        string propertyName,
        bool isStatic,
        bool allowCaseInsensitiveName = false)
    {
        return property.IsStatic == isStatic &&
               !property.IsIndexer &&
               property.GetMethod is not null &&
               (!allowCaseInsensitiveName || string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesField(
        IFieldSymbol field,
        string fieldName,
        bool isStatic,
        bool allowCaseInsensitiveName = false)
    {
        return field.IsStatic == isStatic &&
               !field.IsImplicitlyDeclared &&
               (!allowCaseInsensitiveName || string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesMethod(
        IMethodSymbol method,
        string methodName,
        bool isStatic,
        int? parameterCount,
        bool allowVoidReturn,
        bool allowCaseInsensitiveName = false)
    {
        return method.IsStatic == isStatic &&
               !method.IsImplicitlyDeclared &&
               method.MethodKind == MethodKind.Ordinary &&
               (allowVoidReturn || !method.ReturnsVoid) &&
               (parameterCount is null || method.Parameters.Length == parameterCount.Value) &&
               (!allowCaseInsensitiveName || string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase));
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            yield return interfaceType;
        }
    }
}
