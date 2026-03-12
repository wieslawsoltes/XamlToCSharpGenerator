using System;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Configures runtime registration behavior for AXSG hot reload targets.
/// </summary>
public sealed class SourceGenHotReloadRegistrationOptions
{
    /// <summary>
    /// Gets the logical tracking type used to correlate runtime instances with metadata updates.
    /// Defaults to the runtime instance type when not specified.
    /// </summary>
    public Type? TrackingType { get; init; }

    /// <summary>
    /// Gets the build URI associated with the registered XAML artifact.
    /// </summary>
    public string? BuildUri { get; init; }

    /// <summary>
    /// Gets the original source path associated with the registered XAML artifact.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>
    /// Gets a callback invoked before the reload action runs.
    /// </summary>
    public Action<object>? BeforeReload { get; init; }

    /// <summary>
    /// Gets a callback that captures state before reload.
    /// </summary>
    public Func<object, object?>? CaptureState { get; init; }

    /// <summary>
    /// Gets a callback that restores captured state after reload.
    /// </summary>
    public Action<object, object?>? RestoreState { get; init; }

    /// <summary>
    /// Gets a callback invoked after the reload action completes.
    /// </summary>
    public Action<object>? AfterReload { get; init; }
}
