using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStudioOperationStatus(
    long OperationId,
    string? RequestId,
    long? CorrelationId,
    SourceGenStudioOperationState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    SourceGenStudioUpdateRequest Request,
    SourceGenStudioUpdateResult? Result = null,
    IReadOnlyList<string>? Diagnostics = null);
