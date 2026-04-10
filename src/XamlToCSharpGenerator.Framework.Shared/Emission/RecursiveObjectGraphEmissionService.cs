using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class RecursiveObjectGraphEmissionService
{
    public delegate string BuildObjectCreationExpressionDelegate(
        ResolvedObjectNode node,
        string serviceProviderReference,
        string baseUriExpression,
        string? rootReference,
        string? intermediateRootReference,
        string? parentStackExpression);

    public delegate string BuildAttachedNodeValueExpressionDelegate(
        ResolvedObjectNode node,
        string nodeReference,
        string serviceProviderReference,
        string rootReference,
        string intermediateRootReference,
        string baseUriExpression,
        string parentStackExpression);

    public delegate void EmitObjectNodeBodyDelegate(
        ResolvedObjectNode node,
        string variableName,
        string? existingVariableName,
        ref int nodeCounter,
        int scopedIndex,
        string topDownAttachValueToken,
        string nodeParentStackExpression,
        string bindingXmlNamespaceMapReference,
        string? topDownAttachmentTemplate,
        bool completeNameScopeOnNodeCompletion,
        string? objectCreationExpression,
        bool clearDictionaryEntriesOnReplaceWhenNotDeferred,
        string? pendingTopDownAttachmentStatement,
        FrameworkObjectGraphEmissionContext emissionContext,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression);

    private readonly ParentStackEmissionService _parentStackEmissionService;
    private readonly EmitObjectNodeBodyDelegate _emitObjectNodeBody;
    private readonly BuildObjectCreationExpressionDelegate _buildObjectCreationExpression;
    private readonly BuildAttachedNodeValueExpressionDelegate _buildAttachedNodeValueExpression;

    public RecursiveObjectGraphEmissionService(
        ParentStackEmissionService parentStackEmissionService,
        EmitObjectNodeBodyDelegate emitObjectNodeBody,
        BuildObjectCreationExpressionDelegate buildObjectCreationExpression,
        BuildAttachedNodeValueExpressionDelegate buildAttachedNodeValueExpression)
    {
        _parentStackEmissionService = parentStackEmissionService ?? throw new ArgumentNullException(nameof(parentStackEmissionService));
        _emitObjectNodeBody = emitObjectNodeBody ?? throw new ArgumentNullException(nameof(emitObjectNodeBody));
        _buildObjectCreationExpression = buildObjectCreationExpression ?? throw new ArgumentNullException(nameof(buildObjectCreationExpression));
        _buildAttachedNodeValueExpression = buildAttachedNodeValueExpression ?? throw new ArgumentNullException(nameof(buildAttachedNodeValueExpression));
    }

    public string EmitNode(
        ResolvedObjectNode node,
        StringBuilder sourceBuilder,
        ref int nodeCounter,
        string indent,
        string rootReference,
        IReadOnlyDictionary<string, string> namedFieldMap,
        IReadOnlyDictionary<string, string> emittedEventBindingMethodNames,
        bool emitNameScopeRegistration,
        string? nameScopeReference,
        string topDownAttachValueToken,
        string bindingXmlNamespaceMapReference,
        string? existingVariableName = null,
        string? topDownAttachmentTemplate = null,
        bool completeNameScopeOnNodeCompletion = false,
        string serviceProviderReference = "__serviceProvider",
        string baseUriExpression = "null",
        ImmutableArray<string> parentStackReferences = default,
        string? intermediateRootReference = null,
        bool emitDebugLineDirectives = false,
        string lineDirectiveFilePath = "")
    {
        var variableName = existingVariableName ?? ("__n" + nodeCounter.ToString());
        intermediateRootReference ??= variableName;
        var nodeParentStackReferences = _parentStackEmissionService.ExtendParentStack(parentStackReferences, variableName);
        var nodeParentStackExpression = _parentStackEmissionService.BuildParentStackExpression(nodeParentStackReferences);
        var emissionContext = new FrameworkObjectGraphEmissionContext(
            sourceBuilder,
            indent,
            rootReference,
            namedFieldMap,
            emittedEventBindingMethodNames,
            emitNameScopeRegistration,
            nameScopeReference,
            bindingXmlNamespaceMapReference,
            serviceProviderReference,
            baseUriExpression,
            nodeParentStackReferences,
            intermediateRootReference,
            emitDebugLineDirectives,
            lineDirectiveFilePath);

        if (existingVariableName is null)
        {
            nodeCounter++;
        }

        string? pendingTopDownAttachmentStatement = null;
        if (node.UseTopDownInitialization &&
            !string.IsNullOrWhiteSpace(topDownAttachmentTemplate))
        {
            var attachmentValueExpression = _buildAttachedNodeValueExpression(
                node,
                variableName,
                serviceProviderReference,
                rootReference,
                intermediateRootReference,
                baseUriExpression,
                nodeParentStackExpression);
            pendingTopDownAttachmentStatement = ReplaceOrdinal(
                topDownAttachmentTemplate!,
                topDownAttachValueToken,
                attachmentValueExpression);
        }

        string EmitNodeFromContext(
            ResolvedObjectNode contextNode,
            FrameworkObjectGraphEmissionContext context,
            ref int scopedNodeCounter,
            string? scopedExistingVariableName,
            string? scopedTopDownAttachmentTemplate,
            bool scopedCompleteNameScopeOnNodeCompletion)
        {
            return EmitNode(
                contextNode,
                context.SourceBuilder,
                ref scopedNodeCounter,
                context.Indent,
                context.RootReference,
                context.NamedFieldMap,
                context.EmittedEventBindingMethodNames,
                context.EmitNameScopeRegistration,
                context.NameScopeReference,
                topDownAttachValueToken,
                bindingXmlNamespaceMapReference,
                scopedExistingVariableName,
                scopedTopDownAttachmentTemplate,
                scopedCompleteNameScopeOnNodeCompletion,
                context.ServiceProviderReference,
                context.BaseUriExpression,
                context.ParentStackReferences,
                context.IntermediateRootReference,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath);
        }

        string BuildAttachedNodeValueExpressionFromContext(
            ResolvedObjectNode contextNode,
            string nodeReference,
            FrameworkObjectGraphEmissionContext context,
            string parentStackExpression)
        {
            return _buildAttachedNodeValueExpression(
                contextNode,
                nodeReference,
                context.ServiceProviderReference,
                context.RootReference,
                context.IntermediateRootReference,
                context.BaseUriExpression,
                parentStackExpression);
        }

        _emitObjectNodeBody(
            node,
            variableName,
            existingVariableName,
            ref nodeCounter,
            nodeCounter,
            topDownAttachValueToken,
            nodeParentStackExpression,
            bindingXmlNamespaceMapReference,
            topDownAttachmentTemplate,
            completeNameScopeOnNodeCompletion,
            existingVariableName is null
                ? _buildObjectCreationExpression(
                    node,
                    serviceProviderReference,
                    baseUriExpression,
                    rootReference,
                    intermediateRootReference,
                    _parentStackEmissionService.BuildParentStackExpression(parentStackReferences))
                : null,
            clearDictionaryEntriesOnReplaceWhenNotDeferred:
            node.ChildAttachmentMode == ResolvedChildAttachmentMode.DictionaryAdd &&
            (node.Children.Length > 0 || existingVariableName is not null),
            pendingTopDownAttachmentStatement,
            emissionContext,
            EmitNodeFromContext,
            BuildAttachedNodeValueExpressionFromContext);
        return variableName;
    }

    private static string ReplaceOrdinal(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0)
        {
            return source;
        }

        return string.Concat(
            source.Substring(0, index),
            newValue,
            source.Substring(index + oldValue.Length));
    }
}
