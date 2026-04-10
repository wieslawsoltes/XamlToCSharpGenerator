using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed record BoundObjectNodePropertyElementPlan(
    XamlPropertyElement PropertyElement,
    PropertyAliasResolution PropertyAlias,
    string NormalizedPropertyName,
    ImmutableArray<ResolvedObjectNode> ObjectValues);

public sealed record BoundObjectNodePropertyElementSet(
    ImmutableArray<BoundObjectNodePropertyElementPlan> PropertyElementPlans,
    ImmutableArray<ResolvedObjectNode> Children,
    ResolvedChildAttachmentMode ExplicitAttachmentMode,
    string? ExplicitContentPropertyName);

public sealed class ObjectNodePropertyElementBindingService
{
    public delegate bool ShouldSkipBranchDelegate(
        ConditionalXamlExpression? condition,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options);

    public delegate bool IsDesignTimePropertyTokenDelegate(string propertyName);
    public delegate PropertyAliasResolution ResolvePropertyAliasDelegate(INamedTypeSymbol? targetType, string propertyToken);
    public delegate INamedTypeSymbol? ResolvePropertyElementSetterTargetTypeDelegate(
        INamedTypeSymbol? objectType,
        string propertyName,
        INamedTypeSymbol? inheritedSetterTargetType);

    public delegate bool TryHandleInlinePropertyElementCodeDelegate(
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        out string code);

    public delegate bool TryBindInlinePropertyElementCodeSubscriptionDelegate(
        INamedTypeSymbol? objectType,
        XamlPropertyElement propertyElement,
        string rawCode,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedEventSubscription? subscription);

    public delegate ResolvedObjectNode BindObjectNodeDelegate(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        int inheritedBindingPriorityScope,
        INamedTypeSymbol? forcedType,
        INamedTypeSymbol? rootTypeSymbol,
        BindingScopeContext? parentScopeContext,
        string? parentPropertyName);

    private readonly ShouldSkipBranchDelegate _shouldSkipBranch;
    private readonly IsDesignTimePropertyTokenDelegate _isDesignTimePropertyToken;
    private readonly ResolvePropertyAliasDelegate _resolvePropertyAlias;
    private readonly ResolvePropertyElementSetterTargetTypeDelegate _resolvePropertyElementSetterTargetType;
    private readonly TryHandleInlinePropertyElementCodeDelegate _tryHandleInlinePropertyElementCode;
    private readonly TryBindInlinePropertyElementCodeSubscriptionDelegate _tryBindInlinePropertyElementCodeSubscription;

    public ObjectNodePropertyElementBindingService(
        ShouldSkipBranchDelegate shouldSkipBranch,
        IsDesignTimePropertyTokenDelegate isDesignTimePropertyToken,
        ResolvePropertyAliasDelegate resolvePropertyAlias,
        ResolvePropertyElementSetterTargetTypeDelegate resolvePropertyElementSetterTargetType,
        TryHandleInlinePropertyElementCodeDelegate tryHandleInlinePropertyElementCode,
        TryBindInlinePropertyElementCodeSubscriptionDelegate tryBindInlinePropertyElementCodeSubscription)
    {
        _shouldSkipBranch = shouldSkipBranch ?? throw new ArgumentNullException(nameof(shouldSkipBranch));
        _isDesignTimePropertyToken = isDesignTimePropertyToken ?? throw new ArgumentNullException(nameof(isDesignTimePropertyToken));
        _resolvePropertyAlias = resolvePropertyAlias ?? throw new ArgumentNullException(nameof(resolvePropertyAlias));
        _resolvePropertyElementSetterTargetType = resolvePropertyElementSetterTargetType ?? throw new ArgumentNullException(nameof(resolvePropertyElementSetterTargetType));
        _tryHandleInlinePropertyElementCode = tryHandleInlinePropertyElementCode ?? throw new ArgumentNullException(nameof(tryHandleInlinePropertyElementCode));
        _tryBindInlinePropertyElementCodeSubscription = tryBindInlinePropertyElementCodeSubscription ??
            throw new ArgumentNullException(nameof(tryBindInlinePropertyElementCodeSubscription));
    }

    public BoundObjectNodePropertyElementSet Bind(
        XamlObjectNode node,
        INamedTypeSymbol? objectType,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        int bindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedEventSubscription>.Builder eventSubscriptions,
        BindingScopeContext scopeContext,
        BindObjectNodeDelegate bindObjectNode)
    {
        var propertyElementPlans = ImmutableArray.CreateBuilder<BoundObjectNodePropertyElementPlan>();
        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        var explicitAttachmentMode = ResolvedChildAttachmentMode.None;
        string? explicitContentPropertyName = null;

        foreach (var propertyElement in node.PropertyElements)
        {
            if (_shouldSkipBranch(propertyElement.Condition, compilation, document, diagnostics, options) ||
                _isDesignTimePropertyToken(propertyElement.PropertyName))
            {
                continue;
            }

            var propertyAlias = _resolvePropertyAlias(objectType, propertyElement.PropertyName);
            var normalizedPropertyName = propertyAlias.ResolvedPropertyName;
            var propertySetterTargetType = _resolvePropertyElementSetterTargetType(
                objectType,
                normalizedPropertyName,
                inheritedSetterTargetType);

            if (TryBindInlinePropertyElementCodeSubscription(
                    objectType,
                    propertyElement,
                    compilation,
                    diagnostics,
                    document,
                    options,
                    nodeDataType,
                    rootTypeSymbol,
                    out var propertyElementEventSubscription))
            {
                if (propertyElementEventSubscription is not null)
                {
                    eventSubscriptions.Add(propertyElementEventSubscription);
                }

                continue;
            }

            if (normalizedPropertyName.Equals("Content", StringComparison.Ordinal))
            {
                explicitAttachmentMode = ResolvedChildAttachmentMode.Content;
                explicitContentPropertyName = normalizedPropertyName;
            }
            else if (normalizedPropertyName.Equals("Children", StringComparison.Ordinal))
            {
                explicitAttachmentMode = ResolvedChildAttachmentMode.ChildrenCollection;
                explicitContentPropertyName = normalizedPropertyName;
            }
            else if (normalizedPropertyName.Equals("Items", StringComparison.Ordinal))
            {
                explicitAttachmentMode = ResolvedChildAttachmentMode.ItemsCollection;
                explicitContentPropertyName = normalizedPropertyName;
            }

            var boundValues = BindPropertyElementChildValues(
                propertyElement,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                nodeDataType,
                propertySetterTargetType,
                bindingPriorityScope,
                rootTypeSymbol,
                scopeContext,
                bindObjectNode);

            if (explicitAttachmentMode != ResolvedChildAttachmentMode.None &&
                explicitContentPropertyName == normalizedPropertyName)
            {
                children.AddRange(boundValues);
                continue;
            }

            propertyElementPlans.Add(new BoundObjectNodePropertyElementPlan(
                propertyElement,
                propertyAlias,
                normalizedPropertyName,
                boundValues));
        }

        return new BoundObjectNodePropertyElementSet(
            propertyElementPlans.ToImmutable(),
            children.ToImmutable(),
            explicitAttachmentMode,
            explicitContentPropertyName);
    }

    private bool TryBindInlinePropertyElementCodeSubscription(
        INamedTypeSymbol? objectType,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        if (objectType is null || propertyElement.ObjectValues.Length != 1)
        {
            return false;
        }

        var valueNode = propertyElement.ObjectValues[0];
        if (!_tryHandleInlinePropertyElementCode(valueNode, compilation, document, out var rawCode))
        {
            return false;
        }

        return _tryBindInlinePropertyElementCodeSubscription(
            objectType,
            propertyElement,
            rawCode,
            compilation,
            diagnostics,
            document,
            options,
            nodeDataType,
            rootTypeSymbol,
            out subscription);
    }

    private ImmutableArray<ResolvedObjectNode> BindPropertyElementChildValues(
        XamlPropertyElement propertyElement,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        BindingScopeContext scopeContext,
        BindObjectNodeDelegate bindObjectNode)
    {
        var values = ImmutableArray.CreateBuilder<ResolvedObjectNode>(propertyElement.ObjectValues.Length);
        foreach (var value in propertyElement.ObjectValues)
        {
            _ = _tryHandleInlinePropertyElementCode(value, compilation, document, out _);
            values.Add(bindObjectNode(
                value,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                unsafeAccessors,
                compileBindingsEnabled,
                nodeDataType,
                setterTargetType,
                bindingPriorityScope,
                forcedType: null,
                rootTypeSymbol,
                scopeContext,
                propertyElement.PropertyName));
        }

        return values.ToImmutable();
    }
}
