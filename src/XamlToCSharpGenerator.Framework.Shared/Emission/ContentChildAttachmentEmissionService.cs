using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ContentChildAttachmentEmissionService
{
    private readonly ClrObjectNodeEmissionService _clrObjectNodeEmissionService;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;
    private readonly ParentStackEmissionService _parentStackEmissionService;

    public ContentChildAttachmentEmissionService(
        ClrObjectNodeEmissionService clrObjectNodeEmissionService,
        SourceMappedLineEmissionService sourceMappedLineEmissionService,
        ParentStackEmissionService parentStackEmissionService)
    {
        _clrObjectNodeEmissionService = clrObjectNodeEmissionService;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
        _parentStackEmissionService = parentStackEmissionService;
    }

    public bool TryEmitContentAttachmentScaffold(
        ResolvedObjectNode parentNode,
        string parentReference,
        ResolvedObjectNode childNode,
        FrameworkObjectGraphEmissionContext context,
        ref int nodeCounter,
        string topDownAttachValueToken,
        EmitObjectNodeFromSharedContextDelegate emitNode,
        BuildAttachedNodeValueExpressionFromContextDelegate buildAttachedNodeValueExpression)
    {
        if (parentNode.ChildAttachmentMode != ResolvedChildAttachmentMode.Content ||
            string.IsNullOrWhiteSpace(parentNode.ContentPropertyName))
        {
            return false;
        }

        var assignment = new ResolvedPropertyAssignment(
            parentNode.ContentPropertyName!,
            topDownAttachValueToken,
            ClrPropertyOwnerTypeName: parentNode.TypeName,
            ClrPropertyTypeName: parentNode.ContentPropertyTypeName,
            Line: childNode.Line,
            Column: childNode.Column);

        if (!_clrObjectNodeEmissionService.TryBuildDirectClrPropertyAssignment(parentReference, topDownAttachValueToken, assignment, out var topDownStatement))
        {
            return false;
        }

        if (childNode.UseTopDownInitialization)
        {
            _ = emitNode(
                childNode,
                context,
                ref nodeCounter,
                existingVariableName: null,
                topDownAttachmentTemplate: topDownStatement,
                completeNameScopeOnNodeCompletion: false);
            return true;
        }

        var childReference = emitNode(childNode, context, ref nodeCounter, existingVariableName: null, topDownAttachmentTemplate: null, completeNameScopeOnNodeCompletion: false);
        var valueExpression = buildAttachedNodeValueExpression(
            childNode,
            childReference,
            context,
            _parentStackEmissionService.BuildParentStackExpression(context.ParentStackReferences));

        assignment = assignment with
        {
            ValueExpression = valueExpression
        };

        if (!_clrObjectNodeEmissionService.TryBuildDirectClrPropertyAssignment(parentReference, valueExpression, assignment, out var statement))
        {
            return false;
        }

        _sourceMappedLineEmissionService.AppendLine(
            context.SourceBuilder,
            context.Indent,
            context.EmitDebugLineDirectives,
            context.LineDirectiveFilePath,
            childNode.Line,
            statement);
        return true;
    }
}
