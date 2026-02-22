using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotDesignElementInsertRequest
{
    public string? BuildUri { get; init; }

    public Type? TargetType { get; init; }

    public string? TargetTypeName { get; init; }

    public string? ParentElementId { get; init; }

    public string ElementName { get; init; } = string.Empty;

    public string? XamlFragment { get; init; }

    public bool? PersistChangesToSource { get; init; }

    public bool? WaitForHotReload { get; init; }

    public bool? FallbackToRuntimeApplyOnTimeout { get; init; }
}
