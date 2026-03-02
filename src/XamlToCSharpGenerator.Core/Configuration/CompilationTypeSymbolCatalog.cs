using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Core.Configuration;

public sealed record TypeContractResolutionDiagnostic(
    string Code,
    TypeContractId ContractId,
    string Message,
    string FrameworkId,
    string FeatureTag,
    bool IsRequired,
    ImmutableArray<string> MetadataNames);

public interface ITypeSymbolCatalog
{
    Compilation Compilation { get; }

    SemanticContractMap ContractMap { get; }

    ImmutableArray<TypeContractResolutionDiagnostic> Diagnostics { get; }

    bool TryGet(TypeContractId contractId, out INamedTypeSymbol? symbol);

    INamedTypeSymbol? GetOrDefault(TypeContractId contractId);
}

public sealed class CompilationTypeSymbolCatalog : ITypeSymbolCatalog
{
    private static readonly ConditionalWeakTable<Compilation, Dictionary<string, CompilationTypeSymbolCatalog>> Cache = new();

    private readonly ImmutableDictionary<TypeContractId, INamedTypeSymbol?> _symbols;

    private CompilationTypeSymbolCatalog(
        Compilation compilation,
        SemanticContractMap contractMap,
        ImmutableDictionary<TypeContractId, INamedTypeSymbol?> symbols,
        ImmutableArray<TypeContractResolutionDiagnostic> diagnostics)
    {
        Compilation = compilation;
        ContractMap = contractMap;
        _symbols = symbols;
        Diagnostics = diagnostics;
    }

    public Compilation Compilation { get; }

    public SemanticContractMap ContractMap { get; }

    public ImmutableArray<TypeContractResolutionDiagnostic> Diagnostics { get; }

    public static CompilationTypeSymbolCatalog Create(
        Compilation compilation,
        SemanticContractMap contractMap)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (contractMap is null)
        {
            throw new ArgumentNullException(nameof(contractMap));
        }

        var cacheKey = contractMap.CatalogCacheKey;
        var mapCache = Cache.GetOrCreateValue(compilation);
        lock (mapCache)
        {
            if (mapCache.TryGetValue(cacheKey, out var existing))
            {
                return existing;
            }

            var created = BuildCatalog(compilation, contractMap);
            mapCache[cacheKey] = created;
            return created;
        }
    }

    public bool TryGet(TypeContractId contractId, out INamedTypeSymbol? symbol)
    {
        if (_symbols.TryGetValue(contractId, out var resolved) && resolved is not null)
        {
            symbol = resolved;
            return true;
        }

        symbol = null;
        return false;
    }

    public INamedTypeSymbol? GetOrDefault(TypeContractId contractId)
    {
        return _symbols.TryGetValue(contractId, out var resolved)
            ? resolved
            : null;
    }

    private static CompilationTypeSymbolCatalog BuildCatalog(
        Compilation compilation,
        SemanticContractMap contractMap)
    {
        var symbols = ImmutableDictionary.CreateBuilder<TypeContractId, INamedTypeSymbol?>();
        var diagnostics = ImmutableArray.CreateBuilder<TypeContractResolutionDiagnostic>();

        foreach (var contract in contractMap.TypeContracts)
        {
            INamedTypeSymbol? symbol = null;
            foreach (var metadataName in contract.MetadataNames)
            {
                symbol = compilation.GetTypeByMetadataName(metadataName);
                if (symbol is not null)
                {
                    break;
                }
            }

            symbols[contract.Id] = symbol;

            if (symbol is null && contract.IsRequired)
            {
                diagnostics.Add(new TypeContractResolutionDiagnostic(
                    Code: "AXSG0910",
                    ContractId: contract.Id,
                    Message: "Required type contract '" + contract.Id + "' for feature '" + contract.FeatureTag +
                             "' could not be resolved for framework '" + contractMap.FrameworkId + "'.",
                    FrameworkId: contractMap.FrameworkId,
                    FeatureTag: contract.FeatureTag,
                    IsRequired: true,
                    MetadataNames: contract.MetadataNames));
            }
        }

        return new CompilationTypeSymbolCatalog(
            compilation,
            contractMap,
            symbols.ToImmutable(),
            diagnostics.ToImmutable());
    }
}
