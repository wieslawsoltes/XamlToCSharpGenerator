using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public static class SymbolConstructionSemanticsService
{
    public static bool RequiresObjectInitializer(IPropertySymbol property, ResolvedValueRequirements valueRequirements)
    {
        return property.SetMethod?.IsInitOnly == true ||
               valueRequirements.NeedsServiceProvider ||
               valueRequirements.NeedsProvideValueTarget ||
               valueRequirements.NeedsRootObject ||
               valueRequirements.NeedsBaseUri ||
               valueRequirements.NeedsParentStack;
    }

    public static bool HasPublicConstructorWithParameterTypes(INamedTypeSymbol type, params string[] parameterTypeNames)
    {
        return type.InstanceConstructors.Any(constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public &&
            !constructor.IsStatic &&
            ParameterTypesMatch(constructor.Parameters, parameterTypeNames));
    }

    public static bool HasPublicStaticMethod(
        INamedTypeSymbol type,
        string methodName,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames)
    {
        return type.GetMembers(methodName).OfType<IMethodSymbol>().Any(method =>
            method.IsStatic &&
            method.DeclaredAccessibility == Accessibility.Public &&
            TypeNameMatches(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), returnTypeName) &&
            ParameterTypesMatch(method.Parameters, parameterTypeNames));
    }

    public static bool TryFindPublicMethod(
        INamedTypeSymbol type,
        string methodName,
        bool isStatic,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames,
        out IMethodSymbol method)
    {
        method = type.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(candidate =>
            candidate.IsStatic == isStatic &&
            candidate.DeclaredAccessibility == Accessibility.Public &&
            TypeNameMatches(candidate.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), returnTypeName) &&
            ParameterTypesMatch(candidate.Parameters, parameterTypeNames))!;
        return method is not null;
    }

    public static bool HasPublicStaticProperty(
        INamedTypeSymbol type,
        string propertyName,
        string returnTypeName)
    {
        return type.GetMembers(propertyName).OfType<IPropertySymbol>().Any(property =>
            property.IsStatic &&
            property.DeclaredAccessibility == Accessibility.Public &&
            TypeNameMatches(property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), returnTypeName));
    }

    public static bool TypeNameMatches(string actualTypeName, string expectedTypeName)
    {
        return string.Equals(
                   NormalizeTypeNameForComparison(actualTypeName),
                   NormalizeTypeNameForComparison(expectedTypeName),
                   StringComparison.Ordinal) ||
               string.Equals(actualTypeName, expectedTypeName, StringComparison.Ordinal);
    }

    public static string NormalizeTypeNameForComparison(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        var normalized = typeName.Trim().Replace("global::", string.Empty);
        return normalized switch
        {
            "void" => "System.Void",
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "char" => "System.Char",
            "float" => "System.Single",
            "double" => "System.Double",
            "decimal" => "System.Decimal",
            "string" => "System.String",
            "object" => "System.Object",
            "nint" => "System.IntPtr",
            "nuint" => "System.UIntPtr",
            _ => normalized
        };
    }

    private static bool ParameterTypesMatch(ImmutableArray<IParameterSymbol> parameters, IReadOnlyList<string> parameterTypeNames)
    {
        if (parameters.Length != parameterTypeNames.Count)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            if (!TypeNameMatches(
                    parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    parameterTypeNames[index]))
            {
                return false;
            }
        }

        return true;
    }
}
