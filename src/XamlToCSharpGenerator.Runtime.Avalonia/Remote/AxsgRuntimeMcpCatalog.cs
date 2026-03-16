using System.Collections.Generic;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;

namespace XamlToCSharpGenerator.Runtime;

internal static class AxsgRuntimeMcpCatalog
{
    internal const string HotReloadStatusResourceUri = "axsg://runtime/hotreload/status";
    internal const string HotReloadEventsResourceUri = "axsg://runtime/hotreload/events";
    internal const string HotDesignStatusResourceUri = "axsg://runtime/hotdesign/status";
    internal const string HotDesignDocumentsResourceUri = "axsg://runtime/hotdesign/documents";
    internal const string HotDesignEventsResourceUri = "axsg://runtime/hotdesign/events";
    internal const string StudioStatusResourceUri = "axsg://runtime/studio/status";
    internal const string StudioEventsResourceUri = "axsg://runtime/studio/events";

    public static IReadOnlyList<McpToolDefinition> CreateTools(AxsgRuntimeQueryService runtimeQueryService)
    {
        return
        [
            new McpToolDefinition(
                "axsg.hotReload.status",
                "Get source-generated hot reload runtime status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(runtimeQueryService.GetHotReloadStatus())),
            new McpToolDefinition(
                "axsg.hotDesign.status",
                "Get source-generated hot design runtime status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(runtimeQueryService.GetHotDesignStatus()))),
            new McpToolDefinition(
                "axsg.hotDesign.documents",
                "List registered hot design documents.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignDocumentsPayload(runtimeQueryService.GetHotDesignDocuments()))),
            new McpToolDefinition(
                "axsg.hotDesign.workspace",
                "Get the hot design workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional active build URI.")),
                    ("search", McpSchemaBuilder.BuildStringSchema("Optional text filter for the workspace snapshot."))),
                (arguments, _) =>
                {
                    var workspace = runtimeQueryService.GetHotDesignWorkspace(
                        NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                        NormalizeOptionalText(arguments?["search"]?.GetValue<string>()));
                    return ValueTask.FromResult<object?>(
                        AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                            workspace,
                            runtimeQueryService.GetStudioStatus()));
                }),
            new McpToolDefinition(
                "axsg.studio.status",
                "Get source-generated studio session status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(runtimeQueryService.GetStudioStatus())))
        ];
    }

    public static IReadOnlyDictionary<string, McpResourceDefinition> CreateResources(
        AxsgRuntimeQueryService runtimeQueryService,
        AxsgRuntimeMcpEventStore? eventStore = null)
    {
        var resources = new Dictionary<string, McpResourceDefinition>(StringComparer.Ordinal)
        {
            [HotReloadStatusResourceUri] = new McpResourceDefinition(
                HotReloadStatusResourceUri,
                "AXSG Hot Reload Status",
                "Current source-generated hot reload runtime status.",
                "application/json",
                _ => ValueTask.FromResult<object?>(runtimeQueryService.GetHotReloadStatus())),
            [HotDesignStatusResourceUri] = new McpResourceDefinition(
                HotDesignStatusResourceUri,
                "AXSG Hot Design Status",
                "Current hot design runtime status.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(runtimeQueryService.GetHotDesignStatus()))),
            [HotDesignDocumentsResourceUri] = new McpResourceDefinition(
                HotDesignDocumentsResourceUri,
                "AXSG Hot Design Documents",
                "Registered hot design documents.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignDocumentsPayload(runtimeQueryService.GetHotDesignDocuments()))),
            [StudioStatusResourceUri] = new McpResourceDefinition(
                StudioStatusResourceUri,
                "AXSG Studio Status",
                "Current source-generated studio session status.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(runtimeQueryService.GetStudioStatus())))
        };

        if (eventStore is not null)
        {
            resources[HotReloadEventsResourceUri] = new McpResourceDefinition(
                HotReloadEventsResourceUri,
                "AXSG Hot Reload Events",
                "Recent hot reload runtime events.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildRuntimeEventsPayload(eventStore.GetHotReloadEvents())));

            resources[HotDesignEventsResourceUri] = new McpResourceDefinition(
                HotDesignEventsResourceUri,
                "AXSG Hot Design Events",
                "Recent hot design runtime events.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildRuntimeEventsPayload(eventStore.GetHotDesignEvents())));

            resources[StudioEventsResourceUri] = new McpResourceDefinition(
                StudioEventsResourceUri,
                "AXSG Studio Events",
                "Recent studio runtime events.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildRuntimeEventsPayload(eventStore.GetStudioEvents())));
        }

        return resources;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
