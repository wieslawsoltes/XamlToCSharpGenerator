using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ObjectNodeFinalizationService
{
    public delegate bool ShouldUseServiceProviderConstructorDelegate(INamedTypeSymbol? symbol);
    public delegate bool IsUsableDuringInitializationDelegate(INamedTypeSymbol? symbol);
    public delegate ImmutableArray<ResolvedCollectionAddInstruction> ResolveChildAddInstructionsDelegate(
        INamedTypeSymbol? targetType,
        string? contentPropertyName,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document);
    public delegate bool IsTypeAssignableToDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);
    public delegate bool IsMarkupExtensionObjectDelegate(
        INamedTypeSymbol? symbol,
        string typeName,
        Compilation compilation);

    private readonly ShouldUseServiceProviderConstructorDelegate _shouldUseServiceProviderConstructor;
    private readonly IsUsableDuringInitializationDelegate _isUsableDuringInitialization;
    private readonly ResolveChildAddInstructionsDelegate _resolveChildAddInstructions;
    private readonly IsTypeAssignableToDelegate _isTypeAssignableTo;
    private readonly IsMarkupExtensionObjectDelegate _isMarkupExtensionObject;

    public ObjectNodeFinalizationService(
        ShouldUseServiceProviderConstructorDelegate shouldUseServiceProviderConstructor,
        IsUsableDuringInitializationDelegate isUsableDuringInitialization,
        ResolveChildAddInstructionsDelegate resolveChildAddInstructions,
        IsTypeAssignableToDelegate isTypeAssignableTo,
        IsMarkupExtensionObjectDelegate isMarkupExtensionObject)
    {
        _shouldUseServiceProviderConstructor = shouldUseServiceProviderConstructor ?? throw new ArgumentNullException(nameof(shouldUseServiceProviderConstructor));
        _isUsableDuringInitialization = isUsableDuringInitialization ?? throw new ArgumentNullException(nameof(isUsableDuringInitialization));
        _resolveChildAddInstructions = resolveChildAddInstructions ?? throw new ArgumentNullException(nameof(resolveChildAddInstructions));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _isMarkupExtensionObject = isMarkupExtensionObject ?? throw new ArgumentNullException(nameof(isMarkupExtensionObject));
    }

    public ResolvedObjectNode Finalize(
        INamedTypeSymbol? symbol,
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node,
        string? keyExpression,
        string? name,
        string typeName,
        bool isBindingObjectNode,
        string? factoryExpression,
        ResolvedValueRequirements factoryValueRequirements,
        ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
        ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        ImmutableArray<ResolvedObjectNode> children,
        ResolvedChildAttachmentMode childAttachmentMode,
        string? contentPropertyName,
        string? contentPropertyTypeName)
    {
        _ = _isTypeAssignableTo;

        var semanticFlags = ObjectNodeSemanticContractService.Classify(typeName);
        if (ResolvedObjectNodeSemanticsService.CanBeDeferredResourceNode(typeName, semanticFlags))
        {
            semanticFlags |= ResolvedObjectNodeSemanticFlags.CanBeDeferredResource;
        }

        if (node.IsShared == false)
        {
            semanticFlags |= ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource;
        }

        if (_isMarkupExtensionObject(symbol, typeName, compilation))
        {
            semanticFlags |= ResolvedObjectNodeSemanticFlags.MarkupExtensionObject;
        }

        return new ResolvedObjectNode(
            KeyExpression: keyExpression,
            Name: name,
            TypeName: typeName,
            IsBindingObjectNode: isBindingObjectNode,
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: factoryValueRequirements,
            UseServiceProviderConstructor: _shouldUseServiceProviderConstructor(symbol),
            UseTopDownInitialization: _isUsableDuringInitialization(symbol),
            PropertyAssignments: propertyAssignments,
            PropertyElementAssignments: propertyElementAssignments,
            EventSubscriptions: eventSubscriptions,
            Children: children,
            ChildAttachmentMode: childAttachmentMode,
            ContentPropertyName: contentPropertyName,
            Line: node.Line,
            Column: node.Column,
            Condition: node.Condition,
            ChildAddInstructions: _resolveChildAddInstructions(symbol, contentPropertyName, children, compilation, document),
            SemanticFlags: semanticFlags,
            ContentPropertyTypeName: contentPropertyTypeName);
    }
}

internal static class ObjectNodeSemanticContractService
{
    public static ResolvedObjectNodeSemanticFlags Classify(string typeName)
    {
        var flags = ResolvedObjectNodeSemanticFlags.None;
        if (typeName.IndexOf("ResourceInclude", StringComparison.Ordinal) >= 0)
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsResourceInclude |
                     ResolvedObjectNodeSemanticFlags.RequiresBaseUriConstructor;
        }

        if (typeName.IndexOf("StyleInclude", StringComparison.Ordinal) >= 0)
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsStyleInclude |
                     ResolvedObjectNodeSemanticFlags.RequiresBaseUriConstructor;
        }

        if (typeName.IndexOf("StaticResource", StringComparison.Ordinal) >= 0)
        {
            flags |= ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension;
        }

        if (typeName.IndexOf("ResourceDictionary", StringComparison.Ordinal) >= 0)
        {
            flags |= ResolvedObjectNodeSemanticFlags.IsResourceDictionary;
        }

        return flags;
    }
}

internal static class ResolvedObjectNodeSemanticsService
{
    public static bool CanBeDeferredResourceNode(
        string typeName,
        ResolvedObjectNodeSemanticFlags semanticFlags)
    {
        if ((semanticFlags & ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension) == ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension)
        {
            return false;
        }

        return typeName.IndexOf("Template", StringComparison.Ordinal) >= 0 ||
               (semanticFlags & ResolvedObjectNodeSemanticFlags.IsResourceDictionary) == ResolvedObjectNodeSemanticFlags.IsResourceDictionary;
    }
}
