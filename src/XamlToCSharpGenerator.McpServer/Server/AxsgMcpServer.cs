using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Remote;
using XamlToCSharpGenerator.LanguageService.Refactorings;
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
        var navigationOptions = (options ?? throw new ArgumentNullException(nameof(options))) with
        {
            IncludeCompilationDiagnostics = false,
            IncludeSemanticDiagnostics = false
        };
        var previewQueryService = new AxsgPreviewQueryService(_engine, navigationOptions);
        var workspaceQueryService = new AxsgWorkspaceLanguageQueryService(_engine, navigationOptions);
        var runtimeQueryService = new AxsgRuntimeQueryService();
        var tools = new List<McpToolDefinition>(AxsgRuntimeMcpCatalog.CreateQueryTools(runtimeQueryService))
        {
            new(
                "axsg.preview.projectContext",
                "Resolve the project and target-relative XAML path used by AXSG preview.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "uri" },
                    ("uri", McpSchemaBuilder.BuildStringSchema("File URI for the XAML or AXAML document.")),
                    ("workspaceRoot", McpSchemaBuilder.BuildStringSchema("Optional workspace root override."))),
                (arguments, cancellationToken) => GetPreviewProjectContextAsync(previewQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.metadataDocument",
                "Get metadata-as-source document text for an AXSG metadata URI or document id.",
                McpSchemaBuilder.BuildObjectSchema(
                    ("documentId", McpSchemaBuilder.BuildStringSchema("AXSG metadata document id.")),
                    ("metadataUri", McpSchemaBuilder.BuildStringSchema("AXSG metadata URI containing an id query parameter."))),
                (arguments, _) => GetMetadataDocumentAsync(workspaceQueryService, arguments)),
            new(
                "axsg.workspace.inlineCSharpProjections",
                "Get inline C# projection documents for a XAML file.",
                McpSchemaBuilder.BuildObjectSchema(
                    new[] { "uri" },
                    ("uri", McpSchemaBuilder.BuildStringSchema("File URI for the XAML document.")),
                    ("workspaceRoot", McpSchemaBuilder.BuildStringSchema("Optional workspace root override.")),
                    ("documentText", McpSchemaBuilder.BuildStringSchema("Optional in-memory document text override.")),
                    ("version", McpSchemaBuilder.BuildIntegerSchema("Optional in-memory document version."))),
                (arguments, cancellationToken) => GetInlineCSharpProjectionsAsync(workspaceQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.csharpReferences",
                "Get XAML references for a C# symbol.",
                BuildWorkspacePositionSchema(),
                (arguments, cancellationToken) => GetCSharpReferencesAsync(workspaceQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.csharpDeclarations",
                "Get XAML declarations for a C# symbol.",
                BuildWorkspacePositionSchema(),
                (arguments, cancellationToken) => GetCSharpDeclarationsAsync(workspaceQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.renamePropagation",
                "Compute XAML rename propagation edits for a C# symbol rename.",
                BuildWorkspaceRenameSchema(),
                (arguments, cancellationToken) => GetRenamePropagationAsync(workspaceQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.prepareRename",
                "Prepare an AXSG rename operation in XAML.",
                BuildWorkspacePositionSchema(),
                (arguments, cancellationToken) => GetPrepareRenameAsync(workspaceQueryService, arguments, cancellationToken)),
            new(
                "axsg.workspace.rename",
                "Compute AXSG rename edits for a XAML position.",
                BuildWorkspaceRenameSchema(),
                (arguments, cancellationToken) => GetRenameAsync(workspaceQueryService, arguments, cancellationToken))
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

    private static ValueTask<object?> GetMetadataDocumentAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments)
    {
        string? documentId = NormalizeOptionalText(arguments?["documentId"]?.GetValue<string>());
        string? metadataUri = NormalizeOptionalText(arguments?["metadataUri"]?.GetValue<string>());
        if (documentId is null && metadataUri is null)
        {
            throw new McpToolException(new
            {
                message = "documentId or metadataUri is required."
            });
        }

        string? text = workspaceQueryService.GetMetadataDocumentText(documentId, metadataUri);
        return ValueTask.FromResult<object?>(text is null ? null : new JsonObject { ["text"] = text });
    }

    private static async ValueTask<object?> GetInlineCSharpProjectionsAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        string uri = RequiredText(arguments, "uri");
        string? workspaceRoot = NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>());
        string? documentText = arguments?["documentText"]?.GetValue<string>();
        int version = OptionalInteger(arguments, "version") ?? 0;

        var projections = await workspaceQueryService.GetInlineCSharpProjectionsAsync(
            uri,
            workspaceRoot,
            documentText,
            version,
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (XamlInlineCSharpProjection projection in projections)
        {
            payload.Add(new JsonObject
            {
                ["id"] = projection.Id,
                ["kind"] = projection.Kind,
                ["xamlRange"] = SerializeRange(NormalizeTransportRange(projection.XamlRange)),
                ["projectedCodeRange"] = SerializeRange(NormalizeTransportRange(projection.ProjectedCodeRange)),
                ["projectedText"] = projection.ProjectedText
            });
        }

        return payload;
    }

    private static async ValueTask<object?> GetCSharpReferencesAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        var references = await workspaceQueryService.GetCSharpReferencesAsync(
            RequiredText(arguments, "uri"),
            ParseRequiredPosition(arguments),
            NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>()),
            arguments?["documentText"]?.GetValue<string>(),
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (XamlReferenceLocation reference in references)
        {
            payload.Add(new JsonObject
            {
                ["uri"] = reference.Uri,
                ["range"] = SerializeRange(NormalizeTransportRange(reference.Range)),
                ["isDeclaration"] = reference.IsDeclaration
            });
        }

        return payload;
    }

    private static async ValueTask<object?> GetCSharpDeclarationsAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        var declarations = await workspaceQueryService.GetCSharpDeclarationsAsync(
            RequiredText(arguments, "uri"),
            ParseRequiredPosition(arguments),
            NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>()),
            arguments?["documentText"]?.GetValue<string>(),
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (XamlDefinitionLocation declaration in declarations)
        {
            payload.Add(new JsonObject
            {
                ["uri"] = declaration.Uri,
                ["range"] = SerializeRange(NormalizeTransportRange(declaration.Range))
            });
        }

        return payload;
    }

    private static async ValueTask<object?> GetRenamePropagationAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        XamlWorkspaceEdit edit = await workspaceQueryService.GetRenamePropagationEditsAsync(
            RequiredText(arguments, "uri"),
            ParseRequiredPosition(arguments),
            RequiredText(arguments, "newName"),
            NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>()),
            arguments?["documentText"]?.GetValue<string>(),
            cancellationToken).ConfigureAwait(false);

        return SerializeWorkspaceEdit(edit);
    }

    private static async ValueTask<object?> GetPrepareRenameAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        XamlPrepareRenameResult? result = await workspaceQueryService.PrepareRenameAsync(
            RequiredText(arguments, "uri"),
            ParseRequiredPosition(arguments),
            NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>()),
            arguments?["documentText"]?.GetValue<string>(),
            cancellationToken).ConfigureAwait(false);

        return result is null
            ? null
            : new JsonObject
            {
                ["range"] = SerializeRange(result.Range),
                ["placeholder"] = result.Placeholder
            };
    }

    private static async ValueTask<object?> GetRenameAsync(
        AxsgWorkspaceLanguageQueryService workspaceQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        XamlWorkspaceEdit edit = await workspaceQueryService.RenameAsync(
            RequiredText(arguments, "uri"),
            ParseRequiredPosition(arguments),
            RequiredText(arguments, "newName"),
            NormalizeOptionalText(arguments?["workspaceRoot"]?.GetValue<string>()),
            arguments?["documentText"]?.GetValue<string>(),
            cancellationToken).ConfigureAwait(false);

        return SerializeWorkspaceEdit(edit);
    }

    private static async ValueTask<object?> GetPreviewProjectContextAsync(
        AxsgPreviewQueryService previewQueryService,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        string? uri = NormalizeOptionalText(arguments?["uri"]?.GetValue<string>());
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

    private static JsonObject BuildWorkspacePositionSchema()
    {
        return McpSchemaBuilder.BuildObjectSchema(
            new[] { "uri", "line", "character" },
            ("uri", McpSchemaBuilder.BuildStringSchema("File URI for the C# or XAML document.")),
            ("line", McpSchemaBuilder.BuildIntegerSchema("Zero-based source line.")),
            ("character", McpSchemaBuilder.BuildIntegerSchema("Zero-based source character.")),
            ("workspaceRoot", McpSchemaBuilder.BuildStringSchema("Optional workspace root override.")),
            ("documentText", McpSchemaBuilder.BuildStringSchema("Optional in-memory document text override.")));
    }

    private static JsonObject BuildWorkspaceRenameSchema()
    {
        return McpSchemaBuilder.BuildObjectSchema(
            new[] { "uri", "line", "character", "newName" },
            ("uri", McpSchemaBuilder.BuildStringSchema("File URI for the C# or XAML document.")),
            ("line", McpSchemaBuilder.BuildIntegerSchema("Zero-based source line.")),
            ("character", McpSchemaBuilder.BuildIntegerSchema("Zero-based source character.")),
            ("newName", McpSchemaBuilder.BuildStringSchema("Requested replacement identifier.")),
            ("workspaceRoot", McpSchemaBuilder.BuildStringSchema("Optional workspace root override.")),
            ("documentText", McpSchemaBuilder.BuildStringSchema("Optional in-memory document text override.")));
    }

    private static JsonObject SerializeRange(SourceRange range)
    {
        return new JsonObject
        {
            ["start"] = new JsonObject
            {
                ["line"] = range.Start.Line,
                ["character"] = range.Start.Character
            },
            ["end"] = new JsonObject
            {
                ["line"] = range.End.Line,
                ["character"] = range.End.Character
            }
        };
    }

    private static JsonObject SerializeWorkspaceEdit(XamlWorkspaceEdit edit)
    {
        var changes = new JsonObject();
        foreach (var pair in edit.Changes)
        {
            var edits = new JsonArray();
            foreach (XamlDocumentTextEdit documentEdit in pair.Value)
            {
                edits.Add(new JsonObject
                {
                    ["range"] = SerializeRange(documentEdit.Range),
                    ["newText"] = documentEdit.NewText
                });
            }

            changes[pair.Key] = edits;
        }

        return new JsonObject
        {
            ["changes"] = changes
        };
    }

    private static SourceRange NormalizeTransportRange(SourceRange range)
    {
        int startLine = Math.Max(0, range.Start.Line);
        int startCharacter = Math.Max(0, range.Start.Character);
        int endLine = Math.Max(0, range.End.Line);
        int endCharacter = Math.Max(0, range.End.Character);

        if (endLine < startLine || (endLine == startLine && endCharacter <= startCharacter))
        {
            endLine = startLine;
            endCharacter = startCharacter + 1;
        }

        return new SourceRange(
            new SourcePosition(startLine, startCharacter),
            new SourcePosition(endLine, endCharacter));
    }

    private static SourcePosition ParseRequiredPosition(JsonObject? arguments)
    {
        return new SourcePosition(
            Math.Max(0, RequiredInteger(arguments, "line")),
            Math.Max(0, RequiredInteger(arguments, "character")));
    }

    private static int RequiredInteger(JsonObject? arguments, string name)
    {
        int? value = OptionalInteger(arguments, name);
        if (value.HasValue)
        {
            return value.Value;
        }

        throw new McpToolException(new
        {
            message = name + " is required."
        });
    }

    private static int? OptionalInteger(JsonObject? arguments, string name)
    {
        JsonNode? node = arguments?[name];
        if (node is null)
        {
            return null;
        }

        return node.GetValue<int>();
    }

    private static string RequiredText(JsonObject? arguments, string name)
    {
        string? value = NormalizeOptionalText(arguments?[name]?.GetValue<string>());
        if (value is not null)
        {
            return value;
        }

        throw new McpToolException(new
        {
            message = name + " is required."
        });
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
