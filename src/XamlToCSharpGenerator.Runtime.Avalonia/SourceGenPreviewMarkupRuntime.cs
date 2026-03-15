using System;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Provides preview-only runtime values for AXSG markup extensions that normally require source generation.
/// </summary>
public interface ISourceGenPreviewMarkupRuntime
{
    /// <summary>
    /// Produces a runtime value for preview-only markup evaluation.
    /// </summary>
    object? ProvideValue(
        string? code,
        string? codeBase64Url,
        string? dependencyNamesBase64Url,
        IServiceProvider serviceProvider);
}

/// <summary>
/// Hosts the currently installed preview-only markup runtime.
/// </summary>
public static class SourceGenPreviewMarkupRuntime
{
    private static readonly object Sync = new();
    private static ISourceGenPreviewMarkupRuntime? _runtime;

    /// <summary>
    /// Gets a value indicating whether a preview runtime is currently installed.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            lock (Sync)
            {
                return _runtime is not null;
            }
        }
    }

    /// <summary>
    /// Installs a preview-only markup runtime.
    /// </summary>
    public static void Install(ISourceGenPreviewMarkupRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        lock (Sync)
        {
            _runtime = runtime;
        }
    }

    /// <summary>
    /// Removes the current preview-only markup runtime.
    /// </summary>
    public static void Uninstall()
    {
        lock (Sync)
        {
            _runtime = null;
        }
    }

    /// <summary>
    /// Tries to provide a preview-only runtime value.
    /// </summary>
    public static bool TryProvideValue(
        string? code,
        string? codeBase64Url,
        string? dependencyNamesBase64Url,
        IServiceProvider serviceProvider,
        out object? value)
    {
        ISourceGenPreviewMarkupRuntime? runtime;
        lock (Sync)
        {
            runtime = _runtime;
        }

        if (runtime is null)
        {
            value = null;
            return false;
        }

        value = runtime.ProvideValue(code, codeBase64Url, dependencyNamesBase64Url, serviceProvider);
        return true;
    }

    internal static void ResetForTests()
    {
        Uninstall();
    }
}
