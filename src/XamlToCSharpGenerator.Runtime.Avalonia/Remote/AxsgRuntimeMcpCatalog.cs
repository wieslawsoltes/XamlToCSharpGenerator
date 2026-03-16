using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;

namespace XamlToCSharpGenerator.Runtime;

internal static class AxsgRuntimeMcpCatalog
{
    internal const string HotReloadStatusResourceUri = "axsg://runtime/hotreload/status";
    internal const string HotReloadEventsResourceUri = "axsg://runtime/hotreload/events";
    internal const string HotDesignStatusResourceUri = "axsg://runtime/hotdesign/status";
    internal const string HotDesignDocumentsResourceUri = "axsg://runtime/hotdesign/documents";
    internal const string HotDesignCurrentWorkspaceResourceUri = "axsg://runtime/hotdesign/workspace/current";
    internal const string HotDesignSelectedDocumentResourceUri = "axsg://runtime/hotdesign/document/selected";
    internal const string HotDesignSelectedElementResourceUri = "axsg://runtime/hotdesign/element/selected";
    internal const string HotDesignEventsResourceUri = "axsg://runtime/hotdesign/events";
    internal const string HotDesignWorkspaceByBuildUriResourceUriPrefix = "axsg://runtime/hotdesign/workspace/by-build-uri/";
    internal const string StudioStatusResourceUri = "axsg://runtime/studio/status";
    internal const string StudioScopesResourceUri = "axsg://runtime/studio/scopes";
    internal const string StudioEventsResourceUri = "axsg://runtime/studio/events";

    public static IReadOnlyList<McpToolDefinition> CreateQueryTools(
        AxsgRuntimeQueryService runtimeQueryService)
    {
        ArgumentNullException.ThrowIfNull(runtimeQueryService);

        return
        [
            new McpToolDefinition(
                "axsg.hotReload.status",
                "Get source-generated hot reload runtime status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotReloadStatusPayload(runtimeQueryService.GetHotReloadStatus()))),
            new McpToolDefinition(
                "axsg.hotReload.trackedDocuments",
                "List the currently tracked hot reload documents.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotReloadTrackedDocumentsPayload(
                        runtimeQueryService.GetHotReloadTrackedDocuments()))),
            new McpToolDefinition(
                "axsg.hotReload.remoteTransportStatus",
                "Get the current hot reload transport status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotReloadTransportStatusPayload(
                        runtimeQueryService.GetHotReloadTransportStatus()))),
            new McpToolDefinition(
                "axsg.hotReload.lastOperation",
                "Get the last remote hot reload operation status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotReloadRemoteOperationStatusPayload(
                        runtimeQueryService.GetHotReloadLastOperation()))),
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
                            runtimeQueryService.GetStudioStatus(),
                            runtimeQueryService.GetHotDesignHitTestMode()));
                }),
            new McpToolDefinition(
                "axsg.studio.status",
                "Get source-generated studio session status.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(runtimeQueryService.GetStudioStatus()))),
            new McpToolDefinition(
                "axsg.studio.scopes",
                "List the current studio scopes.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioScopesPayload(runtimeQueryService.GetStudioScopes())))
        ];
    }

    public static IReadOnlyList<McpToolDefinition> CreateTools(
        AxsgRuntimeQueryService runtimeQueryService,
        AxsgRuntimeHotReloadService? hotReloadService = null,
        AxsgRuntimeHotDesignService? hotDesignService = null,
        AxsgRuntimeStudioService? studioService = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeQueryService);
        hotReloadService ??= new AxsgRuntimeHotReloadService(runtimeQueryService);
        hotDesignService ??= new AxsgRuntimeHotDesignService(runtimeQueryService);
        studioService ??= new AxsgRuntimeStudioService(runtimeQueryService);

        var tools = new List<McpToolDefinition>(CreateQueryTools(runtimeQueryService))
        {
            new McpToolDefinition(
                "axsg.hotReload.enable",
                "Enable source-generated hot reload runtime.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteHotReloadStatusMutation(hotReloadService.Enable)),
            new McpToolDefinition(
                "axsg.hotReload.disable",
                "Disable source-generated hot reload runtime.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteHotReloadStatusMutation(hotReloadService.Disable)),
            new McpToolDefinition(
                "axsg.hotReload.toggle",
                "Toggle source-generated hot reload runtime.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteHotReloadStatusMutation(hotReloadService.Toggle)),
            new McpToolDefinition(
                "axsg.hotDesign.enable",
                "Enable source-generated hot design runtime mode.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(hotDesignService.Enable()))),
            new McpToolDefinition(
                "axsg.hotDesign.disable",
                "Disable source-generated hot design runtime mode.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(hotDesignService.Disable()))),
            new McpToolDefinition(
                "axsg.hotDesign.toggle",
                "Toggle source-generated hot design runtime mode.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotDesignStatusPayload(hotDesignService.Toggle()))),
            new McpToolDefinition(
                "axsg.hotDesign.selectDocument",
                "Select the active hot design document and return the updated workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "buildUri" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Build URI for the hot design document."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () => hotDesignService.SelectDocument(RequiredText(arguments, "buildUri")))),
            new McpToolDefinition(
                "axsg.hotDesign.selectElement",
                "Select the active hot design element and return the updated workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementId" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override. Defaults to the active document.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Element id to select."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () => hotDesignService.SelectElement(
                        NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                        RequiredText(arguments, "elementId")))),
            new McpToolDefinition(
                "axsg.hotDesign.applyDocumentText",
                "Apply updated XAML text to a hot design document and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "xamlText" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override. Defaults to the active document.")),
                    ("xamlText", McpSchemaBuilder.BuildStringSchema("Updated XAML text."))),
                async (arguments, cancellationToken) =>
                {
                    SourceGenHotDesignApplyResult result = await hotDesignService
                        .ApplyDocumentTextAsync(
                            NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                            RequiredText(arguments, "xamlText"),
                            cancellationToken)
                        .ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.applyPropertyUpdate",
                "Apply a hot design property update and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "propertyName" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("targetTypeName", McpSchemaBuilder.BuildStringSchema("Optional target type full name override.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Optional target element id.")),
                    ("propertyName", McpSchemaBuilder.BuildStringSchema("Property name to update.")),
                    ("propertyValue", McpSchemaBuilder.BuildStringSchema("Optional property value.")),
                    ("removeProperty", McpSchemaBuilder.BuildBooleanSchema("Remove the property instead of setting a value.")),
                    ("persistChangesToSource", McpSchemaBuilder.BuildBooleanSchema("Optional override for source persistence.")),
                    ("waitForHotReload", McpSchemaBuilder.BuildBooleanSchema("Optional override for waiting on hot reload.")),
                    ("fallbackToRuntimeApplyOnTimeout", McpSchemaBuilder.BuildBooleanSchema("Optional override for runtime fallback on timeout."))),
                async (arguments, cancellationToken) =>
                {
                    var request = new SourceGenHotDesignPropertyUpdateRequest
                    {
                        BuildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                        TargetTypeName = NormalizeOptionalText(arguments?["targetTypeName"]?.GetValue<string>()),
                        ElementId = NormalizeOptionalText(arguments?["elementId"]?.GetValue<string>()),
                        PropertyName = RequiredText(arguments, "propertyName"),
                        PropertyValue = NormalizeOptionalText(arguments?["propertyValue"]?.GetValue<string>()),
                        RemoveProperty = OptionalBoolean(arguments, "removeProperty") ?? false,
                        PersistChangesToSource = OptionalBoolean(arguments, "persistChangesToSource"),
                        WaitForHotReload = OptionalBoolean(arguments, "waitForHotReload"),
                        FallbackToRuntimeApplyOnTimeout = OptionalBoolean(arguments, "fallbackToRuntimeApplyOnTimeout")
                    };
                    SourceGenHotDesignApplyResult result = await hotDesignService.ApplyPropertyUpdateAsync(request, cancellationToken).ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, request.BuildUri ?? result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.insertElement",
                "Insert a hot design element and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementName" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("targetTypeName", McpSchemaBuilder.BuildStringSchema("Optional target type full name override.")),
                    ("parentElementId", McpSchemaBuilder.BuildStringSchema("Optional parent element id.")),
                    ("elementName", McpSchemaBuilder.BuildStringSchema("Element name to insert.")),
                    ("xamlFragment", McpSchemaBuilder.BuildStringSchema("Optional XAML fragment override.")),
                    ("persistChangesToSource", McpSchemaBuilder.BuildBooleanSchema("Optional override for source persistence.")),
                    ("waitForHotReload", McpSchemaBuilder.BuildBooleanSchema("Optional override for waiting on hot reload.")),
                    ("fallbackToRuntimeApplyOnTimeout", McpSchemaBuilder.BuildBooleanSchema("Optional override for runtime fallback on timeout."))),
                async (arguments, cancellationToken) =>
                {
                    var request = new SourceGenHotDesignElementInsertRequest
                    {
                        BuildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                        TargetTypeName = NormalizeOptionalText(arguments?["targetTypeName"]?.GetValue<string>()),
                        ParentElementId = NormalizeOptionalText(arguments?["parentElementId"]?.GetValue<string>()),
                        ElementName = RequiredText(arguments, "elementName"),
                        XamlFragment = NormalizeOptionalText(arguments?["xamlFragment"]?.GetValue<string>()),
                        PersistChangesToSource = OptionalBoolean(arguments, "persistChangesToSource"),
                        WaitForHotReload = OptionalBoolean(arguments, "waitForHotReload"),
                        FallbackToRuntimeApplyOnTimeout = OptionalBoolean(arguments, "fallbackToRuntimeApplyOnTimeout")
                    };
                    SourceGenHotDesignApplyResult result = await hotDesignService.InsertElementAsync(request, cancellationToken).ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, request.BuildUri ?? result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.removeElement",
                "Remove a hot design element and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "elementId" },
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override.")),
                    ("targetTypeName", McpSchemaBuilder.BuildStringSchema("Optional target type full name override.")),
                    ("elementId", McpSchemaBuilder.BuildStringSchema("Element id to remove.")),
                    ("persistChangesToSource", McpSchemaBuilder.BuildBooleanSchema("Optional override for source persistence.")),
                    ("waitForHotReload", McpSchemaBuilder.BuildBooleanSchema("Optional override for waiting on hot reload.")),
                    ("fallbackToRuntimeApplyOnTimeout", McpSchemaBuilder.BuildBooleanSchema("Optional override for runtime fallback on timeout."))),
                async (arguments, cancellationToken) =>
                {
                    var request = new SourceGenHotDesignElementRemoveRequest
                    {
                        BuildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
                        TargetTypeName = NormalizeOptionalText(arguments?["targetTypeName"]?.GetValue<string>()),
                        ElementId = RequiredText(arguments, "elementId"),
                        PersistChangesToSource = OptionalBoolean(arguments, "persistChangesToSource"),
                        WaitForHotReload = OptionalBoolean(arguments, "waitForHotReload"),
                        FallbackToRuntimeApplyOnTimeout = OptionalBoolean(arguments, "fallbackToRuntimeApplyOnTimeout")
                    };
                    SourceGenHotDesignApplyResult result = await hotDesignService.RemoveElementAsync(request, cancellationToken).ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, request.BuildUri ?? result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.undo",
                "Undo the last hot design change and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override. Defaults to the active document."))),
                async (arguments, cancellationToken) =>
                {
                    string? buildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>());
                    SourceGenHotDesignApplyResult result = await hotDesignService.UndoAsync(buildUri, cancellationToken).ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, buildUri ?? result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.redo",
                "Redo the last hot design change and return the apply result plus refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI override. Defaults to the active document."))),
                async (arguments, cancellationToken) =>
                {
                    string? buildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>());
                    SourceGenHotDesignApplyResult result = await hotDesignService.RedoAsync(buildUri, cancellationToken).ConfigureAwait(false);
                    return BuildHotDesignMutationPayload(runtimeQueryService, result, buildUri ?? result.BuildUri);
                }),
            new McpToolDefinition(
                "axsg.hotDesign.setWorkspaceMode",
                "Set the hot design workspace mode and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Workspace mode: Agent, Design, or Interactive."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetWorkspaceMode(ParseEnum<SourceGenHotDesignWorkspaceMode>(arguments, "mode"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setPropertyFilterMode",
                "Set the hot design property filter mode and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Property filter mode: Smart or All."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetPropertyFilterMode(ParseEnum<SourceGenHotDesignPropertyFilterMode>(arguments, "mode"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setHitTestMode",
                "Set the hot design hit-test mode and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "mode" },
                    ("mode", McpSchemaBuilder.BuildStringSchema("Hit test mode: Logical or Visual."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetHitTestMode(ParseEnum<SourceGenHotDesignHitTestMode>(arguments, "mode"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.togglePanel",
                "Toggle the visibility of a hot design panel and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "panel" },
                    ("panel", McpSchemaBuilder.BuildStringSchema("Panel kind: Toolbar, Elements, Toolbox, Canvas, or Properties."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.TogglePanel(ParseEnum<SourceGenHotDesignPanelKind>(arguments, "panel"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setPanelVisibility",
                "Set the visibility of a hot design panel and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "panel", "visible" },
                    ("panel", McpSchemaBuilder.BuildStringSchema("Panel kind: Toolbar, Elements, Toolbox, Canvas, or Properties.")),
                    ("visible", McpSchemaBuilder.BuildBooleanSchema("True to show the panel, false to hide it."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetPanelVisibility(
                            ParseEnum<SourceGenHotDesignPanelKind>(arguments, "panel"),
                            RequiredBoolean(arguments, "visible"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setCanvasZoom",
                "Set the hot design canvas zoom and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "zoom" },
                    ("zoom", McpSchemaBuilder.BuildNumberSchema("Canvas zoom level."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetCanvasZoom(RequiredNumber(arguments, "zoom"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setCanvasFormFactor",
                "Set the hot design canvas form factor and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "formFactor" },
                    ("formFactor", McpSchemaBuilder.BuildStringSchema("Named form factor.")),
                    ("width", McpSchemaBuilder.BuildNumberSchema("Optional width override.")),
                    ("height", McpSchemaBuilder.BuildNumberSchema("Optional height override."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetCanvasFormFactor(
                            RequiredText(arguments, "formFactor"),
                            OptionalNumber(arguments, "width"),
                            OptionalNumber(arguments, "height"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.hotDesign.setCanvasTheme",
                "Set the hot design canvas theme and return the refreshed workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "darkTheme" },
                    ("darkTheme", McpSchemaBuilder.BuildBooleanSchema("True for dark theme, false for light theme."))),
                (arguments, _) => ExecuteHotDesignWorkspaceMutation(
                    runtimeQueryService,
                    () =>
                    {
                        hotDesignService.SetCanvasTheme(RequiredBoolean(arguments, "darkTheme"));
                        return runtimeQueryService.GetHotDesignWorkspace();
                    })),
            new McpToolDefinition(
                "axsg.studio.enable",
                "Enable source-generated studio mode and return the updated studio status snapshot.",
                BuildStudioOptionsSchema(),
                (arguments, _) => ExecuteStudioStatusMutation(
                    () => CreateStudioOptionsOrNull(arguments, runtimeQueryService.GetStudioStatus().Options) is { } options
                        ? studioService.Enable(options)
                        : studioService.Enable())),
            new McpToolDefinition(
                "axsg.studio.disable",
                "Disable source-generated studio mode and return the updated studio status snapshot.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteStudioStatusMutation(studioService.Disable)),
            new McpToolDefinition(
                "axsg.studio.configure",
                "Configure studio runtime options and return the updated studio status snapshot.",
                BuildStudioOptionsSchema(),
                (arguments, _) => ExecuteStudioStatusMutation(
                    () => studioService.Configure(CreateStudioOptions(arguments, runtimeQueryService.GetStudioStatus().Options)))),
            new McpToolDefinition(
                "axsg.studio.startSession",
                "Start a new studio session and return the updated studio status snapshot.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteStudioStatusMutation(
                    () =>
                    {
                        studioService.StartSession();
                        return runtimeQueryService.GetStudioStatus();
                    })),
            new McpToolDefinition(
                "axsg.studio.stopSession",
                "Stop the active studio session and return the updated studio status snapshot.",
                McpSchemaBuilder.BuildObjectSchema(),
                (_, _) => ExecuteStudioStatusMutation(studioService.StopSession)),
            new McpToolDefinition(
                "axsg.studio.applyUpdate",
                "Apply a studio update request and return the apply result plus refreshed studio status and workspace snapshot.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "xamlText" },
                    ("requestId", McpSchemaBuilder.BuildStringSchema("Optional studio request identifier.")),
                    ("correlationId", McpSchemaBuilder.BuildIntegerSchema("Optional correlation id.")),
                    ("buildUri", McpSchemaBuilder.BuildStringSchema("Optional build URI. Defaults to the active document.")),
                    ("targetTypeName", McpSchemaBuilder.BuildStringSchema("Optional target type full name override.")),
                    ("scopeKind", McpSchemaBuilder.BuildStringSchema("Optional scope kind override.")),
                    ("scopeId", McpSchemaBuilder.BuildStringSchema("Optional scope id override.")),
                    ("xamlText", McpSchemaBuilder.BuildStringSchema("Updated XAML text.")),
                    ("waitMode", McpSchemaBuilder.BuildStringSchema("Optional wait mode override.")),
                    ("fallbackPolicy", McpSchemaBuilder.BuildStringSchema("Optional fallback policy override.")),
                    ("persistChangesToSource", McpSchemaBuilder.BuildBooleanSchema("Optional source persistence override.")),
                    ("timeoutMs", McpSchemaBuilder.BuildIntegerSchema("Optional timeout override in milliseconds."))),
                async (arguments, cancellationToken) =>
                {
                    SourceGenStudioUpdateResult result;
                    try
                    {
                        result = await studioService
                            .ApplyUpdateAsync(BuildStudioUpdateRequest(arguments), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                    {
                        throw new McpToolException(new
                        {
                            message = ex.Message
                        });
                    }

                    return AxsgRuntimePayloadBuilder.BuildStudioApplyPayload(
                        result,
                        runtimeQueryService.GetStudioStatus(),
                        TryBuildWorkspacePayload(runtimeQueryService, result.BuildUri));
                }),
        };

        return tools;
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
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildHotReloadStatusPayload(runtimeQueryService.GetHotReloadStatus()))),
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
            [HotDesignCurrentWorkspaceResourceUri] = new McpResourceDefinition(
                HotDesignCurrentWorkspaceResourceUri,
                "AXSG Hot Design Workspace",
                "Current hot design workspace snapshot.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    BuildHotDesignWorkspaceResourcePayload(runtimeQueryService, buildUri: null))),
            [HotDesignSelectedDocumentResourceUri] = new McpResourceDefinition(
                HotDesignSelectedDocumentResourceUri,
                "AXSG Hot Design Selected Document",
                "Currently selected hot design document projection.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    BuildHotDesignSelectedDocumentResourcePayload(runtimeQueryService, buildUri: null))),
            [HotDesignSelectedElementResourceUri] = new McpResourceDefinition(
                HotDesignSelectedElementResourceUri,
                "AXSG Hot Design Selected Element",
                "Currently selected hot design element projection.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    BuildHotDesignSelectedElementResourcePayload(runtimeQueryService, buildUri: null))),
            [StudioStatusResourceUri] = new McpResourceDefinition(
                StudioStatusResourceUri,
                "AXSG Studio Status",
                "Current source-generated studio session status.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(runtimeQueryService.GetStudioStatus()))),
            [StudioScopesResourceUri] = new McpResourceDefinition(
                StudioScopesResourceUri,
                "AXSG Studio Scopes",
                "Current source-generated studio scopes.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    AxsgRuntimePayloadBuilder.BuildStudioScopesPayload(runtimeQueryService.GetStudioScopes())))
        };

        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents = runtimeQueryService.GetHotDesignDocuments();
        for (int index = 0; index < documents.Count; index++)
        {
            SourceGenHotDesignDocumentDescriptor document = documents[index];
            string resourceUri = CreateHotDesignWorkspaceByBuildUriResourceUri(document.BuildUri);
            string buildUri = document.BuildUri;
            resources[resourceUri] = new McpResourceDefinition(
                resourceUri,
                "AXSG Hot Design Workspace: " + buildUri,
                "Hot design workspace snapshot for build URI '" + buildUri + "'.",
                "application/json",
                _ => ValueTask.FromResult<object?>(
                    BuildHotDesignWorkspaceResourcePayload(runtimeQueryService, buildUri)));
        }

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

    internal static string CreateHotDesignWorkspaceByBuildUriResourceUri(string buildUri)
    {
        if (string.IsNullOrWhiteSpace(buildUri))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(buildUri));
        }

        return HotDesignWorkspaceByBuildUriResourceUriPrefix + Uri.EscapeDataString(buildUri.Trim());
    }

    internal static IEnumerable<string> EnumerateHotDesignWorkspaceResourceUris(
        IReadOnlyList<SourceGenHotDesignDocumentDescriptor> documents)
    {
        yield return HotDesignCurrentWorkspaceResourceUri;

        for (int index = 0; index < documents.Count; index++)
        {
            yield return CreateHotDesignWorkspaceByBuildUriResourceUri(documents[index].BuildUri);
        }
    }

    private static JsonObject BuildStudioOptionsSchema()
    {
        return McpSchemaBuilder.BuildObjectSchema(
            ("persistChangesToSource", McpSchemaBuilder.BuildBooleanSchema("Optional source persistence setting.")),
            ("waitMode", McpSchemaBuilder.BuildStringSchema("Optional wait mode override.")),
            ("updateTimeoutMs", McpSchemaBuilder.BuildIntegerSchema("Optional update timeout in milliseconds.")),
            ("fallbackPolicy", McpSchemaBuilder.BuildStringSchema("Optional fallback policy override.")),
            ("showOverlayIndicator", McpSchemaBuilder.BuildBooleanSchema("Optional overlay indicator visibility.")),
            ("enableExternalWindow", McpSchemaBuilder.BuildBooleanSchema("Optional external window toggle.")),
            ("autoOpenStudioWindowOnStartup", McpSchemaBuilder.BuildBooleanSchema("Optional auto-open studio window toggle.")),
            ("enableTracing", McpSchemaBuilder.BuildBooleanSchema("Optional tracing toggle.")),
            ("canvasLayoutMode", McpSchemaBuilder.BuildStringSchema("Optional canvas layout mode override.")),
            ("maxOperationHistoryEntries", McpSchemaBuilder.BuildIntegerSchema("Optional operation history limit.")),
            ("enableRemoteDesign", McpSchemaBuilder.BuildBooleanSchema("Optional remote design toggle.")),
            ("remoteHost", McpSchemaBuilder.BuildStringSchema("Optional remote design host.")),
            ("remotePort", McpSchemaBuilder.BuildIntegerSchema("Optional remote design port.")),
            ("vncEndpoint", McpSchemaBuilder.BuildStringSchema("Optional VNC endpoint.")),
            ("autoOpenVncViewerOnDesktop", McpSchemaBuilder.BuildBooleanSchema("Optional auto-open VNC viewer toggle.")));
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ValueTask<object?> ExecuteStudioStatusMutation(Func<SourceGenStudioStatusSnapshot> mutation)
    {
        try
        {
            return ValueTask.FromResult<object?>(
                AxsgRuntimePayloadBuilder.BuildStudioStatusPayload(mutation()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new McpToolException(new
            {
                message = ex.Message
            });
        }
    }

    private static ValueTask<object?> ExecuteHotReloadStatusMutation(Func<SourceGenHotReloadStatus> mutation)
    {
        try
        {
            return ValueTask.FromResult<object?>(
                AxsgRuntimePayloadBuilder.BuildHotReloadStatusPayload(mutation()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new McpToolException(new
            {
                message = ex.Message
            });
        }
    }

    private static ValueTask<object?> ExecuteHotDesignWorkspaceMutation(
        AxsgRuntimeQueryService runtimeQueryService,
        Func<SourceGenHotDesignWorkspaceSnapshot> mutation)
    {
        try
        {
            SourceGenHotDesignWorkspaceSnapshot workspace = mutation();
            return ValueTask.FromResult<object?>(
                AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                    workspace,
                    runtimeQueryService.GetStudioStatus(),
                    runtimeQueryService.GetHotDesignHitTestMode()));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new McpToolException(new
            {
                message = ex.Message
            });
        }
    }

    private static object? TryBuildWorkspacePayload(
        AxsgRuntimeQueryService runtimeQueryService,
        string? buildUri)
    {
        try
        {
            return BuildHotDesignWorkspaceResourcePayload(runtimeQueryService, buildUri);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return new
            {
                error = ex.Message
            };
        }
    }

    private static object BuildHotDesignWorkspaceResourcePayload(
        AxsgRuntimeQueryService runtimeQueryService,
        string? buildUri)
    {
        return AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
            runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null),
            runtimeQueryService.GetStudioStatus(),
            runtimeQueryService.GetHotDesignHitTestMode());
    }

    private static object BuildHotDesignSelectedDocumentResourcePayload(
        AxsgRuntimeQueryService runtimeQueryService,
        string? buildUri)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null);
        return AxsgRuntimePayloadBuilder.BuildHotDesignSelectedDocumentPayload(
            workspace.ActiveBuildUri,
            FindSelectedDocument(workspace));
    }

    private static object BuildHotDesignSelectedElementResourcePayload(
        AxsgRuntimeQueryService runtimeQueryService,
        string? buildUri)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null);
        return AxsgRuntimePayloadBuilder.BuildHotDesignSelectedElementPayload(
            workspace.ActiveBuildUri,
            workspace.SelectedElementId,
            FindSelectedElement(workspace.Elements, workspace.SelectedElementId));
    }

    private static SourceGenHotDesignDocumentDescriptor? FindSelectedDocument(SourceGenHotDesignWorkspaceSnapshot workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace.ActiveBuildUri))
        {
            return null;
        }

        for (int index = 0; index < workspace.Documents.Count; index++)
        {
            SourceGenHotDesignDocumentDescriptor document = workspace.Documents[index];
            if (string.Equals(document.BuildUri, workspace.ActiveBuildUri, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static SourceGenHotDesignElementNode? FindSelectedElement(
        IReadOnlyList<SourceGenHotDesignElementNode> elements,
        string? selectedElementId)
    {
        if (string.IsNullOrWhiteSpace(selectedElementId))
        {
            return null;
        }

        for (int index = 0; index < elements.Count; index++)
        {
            SourceGenHotDesignElementNode element = elements[index];
            if (string.Equals(element.Id, selectedElementId, StringComparison.Ordinal))
            {
                return element;
            }

            SourceGenHotDesignElementNode? child = FindSelectedElement(element.Children, selectedElementId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static SourceGenStudioOptions? CreateStudioOptionsOrNull(
        JsonObject? arguments,
        SourceGenStudioOptions baseOptions)
    {
        return HasAnyStudioOption(arguments) ? CreateStudioOptions(arguments, baseOptions) : null;
    }

    private static SourceGenStudioOptions CreateStudioOptions(
        JsonObject? arguments,
        SourceGenStudioOptions baseOptions)
    {
        var options = baseOptions.Clone();
        options.PersistChangesToSource = OptionalBoolean(arguments, "persistChangesToSource") ?? options.PersistChangesToSource;
        options.WaitMode = OptionalEnum(arguments, "waitMode", options.WaitMode);
        if (OptionalInteger(arguments, "updateTimeoutMs") is { } updateTimeoutMs)
        {
            options.UpdateTimeout = TimeSpan.FromMilliseconds(updateTimeoutMs);
        }

        options.FallbackPolicy = OptionalEnum(arguments, "fallbackPolicy", options.FallbackPolicy);
        options.ShowOverlayIndicator = OptionalBoolean(arguments, "showOverlayIndicator") ?? options.ShowOverlayIndicator;
        options.EnableExternalWindow = OptionalBoolean(arguments, "enableExternalWindow") ?? options.EnableExternalWindow;
        options.AutoOpenStudioWindowOnStartup = OptionalBoolean(arguments, "autoOpenStudioWindowOnStartup") ?? options.AutoOpenStudioWindowOnStartup;
        options.EnableTracing = OptionalBoolean(arguments, "enableTracing") ?? options.EnableTracing;
        options.CanvasLayoutMode = OptionalEnum(arguments, "canvasLayoutMode", options.CanvasLayoutMode);
        options.MaxOperationHistoryEntries = OptionalInteger(arguments, "maxOperationHistoryEntries") ?? options.MaxOperationHistoryEntries;
        options.EnableRemoteDesign = OptionalBoolean(arguments, "enableRemoteDesign") ?? options.EnableRemoteDesign;
        options.RemoteHost = NormalizeOptionalText(arguments?["remoteHost"]?.GetValue<string>()) ?? options.RemoteHost;
        options.RemotePort = OptionalInteger(arguments, "remotePort") ?? options.RemotePort;
        options.VncEndpoint = NormalizeOptionalText(arguments?["vncEndpoint"]?.GetValue<string>()) ?? options.VncEndpoint;
        options.AutoOpenVncViewerOnDesktop = OptionalBoolean(arguments, "autoOpenVncViewerOnDesktop") ?? options.AutoOpenVncViewerOnDesktop;
        return options;
    }

    private static SourceGenStudioUpdateRequest BuildStudioUpdateRequest(JsonObject? arguments)
    {
        return new SourceGenStudioUpdateRequest
        {
            RequestId = NormalizeOptionalText(arguments?["requestId"]?.GetValue<string>()),
            CorrelationId = OptionalLong(arguments, "correlationId"),
            BuildUri = NormalizeOptionalText(arguments?["buildUri"]?.GetValue<string>()),
            TargetTypeName = NormalizeOptionalText(arguments?["targetTypeName"]?.GetValue<string>()),
            ScopeKind = OptionalEnum(arguments, "scopeKind", SourceGenStudioScopeKind.RootControl),
            ScopeId = NormalizeOptionalText(arguments?["scopeId"]?.GetValue<string>()),
            XamlText = RequiredText(arguments, "xamlText"),
            WaitMode = OptionalNullableEnum<SourceGenStudioWaitMode>(arguments, "waitMode"),
            FallbackPolicy = OptionalNullableEnum<SourceGenStudioFallbackPolicy>(arguments, "fallbackPolicy"),
            PersistChangesToSource = OptionalBoolean(arguments, "persistChangesToSource"),
            Timeout = OptionalInteger(arguments, "timeoutMs") is { } timeoutMs ? TimeSpan.FromMilliseconds(timeoutMs) : null
        };
    }

    private static bool HasAnyStudioOption(JsonObject? arguments)
    {
        return arguments is not null && (
            arguments["persistChangesToSource"] is not null ||
            arguments["waitMode"] is not null ||
            arguments["updateTimeoutMs"] is not null ||
            arguments["fallbackPolicy"] is not null ||
            arguments["showOverlayIndicator"] is not null ||
            arguments["enableExternalWindow"] is not null ||
            arguments["autoOpenStudioWindowOnStartup"] is not null ||
            arguments["enableTracing"] is not null ||
            arguments["canvasLayoutMode"] is not null ||
            arguments["maxOperationHistoryEntries"] is not null ||
            arguments["enableRemoteDesign"] is not null ||
            arguments["remoteHost"] is not null ||
            arguments["remotePort"] is not null ||
            arguments["vncEndpoint"] is not null ||
            arguments["autoOpenVncViewerOnDesktop"] is not null);
    }

    private static object BuildHotDesignMutationPayload(
        AxsgRuntimeQueryService runtimeQueryService,
        SourceGenHotDesignApplyResult result,
        string? buildUri)
    {
        SourceGenHotDesignWorkspaceSnapshot workspace = runtimeQueryService.GetHotDesignWorkspace(buildUri, search: null);
        return new
        {
            applyResult = AxsgRuntimePayloadBuilder.BuildHotDesignApplyResultPayload(result),
            workspace = AxsgRuntimePayloadBuilder.BuildHotDesignWorkspacePayload(
                workspace,
                runtimeQueryService.GetStudioStatus(),
                runtimeQueryService.GetHotDesignHitTestMode())
        };
    }

    private static string RequiredText(JsonObject? arguments, string name)
    {
        string? value = NormalizeOptionalText(arguments?[name]?.GetValue<string>());
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpToolException(new
            {
                message = name + " is required."
            });
        }

        return value;
    }

    private static bool RequiredBoolean(JsonObject? arguments, string name)
    {
        bool? value = OptionalBoolean(arguments, name);
        if (!value.HasValue)
        {
            throw new McpToolException(new
            {
                message = name + " is required."
            });
        }

        return value.Value;
    }

    private static bool? OptionalBoolean(JsonObject? arguments, string name)
    {
        JsonNode? node = arguments?[name];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            throw new McpToolException(new
            {
                message = name + " must be a boolean."
            });
        }
    }

    private static double RequiredNumber(JsonObject? arguments, string name)
    {
        double? value = OptionalNumber(arguments, name);
        if (!value.HasValue)
        {
            throw new McpToolException(new
            {
                message = name + " is required."
            });
        }

        return value.Value;
    }

    private static double? OptionalNumber(JsonObject? arguments, string name)
    {
        JsonNode? node = arguments?[name];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<double>();
        }
        catch
        {
            throw new McpToolException(new
            {
                message = name + " must be a number."
            });
        }
    }

    private static int? OptionalInteger(JsonObject? arguments, string name)
    {
        JsonNode? node = arguments?[name];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch
        {
            throw new McpToolException(new
            {
                message = name + " must be an integer."
            });
        }
    }

    private static long? OptionalLong(JsonObject? arguments, string name)
    {
        JsonNode? node = arguments?[name];
        if (node is null)
        {
            return null;
        }

        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            throw new McpToolException(new
            {
                message = name + " must be an integer."
            });
        }
    }

    private static TEnum ParseEnum<TEnum>(JsonObject? arguments, string name)
        where TEnum : struct, Enum
    {
        string value = RequiredText(arguments, name);
        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out TEnum parsed))
        {
            return parsed;
        }

        throw new McpToolException(new
        {
            message = name + " must be one of: " + string.Join(", ", Enum.GetNames(typeof(TEnum))) + "."
        });
    }

    private static TEnum OptionalEnum<TEnum>(JsonObject? arguments, string name, TEnum fallback)
        where TEnum : struct, Enum
    {
        return arguments?[name] is null ? fallback : ParseEnum<TEnum>(arguments, name);
    }

    private static TEnum? OptionalNullableEnum<TEnum>(JsonObject? arguments, string name)
        where TEnum : struct, Enum
    {
        return arguments?[name] is null ? null : ParseEnum<TEnum>(arguments, name);
    }
}
