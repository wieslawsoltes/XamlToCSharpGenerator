using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadRegistrationOptions
{
    public string? SourcePath { get; init; }

    public Action<object>? BeforeReload { get; init; }

    public Func<object, object?>? CaptureState { get; init; }

    public Action<object, object?>? RestoreState { get; init; }

    public Action<object>? AfterReload { get; init; }
}
