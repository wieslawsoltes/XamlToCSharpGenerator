using System;
using System.Collections.Generic;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ObjectNodeMemberEmissionService
{
    private readonly FrameworkValueOperationEmissionService _valueOperationEmissionService;
    private readonly ClrObjectNodeEmissionService _clrObjectNodeEmissionService;
    private readonly CollectionAttachmentEmissionService _collectionAttachmentEmissionService;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;
    private readonly ParentStackEmissionService _parentStackEmissionService;

    public ObjectNodeMemberEmissionService(
        FrameworkValueOperationEmissionService valueOperationEmissionService,
        ClrObjectNodeEmissionService clrObjectNodeEmissionService,
        CollectionAttachmentEmissionService collectionAttachmentEmissionService,
        SourceMappedLineEmissionService sourceMappedLineEmissionService,
        ParentStackEmissionService parentStackEmissionService)
    {
        _valueOperationEmissionService = valueOperationEmissionService;
        _clrObjectNodeEmissionService = clrObjectNodeEmissionService;
        _collectionAttachmentEmissionService = collectionAttachmentEmissionService;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
        _parentStackEmissionService = parentStackEmissionService;
    }

    public void EmitPropertyAssignments(
        ResolvedObjectNode node,
        string nodeReference,
        FrameworkObjectGraphEmissionContext context,
        bool isExistingRootInstance)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            var emittedInObjectInitializer = _clrObjectNodeEmissionService.CanEmitResolvedValueInClrObjectInitializer(assignment);
            if (emittedInObjectInitializer &&
                !isExistingRootInstance)
            {
                continue;
            }

            var parentStackExpression = _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences);
            var targetPropertyExpression = BuildMarkupContextTargetPropertyExpression(assignment);
            var expandedValueExpression = _clrObjectNodeEmissionService.ExpandMarkupContextExpression(
                assignment.ValueExpression,
                context.ServiceProviderReference,
                context.RootReference,
                context.IntermediateRootReference,
                nodeReference,
                targetPropertyExpression,
                context.BaseUriExpression,
                parentStackExpression);
            var emittedValueExpression = BuildBindingAwareValueExpression(
                expandedValueExpression,
                assignment.ValueKind,
                assignment.PreserveBindingValue,
                context);

            string? statement = null;
            if (_valueOperationEmissionService.HasFrameworkPropertyOperation(assignment))
            {
                var usesBindingPath =
                    !assignment.PreserveBindingValue &&
                    (assignment.ValueKind == ResolvedValueKind.Binding ||
                     assignment.ValueKind == ResolvedValueKind.TemplateBinding ||
                     assignment.ValueKind == ResolvedValueKind.DynamicResourceBinding);
                var bindingAnchorExpression = BuildBindingAnchorExpression(nodeReference, parentStackExpression);
                statement = usesBindingPath
                    ? _valueOperationEmissionService.BuildFrameworkBindingAssignmentStatement(nodeReference, emittedValueExpression, assignment, bindingAnchorExpression)
                    : _valueOperationEmissionService.BuildFrameworkSetValueStatement(nodeReference, emittedValueExpression, assignment);
            }
            else if (_clrObjectNodeEmissionService.TryBuildDirectClrPropertyAssignment(nodeReference, emittedValueExpression, assignment, out var clrStatement))
            {
                statement = clrStatement;
            }

            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            if (emittedInObjectInitializer &&
                isExistingRootInstance)
            {
                statement = "if (!__rootConstructedWithInitializer) " + statement;
            }

            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                assignment.Line,
                statement!);
        }
    }

    public void EmitPropertyElementAssignments(
        ResolvedObjectNode node,
        string nodeReference,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        string topDownAttachValueToken,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        var clearedCollectionProperties = new HashSet<string>(StringComparer.Ordinal);
        var clearedDictionaryProperties = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in node.PropertyElementAssignments)
        {
            var parentStackExpression = _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences);
            if (IsDictionaryLikePropertyElementAssignment(assignment) &&
                clearedDictionaryProperties.Add(assignment.PropertyName))
            {
                _sourceMappedLineEmissionService.AppendLine(
                    context.SourceBuilder,
                    context.Indent,
                    context.EmitDebugLineDirectives,
                    context.LineDirectiveFilePath,
                    assignment.Line,
                    "if (__replaceExistingCollections) __AXSGObjectGraph.TryClearDictionaryEntries(" +
                    nodeReference +
                    "." +
                    assignment.PropertyName +
                    ");");
            }

            if (!IsDictionaryLikePropertyElementAssignment(assignment) &&
                assignment.IsCollectionAdd &&
                clearedCollectionProperties.Add(assignment.PropertyName))
            {
                _sourceMappedLineEmissionService.AppendLine(
                    context.SourceBuilder,
                    context.Indent,
                    context.EmitDebugLineDirectives,
                    context.LineDirectiveFilePath,
                    assignment.Line,
                    "if (__replaceExistingCollections) __AXSGObjectGraph.TryClearCollection(" +
                    nodeReference +
                    "." +
                    assignment.PropertyName +
                    ");");
            }

            if (_collectionAttachmentEmissionService.TryEmitPropertyElementScaffold(
                    assignment,
                    nodeReference,
                    context,
                    ref nodeCounter,
                    topDownAttachValueToken,
                    emitNode,
                    buildAttachedNodeValueExpression))
            {
                continue;
            }

            if (assignment.ObjectValues.IsDefaultOrEmpty)
            {
                continue;
            }

            var valueNode = assignment.ObjectValues[0];
            var syntheticAssignment = new ResolvedPropertyAssignment(
                assignment.PropertyName,
                valueNode.FactoryExpression ?? string.Empty,
                assignment.ClrPropertyOwnerTypeName,
                assignment.ClrPropertyTypeName,
                assignment.Line,
                assignment.Column,
                assignment.Condition,
                ValueKind: valueNode.IsBindingObjectNode
                    ? ResolvedValueKind.Binding
                    : ResolvedValueKind.Unknown,
                ValueRequirements: valueNode.FactoryValueRequirements,
                FrameworkPropertyOperation: assignment.FrameworkPropertyOperation,
                PreserveBindingValue: assignment.PreserveBindingValue);
            var valueExpression = TryBuildDirectBindingFactoryValueExpression(
                valueNode,
                nodeReference,
                syntheticAssignment,
                context,
                parentStackExpression,
                out var directBindingFactoryExpression)
                ? directBindingFactoryExpression
                : BuildAttachedPropertyElementValueExpression(
                    valueNode,
                    context,
                    ref nodeCounter,
                    parentStackExpression,
                    emitNode,
                    buildAttachedNodeValueExpression);
            var emittedValueExpression = BuildBindingAwareValueExpression(
                valueExpression,
                syntheticAssignment.ValueKind,
                syntheticAssignment.PreserveBindingValue,
                context);

            string? statement = null;
            if (_valueOperationEmissionService.HasFrameworkPropertyOperation(assignment))
            {
                var usesBindingPath =
                    !syntheticAssignment.PreserveBindingValue &&
                    (syntheticAssignment.ValueKind == ResolvedValueKind.Binding ||
                     syntheticAssignment.ValueKind == ResolvedValueKind.TemplateBinding ||
                     syntheticAssignment.ValueKind == ResolvedValueKind.DynamicResourceBinding);
                var bindingAnchorExpression = BuildBindingAnchorExpression(nodeReference, parentStackExpression);
                statement = usesBindingPath
                    ? _valueOperationEmissionService.BuildFrameworkBindingAssignmentStatement(nodeReference, emittedValueExpression, syntheticAssignment, bindingAnchorExpression)
                    : _valueOperationEmissionService.BuildFrameworkSetValueStatement(nodeReference, emittedValueExpression, syntheticAssignment);
            }
            else if (_clrObjectNodeEmissionService.TryBuildDirectClrPropertyAssignment(nodeReference, emittedValueExpression, syntheticAssignment, out var clrStatement))
            {
                statement = clrStatement;
            }

            if (string.IsNullOrWhiteSpace(statement))
            {
                continue;
            }

            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                assignment.Line,
                statement!);
        }
    }

    public void AttachBindingNodeNameScope(
        ResolvedObjectNode node,
        string nodeReference,
        FrameworkObjectGraphEmissionContext context)
    {
        if (!node.IsBindingObjectNode ||
            !context.EmitNameScopeRegistration ||
            string.IsNullOrWhiteSpace(context.NameScopeReference))
        {
            return;
        }

        var bindingVariableName = "__bindingBase_" + nodeReference.TrimStart('_').Replace('.', '_');

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            node.Line,
            "if ((object)" +
            nodeReference +
            " is global::Avalonia.Data.BindingBase " +
            bindingVariableName +
            ") " +
            bindingVariableName +
            ".NameScope = new global::System.WeakReference<global::Avalonia.Controls.INameScope>(" +
            context.NameScopeReference +
            ");");
    }

    private static bool IsDictionaryLikePropertyElementAssignment(ResolvedPropertyElementAssignment assignment)
    {
        if (assignment.IsDictionaryMerge)
        {
            return true;
        }

        if (assignment.CollectionAddInstructions.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var index = 0; index < assignment.CollectionAddInstructions.Length; index++)
        {
            var parameterTypeName = assignment.CollectionAddInstructions[index].ParameterTypeName;
            if (!string.IsNullOrWhiteSpace(parameterTypeName) &&
                parameterTypeName.Contains("KeyValuePair<", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private string BuildMarkupContextTargetPropertyExpression(ResolvedPropertyAssignment assignment)
    {
        var frameworkPropertyExpression = _valueOperationEmissionService.BuildFrameworkPropertyExpression(assignment);
        if (!string.IsNullOrWhiteSpace(frameworkPropertyExpression))
        {
            return _valueOperationEmissionService.BuildMarkupContextTargetPropertyExpression(frameworkPropertyExpression);
        }

        if (!assignment.ValueRequirements.NeedsProvideValueTarget ||
            string.IsNullOrWhiteSpace(assignment.ClrPropertyOwnerTypeName) ||
            string.IsNullOrWhiteSpace(assignment.ClrPropertyTypeName) ||
            assignment.PropertyName.StartsWith("SetClass:", StringComparison.Ordinal))
        {
            return "null";
        }

        if (assignment.IsInitOnlyClrProperty &&
            !string.IsNullOrWhiteSpace(assignment.ClrSetterUnsafeAccessorMethodName))
        {
            return "global::XamlToCSharpGenerator.Runtime.SourceGenProvideValueTargetPropertyFactory.CreateReadOnly<" +
                   assignment.ClrPropertyOwnerTypeName +
                   ", " +
                   assignment.ClrPropertyTypeName +
                   ">(\"" +
                   assignment.PropertyName +
                   "\")";
        }

        return "global::XamlToCSharpGenerator.Runtime.SourceGenProvideValueTargetPropertyFactory.CreateWritable<" +
               assignment.ClrPropertyOwnerTypeName +
               ", " +
               assignment.ClrPropertyTypeName +
               ">(\"" +
               assignment.PropertyName +
               "\", static (__target, __value) => __target." +
               assignment.PropertyName +
               " = __value)";
    }

    private string BuildAttachedPropertyElementValueExpression(
        ResolvedObjectNode valueNode,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        string parentStackExpression,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        var valueReference = emitNode(
            valueNode,
            context,
            ref nodeCounter,
            existingVariableName: null,
            topDownAttachmentTemplate: null,
            completeNameScopeOnNodeCompletion: false);
        return buildAttachedNodeValueExpression(
            valueNode,
            valueReference,
            context,
            parentStackExpression);
    }

    private bool TryBuildDirectBindingFactoryValueExpression(
        ResolvedObjectNode valueNode,
        string targetNodeReference,
        ResolvedPropertyAssignment syntheticAssignment,
        FrameworkObjectGraphEmissionContext context,
        string parentStackExpression,
        out string valueExpression)
    {
        valueExpression = string.Empty;
        if (!valueNode.IsBindingObjectNode ||
            string.IsNullOrWhiteSpace(valueNode.FactoryExpression) ||
            !valueNode.PropertyAssignments.IsDefaultOrEmpty ||
            !valueNode.PropertyElementAssignments.IsDefaultOrEmpty ||
            !valueNode.EventSubscriptions.IsDefaultOrEmpty ||
            !valueNode.Children.IsDefaultOrEmpty ||
            valueNode.UseServiceProviderConstructor ||
            valueNode.UseTopDownInitialization)
        {
            return false;
        }

        var targetPropertyExpression = BuildMarkupContextTargetPropertyExpression(syntheticAssignment);
        valueExpression = _clrObjectNodeEmissionService.ExpandMarkupContextExpression(
            valueNode.FactoryExpression!,
            context.ServiceProviderReference,
            context.RootReference,
            context.IntermediateRootReference,
            targetNodeReference,
            targetPropertyExpression,
            context.BaseUriExpression,
            parentStackExpression);
        return true;
    }

    private static string BuildBindingAnchorExpression(string nodeReference, string parentStackExpression)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ResolveBindingAnchor(" +
               nodeReference +
               ", " +
               parentStackExpression +
               ")";
    }

    private string BuildBindingAwareValueExpression(
        string valueExpression,
        ResolvedValueKind valueKind,
        bool preserveBindingValue,
        FrameworkObjectGraphEmissionContext context)
    {
        if (!ShouldAttachBindingMetadata(valueKind, preserveBindingValue))
        {
            return valueExpression;
        }

        return _valueOperationEmissionService.BuildBindingMetadataAttachmentExpression(
            valueExpression,
            context.NameScopeReference,
            context.BindingXmlNamespaceMapReference);
    }

    private static bool ShouldAttachBindingMetadata(ResolvedValueKind valueKind, bool preserveBindingValue)
    {
        return preserveBindingValue ||
               valueKind == ResolvedValueKind.Binding ||
               valueKind == ResolvedValueKind.TemplateBinding ||
               valueKind == ResolvedValueKind.DynamicResourceBinding;
    }
}
