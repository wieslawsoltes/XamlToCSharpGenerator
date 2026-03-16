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
/// Delegate used by preview-only markup evaluation paths.
/// </summary>
public delegate object? SourceGenPreviewMarkupValueProvider(
    string? code,
    string? codeBase64Url,
    string? dependencyNamesBase64Url,
    IServiceProvider serviceProvider);

/// <summary>
/// Hosts the currently installed preview-only markup runtime.
/// </summary>
public static class SourceGenPreviewMarkupRuntime
{
    private static readonly object Sync = new();
    private static SourceGenPreviewMarkupValueProvider? _provider;

    /// <summary>
    /// Gets a value indicating whether a preview runtime is currently installed.
    /// </summary>
    public static bool IsInstalled
    {
        get
        {
            lock (Sync)
            {
                return _provider is not null;
            }
        }
    }

    /// <summary>
    /// Installs a preview-only markup runtime callback.
    /// </summary>
    public static void Install(SourceGenPreviewMarkupValueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (Sync)
        {
            _provider = provider;
        }
    }

    /// <summary>
    /// Installs a preview-only markup runtime.
    /// </summary>
    public static void Install(ISourceGenPreviewMarkupRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Install(runtime.ProvideValue);
    }

    /// <summary>
    /// Removes the current preview-only markup runtime.
    /// </summary>
    public static void Uninstall()
    {
        lock (Sync)
        {
            _provider = null;
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
        SourceGenPreviewMarkupValueProvider? provider;
        lock (Sync)
        {
            provider = _provider;
        }

        if (provider is null)
        {
            value = null;
            return false;
        }

        value = provider(code, codeBase64Url, dependencyNamesBase64Url, serviceProvider);
        return true;
    }

    internal static void ResetForTests()
    {
        Uninstall();
    }
}
