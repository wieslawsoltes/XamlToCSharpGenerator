using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadRemoteUpdateRequest(
    long OperationId,
    string? RequestId,
    long? CorrelationId,
    bool ApplyAll,
    IReadOnlyList<string> TypeNames,
    IReadOnlyList<string> BuildUris,
    string? Trigger = null);
