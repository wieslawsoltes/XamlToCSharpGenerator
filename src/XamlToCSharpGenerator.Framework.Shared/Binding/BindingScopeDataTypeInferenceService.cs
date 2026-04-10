using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class BindingScopeDataTypeInferenceService
{
    public delegate INamedTypeSymbol? ResolveTypeFromTypeExpressionDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace);

    public delegate bool IsDataTemplateNodeDelegate(XamlObjectNode node);

    public delegate string NormalizePropertyNameDelegate(string propertyName);

    public delegate bool TryParseBindingMarkupDelegate(string value, out BindingMarkup bindingMarkup);

    public delegate bool TryResolveBindingResultTypeForScopeInferenceDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        out ITypeSymbol? resultType);

    public delegate bool TryResolveImplicitCSharpShorthandResultTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string value,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? ownerType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        out ITypeSymbol? resultType);

    public delegate IPropertySymbol? FindPropertyDelegate(INamedTypeSymbol ownerType, string propertyName);

    public delegate string ResolveAliasedPropertyNameDelegate(INamedTypeSymbol ownerType, string propertyName);

    public delegate bool TryGetCollectionElementTypeForInferenceDelegate(ITypeSymbol collectionType, out ITypeSymbol? elementType);

    public delegate bool TryParseBindingMarkupFromObjectNodeDelegate(XamlObjectNode node, out BindingMarkup bindingMarkup);

    public delegate INamedTypeSymbol? ResolveObjectTypeSymbolDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node);

    public delegate bool IsTypeAssignableToDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);

    public delegate bool TrySplitOwnerQualifiedPropertyTokenDelegate(
        string propertyToken,
        out string ownerToken,
        out string propertyName);

    public delegate INamedTypeSymbol? ResolveOwnerQualifiedMemberOwnerTypeDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string ownerToken,
        string fallbackClrNamespace);

    private readonly ResolveTypeFromTypeExpressionDelegate _resolveTypeFromTypeExpression;
    private readonly IsDataTemplateNodeDelegate _isDataTemplateNode;
    private readonly NormalizePropertyNameDelegate _normalizePropertyName;
    private readonly TryParseBindingMarkupDelegate _tryParseBindingMarkup;
    private readonly TryResolveBindingResultTypeForScopeInferenceDelegate _tryResolveBindingResultTypeForScopeInference;
    private readonly TryResolveImplicitCSharpShorthandResultTypeDelegate _tryResolveImplicitCSharpShorthandResultType;
    private readonly FindPropertyDelegate _findProperty;
    private readonly ResolveAliasedPropertyNameDelegate _resolveAliasedPropertyName;
    private readonly TryGetCollectionElementTypeForInferenceDelegate _tryGetCollectionElementTypeForInference;
    private readonly TryParseBindingMarkupFromObjectNodeDelegate _tryParseBindingMarkupFromObjectNode;
    private readonly ResolveObjectTypeSymbolDelegate _resolveObjectTypeSymbol;
    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;
    private readonly TrySplitOwnerQualifiedPropertyTokenDelegate _trySplitOwnerQualifiedPropertyToken;
    private readonly ResolveOwnerQualifiedMemberOwnerTypeDelegate _resolveOwnerQualifiedMemberOwnerType;
    private readonly ImmutableHashSet<string> _inheritDataTypeFromItemsAttributeMetadataNames;

    public BindingScopeDataTypeInferenceService(
        ResolveTypeFromTypeExpressionDelegate resolveTypeFromTypeExpression,
        IsDataTemplateNodeDelegate isDataTemplateNode,
        NormalizePropertyNameDelegate normalizePropertyName,
        TryParseBindingMarkupDelegate tryParseBindingMarkup,
        TryResolveBindingResultTypeForScopeInferenceDelegate tryResolveBindingResultTypeForScopeInference,
        TryResolveImplicitCSharpShorthandResultTypeDelegate tryResolveImplicitCSharpShorthandResultType,
        FindPropertyDelegate findProperty,
        ResolveAliasedPropertyNameDelegate resolveAliasedPropertyName,
        TryGetCollectionElementTypeForInferenceDelegate tryGetCollectionElementTypeForInference,
        TryParseBindingMarkupFromObjectNodeDelegate tryParseBindingMarkupFromObjectNode,
        ResolveObjectTypeSymbolDelegate resolveObjectTypeSymbol,
        IsTypeAssignableToDelegate isTypeAssignableTo,
        TrySplitOwnerQualifiedPropertyTokenDelegate trySplitOwnerQualifiedPropertyToken,
        ResolveOwnerQualifiedMemberOwnerTypeDelegate resolveOwnerQualifiedMemberOwnerType,
        ImmutableArray<string> inheritDataTypeFromItemsAttributeMetadataNames,
        string xaml2006Namespace)
    {
        _ = tryParseBindingMarkup ?? throw new ArgumentNullException(nameof(tryParseBindingMarkup));
        _ = tryResolveBindingResultTypeForScopeInference ?? throw new ArgumentNullException(nameof(tryResolveBindingResultTypeForScopeInference));
        _ = tryResolveImplicitCSharpShorthandResultType ?? throw new ArgumentNullException(nameof(tryResolveImplicitCSharpShorthandResultType));
        _ = findProperty ?? throw new ArgumentNullException(nameof(findProperty));
        _ = resolveAliasedPropertyName ?? throw new ArgumentNullException(nameof(resolveAliasedPropertyName));
        _ = tryGetCollectionElementTypeForInference ?? throw new ArgumentNullException(nameof(tryGetCollectionElementTypeForInference));
        _ = tryParseBindingMarkupFromObjectNode ?? throw new ArgumentNullException(nameof(tryParseBindingMarkupFromObjectNode));
        _ = resolveObjectTypeSymbol ?? throw new ArgumentNullException(nameof(resolveObjectTypeSymbol));
        _ = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _ = trySplitOwnerQualifiedPropertyToken ?? throw new ArgumentNullException(nameof(trySplitOwnerQualifiedPropertyToken));
        _ = resolveOwnerQualifiedMemberOwnerType ?? throw new ArgumentNullException(nameof(resolveOwnerQualifiedMemberOwnerType));
        _ = xaml2006Namespace;

        _resolveTypeFromTypeExpression = resolveTypeFromTypeExpression ?? throw new ArgumentNullException(nameof(resolveTypeFromTypeExpression));
        _isDataTemplateNode = isDataTemplateNode ?? throw new ArgumentNullException(nameof(isDataTemplateNode));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
        _tryParseBindingMarkup = tryParseBindingMarkup;
        _tryResolveBindingResultTypeForScopeInference = tryResolveBindingResultTypeForScopeInference;
        _tryResolveImplicitCSharpShorthandResultType = tryResolveImplicitCSharpShorthandResultType;
        _findProperty = findProperty;
        _resolveAliasedPropertyName = resolveAliasedPropertyName;
        _tryGetCollectionElementTypeForInference = tryGetCollectionElementTypeForInference;
        _tryParseBindingMarkupFromObjectNode = tryParseBindingMarkupFromObjectNode;
        _resolveObjectTypeSymbol = resolveObjectTypeSymbol;
        _isTypeAssignableTo = isTypeAssignableTo;
        _trySplitOwnerQualifiedPropertyToken = trySplitOwnerQualifiedPropertyToken;
        _resolveOwnerQualifiedMemberOwnerType = resolveOwnerQualifiedMemberOwnerType;
        _inheritDataTypeFromItemsAttributeMetadataNames = inheritDataTypeFromItemsAttributeMetadataNames.ToImmutableHashSet(StringComparer.Ordinal);
        _ = xaml2006Namespace;
    }

    public INamedTypeSymbol? ResolveNodeDataType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        INamedTypeSymbol? nodeType,
        INamedTypeSymbol? inheritedDataType,
        GeneratorOptions options,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var resolvedNodeDataType = _resolveTypeFromTypeExpression(
            compilation,
            document,
            node.DataType,
            document.ClassNamespace);
        if (resolvedNodeDataType is not null)
        {
            return resolvedNodeDataType;
        }

        if (_isDataTemplateNode(node))
        {
            foreach (var assignment in node.PropertyAssignments)
            {
                if (assignment.IsAttached ||
                    !_normalizePropertyName(assignment.PropertyName).Equals("DataType", StringComparison.Ordinal))
                {
                    continue;
                }

                var resolvedTemplateDataType = _resolveTypeFromTypeExpression(
                    compilation,
                    document,
                    assignment.Value,
                    document.ClassNamespace);
                if (resolvedTemplateDataType is not null)
                {
                    return resolvedTemplateDataType;
                }
            }
        }

        var explicitDataContextType = ResolveExplicitDataContextType(
            compilation,
            document,
            node,
            nodeType,
            inheritedDataType,
            unsafeAccessors);
        if (explicitDataContextType is not null)
        {
            return explicitDataContextType;
        }

        if (scopeContext.ParentPropertyName is not null &&
            scopeContext.Parent is not null &&
            scopeContext.Parent.NodeType is not null)
        {
            var parentPropertyName = _resolveAliasedPropertyName(scopeContext.Parent.NodeType, scopeContext.ParentPropertyName);
            var inferredFromParentProperty = ResolvePresentedItemDataTypeFromItemsAttribute(
                compilation,
                document,
                scopeContext.Parent,
                scopeContext.Parent.NodeType,
                parentPropertyName,
                options,
                unsafeAccessors);
            if (inferredFromParentProperty is not null)
            {
                return inferredFromParentProperty;
            }
        }

        return inheritedDataType;
    }

    public string ResolveXBindDefaultMode(XamlObjectNode node, string inheritedMode)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (!IsXBindDefaultModeDirective(assignment))
            {
                continue;
            }

            return NormalizeXBindMode(assignment.Value);
        }

        return NormalizeXBindMode(inheritedMode);
    }

    public bool IsXBindDefaultModeDirective(XamlPropertyAssignment assignment)
    {
        var normalized = _normalizePropertyName(assignment.PropertyName);
        return string.Equals(normalized, "DefaultBindMode", StringComparison.Ordinal);
    }

    public bool IsSupportedXBindMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return NormalizeXBindMode(value) is "Default" or "OneTime" or "OneWay" or "TwoWay";
    }

    public string NormalizeXBindMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Default";
        }

        return value!.Trim();
    }

    public INamedTypeSymbol? ResolveAssignmentBindingDataType(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol? ownerType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeDataType,
        GeneratorOptions options,
        BindingScopeContext scopeContext,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        if (string.Equals(_normalizePropertyName(assignment.PropertyName), "DataType", StringComparison.Ordinal))
        {
            return _resolveTypeFromTypeExpression(compilation, document, assignment.Value, document.ClassNamespace);
        }

        if (IsNonAttachedDataContextProperty(assignment.PropertyName, assignment.IsAttached))
        {
            return inheritedDataType;
        }

        if (ownerType is not null)
        {
            var propertyName = _resolveAliasedPropertyName(ownerType, assignment.PropertyName);
            var inferredFromItemsProperty = ResolvePresentedItemDataTypeFromItemsAttribute(
                compilation,
                document,
                scopeContext,
                ownerType,
                propertyName,
                options,
                unsafeAccessors);
            if (inferredFromItemsProperty is not null)
            {
                return inferredFromItemsProperty;
            }
        }

        return nodeDataType ?? inheritedDataType;
    }

    private bool IsNonAttachedDataContextProperty(string propertyName, bool isAttached)
    {
        return !isAttached &&
               _normalizePropertyName(propertyName).Equals("DataContext", StringComparison.Ordinal);
    }

    private INamedTypeSymbol? ResolveExplicitDataContextType(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        INamedTypeSymbol? nodeType,
        INamedTypeSymbol? inheritedDataType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.IsAttached ||
                !_normalizePropertyName(assignment.PropertyName).Equals("DataContext", StringComparison.Ordinal) ||
                !_tryParseBindingMarkup(assignment.Value, out var bindingMarkup))
            {
                continue;
            }

            if (!_tryResolveBindingResultTypeForScopeInference(
                    compilation,
                    document,
                    bindingMarkup,
                    inheritedDataType,
                    nodeType,
                    unsafeAccessors,
                    out var resultType) ||
                resultType is not INamedTypeSymbol resolvedDataContextType)
            {
                continue;
            }

            return resolvedDataContextType;
        }

        return null;
    }

    private INamedTypeSymbol? ResolvePresentedItemDataTypeFromItemsAttribute(
        Compilation compilation,
        XamlDocumentModel document,
        BindingScopeContext propertyOwnerScopeContext,
        INamedTypeSymbol propertyOwnerType,
        string propertyName,
        GeneratorOptions options,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        var property = _findProperty(propertyOwnerType, propertyName);
        if (property is null ||
            !TryGetInheritDataTypeFromItemsAttribute(property, out var ancestorItemsPropertyName, out var ancestorType))
        {
            return null;
        }

        var ancestorScopeContext = FindPresentedItemAncestorScope(propertyOwnerScopeContext, ancestorType);
        if (ancestorScopeContext is null)
        {
            return null;
        }

        var itemsCollectionType = ResolvePresentedItemsCollectionType(
            compilation,
            document,
            ancestorScopeContext,
            ancestorItemsPropertyName,
            options,
            unsafeAccessors);
        if (itemsCollectionType is null ||
            !_tryGetCollectionElementTypeForInference(itemsCollectionType, out var presentedItemType))
        {
            return null;
        }

        return presentedItemType as INamedTypeSymbol;
    }

    private BindingScopeContext? FindPresentedItemAncestorScope(
        BindingScopeContext startingScopeContext,
        INamedTypeSymbol? ancestorType)
    {
        if (ancestorType is null)
        {
            return startingScopeContext;
        }

        for (var current = startingScopeContext; current is not null; current = current.Parent)
        {
            if (current.NodeType is not null &&
                _isTypeAssignableTo(current.NodeType, ancestorType))
            {
                return current;
            }
        }

        return null;
    }

    private ITypeSymbol? ResolvePresentedItemsCollectionType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingScopeContext ancestorScopeContext,
        string ancestorItemsPropertyName,
        GeneratorOptions options,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors)
    {
        foreach (var assignment in ancestorScopeContext.Node.PropertyAssignments)
        {
            if (!MatchesPresentedItemsProperty(
                    compilation,
                    document,
                    ancestorScopeContext.NodeType,
                    assignment.PropertyName,
                    ancestorItemsPropertyName))
            {
                continue;
            }

            var bindingMarkupParsed = _tryParseBindingMarkup(assignment.Value, out var bindingMarkup);
            if (!bindingMarkupParsed)
            {
                if (!_tryResolveImplicitCSharpShorthandResultType(
                        compilation,
                        document,
                        assignment.Value,
                        options,
                        ancestorScopeContext.NodeDataType,
                        ancestorScopeContext.RootTypeSymbol,
                        ancestorScopeContext.NodeType,
                        unsafeAccessors,
                        out var shorthandResultType) ||
                    shorthandResultType is null)
                {
                    continue;
                }

                return shorthandResultType;
            }

            var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || ancestorScopeContext.CompileBindingsEnabled;
            if (!wantsCompiledBinding ||
                !_tryResolveBindingResultTypeForScopeInference(
                    compilation,
                    document,
                    bindingMarkup,
                    ancestorScopeContext.NodeDataType,
                    ancestorScopeContext.NodeType,
                    unsafeAccessors,
                    out var bindingResultType) ||
                bindingResultType is null)
            {
                continue;
            }

            return bindingResultType;
        }

        foreach (var propertyElement in ancestorScopeContext.Node.PropertyElements)
        {
            if (!MatchesPresentedItemsProperty(
                    compilation,
                    document,
                    ancestorScopeContext.NodeType,
                    propertyElement.PropertyName,
                    ancestorItemsPropertyName) ||
                propertyElement.ObjectValues.Length != 1)
            {
                continue;
            }

            var objectValue = propertyElement.ObjectValues[0];
            if (_tryParseBindingMarkupFromObjectNode(objectValue, out var bindingMarkup))
            {
                var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || ancestorScopeContext.CompileBindingsEnabled;
                if (wantsCompiledBinding &&
                    _tryResolveBindingResultTypeForScopeInference(
                        compilation,
                        document,
                        bindingMarkup,
                        ancestorScopeContext.NodeDataType,
                        ancestorScopeContext.NodeType,
                        unsafeAccessors,
                        out var bindingResultType) &&
                    bindingResultType is not null)
                {
                    return bindingResultType;
                }
            }

            var directCollectionType = _resolveObjectTypeSymbol(compilation, document, objectValue);
            if (directCollectionType is not null)
            {
                return directCollectionType;
            }
        }

        return null;
    }

    private bool MatchesPresentedItemsProperty(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? ancestorNodeType,
        string propertyToken,
        string ancestorItemsPropertyName)
    {
        if (ancestorNodeType is null)
        {
            return false;
        }

        if (!_trySplitOwnerQualifiedPropertyToken(propertyToken, out var ownerTypeToken, out var propertyName))
        {
            return _normalizePropertyName(propertyToken).Equals(ancestorItemsPropertyName, StringComparison.Ordinal);
        }

        if (!propertyName.Equals(ancestorItemsPropertyName, StringComparison.Ordinal))
        {
            return false;
        }

        var ancestorProperty = _findProperty(ancestorNodeType, ancestorItemsPropertyName);
        var ownerType = _resolveOwnerQualifiedMemberOwnerType(
            compilation,
            document,
            ownerTypeToken,
            document.ClassNamespace);
        if (ancestorProperty is null ||
            ownerType is null)
        {
            return false;
        }

        var ownerProperty = _findProperty(ownerType, propertyName);
        return ownerProperty is not null &&
               SymbolEqualityComparer.Default.Equals(ownerProperty.OriginalDefinition, ancestorProperty.OriginalDefinition);
    }

    private bool TryGetInheritDataTypeFromItemsAttribute(
        IPropertySymbol property,
        out string ancestorItemsPropertyName,
        out INamedTypeSymbol? ancestorType)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            var metadataName = attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var simpleName = attributeType.Name;
            var isInheritDataTypeFromItemsAttribute =
                _inheritDataTypeFromItemsAttributeMetadataNames.Contains(metadataName) ||
                _inheritDataTypeFromItemsAttributeMetadataNames.Contains(simpleName) ||
                simpleName.Equals("InheritDataTypeFromItemsAttribute", StringComparison.Ordinal);
            if (!isInheritDataTypeFromItemsAttribute ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string propertyNameValue ||
                string.IsNullOrWhiteSpace(propertyNameValue))
            {
                continue;
            }

            ancestorItemsPropertyName = propertyNameValue.Trim();
            ancestorType = null;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (!namedArgument.Key.Equals("AncestorType", StringComparison.Ordinal) ||
                    namedArgument.Value.Value is not INamedTypeSymbol namedType)
                {
                    continue;
                }

                ancestorType = namedType;
                break;
            }

            return true;
        }

        ancestorItemsPropertyName = string.Empty;
        ancestorType = null;
        return false;
    }
}
