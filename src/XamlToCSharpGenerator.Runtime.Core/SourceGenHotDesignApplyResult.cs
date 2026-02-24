using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotDesignApplyResult(
    bool Succeeded,
    string Message,
    string? BuildUri = null,
    Type? TargetType = null,
    string? SourcePath = null,
    bool SourcePersisted = false,
    bool MinimalDiffApplied = false,
    int MinimalDiffStart = -1,
    int MinimalDiffRemovedLength = 0,
    int MinimalDiffInsertedLength = 0,
    bool HotReloadObserved = false,
    bool RuntimeFallbackApplied = false,
    Exception? Error = null);
