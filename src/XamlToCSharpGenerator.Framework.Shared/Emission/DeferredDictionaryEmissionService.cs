using System;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class DeferredDictionaryEmissionService
{
    private readonly IXamlFrameworkDeferredDictionaryEmitterAdapter _adapter;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;
    private readonly ParentStackEmissionService _parentStackEmissionService;

    public DeferredDictionaryEmissionService(
        IXamlFrameworkDeferredDictionaryEmitterAdapter adapter,
        SourceMappedLineEmissionService sourceMappedLineEmissionService,
        ParentStackEmissionService parentStackEmissionService)
    {
        _adapter = adapter;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
        _parentStackEmissionService = parentStackEmissionService;
    }

    public bool TryEmitDictionaryMergePropertyElement(
        ResolvedPropertyElementAssignment assignment,
        string nodeReference,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        if (!assignment.IsDictionaryMerge || assignment.ObjectValues.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var index = 0; index < assignment.ObjectValues.Length; index++)
        {
            var childNode = assignment.ObjectValues[index];
            var childReference = emitNode(childNode, context, ref nodeCounter, existingVariableName: null, topDownAttachmentTemplate: null, completeNameScopeOnNodeCompletion: false);
            var valueExpression = buildAttachedNodeValueExpression(
                childNode,
                childReference,
                context,
                _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences));
            var propertyExpression = string.IsNullOrWhiteSpace(assignment.PropertyName)
                ? "null"
                : "\"" + assignment.PropertyName.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            var deferredServiceProviderExpression = _adapter.BuildCreateDeferredServiceProviderExpression(
                context.ServiceProviderReference,
                context.RootReference,
                nodeReference,
                propertyExpression,
                context.BaseUriExpression,
                _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences));
            _ = deferredServiceProviderExpression;
            _sourceMappedLineEmissionService.AppendLine(
                context.SourceBuilder,
                context.Indent,
                context.EmitDebugLineDirectives,
                context.LineDirectiveFilePath,
                assignment.Line,
                nodeReference + "." + assignment.PropertyName + " = " + BuildClrTypedValueExpression(assignment.ClrPropertyTypeName, valueExpression) + ";");
        }

        return true;
    }

    public void EmitDeferredResourceContent(
        string dictionaryReference,
        string propertyName,
        string keyExpression,
        string valueExpression,
        FrameworkObjectGraphEmissionContext context,
        int line,
        bool isShared = true)
    {
        var normalizedKeyExpression = _adapter.NormalizeDictionaryKeyExpression(propertyName, keyExpression);
        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            line,
            _adapter.BuildDictionaryAddStatement(
                dictionaryReference,
                normalizedKeyExpression,
                valueExpression,
                context.BaseUriExpression,
                isShared));
    }

    private static string BuildClrTypedValueExpression(string? clrPropertyTypeName, string valueExpression)
    {
        if (string.IsNullOrWhiteSpace(clrPropertyTypeName))
        {
            return valueExpression;
        }

        var normalizedTypeName = clrPropertyTypeName.Trim();
        return normalizedTypeName switch
        {
            "global::System.Object" or
            "global::System.Object?" or
            "object" or
            "object?" => valueExpression,
            _ => "(" + normalizedTypeName + ")(" + valueExpression + ")"
        };
    }
}
