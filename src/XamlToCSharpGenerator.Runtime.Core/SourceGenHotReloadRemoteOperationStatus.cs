using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadRemoteOperationStatus(
    long OperationId,
    string? RequestId,
    long? CorrelationId,
    SourceGenStudioOperationState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    SourceGenHotReloadRemoteUpdateRequest Request,
    SourceGenHotReloadRemoteUpdateResult? Result = null,
    IReadOnlyList<string>? Diagnostics = null);
