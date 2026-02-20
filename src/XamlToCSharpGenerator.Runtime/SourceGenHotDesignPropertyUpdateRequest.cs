using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignPropertyUpdateRequest
{
    public string? BuildUri { get; init; }

    public Type? TargetType { get; init; }

    public string? TargetTypeName { get; init; }

    public string? ElementId { get; init; }

    public string PropertyName { get; init; } = string.Empty;

    public string? PropertyValue { get; init; }

    public bool RemoveProperty { get; init; }

    public bool? PersistChangesToSource { get; init; }

    public bool? WaitForHotReload { get; init; }

    public bool? FallbackToRuntimeApplyOnTimeout { get; init; }
}
