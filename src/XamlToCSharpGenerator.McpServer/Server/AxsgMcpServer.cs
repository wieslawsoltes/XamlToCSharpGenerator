using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Remote;
using XamlToCSharpGenerator.RemoteProtocol.Mcp;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.McpServer.Server;

internal sealed class AxsgMcpServer : IDisposable
{
    private readonly XamlLanguageServiceEngine _engine;
    private readonly McpServerCore _server;

    public AxsgMcpServer(
        RemoteProtocol.JsonRpc.JsonRpcMessageReader reader,
        RemoteProtocol.JsonRpc.JsonRpcMessageWriter writer,
        XamlLanguageServiceEngine engine,
        XamlLanguageServiceOptions options)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        var previewQueryService = new AxsgPreviewQueryService(_engine, options ?? throw new ArgumentNullException(nameof(options)));
        var runtimeQueryService = new AxsgRuntimeQueryService();
        var tools = new List<McpToolDefinition>(AxsgRuntimeMcpCatalog.CreateTools(runtimeQueryService))
        {
            new(
                "axsg.preview.projectContext",
                "Resolve the project and target-relative XAML path used by AXSG preview.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "uri" },
                    ("uri", McpSchemaBuilder.BuildStringSchema("File URI for the XAML or AXAML document.")),
                    ("workspaceRoot", McpSchemaBuilder.BuildStringSchema("Optional workspace root override."))),
                (arguments, cancellationToken) => GetPreviewProjectContextAsync(previewQueryService, arguments, cancellationToken))
        };

        _server = new McpServerCore(
            reader,
            writer,
            new McpServerInfo(
                Name: "axsg-mcp",
                Version: typeof(AxsgMcpServer).Assembly.GetName().Version?.ToString() ?? "0.0.0",
                Instructions: "Provides AXSG workspace and query tools. This host is query-oriented: poll tools or resources after editor/workspace changes. Live runtime subscriptions are exposed by the runtime MCP host, not the workspace host."),
            tools,
            AxsgRuntimeMcpCatalog.CreateResources(runtimeQueryService));
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        return await _server.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _server.Dispose();
        _engine.Dispose();
    }

    private static async ValueTask<object?> GetPreviewProjectContextAsync(
        AxsgPreviewQueryService previewQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        var uri = NormalizeOptionalText(arguments?["uri"]?.GetValue<string>());
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new McpToolException(new
            {
                message = "uri is required."
            });
        }

        var workspaceRoot = NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>());
        var context = await previewQueryService.GetPreviewProjectContextAsync(uri, workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (context is null)
        {
            throw new McpToolException(new
            {
                message = "Preview project context could not be resolved.",
                uri
            });
        }

        return context;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
