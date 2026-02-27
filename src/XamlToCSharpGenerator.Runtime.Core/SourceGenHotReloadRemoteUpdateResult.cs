using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadRemoteUpdateResult(
    long OperationId,
    string? RequestId,
    long? CorrelationId,
    SourceGenStudioOperationState State,
    bool IsSuccess,
    string Message,
    IReadOnlyList<string>? Diagnostics = null);
