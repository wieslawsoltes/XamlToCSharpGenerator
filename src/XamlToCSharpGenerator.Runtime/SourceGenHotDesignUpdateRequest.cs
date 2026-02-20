using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignUpdateRequest
{
    public string? BuildUri { get; init; }

    public Type? TargetType { get; init; }

    public string? TargetTypeName { get; init; }

    public string XamlText { get; init; } = string.Empty;

    public bool? PersistChangesToSource { get; init; }

    public bool? WaitForHotReload { get; init; }

    public bool? FallbackToRuntimeApplyOnTimeout { get; init; }
}
