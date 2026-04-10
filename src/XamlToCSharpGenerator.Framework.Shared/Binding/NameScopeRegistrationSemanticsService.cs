using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class NameScopeRegistrationSemanticsService
{
    public delegate bool IsTypeAssignableToDelegate(ITypeSymbol source, ITypeSymbol target);

    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;
    private readonly TypeContractId _namedTypeContractId;

    public NameScopeRegistrationSemanticsService(
        IsTypeAssignableToDelegate isTypeAssignableTo,
        TypeContractId namedTypeContractId)
    {
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _namedTypeContractId = namedTypeContractId;
    }

    public bool SupportsRegistrationFromNameProperty(
        INamedTypeSymbol? resolvedType,
        ITypeSymbolCatalog? typeSymbolCatalog)
    {
        if (resolvedType is null)
        {
            return true;
        }

        var namedContract = typeSymbolCatalog?.GetOrDefault(_namedTypeContractId);
        if (namedContract is null)
        {
            return true;
        }

        return _isTypeAssignableTo(resolvedType, namedContract);
    }
}
