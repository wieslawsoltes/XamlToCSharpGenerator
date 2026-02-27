using System;
using System.Threading;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class MetadataUpdateTransport : ISourceGenHotReloadTransport
{
    internal const string DotNetModifiableAssembliesEnvVarName = "DOTNET_MODIFIABLE_ASSEMBLIES";
    internal const string DotNetWatchNamedPipeEnvVarName = "DOTNET_WATCH_HOTRELOAD_NAMEDPIPE_NAME";
    internal const string DotNetHotReloadNamedPipeEnvVarName = "DOTNET_HOTRELOAD_NAMEDPIPE_NAME";

    private readonly object _sync = new();
    private readonly Action<string>? _trace;
    private DotNetWatchNamedPipeBridge? _watchNamedPipeBridge;
    private string? _watchNamedPipeName;

    public MetadataUpdateTransport(Action<string>? trace = null)
    {
        _trace = trace;
    }

    public string Name => "MetadataUpdate";

    public SourceGenHotReloadTransportCapabilities Capabilities => BuildCapabilities();

    public SourceGenHotReloadHandshakeResult StartHandshake(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _ = timeout;
        _ = cancellationToken;

        var watchPipe = Environment.GetEnvironmentVariable(DotNetWatchNamedPipeEnvVarName);
        var hotReloadPipe = Environment.GetEnvironmentVariable(DotNetHotReloadNamedPipeEnvVarName);
        var hasWatchPipe = !string.IsNullOrWhiteSpace(watchPipe);
        var hasHotReloadPipe = !string.IsNullOrWhiteSpace(hotReloadPipe);
        var preferredPipe = hasWatchPipe ? watchPipe : hotReloadPipe;
        var modifiableAssemblies = Environment.GetEnvironmentVariable(DotNetModifiableAssembliesEnvVarName);
        var hasDebugAssemblies = string.Equals(modifiableAssemblies, "debug", StringComparison.OrdinalIgnoreCase);

        if (ShouldUseNamedPipeBridge() && !string.IsNullOrWhiteSpace(preferredPipe))
        {
            TryStartWatchNamedPipeBridge(preferredPipe!);
            if (!hasDebugAssemblies)
            {
                return SourceGenHotReloadHandshakeResult.Success(
                    "Named pipe bridge initialized for dotnet watch. Metadata updates are disabled because DOTNET_MODIFIABLE_ASSEMBLIES is not set to debug.");
            }
        }

        if (!hasDebugAssemblies)
        {
            return SourceGenHotReloadHandshakeResult.Failure(
                "Metadata transport requires DOTNET_MODIFIABLE_ASSEMBLIES=debug.");
        }

        if (hasWatchPipe || hasHotReloadPipe)
        {
            return SourceGenHotReloadHandshakeResult.Success(
                "Metadata transport configured; waiting for first metadata delta.",
                isPending: true);
        }

        return SourceGenHotReloadHandshakeResult.Success(
            "Metadata transport enabled without explicit named pipe; waiting for first metadata delta.",
            isPending: true);
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_watchNamedPipeBridge is null)
            {
                return;
            }

            try
            {
                _watchNamedPipeBridge.Dispose();
            }
            catch
            {
                // Best effort teardown only.
            }
            finally
            {
                _watchNamedPipeBridge = null;
                _watchNamedPipeName = null;
            }
        }
    }

    private static SourceGenHotReloadTransportCapabilities BuildCapabilities()
    {
        var modifiableAssemblies = Environment.GetEnvironmentVariable(DotNetModifiableAssembliesEnvVarName);
        var hasDebugAssemblies = string.Equals(modifiableAssemblies, "debug", StringComparison.OrdinalIgnoreCase);
        var hasWatchPipe = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DotNetWatchNamedPipeEnvVarName));
        var hasHotReloadPipe = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DotNetHotReloadNamedPipeEnvVarName));
        var hasPipe = hasWatchPipe || hasHotReloadPipe;
        var bridgeMode = ShouldUseNamedPipeBridge() && hasPipe;

        string diagnostic;
        if (bridgeMode && !hasDebugAssemblies)
        {
            diagnostic = "Named pipe bridge mode is enabled for dotnet watch; metadata updates are disabled.";
        }
        else if (!hasDebugAssemblies)
        {
            diagnostic = "DOTNET_MODIFIABLE_ASSEMBLIES is not set to debug.";
        }
        else if (!hasPipe)
        {
            diagnostic = "No hot reload named pipe variables found; metadata path will wait for runtime metadata updates.";
        }
        else
        {
            diagnostic = "Metadata environment is configured.";
        }

        return new SourceGenHotReloadTransportCapabilities(
            isSupported: hasDebugAssemblies || bridgeMode,
            supportsMetadataUpdates: hasDebugAssemblies,
            supportsRemoteConnection: false,
            requiresEndpointConfiguration: false,
            diagnostic);
    }

    private static bool ShouldUseNamedPipeBridge()
    {
        if (IsEnabledByEnvironment("AXSG_DOTNET_WATCH_PROXY_ACTIVE"))
        {
            return false;
        }

        return IsEnabledByEnvironment("AXSG_IOS_HOTRELOAD_ENABLED") ||
               OperatingSystem.IsIOS() ||
               OperatingSystem.IsTvOS() ||
               OperatingSystem.IsAndroid();
    }

    private static bool IsEnabledByEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private void TryStartWatchNamedPipeBridge(string pipeName)
    {
        lock (_sync)
        {
            if (_watchNamedPipeBridge is not null &&
                string.Equals(_watchNamedPipeName, pipeName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                _watchNamedPipeBridge?.Dispose();
            }
            catch
            {
                // Best effort bridge replacement only.
            }

            try
            {
                var bridge = new DotNetWatchNamedPipeBridge(pipeName, Trace);
                bridge.Start();
                _watchNamedPipeBridge = bridge;
                _watchNamedPipeName = pipeName;
                Trace("Started dotnet-watch named pipe bridge for pipe '" + pipeName + "'.");
            }
            catch (Exception ex)
            {
                _watchNamedPipeBridge = null;
                _watchNamedPipeName = null;
                Trace("Failed to start dotnet-watch named pipe bridge: " + ex.Message);
            }
        }
    }

    private void Trace(string message)
    {
        _trace?.Invoke(message);
    }
}
