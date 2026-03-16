using System;
using System.Collections.Generic;
using System.IO;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;

namespace XamlToCSharpGenerator.Runtime;

/// <summary>
/// Hosts the AXSG runtime MCP surface in-process for a running Avalonia application.
/// </summary>
public sealed class XamlSourceGenRuntimeMcpServer : IDisposable
{
    private readonly McpServerCore _server;
    private readonly AxsgRuntimeMcpEventStore _eventStore;
    private readonly AxsgRuntimeMcpNotificationBridge _notificationBridge;

    /// <summary>
    /// Initializes a new instance of the <see cref="XamlSourceGenRuntimeMcpServer"/> class.
    /// </summary>
    /// <param name="input">The JSON-RPC input stream.</param>
    /// <param name="output">The JSON-RPC output stream.</param>
    public XamlSourceGenRuntimeMcpServer(Stream input, Stream output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        var runtimeQueryService = new AxsgRuntimeQueryService();
        var hotReloadService = new AxsgRuntimeHotReloadService(runtimeQueryService);
        var hotDesignService = new AxsgRuntimeHotDesignService(runtimeQueryService);
        var studioService = new AxsgRuntimeStudioService(runtimeQueryService);
        _eventStore = new AxsgRuntimeMcpEventStore();
        IReadOnlyList<McpToolDefinition> tools = AxsgRuntimeMcpCatalog.CreateTools(
            runtimeQueryService,
            hotReloadService,
            hotDesignService,
            studioService);
        _server = new McpServerCore(
            new JsonRpcMessageReader(input),
            new JsonRpcMessageWriter(output),
            new McpServerInfo(
                Name: "axsg-runtime-mcp",
                Version: typeof(XamlSourceGenRuntimeMcpServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Instructions: "Provides AXSG runtime hot reload, hot design, and studio query and control tools from a live process. Prefer resources/subscribe for status and event resources, and use tools for one-shot queries or mutations."),
            () => tools,
            () => AxsgRuntimeMcpCatalog.CreateResources(runtimeQueryService, _eventStore),
            new McpServerCapabilities(ResourcesSubscribe: true, ResourcesListChanged: true));
        _notificationBridge = new AxsgRuntimeMcpNotificationBridge(
            _server,
            runtimeQueryService,
            _eventStore);
    }

    /// <summary>
    /// Runs the MCP request loop until cancellation or end-of-stream.
    /// </summary>
    public Task<int> RunAsync(CancellationToken cancellationToken)
    {
        return _server.RunAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _notificationBridge.Dispose();
        _eventStore.Dispose();
        _server.Dispose();
    }
}
