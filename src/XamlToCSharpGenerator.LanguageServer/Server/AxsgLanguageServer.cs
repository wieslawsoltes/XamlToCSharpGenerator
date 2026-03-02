using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using XamlToCSharpGenerator.LanguageService;
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
    private readonly Dictionary<string, DocumentState> _openDocuments = new(StringComparer.Ordinal);

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

        return _shutdownRequested ? 0 : 1;
    }

    public void Dispose()
    {
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
                if (hasId)
                {
                    await SendResponseAsync(id, BuildInitializeResult(), cancellationToken).ConfigureAwait(false);
                }
                break;

            case "initialized":
                break;

            case "shutdown":
                _shutdownRequested = true;
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
                    await HandleCompletionAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "textDocument/hover":
                if (hasId)
                {
                    await HandleHoverAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "textDocument/definition":
                if (hasId)
                {
                    await HandleDefinitionAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "textDocument/references":
                if (hasId)
                {
                    await HandleReferencesAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "textDocument/documentSymbol":
                if (hasId)
                {
                    await HandleDocumentSymbolAsync(id, parameters, cancellationToken).ConfigureAwait(false);
                }
                break;

            case "textDocument/semanticTokens/full":
                if (hasId)
                {
                    await HandleSemanticTokensAsync(id, parameters, cancellationToken).ConfigureAwait(false);
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

    private async Task HandleDidOpenAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
        var version = textDocument.TryGetProperty("version", out var versionElement)
            ? versionElement.GetInt32()
            : 0;

        _openDocuments[uri] = new DocumentState(text, version);

        var diagnostics = await _engine
            .OpenDocumentAsync(uri, text, version, _options, cancellationToken)
            .ConfigureAwait(false);
        await PublishDiagnosticsAsync(uri, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDidChangeAsync(JsonElement parameters, CancellationToken cancellationToken)
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
            var staleDiagnostics = await _engine
                .GetDiagnosticsAsync(uri, _options, cancellationToken)
                .ConfigureAwait(false);
            await PublishDiagnosticsAsync(uri, staleDiagnostics, cancellationToken).ConfigureAwait(false);
            return;
        }

        var text = state.Text;
        if (parameters.TryGetProperty("contentChanges", out var changesElement) &&
            changesElement.ValueKind == JsonValueKind.Array)
        {
            text = ApplyContentChanges(text, changesElement);
        }

        _openDocuments[uri] = new DocumentState(text, version);
        var diagnostics = await _engine
            .UpdateDocumentAsync(uri, text, version, _options, cancellationToken)
            .ConfigureAwait(false);
        await PublishDiagnosticsAsync(uri, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleDidSaveAsync(JsonElement parameters, CancellationToken cancellationToken)
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

        var diagnostics = await _engine.GetDiagnosticsAsync(uri, _options, cancellationToken).ConfigureAwait(false);
        await PublishDiagnosticsAsync(uri, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private Task HandleDidCloseAsync(JsonElement parameters, CancellationToken cancellationToken)
    {
        var textDocument = parameters.GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;

        _openDocuments.Remove(uri);
        _engine.CloseDocument(uri);
        return PublishDiagnosticsAsync(uri, ImmutableArray<LanguageServiceDiagnostic>.Empty, cancellationToken);
    }

    private async Task HandleCompletionAsync(JsonElement id, JsonElement parameters, CancellationToken cancellationToken)
    {
        var request = ParseTextDocumentPosition(parameters);
        var completions = await _engine.GetCompletionsAsync(
            request.Uri,
            request.Position,
            _options,
            cancellationToken).ConfigureAwait(false);

        var items = new JsonArray();
        foreach (var completion in completions)
        {
            items.Add(new JsonObject
            {
                ["label"] = completion.Label,
                ["kind"] = ToCompletionKind(completion.Kind),
                ["insertText"] = completion.InsertText,
                ["detail"] = completion.Detail,
                ["documentation"] = completion.Documentation,
                ["deprecated"] = completion.IsDeprecated
            });
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
            _options,
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
            _options,
            cancellationToken).ConfigureAwait(false);

        var payload = new JsonArray();
        foreach (var definition in definitions)
        {
            payload.Add(new JsonObject
            {
                ["uri"] = definition.Uri,
                ["range"] = SerializeRange(definition.Range)
            });
        }

        await SendResponseAsync(id, payload, cancellationToken).ConfigureAwait(false);
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
            _options,
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
                ["range"] = SerializeRange(reference.Range)
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
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["result"] = value is null ? null : JsonSerializer.SerializeToNode(value)
        };

        return _writer.WriteAsync(response, cancellationToken);
    }

    private Task SendErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };

        return _writer.WriteAsync(response, cancellationToken);
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
                ["referencesProvider"] = true,
                ["documentSymbolProvider"] = true,
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
}
