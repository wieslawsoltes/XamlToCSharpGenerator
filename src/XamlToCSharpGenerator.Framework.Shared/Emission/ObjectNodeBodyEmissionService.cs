using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ObjectNodeBodyEmissionService
{
    private readonly ObjectNodeLifecycleEmissionService _objectNodeLifecycleEmissionService;
    private readonly ObjectNodeMemberEmissionService _objectNodeMemberEmissionService;
    private readonly ObjectNodeEventSubscriptionEmissionService _objectNodeEventSubscriptionEmissionService;
    private readonly CollectionAttachmentEmissionService _collectionAttachmentEmissionService;
    private readonly ContentChildAttachmentEmissionService _contentChildAttachmentEmissionService;
    private readonly DeferredTemplateScaffoldEmissionService _deferredTemplateScaffoldEmissionService;

    public ObjectNodeBodyEmissionService(
        ObjectNodeLifecycleEmissionService objectNodeLifecycleEmissionService,
        ObjectNodeMemberEmissionService objectNodeMemberEmissionService,
        ObjectNodeEventSubscriptionEmissionService objectNodeEventSubscriptionEmissionService,
        CollectionAttachmentEmissionService collectionAttachmentEmissionService,
        ContentChildAttachmentEmissionService contentChildAttachmentEmissionService,
        DeferredTemplateScaffoldEmissionService deferredTemplateScaffoldEmissionService)
    {
        _objectNodeLifecycleEmissionService = objectNodeLifecycleEmissionService;
        _objectNodeMemberEmissionService = objectNodeMemberEmissionService;
        _objectNodeEventSubscriptionEmissionService = objectNodeEventSubscriptionEmissionService;
        _collectionAttachmentEmissionService = collectionAttachmentEmissionService;
        _contentChildAttachmentEmissionService = contentChildAttachmentEmissionService;
        _deferredTemplateScaffoldEmissionService = deferredTemplateScaffoldEmissionService;
    }

    public void EmitNode(
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
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        _ = nodeParentStackExpression;
        _ = topDownAttachmentTemplate;

        if (!string.IsNullOrWhiteSpace(objectCreationExpression))
        {
            emissionContext.SourceBuilder.AppendLine(
                emissionContext.Indent +
                "var " +
                variableName +
                " = " +
                objectCreationExpression +
                ";");
        }

        _objectNodeLifecycleEmissionService.EmitNodeStart(
            emissionContext.SourceBuilder,
            emissionContext.Indent,
            node,
            variableName,
            emissionContext.NameScopeReference,
            emissionContext.EmitNameScopeRegistration,
            emissionContext.EmitDebugLineDirectives,
            emissionContext.LineDirectiveFilePath,
            scopedIndex);

        if (clearDictionaryEntriesOnReplaceWhenNotDeferred)
        {
            emissionContext.SourceBuilder.AppendLine(
                emissionContext.Indent +
                "if (__replaceExistingCollections) __AXSGObjectGraph.TryClearDictionaryEntries(" +
                variableName +
                ");");
        }

        var emittedChildCollectionClear = false;
        if (node.ChildAttachmentMode is ResolvedChildAttachmentMode.ChildrenCollection or
            ResolvedChildAttachmentMode.ItemsCollection or
            ResolvedChildAttachmentMode.DirectAdd)
        {
            var collectionReference = string.IsNullOrWhiteSpace(node.ContentPropertyName)
                ? variableName
                : variableName + "." + node.ContentPropertyName;
            emissionContext.SourceBuilder.AppendLine(
                emissionContext.Indent +
                "if (__replaceExistingCollections) __AXSGObjectGraph.TryClearCollection(" +
                collectionReference +
                ");");
            emittedChildCollectionClear = true;
        }

        if (!emittedChildCollectionClear &&
            ShouldClearItemsCollectionForItemsSourceAssignment(node))
        {
            emissionContext.SourceBuilder.AppendLine(
                emissionContext.Indent +
                "if (__replaceExistingCollections) __AXSGObjectGraph.TryClearCollection(" +
                variableName +
                ".Items);");
        }

        if (!string.IsNullOrWhiteSpace(pendingTopDownAttachmentStatement))
        {
            emissionContext.SourceBuilder.AppendLine(emissionContext.Indent + pendingTopDownAttachmentStatement);
        }

        _objectNodeMemberEmissionService.AttachBindingNodeNameScope(node, variableName, emissionContext);
        _objectNodeMemberEmissionService.EmitPropertyAssignments(
            node,
            variableName,
            emissionContext,
            isExistingRootInstance: existingVariableName is not null &&
                                    variableName == emissionContext.RootReference);
        _objectNodeMemberEmissionService.EmitPropertyElementAssignments(
            node,
            variableName,
            emissionContext,
            ref nodeCounter,
            topDownAttachValueToken,
            emitNode,
            buildAttachedNodeValueExpression);
        _objectNodeEventSubscriptionEmissionService.EmitSubscriptions(
            emissionContext.SourceBuilder,
            emissionContext.Indent,
            node,
            variableName,
            emissionContext.RootReference,
            emissionContext.EmittedEventBindingMethodNames,
            emissionContext.EmitDebugLineDirectives,
            emissionContext.LineDirectiveFilePath);

        if (!_deferredTemplateScaffoldEmissionService.TryEmitTemplateContentFactory(
                node,
                variableName,
                emissionContext,
                ref nodeCounter,
                emitNode))
        {
            foreach (var childNode in node.Children)
            {
                if (_contentChildAttachmentEmissionService.TryEmitContentAttachmentScaffold(
                        node,
                        variableName,
                        childNode,
                        emissionContext,
                        ref nodeCounter,
                        topDownAttachValueToken,
                        emitNode,
                        buildAttachedNodeValueExpression))
                {
                    continue;
                }

                if (_collectionAttachmentEmissionService.TryEmitChildAttachmentScaffold(
                        node,
                        variableName,
                        childNode,
                        emissionContext,
                        ref nodeCounter,
                        topDownAttachValueToken,
                        emitNode,
                        buildAttachedNodeValueExpression))
                {
                    continue;
                }

                _ = emitNode(childNode, emissionContext, ref nodeCounter, existingVariableName: null, topDownAttachmentTemplate: null, completeNameScopeOnNodeCompletion: false);
            }
        }

        if (completeNameScopeOnNodeCompletion &&
            !string.IsNullOrWhiteSpace(emissionContext.NameScopeReference))
        {
            emissionContext.SourceBuilder.AppendLine(
                emissionContext.Indent +
                "__AXSGObjectGraph.TryCompleteNameScope(" +
                emissionContext.NameScopeReference +
                ");");
        }

        _objectNodeLifecycleEmissionService.EmitNodeEnd(
            emissionContext.SourceBuilder,
            emissionContext.Indent,
            node,
            variableName,
            emissionContext.EmitDebugLineDirectives,
            emissionContext.LineDirectiveFilePath);
    }

    private static bool ShouldClearItemsCollectionForItemsSourceAssignment(ResolvedObjectNode node)
    {
        if (!string.Equals(node.ContentPropertyName, "Items", System.StringComparison.Ordinal) ||
            node.PropertyAssignments.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (string.Equals(assignment.PropertyName, "ItemsSource", System.StringComparison.Ordinal) ||
                assignment.PropertyName.EndsWith(".ItemsSource", System.StringComparison.Ordinal))
            {
                return true;
            }

            var propertyFieldName = assignment.FrameworkPropertyOperation?.PropertyFieldName;
            if (string.Equals(propertyFieldName, "ItemsSourceProperty", System.StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(propertyFieldName) &&
                 propertyFieldName.EndsWith(".ItemsSourceProperty", System.StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
