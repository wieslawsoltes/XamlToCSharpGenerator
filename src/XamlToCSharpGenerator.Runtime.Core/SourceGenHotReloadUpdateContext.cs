using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadUpdateContext
{
    public SourceGenHotReloadUpdateContext(
        SourceGenHotReloadTrigger trigger,
        IReadOnlyList<Type>? requestedTypes,
        IReadOnlyList<Type> reloadedTypes,
        int operationCount,
        long? operationId = null,
        string? requestId = null,
        long? correlationId = null)
    {
        Trigger = trigger;
        RequestedTypes = requestedTypes;
        ReloadedTypes = reloadedTypes ?? throw new ArgumentNullException(nameof(reloadedTypes));
        OperationCount = Math.Max(0, operationCount);
        OperationId = operationId;
        RequestId = requestId;
        CorrelationId = correlationId;
    }

    public SourceGenHotReloadTrigger Trigger { get; }

    public IReadOnlyList<Type>? RequestedTypes { get; }

    public IReadOnlyList<Type> ReloadedTypes { get; }

    public int OperationCount { get; }

    public long? OperationId { get; }

    public string? RequestId { get; }

    public long? CorrelationId { get; }
}
