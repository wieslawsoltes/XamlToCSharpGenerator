using System.Text.Json;

namespace XamlToCSharpGenerator.PreviewerHost;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly SemaphoreSlim OutputGate = new(1, 1);

    public static async Task<int> Main()
    {
        PreviewSession? session = null;

        string? line;
        while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            HostCommand command;
            try
            {
                command = ParseCommand(line);
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(new
                {
                    kind = "response",
                    requestId = (string?)null,
                    ok = false,
                    error = "Invalid JSON command: " + ex.Message
                }).ConfigureAwait(false);
                continue;
            }

            try
            {
                switch (command.Name)
                {
                    case "ping":
                        await WriteResponseAsync(new
                        {
                            kind = "response",
                            command.RequestId,
                            ok = true,
                            payload = new
                            {
                                pong = true
                            }
                        }).ConfigureAwait(false);
                        break;

                    case "start":
                        if (session is not null)
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                        }

                        session = new PreviewSession();
                        session.Log += message => _ = WriteEventAsync("log", new { message });
                        session.PreviewUrlPublished += previewUrl => _ = WriteEventAsync(
                            "previewStarted",
                            new { previewUrl });
                        session.UpdateCompleted += result => _ = WriteEventAsync(
                            "updateResult",
                            new
                            {
                                result.Succeeded,
                                result.Error,
                                result.Exception
                            });
                        session.HostExited += exitCode => _ = WriteEventAsync(
                            "hostExited",
                            new { exitCode });

                        var startRequest = ParseStartRequest(command.Payload);
                        var startResult = await session.StartAsync(startRequest, CancellationToken.None)
                            .ConfigureAwait(false);
                        await WriteResponseAsync(new
                        {
                            kind = "response",
                            command.RequestId,
                            ok = true,
                            payload = new
                            {
                                startResult.PreviewUrl,
                                startResult.TransportPort,
                                startResult.PreviewPort,
                                sessionId = startResult.SessionId
                            }
                        }).ConfigureAwait(false);
                        break;

                    case "update":
                        if (session is null)
                        {
                            throw new InvalidOperationException("Preview session has not been started.");
                        }

                        var updateText = ParseUpdateText(command.Payload);
                        await session.UpdateAsync(updateText, CancellationToken.None).ConfigureAwait(false);
                        await WriteResponseAsync(new
                        {
                            kind = "response",
                            command.RequestId,
                            ok = true
                        }).ConfigureAwait(false);
                        break;

                    case "stop":
                        if (session is not null)
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                            session = null;
                        }

                        await WriteResponseAsync(new
                        {
                            kind = "response",
                            command.RequestId,
                            ok = true
                        }).ConfigureAwait(false);
                        break;

                    default:
                        await WriteResponseAsync(new
                        {
                            kind = "response",
                            command.RequestId,
                            ok = false,
                            error = "Unsupported command '" + command.Name + "'."
                        }).ConfigureAwait(false);
                        break;
                }
            }
            catch (Exception ex)
            {
                await WriteResponseAsync(new
                {
                    kind = "response",
                    command.RequestId,
                    ok = false,
                    error = ex.Message
                }).ConfigureAwait(false);
            }
        }

        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private static HostCommand ParseCommand(string line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Command payload must be a JSON object.");
        }

        var commandName = root.TryGetProperty("command", out var commandElement) &&
                          commandElement.ValueKind == JsonValueKind.String
            ? commandElement.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new InvalidOperationException("Command name is required.");
        }

        var requestId = root.TryGetProperty("requestId", out var requestIdElement) &&
                        requestIdElement.ValueKind == JsonValueKind.String
            ? requestIdElement.GetString()
            : null;
        var payload = root.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : default;
        return new HostCommand(commandName.Trim().ToLowerInvariant(), requestId, payload);
    }

    private static PreviewSessionStartRequest ParseStartRequest(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Start payload must be a JSON object.");
        }

        var dotNetCommand = GetString(payload, "dotNetCommand") ?? "dotnet";
        var hostAssemblyPath = GetRequiredString(payload, "hostAssemblyPath");
        var previewerToolPath = GetRequiredString(payload, "previewerToolPath");
        var runtimeConfigPath = GetString(payload, "runtimeConfigPath")
            ?? Path.ChangeExtension(hostAssemblyPath, ".runtimeconfig.json");
        var depsFilePath = GetString(payload, "depsFilePath")
            ?? Path.ChangeExtension(hostAssemblyPath, ".deps.json");
        var sourceAssemblyPath = GetRequiredString(payload, "sourceAssemblyPath");
        var xamlFileProjectPath = GetRequiredString(payload, "xamlFileProjectPath");
        var xamlText = GetRequiredString(payload, "xamlText");
        var compilerMode = GetString(payload, "previewCompilerMode") ?? "avalonia";
        var previewWidth = GetNullableDouble(payload, "previewWidth");
        var previewHeight = GetNullableDouble(payload, "previewHeight");
        var previewScale = GetNullableDouble(payload, "previewScale");

        return new PreviewSessionStartRequest(
            dotNetCommand,
            Path.GetFullPath(hostAssemblyPath),
            Path.GetFullPath(previewerToolPath),
            Path.GetFullPath(runtimeConfigPath),
            Path.GetFullPath(depsFilePath),
            Path.GetFullPath(sourceAssemblyPath),
            xamlFileProjectPath,
            xamlText,
            compilerMode,
            previewWidth,
            previewHeight,
            previewScale);
    }

    private static string ParseUpdateText(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Update payload must be a JSON object.");
        }

        return GetRequiredString(payload, "xamlText");
    }

    private static string GetRequiredString(JsonElement payload, string propertyName)
    {
        return GetString(payload, propertyName)
               ?? throw new InvalidOperationException(propertyName + " is required.");
    }

    private static string? GetString(JsonElement payload, string propertyName)
    {
        return payload.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static double? GetNullableDouble(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out var value))
        {
            return null;
        }

        return value;
    }

    private static Task WriteEventAsync(string eventName, object payload)
    {
        return WriteResponseAsync(new
        {
            kind = "event",
            @event = eventName,
            payload
        });
    }

    private static async Task WriteResponseAsync(object response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        await OutputGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Console.Out.WriteLineAsync(json).ConfigureAwait(false);
            await Console.Out.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            OutputGate.Release();
        }
    }

    private readonly record struct HostCommand(string Name, string? RequestId, JsonElement Payload);
}
