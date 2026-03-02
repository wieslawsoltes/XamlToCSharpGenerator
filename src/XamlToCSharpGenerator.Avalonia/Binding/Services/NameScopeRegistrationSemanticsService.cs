using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class NameScopeRegistrationSemanticsService
{
    internal delegate bool IsTypeAssignableToDelegate(ITypeSymbol source, ITypeSymbol target);

    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;

    public NameScopeRegistrationSemanticsService(IsTypeAssignableToDelegate isTypeAssignableTo)
    {
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
    }

    public bool SupportsRegistrationFromNameProperty(
        INamedTypeSymbol? resolvedType,
        ITypeSymbolCatalog? typeSymbolCatalog)
    {
        if (resolvedType is null)
        {
            return true;
        }

        var namedContract = typeSymbolCatalog?.GetOrDefault(TypeContractId.AvaloniaInamed);
        if (namedContract is null)
        {
            return true;
        }

        return _isTypeAssignableTo(resolvedType, namedContract);
    }
}
