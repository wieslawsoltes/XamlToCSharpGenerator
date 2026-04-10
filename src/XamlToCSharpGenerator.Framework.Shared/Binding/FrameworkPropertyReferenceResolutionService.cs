using System;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class FrameworkPropertyReferenceResolutionService
{
    public delegate bool TryNormalizeReferenceTokenDelegate(string? rawToken, out string normalizedToken);
    public delegate bool TrySplitOwnerQualifiedPropertyDelegate(string propertyToken, out string ownerToken, out string propertyName);
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string fallbackClrNamespace);
    public delegate bool TryFindFrameworkPropertyFieldDelegate(
        INamedTypeSymbol ownerType,
        string propertyName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField);
    public delegate ITypeSymbol? ResolveFrameworkPropertyValueTypeDelegate(ITypeSymbol propertyFieldType);

    private readonly TryNormalizeReferenceTokenDelegate _tryNormalizeReferenceToken;
    private readonly TrySplitOwnerQualifiedPropertyDelegate _trySplitOwnerQualifiedProperty;
    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly TryFindFrameworkPropertyFieldDelegate _tryFindFrameworkPropertyField;
    private readonly ResolveFrameworkPropertyValueTypeDelegate _resolveFrameworkPropertyValueType;

    public FrameworkPropertyReferenceResolutionService(
        TryNormalizeReferenceTokenDelegate tryNormalizeReferenceToken,
        TrySplitOwnerQualifiedPropertyDelegate trySplitOwnerQualifiedProperty,
        ResolveTypeTokenDelegate resolveTypeToken,
        TryFindFrameworkPropertyFieldDelegate tryFindFrameworkPropertyField,
        ResolveFrameworkPropertyValueTypeDelegate resolveFrameworkPropertyValueType)
    {
        _tryNormalizeReferenceToken = tryNormalizeReferenceToken ?? throw new ArgumentNullException(nameof(tryNormalizeReferenceToken));
        _trySplitOwnerQualifiedProperty = trySplitOwnerQualifiedProperty ?? throw new ArgumentNullException(nameof(trySplitOwnerQualifiedProperty));
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _tryFindFrameworkPropertyField = tryFindFrameworkPropertyField ?? throw new ArgumentNullException(nameof(tryFindFrameworkPropertyField));
        _resolveFrameworkPropertyValueType = resolveFrameworkPropertyValueType ?? throw new ArgumentNullException(nameof(resolveFrameworkPropertyValueType));
    }

    public bool TryResolveReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression,
        out ITypeSymbol? propertyValueType)
    {
        expression = string.Empty;
        propertyValueType = null;

        if (!_tryNormalizeReferenceToken(rawValue, out var normalizedToken))
        {
            return false;
        }

        INamedTypeSymbol? ownerType = defaultOwnerType;
        string propertyName = normalizedToken;

        if (_trySplitOwnerQualifiedProperty(normalizedToken, out var ownerToken, out var splitPropertyName))
        {
            ownerType = _resolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
            propertyName = splitPropertyName;
        }

        if (ownerType is null ||
            !_tryFindFrameworkPropertyField(ownerType, propertyName, out var resolvedOwnerType, out var propertyField))
        {
            return false;
        }

        propertyValueType = _resolveFrameworkPropertyValueType(propertyField.Type);
        expression = resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + propertyField.Name;
        return true;
    }
}
