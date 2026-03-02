using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding.Services;

internal static class ObjectNodeSemanticContractService
{
    public static ResolvedObjectNodeSemanticFlags Classify(
        INamedTypeSymbol? objectType,
        ITypeSymbolCatalog typeSymbolCatalog,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo)
    {
        if (typeSymbolCatalog is null)
        {
            throw new ArgumentNullException(nameof(typeSymbolCatalog));
        }

        if (isTypeAssignableTo is null)
        {
            throw new ArgumentNullException(nameof(isTypeAssignableTo));
        }

        if (objectType is null)
        {
            return ResolvedObjectNodeSemanticFlags.None;
        }

        var flags = ResolvedObjectNodeSemanticFlags.None;
        var isResourceInclude = MatchesContractType(
            objectType,
            typeSymbolCatalog,
            isTypeAssignableTo,
            TypeContractId.ResourceInclude) ||
                                MatchesContractType(
                                    objectType,
                                    typeSymbolCatalog,
                                    isTypeAssignableTo,
                                    TypeContractId.MergeResourceInclude);

        var isStyleInclude = MatchesContractType(
            objectType,
            typeSymbolCatalog,
            isTypeAssignableTo,
            TypeContractId.StyleInclude);

        if (isResourceInclude)
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsResourceInclude;
        }

        if (isStyleInclude)
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsStyleInclude;
        }

        if (isResourceInclude || isStyleInclude)
        {
            flags |= ResolvedObjectNodeSemanticFlags.RequiresBaseUriConstructor;
        }

        if (MatchesContractType(
                objectType,
                typeSymbolCatalog,
                isTypeAssignableTo,
                TypeContractId.StaticResourceExtension))
        {
            flags |= ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension;
        }

        return flags;
    }

    private static bool MatchesContractType(
        ITypeSymbol type,
        ITypeSymbolCatalog typeSymbolCatalog,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        TypeContractId contractId)
    {
        var contractType = typeSymbolCatalog.GetOrDefault(contractId);
        return contractType is not null && isTypeAssignableTo(type, contractType);
    }
}
