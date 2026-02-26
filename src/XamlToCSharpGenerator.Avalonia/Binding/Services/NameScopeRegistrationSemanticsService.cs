using System;
using Microsoft.CodeAnalysis;

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
        Compilation compilation)
    {
        if (resolvedType is null)
        {
            return true;
        }

        var namedContract = compilation.GetTypeByMetadataName("Avalonia.INamed");
        if (namedContract is null)
        {
            return true;
        }

        return _isTypeAssignableTo(resolvedType, namedContract);
    }
}
