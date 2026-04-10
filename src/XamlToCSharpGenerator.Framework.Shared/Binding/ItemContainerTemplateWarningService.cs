using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ItemContainerTemplateWarningService
{
    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly Func<XamlObjectNode, bool> _isDataTemplateNode;
    private readonly Func<XamlObjectNode, XamlObjectNode?> _tryGetTemplateContentNode;
    private readonly Func<Compilation, XamlDocumentModel, XamlObjectNode, INamedTypeSymbol?> _resolveTypeSymbol;

    public ItemContainerTemplateWarningService(
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        Func<XamlObjectNode, bool> isDataTemplateNode,
        Func<XamlObjectNode, XamlObjectNode?> tryGetTemplateContentNode,
        Func<Compilation, XamlDocumentModel, XamlObjectNode, INamedTypeSymbol?> resolveTypeSymbol)
    {
        _resolveContractType = resolveContractType;
        _isTypeAssignableTo = isTypeAssignableTo;
        _isDataTemplateNode = isDataTemplateNode;
        _tryGetTemplateContentNode = tryGetTemplateContentNode;
        _resolveTypeSymbol = resolveTypeSymbol;
    }

    public void Validate(
        INamedTypeSymbol ownerType,
        IPropertySymbol property,
        XamlPropertyElement templateNode,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableDictionary<string, string> itemContainerTypeMappings,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var templateObject = templateNode.ObjectValues.Length > 0 ? templateNode.ObjectValues[0] : null;
        if ((!string.Equals(property.Name, "ItemTemplate", System.StringComparison.Ordinal) &&
             !string.Equals(property.Name, "DataTemplates", System.StringComparison.Ordinal)) ||
            templateObject is null ||
            !_isDataTemplateNode(templateObject))
        {
            return;
        }

        var itemsControlType = _resolveContractType(compilation, TypeContractId.ItemsControl);
        if (itemsControlType is null || !_isTypeAssignableTo(ownerType, itemsControlType))
        {
            return;
        }

        var expectedContainerName = TryResolveExpectedContainerTypeName(ownerType, itemContainerTypeMappings);
        if (string.IsNullOrWhiteSpace(expectedContainerName))
        {
            return;
        }

        var contentNode = _tryGetTemplateContentNode(templateObject);
        if (contentNode is null)
        {
            return;
        }

        var contentType = _resolveTypeSymbol(compilation, document, contentNode);
        if (contentType is null || !IsKnownItemContainerType(contentType, expectedContainerName!))
        {
            return;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0505",
            $"Item container '{contentType.Name}' should not be used inside '{ownerType.Name}.{property.Name}'.",
            document.FilePath,
            contentNode.Line,
            contentNode.Column,
            options.StrictMode));
    }

    private static string? TryResolveExpectedContainerTypeName(
        INamedTypeSymbol ownerType,
        ImmutableDictionary<string, string> itemContainerTypeMappings)
    {
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            if (itemContainerTypeMappings.TryGetValue(current.Name, out var containerTypeName))
            {
                return containerTypeName;
            }
        }

        return null;
    }

    private static bool IsKnownItemContainerType(
        INamedTypeSymbol type,
        string expectedContainerTypeName)
    {
        foreach (var candidate in EnumerateBaseTypesAndSelf(type))
        {
            if (string.Equals(candidate.Name, expectedContainerTypeName, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBaseTypesAndSelf(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }
}
