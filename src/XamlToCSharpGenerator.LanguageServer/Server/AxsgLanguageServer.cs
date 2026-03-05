using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.InlayHints;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.SemanticTokens;
using XamlToCSharpGenerator.LanguageServer.Protocol;

namespace XamlToCSharpGenerator.LanguageServer.Server;

internal sealed class AxsgLanguageServer : IDisposable
{
    private readonly LspMessageReader _reader;
    private readonly LspMessageWriter _writer;
    private readonly XamlLanguageServiceEngine _engine;
    private readonly XamlLanguageServiceOptions _options;
    private readonly XamlLanguageServiceOptions _navigationOptions;
    private XamlInlayHintOptions _inlayHintOptions = XamlInlayHintOptions.Default;
    private readonly ConcurrentDictionary<string, DocumentState> _openDocuments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _diagnosticUpdateTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requestCancellationTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, Task> _inflightRequests = new();
    private long _nextInflightRequestId;

    private bool _shutdownRequested;
    private bool _exitRequested;

    public AxsgLanguageServer(
        LspMessageReader reader,
        LspMessageWriter writer,
        XamlLanguageServiceEngine engine,
        XamlLanguageServiceOptions options)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _navigationOptions = _options with
        {
            IncludeCompilationDiagnostics = false,
            IncludeSemanticDiagnostics = false
        };
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (!_exitRequested && !cancellationToken.IsCancellationRequested)
        {
            using var message = await _reader.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                break;
            }

            await HandleMessageAsync(message.RootElement, cancellationToken).ConfigureAwait(false);
        }

        await DrainInflightRequestsAsync().ConfigureAwait(false);
        return _shutdownRequested ? 0 : 1;
    }

    public void Dispose()
    {
        foreach (var pair in _diagnosticUpdateTokens)
        {
            try
            {
                pair.Value.Cancel();
                pair.Value.Dispose();
            }
            catch
            {
                // Ignore shutdown cancellation failures.
            }
        }

        _diagnosticUpdateTokens.Clear();
        CancelAndDisposeRequestTokens();
        _engine.Dispose();
    }

    private async Task HandleMessageAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!root.TryGetProperty("method", out var methodElement))
        {
            return;
        }

        var method = methodElement.GetString();
        if (string.IsNullOrWhiteSpace(method))
        {
            return;
        }

        var hasId = root.TryGetProperty("id", out var id);
        var parameters = root.TryGetProperty("params", out var paramsElement)
            ? paramsElement
            : default;

        switch (method)
        {
            case "initialize":
                _inlayHintOptions = ParseInlayHintOptions(parameters);
                if (hasId)
                {
                    await SendResponseAsync(id, BuildInitializeResult(), cancellationToken).ConfigureAwait(false);
                }
                break;

            case "initialized":
                break;

            case "$/cancelRequest":
                HandleCancelRequest(parameters);
                break;

            case "shutdown":
                _shutdownRequested = true;
                CancelAndDisposeRequestTokens();
                if (hasId)
                {
                    await SendResponseAsync(id, value: null, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "exit":
                _exitRequested = true;
                break;

            case "textDocument/didOpen":
                await HandleDidOpenAsync(parameters, cancellationToken).ConfigureAwait(false);
                break;

            case "textDocument/didChange":
                await HandleDidChangeAsync(parameters, cancellationToken).ConfigureAwait(false);
                break;

            case "textDocument/didSave":
                await HandleDidSaveAsync(parameters, cancellationToken).ConfigureAwait(false);
                break;

            case "textDocument/didClose":
                await HandleDidCloseAsync(parameters, cancellationToken).ConfigureAwait(false);
                break;

            case "textDocument/completion":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleCompletionAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/hover":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleHoverAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/definition":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleDefinitionAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/declaration":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleDeclarationAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/references":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleReferencesAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/documentSymbol":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleDocumentSymbolAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/semanticTokens/full":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleSemanticTokensAsync(requestId, requestParameters, token));
                }
                break;

            case "textDocument/inlayHint":
                if (hasId)
                {
                    var requestId = id.Clone();
                    var requestParameters = parameters.Clone();
                    QueueRequest(requestId, cancellationToken, token => HandleInlayHintAsync(requestId, requestParameters, token));
                }
                break;

            default:
                if (hasId)
                {
                    await SendErrorAsync(
                        id,
                        code: -32601,
                        message: "Method not found: " + method,
                        cancellationToken).ConfigureAwait(false);
                }
                break;
        }
    }

    private Task HandleDidOpenAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
        var version = textDocument.TryGetProperty("version", out var versionElement)
            ? versionElement.GetInt32()
            : 0;

        _openDocuments[uri] = new DocumentState(text, version);
        _engine.UpsertDocument(uri, text, version);
        QueueDiagnosticsUpdate(uri, version);
        return Task.CompletedTask;
    }

    private Task HandleDidChangeAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var version = textDocument.TryGetProperty("version", out var versionElement)
            ? versionElement.GetInt32()
            : 0;

        if (!_openDocuments.TryGetValue(uri, out var state))
        {
            state = new DocumentState(string.Empty, 0);
        }

        if (version < state.Version)
        {
            return Task.CompletedTask;
        }

        var text = state.Text;
        if (parameters.TryGetProperty("contentChanges", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Array)
        {
            text = ApplyContentChanges(text, changesElement);
        }

        _openDocuments[uri] = new DocumentState(text, version);
        _engine.UpsertDocument(uri, text, version);
        QueueDiagnosticsUpdate(uri, version);
        return Task.CompletedTask;
    }

    private Task HandleDidSaveAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        if (parameters.TryGetProperty("text", out var textElement) &&
            textElement.ValueKind == JsonValueKind.String)
        {
            var savedText = textElement.GetString() ?? string.Empty;
            if (_openDocuments.TryGetValue(uri, out var state))
            {
                _openDocuments[uri] = new DocumentState(savedText, state.Version);
            }
            else
            {
                _openDocuments[uri] = new DocumentState(savedText, 0);
            }
        }

        if (_openDocuments.TryGetValue(uri, out var documentState))
        {
            QueueDiagnosticsUpdate(uri, documentState.Version);
        }

        return Task.CompletedTask;
    }

    private Task HandleDidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;

        _openDocuments.TryRemove(uri, out _);
        if (_diagnosticUpdateTokens.TryRemove(uri, out var updateTokenSource))
        {
            updateTokenSource.Cancel();
            updateTokenSource.Dispose();
        }

        _engine.CloseDocument(uri);
        return PublishDiagnosticsAsync(uri, ImmutableArray<LanguageServiceDiagnostic>.Empty, cancellationToken);
    }

    private void QueueDiagnosticsUpdate(string uri, int expectedVersion)
    {
        var tokenSource = new CancellationTokenSource();
        _diagnosticUpdateTokens.AddOrUpdate(
            uri,
            tokenSource,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return tokenSource;
            });

        _ = Task.Run(
            () => RunDiagnosticsUpdateAsync(uri, expectedVersion, tokenSource),
            CancellationToken.None);
    }

    private async Task RunDiagnosticsUpdateAsync(string uri, int expectedVersion, CancellationTokenSource tokenSource)
    {
        try
        {
            var diagnostics = await _engine
                .GetDiagnosticsAsync(uri, _options, tokenSource.Token)
                .ConfigureAwait(false);

            if (tokenSource.IsCancellationRequested)
            {
                return;
            }

            if (!_openDocuments.TryGetValue(uri, out var state) || state.Version != expectedVersion)
            {
                return;
            }

            await PublishDiagnosticsAsync(uri, diagnostics, tokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit.
        }
        catch
        {
            // Do not terminate language server loop on background diagnostics failures.
        }
        finally
        {
            if (_diagnosticUpdateTokens.TryGetValue(uri, out var activeToken) &&
                ReferenceEquals(activeToken, tokenSource))
            {
                _diagnosticUpdateTokens.TryRemove(uri, out _);
            }

            tokenSource.Dispose();
        }
    }

    private void HandleCancelRequest(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("id", out var id))
        {
            return;
        }

        var requestKey = CreateRequestKey(id);
        if (string.IsNullOrEmpty(requestKey))
        {
            return;
        }

        if (_requestCancellationTokens.TryRemove(requestKey, out var tokenSource))
        {
            tokenSource.Cancel();
            tokenSource.Dispose();
        }
    }

    private void QueueRequest(
        JsonElement id,
        CancellationToken serverCancellationToken,
        Func<CancellationToken, Task> requestHandler)
    {
        var requestKey = CreateRequestKey(id);
        if (string.IsNullOrEmpty(requestKey))
        {
            return;
        }

        var requestTokenSource = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        _requestCancellationTokens.AddOrUpdate(
            requestKey,
            requestTokenSource,
            (_, existing) =>
            {
                existing.Cancel();
                existing.Dispose();
                return requestTokenSource;
            });

        var inflightId = Interlocked.Increment(ref _nextInflightRequestId);
        var requestTask = Task.Run(
            async () =>
            {
                try
                {
                    await requestHandler(requestTokenSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is expected for stale requests.
                }
                catch (Exception ex)
                {
                    if (!requestTokenSource.IsCancellationRequested &&
                        !serverCancellationToken.IsCancellationRequested)
                    {
                        await SendErrorAsync(
                                id,
                                code: -32603,
                                message: "Request failed: " + ex.Message,
                                CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                }
                finally
                {
                    if (_requestCancellationTokens.TryGetValue(requestKey, out var active) &&
                        ReferenceEquals(active, requestTokenSource))
                    {
                        _requestCancellationTokens.TryRemove(requestKey, out _);
                    }

                    requestTokenSource.Dispose();
                    _inflightRequests.TryRemove(inflightId, out _);
                }
            },
            CancellationToken.None);

        _inflightRequests[inflightId] = requestTask;
    }

    private static string CreateRequestKey(JsonElement id)
    {
        if (id.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        return id.GetRawText();
    }

    private void CancelAndDisposeRequestTokens()
    {
        foreach (var pair in _requestCancellationTokens)
        {
            try
            {
                pair.Value.Cancel();
                pair.Value.Dispose();
            }
            catch
            {
                // Ignore shutdown cancellation failures.
            }
        }

        _requestCancellationTokens.Clear();
    }

    private async Task DrainInflightRequestsAsync()
    {
        var pendingRequests = _inflightRequests.Values.ToArray();

        if (pendingRequests.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(pendingRequests).ConfigureAwait(false);
        }
        catch
        {
            // Individual request failures are handled per-request.
        }
    }

    private async Task HandleCompletionAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentPosition(parameters);
        var completions = await _engine.GetCompletionsAsync(
            request.Uri,
            request.Position,
            _navigationOptions,
            cancellationToken).ConfigureAwait(false);

        var items = new JsonArray();
        foreach (var completion in completions)
        {
            var completionItem = new JsonObject
            {
                ["label"] = completion.Label,
                ["kind"] = ToCompletionKind(completion.Kind),
                ["insertText"] = completion.InsertText,
                ["detail"] = completion.Detail,
                ["documentation"] = completion.Documentation,
                ["deprecated"] = completion.IsDeprecated
            };

            if (ContainsSnippetPlaceholder(completion.InsertText))
            {
                completionItem["insertTextFormat"] = 2;
            }

            items.Add(completionItem);
        }

        var result = new JsonObject
        {
            ["isIncomplete"] = false,
            ["items"] = items
        };

        await SendResponseAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleHoverAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentPosition(parameters);
        var hover = await _engine.GetHoverAsync(
            request.Uri,
            request.Position,
            _navigationOptions,
            cancellationToken).ConfigureAwait(false);

        if (hover is null)
        {
            await SendResponseAsync(id, value: null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["kind"] = "markdown",
                ["value"] = hover.Markdown
            }
        };

        if (hover.Range is { } range)
        {
            result["range"] = SerializeRange(range);
        }

        await SendResponseAsync(id, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDefinitionAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentPosition(parameters);
        var definitions = await _engine.GetDefinitionsAsync(
            request.Uri,
            request.Position,
            _navigationOptions,
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (var definition in definitions)
        {
            payload.Add(new JsonObject
            {
                ["uri"] = definition.Uri,
                ["range"] = SerializeRange(NormalizeTransportRange(definition.Range))
            });
        }

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
    }

    private Task HandleDeclarationAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        // Declaration semantics are aligned with definition for XAML symbols.
        return HandleDefinitionAsync(id, parameters, cancellationToken);
    }

    private async Task HandleReferencesAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentPosition(parameters);
        var includeDeclaration = true;
        if (parameters.TryGetProperty("context", out var contextElement) &&
            contextElement.TryGetProperty("includeDeclaration", out var includeDeclarationElement) &&
            includeDeclarationElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            includeDeclaration = includeDeclarationElement.GetBoolean();
        }

        var references = await _engine.GetReferencesAsync(
            request.Uri,
            request.Position,
            _navigationOptions,
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (var reference in references)
        {
            if (!includeDeclaration && reference.IsDeclaration)
            {
                continue;
            }

            payload.Add(new JsonObject
            {
                ["uri"] = reference.Uri,
                ["range"] = SerializeRange(NormalizeTransportRange(reference.Range))
            });
        }

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDocumentSymbolAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var symbols = await _engine.GetDocumentSymbolsAsync(uri, _options, cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (var symbol in symbols)
        {
            payload.Add(SerializeSymbol(symbol));
        }

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleSemanticTokensAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var tokens = await _engine.GetSemanticTokensAsync(uri, _options, cancellationToken).ConfigureAwait(false);

        var encoded = EncodeSemanticTokens(tokens);
        var payload = new JsonObject
        {
            ["data"] = new JsonArray(encoded.Select(static value => JsonValue.Create(value)).ToArray())
        };

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleInlayHintAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentRange(parameters);
        var hints = await _engine.GetInlayHintsAsync(
            request.Uri,
            request.Range,
            _options,
            _inlayHintOptions,
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (var hint in hints)
        {
            var hintObject = new JsonObject
            {
                ["position"] = new JsonObject
                {
                    ["line"] = hint.Position.Line,
                    ["character"] = hint.Position.Character
                },
                ["kind"] = (int)hint.Kind,
                ["paddingLeft"] = hint.PaddingLeft,
                ["paddingRight"] = hint.PaddingRight
            };

            if (hint.LabelParts.IsDefaultOrEmpty)
            {
                hintObject["label"] = hint.Label;
            }
            else
            {
                var labelParts = new JsonArray();
                foreach (var part in hint.LabelParts)
                {
                    var partObject = new JsonObject
                    {
                        ["value"] = part.Value
                    };

                    if (!string.IsNullOrWhiteSpace(part.Tooltip))
                    {
                        partObject["tooltip"] = new JsonObject
                        {
                            ["kind"] = "markdown",
                            ["value"] = part.Tooltip
                        };
                    }

                    if (part.DefinitionLocation is { } definitionLocation)
                    {
                        partObject["location"] = new JsonObject
                        {
                            ["uri"] = definitionLocation.Uri,
                            ["range"] = SerializeRange(NormalizeTransportRange(definitionLocation.Range))
                        };
                    }

                    labelParts.Add(partObject);
                }

                hintObject["label"] = labelParts;
            }

            if (!string.IsNullOrWhiteSpace(hint.Tooltip))
            {
                hintObject["tooltip"] = new JsonObject
                {
                    ["kind"] = "markdown",
                    ["value"] = hint.Tooltip
                };
            }

            payload.Add(hintObject);
        }

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
    }

    private Task PublishDiagnosticsAsync(
        string uri,
        ImmutableArray<LanguageServiceDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var diagnosticsArray = new JsonArray();
        foreach (var diagnostic in diagnostics)
        {
            diagnosticsArray.Add(new JsonObject
            {
                ["range"] = SerializeRange(diagnostic.Range),
                ["severity"] = (int)diagnostic.Severity,
                ["code"] = diagnostic.Code,
                ["source"] = diagnostic.Source ?? "AXSGLS",
                ["message"] = diagnostic.Message
            });
        }

        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject
            {
                ["uri"] = uri,
                ["diagnostics"] = diagnosticsArray
            }
        };

        return _writer.WriteAsync(notification, cancellationToken);
    }

    private Task SendResponseAsync(JsonElement id, object? value, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneJsonElement(id),
            ["result"] = SerializeResultValue(value)
        };

        return _writer.WriteAsync(response, cancellationToken);
    }

    private Task SendErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = CloneJsonElement(id),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        return _writer.WriteAsync(response, cancellationToken);
    }

    private static JsonNode? CloneJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => JsonValue.Create(element.GetString()),
            JsonValueKind.Number => CloneNumberElement(element),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            JsonValueKind.Object => CloneObjectElement(element),
            JsonValueKind.Array => CloneArrayElement(element),
            _ => JsonValue.Create(element.GetRawText())
        };
    }

    private static JsonNode CloneNumberElement(JsonElement element)
    {
        if (element.TryGetInt64(out var int64Value))
        {
            return JsonValue.Create(int64Value)!;
        }

        if (element.TryGetUInt64(out var uint64Value))
        {
            return JsonValue.Create(uint64Value)!;
        }

        if (element.TryGetDecimal(out var decimalValue))
        {
            return JsonValue.Create(decimalValue)!;
        }

        if (element.TryGetDouble(out var doubleValue))
        {
            return JsonValue.Create(doubleValue)!;
        }

        var rawNumber = element.GetRawText();
        if (decimal.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDecimal))
        {
            return JsonValue.Create(parsedDecimal)!;
        }

        if (double.TryParse(rawNumber, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
        {
            return JsonValue.Create(parsedDouble)!;
        }

        return JsonValue.Create(rawNumber)!;
    }

    private static JsonObject CloneObjectElement(JsonElement element)
    {
        var objectNode = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            objectNode[property.Name] = CloneJsonElement(property.Value);
        }

        return objectNode;
    }

    private static JsonArray CloneArrayElement(JsonElement element)
    {
        var arrayNode = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            arrayNode.Add(CloneJsonElement(item));
        }

        return arrayNode;
    }

    private static JsonNode? SerializeResultValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonNode node)
        {
            return node.DeepClone();
        }

        if (value is JsonElement element)
        {
            return CloneJsonElement(element);
        }

        return JsonSerializer.SerializeToNode(value);
    }

    private static TextDocumentPositionRequest ParseTextDocumentPosition(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var position = parameters.GetProperty("position");

        return new TextDocumentPositionRequest(
            uri,
            new SourcePosition(
                position.GetProperty("line").GetInt32(),
                position.GetProperty("character").GetInt32()));
    }

    private static TextDocumentRangeRequest ParseTextDocumentRange(JsonElement parameters)
    {
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var range = parameters.GetProperty("range");
        var start = range.GetProperty("start");
        var end = range.GetProperty("end");

        return new TextDocumentRangeRequest(
            uri,
            new SourceRange(
                new SourcePosition(
                    start.GetProperty("line").GetInt32(),
                    start.GetProperty("character").GetInt32()),
                new SourcePosition(
                    end.GetProperty("line").GetInt32(),
                    end.GetProperty("character").GetInt32())));
    }

    private static XamlInlayHintOptions ParseInlayHintOptions(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("initializationOptions", out var initializationOptions) ||
            initializationOptions.ValueKind != JsonValueKind.Object ||
            !initializationOptions.TryGetProperty("inlayHints", out var inlayHintsElement) ||
            inlayHintsElement.ValueKind != JsonValueKind.Object)
        {
            return XamlInlayHintOptions.Default;
        }

        var enabled = true;
        if (inlayHintsElement.TryGetProperty("bindingTypeHintsEnabled", out var enabledElement) &&
            enabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            enabled = enabledElement.GetBoolean();
        }

        var displayStyle = XamlInlayHintTypeDisplayStyle.Short;
        if (inlayHintsElement.TryGetProperty("typeDisplayStyle", out var displayStyleElement) &&
            displayStyleElement.ValueKind == JsonValueKind.String)
        {
            var rawValue = displayStyleElement.GetString();
            if (string.Equals(rawValue, "qualified", StringComparison.OrdinalIgnoreCase))
            {
                displayStyle = XamlInlayHintTypeDisplayStyle.Qualified;
            }
        }

        return new XamlInlayHintOptions(enabled, displayStyle);
    }

    private static JsonObject BuildInitializeResult()
    {
        var tokenTypes = new JsonArray(XamlSemanticTokenService.TokenTypes.Select(static value => JsonValue.Create(value)).ToArray());

        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["textDocumentSync"] = new JsonObject
                {
                    ["openClose"] = true,
                    ["change"] = 2,
                    ["save"] = new JsonObject
                    {
                        ["includeText"] = true
                    }
                },
                ["completionProvider"] = new JsonObject
                {
                    ["resolveProvider"] = false,
                    ["triggerCharacters"] = new JsonArray("<", ":", ".", "{", " ")
                },
                ["hoverProvider"] = true,
                ["definitionProvider"] = true,
                ["declarationProvider"] = true,
                ["referencesProvider"] = true,
                ["documentSymbolProvider"] = true,
                ["inlayHintProvider"] = true,
                ["semanticTokensProvider"] = new JsonObject
                {
                    ["legend"] = new JsonObject
                    {
                        ["tokenTypes"] = tokenTypes,
                        ["tokenModifiers"] = new JsonArray()
                    },
                    ["full"] = true
                }
            },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "AXSG Language Server",
                ["version"] = "1.0.0"
            }
        };
    }

    private static int ToCompletionKind(XamlCompletionItemKind kind)
    {
        return kind switch
        {
            XamlCompletionItemKind.Element => 7,
            XamlCompletionItemKind.Property => 10,
            XamlCompletionItemKind.AttachedProperty => 10,
            XamlCompletionItemKind.MarkupExtension => 3,
            XamlCompletionItemKind.EnumValue => 12,
            XamlCompletionItemKind.Resource => 21,
            XamlCompletionItemKind.Keyword => 14,
            XamlCompletionItemKind.Snippet => 15,
            _ => 1
        };
    }

    private static bool ContainsSnippetPlaceholder(string? insertText)
    {
        if (string.IsNullOrWhiteSpace(insertText))
        {
            return false;
        }

        for (var i = 0; i < insertText.Length - 1; i++)
        {
            if (insertText[i] != '$')
            {
                continue;
            }

            var next = insertText[i + 1];
            if (char.IsDigit(next) || next == '{')
            {
                return true;
            }
        }

        return false;
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

    private static SourceRange NormalizeTransportRange(SourceRange range)
    {
        var startLine = Math.Max(0, range.Start.Line);
        var startCharacter = Math.Max(0, range.Start.Character);
        var endLine = Math.Max(0, range.End.Line);
        var endCharacter = Math.Max(0, range.End.Character);

        if (endLine < startLine || (endLine == startLine && endCharacter <= startCharacter))
        {
            endLine = startLine;
            endCharacter = startCharacter + 1;
        }

        return new SourceRange(
            new SourcePosition(startLine, startCharacter),
            new SourcePosition(endLine, endCharacter));
    }

    private static JsonObject SerializeSymbol(XamlDocumentSymbol symbol)
    {
        var children = new JsonArray();
        foreach (var child in symbol.Children)
        {
            children.Add(SerializeSymbol(child));
        }

        return new JsonObject
        {
            ["name"] = symbol.Name,
            ["kind"] = (int)symbol.Kind,
            ["range"] = SerializeRange(symbol.Range),
            ["selectionRange"] = SerializeRange(symbol.SelectionRange),
            ["children"] = children
        };
    }

    private static int[] EncodeSemanticTokens(ImmutableArray<XamlSemanticToken> tokens)
    {
        if (tokens.IsDefaultOrEmpty)
        {
            return [];
        }

        var sorted = tokens
            .OrderBy(static token => token.Line)
            .ThenBy(static token => token.Character)
            .ToArray();

        var data = new List<int>(sorted.Length * 5);
        var previousLine = 0;
        var previousCharacter = 0;

        for (var index = 0; index < sorted.Length; index++)
        {
            var token = sorted[index];
            var deltaLine = index == 0 ? token.Line : token.Line - previousLine;
            var deltaCharacter = index == 0 || deltaLine > 0
                ? token.Character
                : token.Character - previousCharacter;

            previousLine = token.Line;
            previousCharacter = token.Character;

            var tokenTypeIndex = XamlSemanticTokenService.TokenTypes.IndexOf(token.TokenType);
            if (tokenTypeIndex < 0)
            {
                tokenTypeIndex = 0;
            }

            data.Add(deltaLine);
            data.Add(deltaCharacter);
            data.Add(Math.Max(1, token.Length));
            data.Add(tokenTypeIndex);
            data.Add(0);
        }

        return data.ToArray();
    }

    private static string ApplyContentChanges(string currentText, JsonElement changesElement)
    {
        var updated = currentText ?? string.Empty;
        foreach (var change in changesElement.EnumerateArray())
        {
            var changeText = change.TryGetProperty("text", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            if (!change.TryGetProperty("range", out var rangeElement) ||
                rangeElement.ValueKind != JsonValueKind.Object)
            {
                updated = changeText;
                continue;
            }

            if (!rangeElement.TryGetProperty("start", out var startElement) ||
                !rangeElement.TryGetProperty("end", out var endElement))
            {
                updated = changeText;
                continue;
            }

            var startLine = startElement.TryGetProperty("line", out var startLineElement)
                ? startLineElement.GetInt32()
                : 0;
            var startCharacter = startElement.TryGetProperty("character", out var startCharacterElement)
                ? startCharacterElement.GetInt32()
                : 0;
            var endLine = endElement.TryGetProperty("line", out var endLineElement)
                ? endLineElement.GetInt32()
                : startLine;
            var endCharacter = endElement.TryGetProperty("character", out var endCharacterElement)
                ? endCharacterElement.GetInt32()
                : startCharacter;

            var startOffset = GetOffsetFromLspPosition(updated, startLine, startCharacter);
            var endOffset = GetOffsetFromLspPosition(updated, endLine, endCharacter);
            if (startOffset > endOffset)
            {
                (startOffset, endOffset) = (endOffset, startOffset);
            }

            startOffset = Math.Max(0, Math.Min(startOffset, updated.Length));
            endOffset = Math.Max(startOffset, Math.Min(endOffset, updated.Length));
            updated = updated.Substring(0, startOffset) + changeText + updated.Substring(endOffset);
        }

        return updated;
    }

    private static int GetOffsetFromLspPosition(string text, int line, int character)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var targetLine = Math.Max(0, line);
        var targetCharacter = Math.Max(0, character);
        var currentLine = 0;
        var index = 0;

        while (index < text.Length && currentLine < targetLine)
        {
            if (text[index] == '\r')
            {
                if (index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                currentLine++;
            }
            else if (text[index] == '\n')
            {
                currentLine++;
            }

            index++;
        }

        if (currentLine < targetLine)
        {
            return text.Length;
        }

        var lineStart = index;
        var lineLength = 0;
        while (index < text.Length && text[index] != '\r' && text[index] != '\n')
        {
            lineLength++;
            index++;
        }

        var clampedCharacter = Math.Min(targetCharacter, lineLength);
        return lineStart + clampedCharacter;
    }

    private readonly record struct DocumentState(string Text, int Version);
    private sealed record TextDocumentPositionRequest(string Uri, SourcePosition Position);
    private sealed record TextDocumentRangeRequest(string Uri, SourceRange Range);
}
