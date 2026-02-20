using System;
using Avalonia;

namespace XamlToCSharpGenerator.Runtime;

public static class AppBuilderExtensions
{
    public static AppBuilder UseAvaloniaSourceGeneratedXaml(this AppBuilder builder)
    {
        AvaloniaSourceGeneratedXamlLoader.Enable();
        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.TryEnableIdePollingFallbackFromEnvironment();
        return builder.AfterSetup(_ =>
        {
            AvaloniaSourceGeneratedXamlLoader.Enable();
            XamlSourceGenHotReloadManager.Enable();
            XamlSourceGenHotReloadManager.TryEnableIdePollingFallbackFromEnvironment();
        });
    }

    public static AppBuilder UseAvaloniaSourceGeneratedXamlHotReload(this AppBuilder builder, bool enable = true)
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
        if (enable)
        {
            XamlSourceGenHotReloadManager.EnableIdePollingFallback(pollingIntervalMs);
        }
        else
        {
            XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        }

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
        XamlSourceGenHotReloadManager.RegisterHandler(handler, elementType);
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
        ConfigureHotDesign(enable, configure, applier);
        return builder.AfterSetup(_ => ConfigureHotDesign(enable, configure, applier));
    }

    private static void ConfigureHotDesign(
        bool enable,
        Action<SourceGenHotDesignOptions>? configure,
        ISourceGenHotDesignUpdateApplier? applier)
    {
        if (applier is not null)
        {
            XamlSourceGenHotDesignManager.RegisterApplier(applier);
        }

        if (enable)
        {
            if (configure is null)
            {
                XamlSourceGenHotDesignManager.Enable();
                return;
            }

            var options = new SourceGenHotDesignOptions();
            configure(options);
            XamlSourceGenHotDesignManager.Enable(options);
            return;
        }

        XamlSourceGenHotDesignManager.Disable();
    }
}
