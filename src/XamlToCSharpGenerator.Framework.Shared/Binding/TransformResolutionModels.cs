using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct TypeAliasKey(string XmlNamespace, string XamlTypeName);

public readonly record struct XmlnsDefinitionTarget(
    string XmlNamespace,
    string ClrNamespace,
    string? AssemblyName,
    bool IsFrameworkAttribute);

public sealed record ResolvedPropertyAliasRule(
    string TargetTypeName,
    INamedTypeSymbol? TargetTypeSymbol,
    string XamlPropertyName,
    string? ClrPropertyName,
    string? FrameworkId,
    string? PropertyOwnerTypeName,
    INamedTypeSymbol? PropertyOwnerTypeSymbol,
    string? PropertyFieldName);

public readonly record struct PropertyAliasResolution(
    string ResolvedPropertyName,
    string? FrameworkId = null,
    string? FrameworkPropertyOwnerTypeName = null,
    INamedTypeSymbol? FrameworkPropertyOwnerTypeSymbol = null,
    string? FrameworkPropertyFieldName = null)
{
    public bool HasFrameworkPropertyAlias(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal) &&
               (!string.IsNullOrWhiteSpace(FrameworkPropertyOwnerTypeName) ||
                FrameworkPropertyOwnerTypeSymbol is not null ||
                !string.IsNullOrWhiteSpace(FrameworkPropertyFieldName));
    }

    public string? GetFrameworkPropertyOwnerTypeName(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal)
            ? FrameworkPropertyOwnerTypeName
            : null;
    }

    public INamedTypeSymbol? GetFrameworkPropertyOwnerTypeSymbol(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal)
            ? FrameworkPropertyOwnerTypeSymbol
            : null;
    }

    public string? GetFrameworkPropertyFieldName(string frameworkId)
    {
        return string.Equals(FrameworkId, frameworkId, StringComparison.Ordinal)
            ? FrameworkPropertyFieldName
            : null;
    }
}

public sealed record ResolvedTransformExtensions(
    ImmutableDictionary<TypeAliasKey, INamedTypeSymbol> TypeAliases,
    ImmutableArray<ResolvedPropertyAliasRule> PropertyAliases)
{
    public static ResolvedTransformExtensions Empty { get; } = new(
        ImmutableDictionary<TypeAliasKey, INamedTypeSymbol>.Empty,
        ImmutableArray<ResolvedPropertyAliasRule>.Empty);
}
