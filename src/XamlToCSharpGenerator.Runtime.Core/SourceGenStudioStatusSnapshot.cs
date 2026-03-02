using System;
using System.Collections.Generic;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenStudioStatusSnapshot(
    bool IsEnabled,
    Guid SessionId,
    SourceGenStudioOperationState CurrentState,
    int RegisteredDocumentCount,
    int ActiveScopeCount,
    IReadOnlyList<SourceGenStudioScopeDescriptor> Scopes,
    IReadOnlyList<SourceGenStudioOperationStatus> Operations,
    SourceGenStudioOptions Options,
    SourceGenStudioRemoteStatus Remote);
