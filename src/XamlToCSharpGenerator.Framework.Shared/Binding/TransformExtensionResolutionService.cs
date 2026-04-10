using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TransformExtensionResolutionService
{
    private readonly Func<Compilation, IEnumerable<IAssemblySymbol>> _enumerateAssemblies;
    private readonly Func<string, string> _normalizeTypeToken;
    private readonly Func<string, string> _normalizePropertyName;

    public TransformExtensionResolutionService(
        Func<Compilation, IEnumerable<IAssemblySymbol>> enumerateAssemblies,
        Func<string, string> normalizeTypeToken,
        Func<string, string> normalizePropertyName)
    {
        _enumerateAssemblies = enumerateAssemblies ?? throw new ArgumentNullException(nameof(enumerateAssemblies));
        _normalizeTypeToken = normalizeTypeToken ?? throw new ArgumentNullException(nameof(normalizeTypeToken));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
    }

    public ResolvedTransformExtensions Resolve(
        Compilation compilation,
        XamlTransformConfiguration configuration,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        string frameworkId,
        string typeAliasAttributeMetadataName,
        string propertyAliasAttributeMetadataName,
        string frameworkPropertyAliasAttributeMetadataName,
        string compatibilityFrameworkPropertyAliasAttributeMetadataName)
    {
        var typeAliases = ImmutableDictionary.CreateBuilder<TypeAliasKey, INamedTypeSymbol>();
        var propertyAliases = ImmutableArray.CreateBuilder<ResolvedPropertyAliasRule>();

        foreach (var alias in configuration.TypeAliases)
        {
            TryAddConfiguredTypeAlias(compilation, alias, diagnostics, typeAliases);
        }

        foreach (var alias in configuration.PropertyAliases)
        {
            propertyAliases.Add(BuildConfiguredPropertyAlias(compilation, alias, frameworkId));
        }

        foreach (var assembly in _enumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                var attributeTypeName = attribute.AttributeClass?.ToDisplayString();
                if (string.Equals(attributeTypeName, typeAliasAttributeMetadataName, StringComparison.Ordinal))
                {
                    TryAddAttributeTypeAlias(compilation, attribute, diagnostics, typeAliases);
                    continue;
                }

                if (string.Equals(attributeTypeName, propertyAliasAttributeMetadataName, StringComparison.Ordinal))
                {
                    if (TryBuildPropertyAliasFromAttribute(compilation, attribute, frameworkId: null, out var propertyAlias))
                    {
                        propertyAliases.Add(propertyAlias);
                    }

                    continue;
                }

                if (string.Equals(attributeTypeName, frameworkPropertyAliasAttributeMetadataName, StringComparison.Ordinal))
                {
                    if (TryBuildPropertyAliasFromAttribute(compilation, attribute, frameworkId, out var propertyAlias))
                    {
                        propertyAliases.Add(propertyAlias);
                    }

                    continue;
                }

                if (string.Equals(attributeTypeName, compatibilityFrameworkPropertyAliasAttributeMetadataName, StringComparison.Ordinal))
                {
                    if (TryBuildCompatibilityPropertyAliasFromAttribute(compilation, attribute, frameworkId, out var propertyAlias))
                    {
                        propertyAliases.Add(propertyAlias);
                    }
                }
            }
        }

        return new ResolvedTransformExtensions(typeAliases.ToImmutable(), propertyAliases.ToImmutable());
    }

    private void TryAddConfiguredTypeAlias(
        Compilation compilation,
        XamlTypeAliasRule alias,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableDictionary<TypeAliasKey, INamedTypeSymbol>.Builder typeAliases)
    {
        var resolvedType = ResolveTypeByMetadataName(compilation, alias.ClrTypeName);
        if (resolvedType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0901",
                "Could not resolve transform type alias CLR type '" + alias.ClrTypeName + "'.",
                alias.Source,
                alias.Line,
                alias.Column,
                false));
            return;
        }

        typeAliases[new TypeAliasKey(alias.XmlNamespace.Trim(), alias.XamlTypeName.Trim())] = resolvedType;
    }

    private void TryAddAttributeTypeAlias(
        Compilation compilation,
        AttributeData attribute,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableDictionary<TypeAliasKey, INamedTypeSymbol>.Builder typeAliases)
    {
        if (attribute.ConstructorArguments.Length < 3 ||
            attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
            attribute.ConstructorArguments[1].Value is not string xamlTypeName ||
            attribute.ConstructorArguments[2].Value is not string clrTypeName)
        {
            return;
        }

        var resolvedType = ResolveTypeByMetadataName(compilation, clrTypeName);
        if (resolvedType is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0901",
                "Could not resolve transform type alias CLR type '" + clrTypeName + "'.",
                attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath ?? compilation.AssemblyName ?? "<assembly>",
                1,
                1,
                false));
            return;
        }

        typeAliases[new TypeAliasKey(xmlNamespace.Trim(), xamlTypeName.Trim())] = resolvedType;
    }

    private ResolvedPropertyAliasRule BuildConfiguredPropertyAlias(
        Compilation compilation,
        XamlPropertyAliasRule alias,
        string frameworkId)
    {
        var targetTypeSymbol = ResolveConfiguredAliasType(compilation, alias.TargetTypeName);
        var frameworkOwnerTypeName = alias.GetFrameworkPropertyOwnerTypeName(frameworkId);
        var frameworkOwnerTypeSymbol = ResolveConfiguredAliasType(compilation, frameworkOwnerTypeName);

        return new ResolvedPropertyAliasRule(
            alias.TargetTypeName,
            targetTypeSymbol,
            _normalizePropertyName(alias.XamlPropertyName),
            alias.ClrPropertyName,
            frameworkOwnerTypeName is null ? null : frameworkId,
            frameworkOwnerTypeName,
            frameworkOwnerTypeSymbol,
            alias.GetFrameworkPropertyFieldName(frameworkId));
    }

    private bool TryBuildPropertyAliasFromAttribute(
        Compilation compilation,
        AttributeData attribute,
        string? frameworkId,
        out ResolvedPropertyAliasRule alias)
    {
        alias = new ResolvedPropertyAliasRule(string.Empty, null, string.Empty, null, null, null, null, null);

        if (frameworkId is null)
        {
            if (attribute.ConstructorArguments.Length < 3 ||
                attribute.ConstructorArguments[0].Value is not string targetTypeName ||
                attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
                attribute.ConstructorArguments[2].Value is not string clrPropertyName)
            {
                return false;
            }

            alias = new ResolvedPropertyAliasRule(
                targetTypeName,
                ResolveConfiguredAliasType(compilation, targetTypeName),
                _normalizePropertyName(xamlPropertyName),
                clrPropertyName,
                null,
                null,
                null,
                null);
            return true;
        }

        if (attribute.ConstructorArguments.Length < 5 ||
            attribute.ConstructorArguments[0].Value is not string declaredFrameworkId ||
            !string.Equals(declaredFrameworkId, frameworkId, StringComparison.Ordinal) ||
            attribute.ConstructorArguments[1].Value is not string scopedTargetTypeName ||
            attribute.ConstructorArguments[2].Value is not string scopedXamlPropertyName ||
            attribute.ConstructorArguments[3].Value is not string propertyOwnerTypeName ||
            attribute.ConstructorArguments[4].Value is not string propertyFieldName)
        {
            return false;
        }

        alias = new ResolvedPropertyAliasRule(
            scopedTargetTypeName,
            ResolveConfiguredAliasType(compilation, scopedTargetTypeName),
            _normalizePropertyName(scopedXamlPropertyName),
            null,
            frameworkId,
            propertyOwnerTypeName,
            ResolveConfiguredAliasType(compilation, propertyOwnerTypeName),
            propertyFieldName);
        return true;
    }

    private bool TryBuildCompatibilityPropertyAliasFromAttribute(
        Compilation compilation,
        AttributeData attribute,
        string frameworkId,
        out ResolvedPropertyAliasRule alias)
    {
        alias = new ResolvedPropertyAliasRule(string.Empty, null, string.Empty, null, null, null, null, null);
        if (!string.Equals(frameworkId, FrameworkProfileIds.Avalonia, StringComparison.Ordinal))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length < 4 ||
            attribute.ConstructorArguments[0].Value is not string targetTypeName ||
            attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
            attribute.ConstructorArguments[2].Value is not string propertyOwnerTypeName ||
            attribute.ConstructorArguments[3].Value is not string propertyFieldName)
        {
            return false;
        }

        alias = new ResolvedPropertyAliasRule(
            targetTypeName,
            ResolveConfiguredAliasType(compilation, targetTypeName),
            _normalizePropertyName(xamlPropertyName),
            null,
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            ResolveConfiguredAliasType(compilation, propertyOwnerTypeName),
            propertyFieldName);
        return true;
    }

    private INamedTypeSymbol? ResolveConfiguredAliasType(Compilation compilation, string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.Equals(typeName, "*", StringComparison.Ordinal))
        {
            return null;
        }

        return ResolveTypeByMetadataName(compilation, typeName!);
    }

    private INamedTypeSymbol? ResolveTypeByMetadataName(Compilation compilation, string clrTypeName)
    {
        var normalized = _normalizeTypeToken(clrTypeName);
        var direct = compilation.GetTypeByMetadataName(normalized);
        if (direct is not null)
        {
            return direct;
        }

        foreach (var assembly in _enumerateAssemblies(compilation))
        {
            var candidate = assembly.GetTypeByMetadataName(normalized);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }
}
