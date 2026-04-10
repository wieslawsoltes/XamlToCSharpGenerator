using System.Collections.Generic;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ObjectNodeEventSubscriptionEmissionService
{
    private readonly IXamlFrameworkEventSubscriptionEmitterAdapter _adapter;
    private readonly EventBindingEmissionService _eventBindingEmissionService;
    private readonly SourceMappedLineEmissionService _sourceMappedLineEmissionService;

    public ObjectNodeEventSubscriptionEmissionService(
        IXamlFrameworkEventSubscriptionEmitterAdapter adapter,
        EventBindingEmissionService eventBindingEmissionService,
        SourceMappedLineEmissionService sourceMappedLineEmissionService)
    {
        _adapter = adapter;
        _eventBindingEmissionService = eventBindingEmissionService;
        _sourceMappedLineEmissionService = sourceMappedLineEmissionService;
    }

    public void EmitSubscriptions(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedObjectNode node,
        string nodeReference,
        string rootReference,
        IReadOnlyDictionary<string, string> emittedEventBindingMethodNames,
        bool emitDebugLineDirectives,
        string lineDirectiveFilePath)
    {
        for (var index = 0; index < node.EventSubscriptions.Length; index++)
        {
            var eventSubscription = node.EventSubscriptions[index];
            var emittedMethodName = _eventBindingEmissionService.ResolveEmittedMethodName(
                eventSubscription,
                emittedEventBindingMethodNames);
            var statements = _adapter.BuildSubscriptionStatements(
                nodeReference,
                rootReference,
                emittedMethodName,
                eventSubscription);
            for (var statementIndex = 0; statementIndex < statements.Length; statementIndex++)
            {
                _sourceMappedLineEmissionService.AppendLine(
                    sourceBuilder,
                    indent,
                    emitDebugLineDirectives,
                    lineDirectiveFilePath,
                    eventSubscription.Line,
                    statements[statementIndex]);
            }
        }
    }
}
