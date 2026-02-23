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
}
