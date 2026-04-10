using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ObjectNodeLifecycleEmissionService
{
    private readonly IXamlFrameworkObjectNodeLifecycleEmitterAdapter _adapter;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;

    public ObjectNodeLifecycleEmissionService(
        IXamlFrameworkObjectNodeLifecycleEmitterAdapter adapter,
        SourceMappedLineEmissionService sourceMappedLineEmissionService)
    {
        _adapter = adapter;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
    }

    public void EmitNodeStart(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedObjectNode node,
        string nodeReference,
        string? nameScopeReference,
        bool emitNameScopeRegistration,
        bool emitDebugLineDirectives,
        string lineDirectiveFilePath,
        int scopedIndex)
    {
        _sourceMappedLineEmissionService.AppendLine(
            sourceBuilder,
            indent,
            emitDebugLineDirectives,
            lineDirectiveFilePath,
            node.Line,
            _adapter.BuildBeginInitStatement(nodeReference));

        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            _sourceMappedLineEmissionService.AppendLine(
                sourceBuilder,
                indent,
                emitDebugLineDirectives,
                lineDirectiveFilePath,
                node.Line,
                _adapter.BuildAssignObjectNameStatement(nodeReference, node.Name!));

            if (emitNameScopeRegistration && !string.IsNullOrWhiteSpace(nameScopeReference))
            {
                _sourceMappedLineEmissionService.AppendLine(
                    sourceBuilder,
                    indent,
                    emitDebugLineDirectives,
                    lineDirectiveFilePath,
                    node.Line,
                    _adapter.BuildRegisterNameScopeEntryStatement(nameScopeReference!, node.Name!, nodeReference));
            }
        }

        if (emitNameScopeRegistration && !string.IsNullOrWhiteSpace(nameScopeReference))
        {
            _sourceMappedLineEmissionService.AppendLine(
                sourceBuilder,
                indent,
                emitDebugLineDirectives,
                lineDirectiveFilePath,
                node.Line,
                _adapter.BuildAttachNameScopeStatement(nodeReference, nameScopeReference!, scopedIndex));
        }
    }

    public void EmitNodeEnd(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedObjectNode node,
        string nodeReference,
        bool emitDebugLineDirectives,
        string lineDirectiveFilePath)
    {
        _sourceMappedLineEmissionService.AppendLine(
            sourceBuilder,
            indent,
            emitDebugLineDirectives,
            lineDirectiveFilePath,
            node.Line,
            _adapter.BuildEndInitStatement(nodeReference));
    }
}
