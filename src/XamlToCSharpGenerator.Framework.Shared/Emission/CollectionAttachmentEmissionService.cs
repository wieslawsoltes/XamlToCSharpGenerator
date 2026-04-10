using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class CollectionAttachmentEmissionService
{
    private readonly IXamlFrameworkCollectionAttachmentEmitterAdapter _adapter;
    private readonly IXamlFrameworkDeferredDictionaryEmitterAdapter _deferredDictionaryAdapter;
    private readonly DeferredDictionaryEmissionService _deferredDictionaryEmissionService;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;
    private readonly ParentStackEmissionService _parentStackEmissionService;

    public CollectionAttachmentEmissionService(
        IXamlFrameworkCollectionAttachmentEmitterAdapter adapter,
        IXamlFrameworkDeferredDictionaryEmitterAdapter deferredDictionaryAdapter,
        DeferredDictionaryEmissionService deferredDictionaryEmissionService,
        SourceMappedLineEmissionService sourceMappedLineEmissionService,
        ParentStackEmissionService parentStackEmissionService)
    {
        _adapter = adapter;
        _deferredDictionaryAdapter = deferredDictionaryAdapter;
        _deferredDictionaryEmissionService = deferredDictionaryEmissionService;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
        _parentStackEmissionService = parentStackEmissionService;
    }

    public bool TryEmitPropertyElementScaffold(
        ResolvedPropertyElementAssignment assignment,
        string nodeReference,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        string topDownAttachValueToken,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        if (_deferredDictionaryEmissionService.TryEmitDictionaryMergePropertyElement(
                assignment,
                nodeReference,
                context,
                ref nodeCounter,
                emitNode,
                buildAttachedNodeValueExpression))
        {
            return true;
        }

        if (!assignment.IsCollectionAdd && assignment.CollectionAddInstructions.IsDefaultOrEmpty)
        {
            return false;
        }

        var instruction = assignment.CollectionAddInstructions.IsDefaultOrEmpty
            ? null
            : assignment.CollectionAddInstructions[0];

        foreach (var valueNode in assignment.ObjectValues)
        {
            var valueReference = emitNode(valueNode, context, ref nodeCounter, existingVariableName: null, topDownAttachmentTemplate: null, completeNameScopeOnNodeCompletion: false);
            var valueExpression = buildAttachedNodeValueExpression(
                valueNode,
                valueReference,
                context,
                _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences));
            var collectionReference = nodeReference + "." + assignment.PropertyName;

            if (_adapter.ShouldApplyMergedResourceInclude(valueNode))
            {
                _sourceMappedLineEmissionService.AppendLine(
                    context.SourceBuilder,
                    context.Indent,
                    context.EmitDebugLineDirectives,
                    context.LineDirectiveFilePath,
                    assignment.Line,
                    _adapter.BuildApplyMergedResourceIncludeStatement(nodeReference, valueExpression, context.BaseUriExpression));
                continue;
            }

            if (_adapter.ShouldApplyStyleInclude(valueNode))
            {
                _sourceMappedLineEmissionService.AppendLine(
                    context.SourceBuilder,
                    context.Indent,
                    context.EmitDebugLineDirectives,
                    context.LineDirectiveFilePath,
                    assignment.Line,
                    _adapter.BuildApplyStyleIncludeStatement(collectionReference, nodeReference, valueExpression, context.BaseUriExpression));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(valueNode.KeyExpression) &&
                instruction?.ParameterTypeName.Contains("KeyValuePair<", StringComparison.Ordinal) == true)
            {
                var normalizedKeyExpression = _deferredDictionaryAdapter.NormalizeDictionaryKeyExpression(
                    assignment.PropertyName,
                    valueNode.KeyExpression!);
                if (TryEmitDeferredDictionaryEntry(
                        valueNode,
                        nodeReference,
                        collectionReference,
                        assignment.PropertyName,
                        normalizedKeyExpression,
                        assignment.Line,
                        context,
                        ref nodeCounter,
                        emitNode))
                {
                    continue;
                }

                _sourceMappedLineEmissionService.AppendLine(
                    context.SourceBuilder,
                    context.Indent,
                    context.EmitDebugLineDirectives,
                    context.LineDirectiveFilePath,
                    assignment.Line,
                    _deferredDictionaryAdapter.BuildDictionaryAddStatement(
                        collectionReference,
                        normalizedKeyExpression,
                        valueExpression,
                        context.BaseUriExpression,
                        isShared: !valueNode.HasSemantic(ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource)));
                continue;
            }

            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                assignment.Line,
                _adapter.BuildCollectionAddStatement(collectionReference, valueExpression, instruction));
        }

        return assignment.ObjectValues.Length > 0;
    }

    public bool TryEmitChildAttachmentScaffold(
        ResolvedObjectNode parentNode,
        string parentReference,
        ResolvedObjectNode childNode,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        string topDownAttachValueToken,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        if (parentNode.ChildAttachmentMode == ResolvedChildAttachmentMode.None)
        {
            return false;
        }

        var collectionReference = string.IsNullOrWhiteSpace(parentNode.ContentPropertyName)
            ? parentReference
            : parentReference + "." + parentNode.ContentPropertyName;
        var collectionInstruction = parentNode.ChildAddInstructions.IsDefaultOrEmpty
            ? null
            : parentNode.ChildAddInstructions[0];

        if (childNode.UseTopDownInitialization)
        {
            var topDownAttachmentTemplate = BuildChildAttachmentStatementTemplate(
                parentNode,
                parentReference,
                childNode,
                topDownAttachValueToken,
                collectionReference,
                collectionInstruction,
                context.BaseUriExpression);
            _ = emitNode(
                childNode,
                context,
                ref nodeCounter,
                existingVariableName: null,
                topDownAttachmentTemplate: topDownAttachmentTemplate,
                completeNameScopeOnNodeCompletion: false);
            return true;
        }

        var childReference = emitNode(childNode, context, ref nodeCounter, existingVariableName: null, topDownAttachmentTemplate: null, completeNameScopeOnNodeCompletion: false);
        var valueExpression = buildAttachedNodeValueExpression(
            childNode,
            childReference,
            context,
            _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences));

        if (_adapter.ShouldApplyMergedResourceInclude(childNode))
        {
            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                childNode.Line,
                _adapter.BuildApplyMergedResourceIncludeStatement(parentReference, valueExpression, context.BaseUriExpression));
            return true;
        }

        if (_adapter.ShouldApplyStyleInclude(childNode))
        {
            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                childNode.Line,
                _adapter.BuildApplyStyleIncludeStatement(collectionReference, parentReference, valueExpression, context.BaseUriExpression));
            return true;
        }

        if (parentNode.ChildAttachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            var keyExpression = string.IsNullOrWhiteSpace(childNode.KeyExpression)
                ? "\"__missing_key\""
                : childNode.KeyExpression!;
            var normalizedKeyExpression = _deferredDictionaryAdapter.NormalizeDictionaryKeyExpression(
                parentNode.ContentPropertyName ?? string.Empty,
                keyExpression);
            if (TryEmitDeferredDictionaryEntry(
                    childNode,
                    parentReference,
                    collectionReference,
                    parentNode.ContentPropertyName,
                    normalizedKeyExpression,
                    childNode.Line,
                    context,
                    ref nodeCounter,
                    emitNode))
            {
                return true;
            }

            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                childNode.Line,
                _deferredDictionaryAdapter.BuildDictionaryAddStatement(
                    collectionReference,
                    normalizedKeyExpression,
                    valueExpression,
                    context.BaseUriExpression,
                    isShared: !childNode.HasSemantic(ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource)));
            return true;
        }

        if (_adapter.TryBuildSpecialChildAttachmentStatement(
                parentNode,
                parentReference,
                childNode,
                valueExpression,
                collectionInstruction,
                out var specialStatement))
        {
            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                childNode.Line,
                specialStatement);
            return true;
        }

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            childNode.Line,
            _adapter.BuildCollectionAddStatement(collectionReference, valueExpression, collectionInstruction));
        return true;
    }

    private string BuildChildAttachmentStatementTemplate(
        ResolvedObjectNode parentNode,
        string parentReference,
        ResolvedObjectNode childNode,
        string topDownAttachValueToken,
        string collectionReference,
        ResolvedCollectionAddInstruction? collectionInstruction,
        string documentUriExpression)
    {
        if (_adapter.ShouldApplyMergedResourceInclude(childNode))
        {
            return _adapter.BuildApplyMergedResourceIncludeStatement(parentReference, topDownAttachValueToken, documentUriExpression);
        }

        if (_adapter.ShouldApplyStyleInclude(childNode))
        {
            return _adapter.BuildApplyStyleIncludeStatement(collectionReference, parentReference, topDownAttachValueToken, documentUriExpression);
        }

        if (parentNode.ChildAttachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            var keyExpression = string.IsNullOrWhiteSpace(childNode.KeyExpression)
                ? "\"__missing_key\""
                : childNode.KeyExpression!;
            return _deferredDictionaryAdapter.BuildDictionaryAddStatement(
                collectionReference,
                keyExpression,
                topDownAttachValueToken,
                documentUriExpression,
                isShared: !childNode.HasSemantic(ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource));
        }

        if (_adapter.TryBuildSpecialChildAttachmentStatement(
                parentNode,
                parentReference,
                childNode,
                topDownAttachValueToken,
                collectionInstruction,
                out var specialStatement))
        {
            return specialStatement;
        }

        return _adapter.BuildCollectionAddStatement(collectionReference, topDownAttachValueToken, collectionInstruction);
    }

    private bool TryEmitDeferredDictionaryEntry(
        ResolvedObjectNode valueNode,
        string parentReference,
        string collectionReference,
        string? propertyName,
        string normalizedKeyExpression,
        int line,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        EmitObjectNodeFromSharedContextDelegate emitNode)
    {
        if (!valueNode.HasSemantic(ResolvedObjectNodeSemanticFlags.IsNotSharedDeferredResource))
        {
            return false;
        }

        var suffix = nodeCounter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var deferredContentReference = "__deferredResourceContent" + suffix;
        var deferredServiceProviderReference = "__deferredResourceServiceProvider" + suffix;
        var deferredNameScopeReference = "__deferredResourceNameScope" + suffix;
        var needsNameScope = RequiresDeferredResourceNameScope(valueNode);
        var nestedIndent = context.Indent + "    ";
        var deferredParentStackExpression = BuildDeferredResourceParentStackExpression(
            collectionReference,
            context.ParentStackReferences);

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            "var " + deferredContentReference + " = global::XamlToCSharpGenerator.Runtime.SourceGenDeferredContentRuntime.CreateShared(__deferredServiceProvider =>");
        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            "{");

        if (needsNameScope)
        {
            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                nestedIndent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                line,
                "var " + deferredNameScopeReference + " = global::XamlToCSharpGenerator.Runtime.SourceGenDeferredServiceProviderFactory.CreateDeferredResourceNameScope(__deferredServiceProvider);");
        }

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            nestedIndent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            "var " + deferredServiceProviderReference + " = global::XamlToCSharpGenerator.Runtime.SourceGenDeferredServiceProviderFactory.CreateDeferredResourceServiceProvider(__deferredServiceProvider, " +
            context.RootReference + ", " +
            parentReference + ", " +
            context.BaseUriExpression + ", " +
            deferredParentStackExpression +
            (needsNameScope ? ", " + deferredNameScopeReference : string.Empty) +
            ");");

        var deferredContext = context with
        {
            Indent = nestedIndent,
            EmitNameScopeRegistration = needsNameScope,
            NameScopeReference = needsNameScope ? deferredNameScopeReference : null,
            ServiceProviderReference = deferredServiceProviderReference,
            ParentStackReferences = BuildDeferredResourceParentStackReferences(
                collectionReference,
                context.ParentStackReferences),
            IntermediateRootReference = parentReference
        };

        var valueReference = emitNode(
            valueNode,
            deferredContext,
            ref nodeCounter,
            existingVariableName: null,
            topDownAttachmentTemplate: null,
            completeNameScopeOnNodeCompletion: needsNameScope);

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            nestedIndent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            "return " + valueReference + ";");
        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            "});");

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            _deferredDictionaryAdapter.BuildDictionaryAddStatement(
                collectionReference,
                normalizedKeyExpression,
                deferredContentReference,
                context.BaseUriExpression,
                isShared: false));
        return true;
    }

    private ImmutableArray<string> BuildDeferredResourceParentStackReferences(
        string collectionReference,
        ImmutableArray<string> parentStackReferences)
    {
        var builder = ImmutableArray.CreateBuilder<string>(1 + parentStackReferences.Length);
        builder.Add(collectionReference);
        for (var index = parentStackReferences.Length - 1; index >= 0; index--)
        {
            builder.Add(parentStackReferences[index]);
        }

        return builder.ToImmutable();
    }

    private string BuildDeferredResourceParentStackExpression(
        string collectionReference,
        ImmutableArray<string> parentStackReferences)
    {
        return _parentStackEmissionService.BuildParentStackExpression(
            BuildDeferredResourceParentStackReferences(collectionReference, parentStackReferences));
    }

    private static bool RequiresDeferredResourceNameScope(ResolvedObjectNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            return true;
        }

        if (!node.Children.IsDefaultOrEmpty)
        {
            for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
            {
                if (RequiresDeferredResourceNameScope(node.Children[childIndex]))
                {
                    return true;
                }
            }
        }

        if (!node.PropertyElementAssignments.IsDefaultOrEmpty)
        {
            for (var assignmentIndex = 0; assignmentIndex < node.PropertyElementAssignments.Length; assignmentIndex++)
            {
                var objectValues = node.PropertyElementAssignments[assignmentIndex].ObjectValues;
                for (var valueIndex = 0; valueIndex < objectValues.Length; valueIndex++)
                {
                    if (RequiresDeferredResourceNameScope(objectValues[valueIndex]))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
