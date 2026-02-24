using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStudioUpdateResult(
    bool Succeeded,
    string Message,
    long OperationId,
    string? RequestId,
    long? CorrelationId,
    SourceGenStudioOperationState State,
    bool SourcePersisted,
    bool LocalUpdateObserved,
    bool RuntimeFallbackApplied,
    string? BuildUri = null,
    Type? TargetType = null,
    Exception? Error = null,
    IReadOnlyList<string>? Diagnostics = null);
