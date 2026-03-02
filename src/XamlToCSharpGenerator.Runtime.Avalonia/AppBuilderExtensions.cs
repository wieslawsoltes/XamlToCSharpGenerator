using System;
using Avalonia;

namespace XamlToCSharpGenerator.Runtime;

public static class AppBuilderExtensions
{
    public static AppBuilder UseAvaloniaSourceGeneratedXaml(this AppBuilder builder)
    {
        return builder.AfterSetup(_ =>
        {
            AvaloniaSourceGeneratedXamlLoader.Enable();
            XamlSourceGenHotReloadManager.Enable();
            XamlSourceGenHotReloadManager.TryEnableIdePollingFallbackFromEnvironment();
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedRuntimeXamlCompilation(
        this AppBuilder builder,
        bool enable = true,
        Action<SourceGenRuntimeXamlCompilationOptions>? configure = null)
    {
        return builder.AfterSetup(_ => ConfigureRuntimeCompilation(enable, configure));
    }

    public static AppBuilder UseAvaloniaSourceGeneratedXamlHotReload(this AppBuilder builder, bool enable = true)
    {
        return builder.AfterSetup(_ =>
        {
            if (enable)
            {
                XamlSourceGenHotReloadManager.Enable();
                XamlSourceGenHotReloadManager.TryEnableIdePollingFallbackFromEnvironment();
            }
            else
            {
                XamlSourceGenHotReloadManager.Disable();
                XamlSourceGenHotReloadManager.DisableIdePollingFallback();
            }
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedXamlIdeHotReloadFallback(
        this AppBuilder builder,
        bool enable = true,
        int pollingIntervalMs = 1000)
    {
        return builder.AfterSetup(_ =>
        {
            if (enable)
            {
                XamlSourceGenHotReloadManager.EnableIdePollingFallback(pollingIntervalMs);
            }
            else
            {
                XamlSourceGenHotReloadManager.DisableIdePollingFallback();
            }
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedXamlHotReloadHandler(
        this AppBuilder builder,
        ISourceGenHotReloadHandler handler,
        Type? elementType = null)
    {
        return builder.AfterSetup(_ =>
        {
            XamlSourceGenHotReloadManager.RegisterHandler(handler, elementType);
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedXamlHotDesign(
        this AppBuilder builder,
        bool enable = true,
        Action<SourceGenHotDesignOptions>? configure = null,
        ISourceGenHotDesignUpdateApplier? applier = null)
    {
        return builder.AfterSetup(_ => ConfigureHotDesignCompatibility(enable, configure, applier));
    }

    public static AppBuilder UseAvaloniaSourceGeneratedStudio(
        this AppBuilder builder,
        Action<SourceGenStudioOptions>? configure = null)
    {
        return builder.AfterSetup(_ =>
        {
            var options = new SourceGenStudioOptions();
            configure?.Invoke(options);
            XamlSourceGenStudioHost.Start(options);
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedStudioFromEnvironment(
        this AppBuilder builder,
        Action<SourceGenStudioOptions>? configure = null)
    {
        return builder.AfterSetup(_ =>
        {
            var options = new SourceGenStudioOptions();
            configure?.Invoke(options);

            if (!ApplyStudioEnvironmentOverrides(options))
            {
                return;
            }

            XamlSourceGenStudioHost.Start(options);
        });
    }

    private static void ConfigureHotDesignCompatibility(
        bool enable,
        Action<SourceGenHotDesignOptions>? configure,
        ISourceGenHotDesignUpdateApplier? applier)
    {
        if (applier is not null)
        {
            XamlSourceGenHotDesignManager.RegisterApplier(applier);
        }

        if (!enable)
        {
            XamlSourceGenHotDesignManager.Disable();
            return;
        }

        var hotDesignOptions = new SourceGenHotDesignOptions();
        configure?.Invoke(hotDesignOptions);
        XamlSourceGenHotDesignManager.Enable(hotDesignOptions);
    }

    private static void ConfigureRuntimeCompilation(
        bool enable,
        Action<SourceGenRuntimeXamlCompilationOptions>? configure)
    {
        AvaloniaSourceGeneratedXamlLoader.Enable();
        AvaloniaSourceGeneratedXamlLoader.ConfigureRuntimeCompilation(options =>
        {
            options.EnableRuntimeCompilationFallback = enable;
            configure?.Invoke(options);
        });
    }

    private static bool ApplyStudioEnvironmentOverrides(SourceGenStudioOptions options)
    {
        var explicitEnable = TryGetBooleanEnvironment("AXSG_STUDIO_ENABLE", out var enabledFromEnv)
            ? enabledFromEnv
            : (TryGetBooleanEnvironment("AXSG_STUDIO", out var legacyEnabled) && legacyEnabled);
        var enabled = explicitEnable;

        if (TryGetBooleanEnvironment("AXSG_STUDIO_REMOTE_ENABLE", out var remoteEnabled))
        {
            options.EnableRemoteDesign = remoteEnabled;
            enabled |= remoteEnabled;
        }

        if (TryGetStringEnvironment("AXSG_STUDIO_REMOTE_HOST", out var remoteHost))
        {
            options.RemoteHost = remoteHost!;
            enabled = true;
            options.EnableRemoteDesign = true;
        }

        if (TryGetIntegerEnvironment("AXSG_STUDIO_REMOTE_PORT", out var remotePort) && remotePort > 0)
        {
            options.RemotePort = remotePort;
            enabled = true;
            options.EnableRemoteDesign = true;
        }

        if (TryGetStringEnvironment("AXSG_STUDIO_VNC_ENDPOINT", out var vncEndpoint))
        {
            options.VncEndpoint = vncEndpoint;
            enabled = true;
            options.EnableRemoteDesign = true;
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_VNC_AUTO_OPEN", out var autoOpenVnc))
        {
            options.AutoOpenVncViewerOnDesktop = autoOpenVnc;
            enabled |= autoOpenVnc;
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_OVERLAY_INDICATOR", out var showIndicator))
        {
            options.ShowOverlayIndicator = showIndicator;
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_EXTERNAL_WINDOW", out var enableExternalWindow))
        {
            options.EnableExternalWindow = enableExternalWindow;
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_AUTO_OPEN_WINDOW", out var autoOpenStudioWindow))
        {
            options.AutoOpenStudioWindowOnStartup = autoOpenStudioWindow;
            enabled |= autoOpenStudioWindow;
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_TRACE", out var enableTracing))
        {
            options.EnableTracing = enableTracing;
        }

        if (TryGetIntegerEnvironment("AXSG_STUDIO_MAX_HISTORY", out var maxHistory) && maxHistory > 0)
        {
            options.MaxOperationHistoryEntries = maxHistory;
        }

        if (TryGetStringEnvironment("AXSG_STUDIO_WAIT_MODE", out var waitModeRaw) &&
            Enum.TryParse<SourceGenStudioWaitMode>(waitModeRaw, ignoreCase: true, out var waitMode))
        {
            options.WaitMode = waitMode;
        }

        if (TryGetStringEnvironment("AXSG_STUDIO_FALLBACK_POLICY", out var fallbackPolicyRaw) &&
            Enum.TryParse<SourceGenStudioFallbackPolicy>(fallbackPolicyRaw, ignoreCase: true, out var fallbackPolicy))
        {
            options.FallbackPolicy = fallbackPolicy;
        }

        if (TryGetIntegerEnvironment("AXSG_STUDIO_TIMEOUT_MS", out var timeoutMs) && timeoutMs > 0)
        {
            options.UpdateTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        }

        if (TryGetBooleanEnvironment("AXSG_STUDIO_PERSIST_SOURCE", out var persistChanges))
        {
            options.PersistChangesToSource = persistChanges;
        }

        return enabled;
    }

    private static bool TryGetStringEnvironment(string variableName, out string? value)
    {
        value = Environment.GetEnvironmentVariable(variableName);
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetIntegerEnvironment(string variableName, out int value)
    {
        value = 0;
        return int.TryParse(Environment.GetEnvironmentVariable(variableName), out value);
    }

    private static bool TryGetBooleanEnvironment(string variableName, out bool value)
    {
        value = false;
        var raw = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim();
        if (string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return bool.TryParse(normalized, out value);
    }
}
