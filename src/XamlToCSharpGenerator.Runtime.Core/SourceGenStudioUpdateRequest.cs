using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenStudioUpdateRequest
{
    public string? RequestId { get; init; }

    public long? CorrelationId { get; init; }

    public string? BuildUri { get; init; }

    public Type? TargetType { get; init; }

    public string? TargetTypeName { get; init; }

    public SourceGenStudioScopeKind ScopeKind { get; init; } = SourceGenStudioScopeKind.RootControl;

    public string? ScopeId { get; init; }

    public string XamlText { get; init; } = string.Empty;

    public SourceGenStudioWaitMode? WaitMode { get; init; }

    public SourceGenStudioFallbackPolicy? FallbackPolicy { get; init; }

    public bool? PersistChangesToSource { get; init; }

    public TimeSpan? Timeout { get; init; }
}
