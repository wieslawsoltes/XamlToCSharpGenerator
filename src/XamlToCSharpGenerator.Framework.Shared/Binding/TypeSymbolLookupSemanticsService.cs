using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public static class TypeSymbolLookupSemanticsService
{
    public static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (AreEquivalentTypesIgnoringNullable(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is not INamedTypeSymbol sourceNamed)
        {
            return false;
        }

        for (INamedTypeSymbol? current = sourceNamed; current is not null; current = current.BaseType)
        {
            if (AreEquivalentTypesIgnoringNullable(current, targetType))
            {
                return true;
            }
        }

        foreach (var implementedInterface in sourceNamed.AllInterfaces)
        {
            if (AreEquivalentTypesIgnoringNullable(implementedInterface, targetType))
            {
                return true;
            }
        }

        return false;
    }

    public static bool AreEquivalentTypesIgnoringNullable(ITypeSymbol left, ITypeSymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
               SymbolEqualityComparer.Default.Equals(
                   left.WithNullableAnnotation(NullableAnnotation.None),
                   right.WithNullableAnnotation(NullableAnnotation.None));
    }

    public static IEnumerable<INamedTypeSymbol> EnumerateInstanceMemberLookupTypes(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            var pending = new Stack<INamedTypeSymbol>();
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            pending.Push(type);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;

                for (var index = current.Interfaces.Length - 1; index >= 0; index--)
                {
                    pending.Push(current.Interfaces[index]);
                }
            }

            yield break;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    public static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    public static IPropertySymbol? FindProperty(INamedTypeSymbol type, string propertyName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    public static IPropertySymbol? FindPublicProperty(INamedTypeSymbol type, string propertyName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var property in current.GetMembers(propertyName).OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }
        }

        return null;
    }

    public static IMethodSymbol? FindParameterlessMethod(INamedTypeSymbol type, string methodName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary &&
                member.Parameters.Length == 0);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    public static IMethodSymbol? FindAccessibleParameterlessMethod(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol type,
        string methodName,
        out bool foundInaccessibleMethod)
    {
        foundInaccessibleMethod = false;

        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var member in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (member.IsStatic ||
                    member.MethodKind != MethodKind.Ordinary ||
                    member.Parameters.Length != 0)
                {
                    continue;
                }

                if (!compilation.IsSymbolAccessibleWithin(member, accessibilityWithin, type))
                {
                    foundInaccessibleMethod = true;
                    continue;
                }

                return member;
            }
        }

        return null;
    }

    public static IMethodSymbol? FindAttachedPropertyGetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol targetType)
    {
        var getterName = "Get" + propertyName;
        foreach (var method in ownerType.GetMembers(getterName).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic ||
                method.MethodKind != MethodKind.Ordinary ||
                method.Parameters.Length != 1)
            {
                continue;
            }

            if (IsTypeAssignableTo(targetType, method.Parameters[0].Type))
            {
                return method;
            }
        }

        return null;
    }

    public static IPropertySymbol? FindAccessibleProperty(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol type,
        string propertyName,
        out bool foundInaccessibleProperty)
    {
        foundInaccessibleProperty = false;

        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var property in current.GetMembers(propertyName).OfType<IPropertySymbol>())
            {
                if (!compilation.IsSymbolAccessibleWithin(property, accessibilityWithin, type))
                {
                    foundInaccessibleProperty = true;
                    continue;
                }

                return property;
            }
        }

        return null;
    }

    public static ISymbol GetGeneratedCodeAccessibilityWithinSymbol(
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (document.IsClassBacked &&
            !string.IsNullOrWhiteSpace(document.ClassFullName))
        {
            var classSymbol = compilation.GetTypeByMetadataName(document.ClassFullName!);
            if (classSymbol is not null)
            {
                return classSymbol;
            }
        }

        return compilation.Assembly;
    }
}
