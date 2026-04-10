using System;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class WritablePropertyResolutionService
{
    public bool TryGetWritableProperty(
        INamedTypeSymbol typeSymbol,
        string propertyName,
        out IPropertySymbol? propertySymbol)
    {
        for (INamedTypeSymbol? current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol candidate &&
                    !candidate.IsStatic &&
                    candidate.SetMethod is not null)
                {
                    propertySymbol = candidate;
                    return true;
                }
            }
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            foreach (var member in interfaceType.GetMembers(propertyName))
            {
                if (member is IPropertySymbol candidate &&
                    !candidate.IsStatic &&
                    candidate.SetMethod is not null)
                {
                    propertySymbol = candidate;
                    return true;
                }
            }
        }

        propertySymbol = null;
        return false;
    }
}
