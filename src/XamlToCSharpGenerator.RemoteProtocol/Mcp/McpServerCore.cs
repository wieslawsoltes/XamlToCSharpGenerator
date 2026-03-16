using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.JsonRpc;

namespace XamlToCSharpGenerator.RemoteProtocol.Mcp;

/// <summary>
/// Describes the MCP server identity returned from the initialize handshake.
/// </summary>
public sealed record McpServerInfo(
    string Name,
    string Version,
    string Instructions);

/// <summary>
/// Describes a single MCP tool exposed by the server.
/// </summary>
public sealed record McpToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema,
    Func<JsonObject?, CancellationToken, ValueTask<object?>> Handler);

/// <summary>
/// Describes a single MCP resource exposed by the server.
/// </summary>
public sealed record McpResourceDefinition(
    string Uri,
    string Name,
    string Description,
    string MimeType,
    Func<CancellationToken, ValueTask<object?>> Reader);

/// <summary>
/// Describes the optional MCP server capabilities enabled for a host instance.
/// </summary>
public sealed record McpServerCapabilities(
    bool ToolsListChanged = false,
    bool ResourcesSubscribe = false,
    bool ResourcesListChanged = false);

/// <summary>
/// Represents a JSON-RPC protocol error raised while processing an MCP request.
/// </summary>
public sealed class McpRequestException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpRequestException"/> class.
    /// </summary>
    public McpRequestException(int code, string message, JsonNode? data = null)
        : base(message)
    {
        Code = code;
        ErrorData = data;
    }

    /// <summary>
    /// Gets the JSON-RPC error code.
    /// </summary>
    public int Code { get; }

    /// <summary>
    /// Gets the optional error data payload.
    /// </summary>
    public JsonNode? ErrorData { get; }
}

/// <summary>
/// Represents a structured MCP tool failure that should be returned as a tool result.
/// </summary>
public sealed class McpToolException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolException"/> class.
    /// </summary>
    public McpToolException(object? result)
    {
        Result = result;
    }

    /// <summary>
    /// Gets the structured tool error result.
    /// </summary>
    public object? Result { get; }
}

/// <summary>
/// Hosts an MCP server over JSON-RPC streams.
/// </summary>
public sealed class McpServerCore : IDisposable
{
    private const string LatestProtocolVersion = "2025-11-25";
    private const int JsonRpcErrorInvalidParams = -32602;
    private const int JsonRpcErrorMethodNotFound = -32601;
    private const int JsonRpcErrorInternal = -32603;
    private const int JsonRpcErrorResourceNotFound = -32002;

    private readonly JsonRpcMessageReader _reader;
    private readonly JsonRpcMessageWriter _writer;
    private readonly McpServerInfo _serverInfo;
    private readonly Func<IReadOnlyList<McpToolDefinition>> _toolsProvider;
    private readonly Func<IReadOnlyDictionary<string, McpResourceDefinition>> _resourcesProvider;
    private readonly McpServerCapabilities _capabilities;
    private readonly object _subscriptionGate = new();
    private readonly HashSet<string> _resourceSubscriptions = new(StringComparer.Ordinal);
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerCore"/> class.
    /// </summary>
    public McpServerCore(
        JsonRpcMessageReader reader,
        JsonRpcMessageWriter writer,
        McpServerInfo serverInfo,
        IReadOnlyList<McpToolDefinition> tools,
        IReadOnlyDictionary<string, McpResourceDefinition> resources,
        McpServerCapabilities? capabilities = null)
        : this(
            reader,
            writer,
            serverInfo,
            () => tools ?? throw new ArgumentNullException(nameof(tools)),
            () => resources ?? throw new ArgumentNullException(nameof(resources)),
            capabilities)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerCore"/> class using dynamic tool/resource providers.
    /// </summary>
    public McpServerCore(
        JsonRpcMessageReader reader,
        JsonRpcMessageWriter writer,
        McpServerInfo serverInfo,
        Func<IReadOnlyList<McpToolDefinition>> toolsProvider,
        Func<IReadOnlyDictionary<string, McpResourceDefinition>> resourcesProvider,
        McpServerCapabilities? capabilities = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
        _toolsProvider = toolsProvider ?? throw new ArgumentNullException(nameof(toolsProvider));
        _resourcesProvider = resourcesProvider ?? throw new ArgumentNullException(nameof(resourcesProvider));
        _capabilities = capabilities ?? new McpServerCapabilities();
    }

    /// <summary>
    /// Runs the MCP request loop until cancellation or end-of-stream.
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using JsonDocument? message = await _reader.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                return 0;
            }

            await HandleMessageAsync(message.RootElement, cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private async Task HandleMessageAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("method", out JsonElement methodElement))
        {
            return;
        }

        string? method = methodElement.GetString();
        if (string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        bool hasId = root.TryGetProperty("id", out JsonElement idElement);
        JsonElement id = hasId ? idElement.Clone() : default;
        JsonElement parameters = root.TryGetProperty("params", out JsonElement paramsElement)
            ? paramsElement
            : default;

        try
        {
            switch (method)
            {
                case "initialize":
                    if (hasId)
                    {
                        await HandleInitializeAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "notifications/initialized":
                    lock (_subscriptionGate)
                    {
                        _initialized = true;
                    }
                    break;

                case "ping":
                    if (hasId)
                    {
                        await SendResponseAsync(id, new JsonObject(), cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "tools/list":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await SendResponseAsync(
                            id,
                            new JsonObject
                            {
                                ["tools"] = BuildToolsPayload()
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "tools/call":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await HandleToolCallAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "resources/list":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await SendResponseAsync(
                            id,
                            new JsonObject
                            {
                                ["resources"] = BuildResourcesPayload()
                            },
                            cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "resources/subscribe":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await HandleSubscribeResourceAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "resources/unsubscribe":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await HandleUnsubscribeResourceAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                case "resources/read":
                    if (hasId)
                    {
                        EnsureInitialized();
                        await HandleReadResourceAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                    }

                    break;

                default:
                    if (hasId)
                    {
                        await SendErrorAsync(id, JsonRpcErrorMethodNotFound, "Method not found.", cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }
        }
        catch (McpRequestException ex)
        {
            if (hasId)
            {
                await SendErrorAsync(id, ex.Code, ex.Message, cancellationToken, ex.ErrorData).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (hasId)
            {
                await SendErrorAsync(id, JsonRpcErrorInternal, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleInitializeAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        string? requestedProtocolVersion = TryGetString(parameters, "protocolVersion");
        string responseProtocolVersion = string.Equals(requestedProtocolVersion, LatestProtocolVersion, StringComparison.Ordinal)
            ? requestedProtocolVersion!
            : LatestProtocolVersion;

        var result = new JsonObject
        {
            ["protocolVersion"] = responseProtocolVersion,
            ["capabilities"] = new JsonObject
            {
                ["tools"] = new JsonObject
                {
                    ["listChanged"] = _capabilities.ToolsListChanged
                },
                ["resources"] = new JsonObject
                {
                    ["subscribe"] = _capabilities.ResourcesSubscribe,
                    ["listChanged"] = _capabilities.ResourcesListChanged
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = _serverInfo.Name,
                ["version"] = _serverInfo.Version
            },
            ["instructions"] = _serverInfo.Instructions
        };

        await SendResponseAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends an MCP resource-updated notification to subscribed clients.
    /// </summary>
    public Task NotifyResourceUpdatedAsync(string resourceUri, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(resourceUri));
        }

        if (!_capabilities.ResourcesSubscribe ||
            !GetResourceDefinitions().ContainsKey(resourceUri) ||
            !ShouldNotifySubscribedResource(resourceUri))
        {
            return Task.CompletedTask;
        }

        return _writer.WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/resources/updated",
                ["params"] = new JsonObject
                {
                    ["uri"] = resourceUri
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends an MCP tool-list-changed notification when enabled for the host.
    /// </summary>
    public Task NotifyToolsListChangedAsync(CancellationToken cancellationToken = default)
    {
        if (!_capabilities.ToolsListChanged)
        {
            return Task.CompletedTask;
        }

        lock (_subscriptionGate)
        {
            if (!_initialized)
            {
                return Task.CompletedTask;
            }
        }

        return _writer.WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/tools/list_changed"
            },
            cancellationToken);
    }

    /// <summary>
    /// Sends an MCP resource-list-changed notification when enabled for the host.
    /// </summary>
    public Task NotifyResourcesListChangedAsync(CancellationToken cancellationToken = default)
    {
        if (!_capabilities.ResourcesListChanged)
        {
            return Task.CompletedTask;
        }

        lock (_subscriptionGate)
        {
            if (!_initialized)
            {
                return Task.CompletedTask;
            }
        }

        return _writer.WriteAsync(
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/resources/list_changed"
            },
            cancellationToken);
    }

    private void EnsureInitialized()
    {
        lock (_subscriptionGate)
        {
            if (_initialized)
            {
                return;
            }
        }

        throw new McpRequestException(JsonRpcErrorInvalidParams, "Client must send notifications/initialized before normal operations.");
    }

    private async Task HandleToolCallAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        string? toolName = TryGetString(parameters, "name");
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new McpRequestException(JsonRpcErrorInvalidParams, "Tool name is required.");
        }

        McpToolDefinition? tool = GetToolDefinitions().FirstOrDefault(candidate => string.Equals(candidate.Name, toolName, StringComparison.Ordinal));
        if (tool is null)
        {
            throw new McpRequestException(JsonRpcErrorMethodNotFound, "Tool not found.");
        }

        JsonObject? arguments = TryGetObject(parameters, "arguments");
        try
        {
            object? result = await tool.Handler(arguments, cancellationToken).ConfigureAwait(false);
            await SendResponseAsync(id, BuildToolResult(result, isError: false), cancellationToken).ConfigureAwait(false);
        }
        catch (McpToolException ex)
        {
            await SendResponseAsync(id, BuildToolResult(ex.Result, isError: true), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleReadResourceAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        string? resourceUri = TryGetString(parameters, "uri");
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new McpRequestException(JsonRpcErrorInvalidParams, "Resource uri is required.");
        }

        if (!GetResourceDefinitions().TryGetValue(resourceUri, out McpResourceDefinition? resource))
        {
            throw new McpRequestException(
                JsonRpcErrorResourceNotFound,
                "Resource not found.",
                new JsonObject { ["uri"] = resourceUri });
        }

        object? payload = await resource.Reader(cancellationToken).ConfigureAwait(false);
        await SendResponseAsync(
            id,
            new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["uri"] = resource.Uri,
                        ["mimeType"] = resource.MimeType,
                        ["text"] = SerializeContent(payload)
                    }
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSubscribeResourceAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!_capabilities.ResourcesSubscribe)
        {
            throw new McpRequestException(JsonRpcErrorMethodNotFound, "Resource subscriptions are not supported.");
        }

        string? resourceUri = TryGetString(parameters, "uri");
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new McpRequestException(JsonRpcErrorInvalidParams, "Resource uri is required.");
        }

        if (!GetResourceDefinitions().ContainsKey(resourceUri))
        {
            throw new McpRequestException(
                JsonRpcErrorResourceNotFound,
                "Resource not found.",
                new JsonObject { ["uri"] = resourceUri });
        }

        lock (_subscriptionGate)
        {
            _resourceSubscriptions.Add(resourceUri);
        }

        await SendResponseAsync(id, new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleUnsubscribeResourceAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        if (!_capabilities.ResourcesSubscribe)
        {
            throw new McpRequestException(JsonRpcErrorMethodNotFound, "Resource subscriptions are not supported.");
        }

        string? resourceUri = TryGetString(parameters, "uri");
        if (string.IsNullOrWhiteSpace(resourceUri))
        {
            throw new McpRequestException(JsonRpcErrorInvalidParams, "Resource uri is required.");
        }

        lock (_subscriptionGate)
        {
            _resourceSubscriptions.Remove(resourceUri);
        }

        await SendResponseAsync(id, new JsonObject(), cancellationToken).ConfigureAwait(false);
    }

    private JsonArray BuildToolsPayload()
    {
        var tools = new JsonArray();
        foreach (McpToolDefinition tool in GetToolDefinitions())
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.ToJsonString())
            });
        }

        return tools;
    }

    private JsonArray BuildResourcesPayload()
    {
        var resources = new JsonArray();
        foreach (McpResourceDefinition resource in GetResourceDefinitions().Values)
        {
            resources.Add(new JsonObject
            {
                ["uri"] = resource.Uri,
                ["name"] = resource.Name,
                ["description"] = resource.Description,
                ["mimeType"] = resource.MimeType
            });
        }

        return resources;
    }

    private static JsonObject BuildToolResult(object? payload, bool isError)
    {
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = SerializeContent(payload)
                }
            },
            ["structuredContent"] = JsonRpcNodeHelpers.SerializeResultValue(payload),
            ["isError"] = isError
        };
    }

    private static string SerializeContent(object? payload)
    {
        return payload switch
        {
            null => "{}",
            string text => text,
            _ => JsonSerializer.Serialize(payload, JsonRpcSerializer.DefaultOptions)
        };
    }

    private Task SendResponseAsync(JsonElement id, object? value, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonRpcNodeHelpers.CloneJsonElement(id),
            ["result"] = JsonRpcNodeHelpers.SerializeResultValue(value)
        };

        return _writer.WriteAsync(response, cancellationToken);
    }

    private Task SendErrorAsync(
        JsonElement id,
        int code,
        string message,
        CancellationToken cancellationToken,
        JsonNode? data = null)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonRpcNodeHelpers.CloneJsonElement(id),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = data
            }
        };

        return _writer.WriteAsync(response, cancellationToken);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static JsonObject? TryGetObject(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return JsonNode.Parse(property.GetRawText()) as JsonObject;
    }

    private bool ShouldNotifySubscribedResource(string resourceUri)
    {
        lock (_subscriptionGate)
        {
            return _initialized && _resourceSubscriptions.Contains(resourceUri);
        }
    }

    private IReadOnlyList<McpToolDefinition> GetToolDefinitions()
    {
        IReadOnlyList<McpToolDefinition>? tools = _toolsProvider();
        return tools ?? throw new InvalidOperationException("Tool provider returned null.");
    }

    private IReadOnlyDictionary<string, McpResourceDefinition> GetResourceDefinitions()
    {
        IReadOnlyDictionary<string, McpResourceDefinition>? resources = _resourcesProvider();
        return resources ?? throw new InvalidOperationException("Resource provider returned null.");
    }
}
