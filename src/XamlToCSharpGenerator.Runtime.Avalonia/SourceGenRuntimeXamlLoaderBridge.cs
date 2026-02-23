using System;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenRuntimeXamlLoaderBridge
{
    private const string DisableDynamicBridgeSwitchName = "XamlToCSharpGenerator.Runtime.DisableDynamicBridge";
    private static readonly object Sync = new();
    private static bool _registered;
    private static SourceGenRuntimeXamlLoaderBridgeRegistrationStatus _registrationStatus =
        SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.NotAttempted;

    public static void EnsureRegistered()
    {
        lock (Sync)
        {
            EnsureRegisteredCore();
        }
    }

    public static SourceGenRuntimeXamlLoaderBridgeRegistrationStatus RegistrationStatus
    {
        get
        {
            lock (Sync)
            {
                return _registrationStatus;
            }
        }
    }

    public static bool IsRegistered
    {
        get
        {
            lock (Sync)
            {
                return _registered;
            }
        }
    }

    internal static void ResetForTests()
    {
        lock (Sync)
        {
            _registered = false;
            _registrationStatus = SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.NotAttempted;
        }
    }

    public static object DispatchLoad(object document, object configuration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(configuration);

        if (document is not RuntimeXamlLoaderDocument runtimeDocument)
        {
            throw new ArgumentException(
                "Unexpected runtime loader document type: " + document.GetType().FullName,
                nameof(document));
        }

        if (configuration is not RuntimeXamlLoaderConfiguration runtimeConfiguration)
        {
            throw new ArgumentException(
                "Unexpected runtime loader configuration type: " + configuration.GetType().FullName,
                nameof(configuration));
        }

        return AvaloniaSourceGeneratedXamlLoader.Load(runtimeDocument, runtimeConfiguration);
    }

    private static void EnsureRegisteredCore()
    {
        if (_registered)
        {
            return;
        }

        if (IsDynamicBridgeDisabled())
        {
            _registrationStatus = SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.DynamicBridgeDisabledBySwitch;
            return;
        }

        // Non-reflection/AOT-safe mode: Avalonia does not expose a public registration seam
        // for runtime loader replacement, so keep bridge inactive until such contract exists.
        _registrationStatus = SourceGenRuntimeXamlLoaderBridgeRegistrationStatus.RuntimeLoaderInterfaceMissing;
    }

    private static bool IsDynamicBridgeDisabled()
    {
        return AppContext.TryGetSwitch(DisableDynamicBridgeSwitchName, out var disabled) && disabled;
    }
}
