using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Generator;

internal static class TransformRulesParser
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";

    public static TransformRuleFileResult Parse(TransformRuleFileInput input)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var typeAliases = ImmutableArray.CreateBuilder<XamlTypeAliasRule>();
        var propertyAliases = ImmutableArray.CreateBuilder<XamlPropertyAliasRule>();

        try
        {
            using var json = JsonDocument.Parse(input.Text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0900",
                    $"Transform rule file '{input.FilePath}' must contain a JSON object root.",
                    input.FilePath,
                    1,
                    1,
                    false));
                return new TransformRuleFileResult(
                    input.FilePath,
                    XamlTransformConfiguration.Empty,
                    diagnostics.ToImmutable());
            }

            var root = json.RootElement;
            ParseTypeAliases(root, input.FilePath, diagnostics, typeAliases);
            ParsePropertyAliases(root, input.FilePath, diagnostics, propertyAliases);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber.HasValue
                ? (int)Math.Max(1L, ex.LineNumber.Value + 1L)
                : 1;
            var column = ex.BytePositionInLine.HasValue
                ? (int)Math.Max(1L, ex.BytePositionInLine.Value + 1L)
                : 1;
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0900",
                $"Transform rule JSON parse failed: {ex.Message}",
                input.FilePath,
                line,
                column,
                false));
        }
        catch (Exception ex)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0900",
                $"Transform rule file '{input.FilePath}' could not be parsed: {ex.Message}",
                input.FilePath,
                1,
                1,
                false));
        }

        return new TransformRuleFileResult(
            input.FilePath,
            new XamlTransformConfiguration(typeAliases.ToImmutable(), propertyAliases.ToImmutable()),
            diagnostics.ToImmutable());
    }

    private static void ParseTypeAliases(
        JsonElement root,
        string filePath,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<XamlTypeAliasRule>.Builder aliases)
    {
        if (!root.TryGetProperty("typeAliases", out var typeAliasesElement) ||
            typeAliasesElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (typeAliasesElement.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0900",
                "Property 'typeAliases' must be an array.",
                filePath,
                1,
                1,
                false));
            return;
        }

        foreach (var aliasElement in typeAliasesElement.EnumerateArray())
        {
            if (aliasElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Each type alias entry must be an object.",
                    filePath,
                    1,
                    1,
                    false));
                continue;
            }

            var xmlNamespace = ReadString(aliasElement, "xmlNamespace") ?? AvaloniaDefaultXmlNamespace;
            var xamlTypeName = ReadString(aliasElement, "xamlType") ?? ReadString(aliasElement, "xamlTypeName");
            var clrTypeName = ReadString(aliasElement, "clrType") ?? ReadString(aliasElement, "clrTypeName");
            if (string.IsNullOrWhiteSpace(xamlTypeName) ||
                string.IsNullOrWhiteSpace(clrTypeName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Type alias entries require non-empty 'xamlType' and 'clrType' values.",
                    filePath,
                    1,
                    1,
                    false));
                continue;
            }

            aliases.Add(new XamlTypeAliasRule(
                xmlNamespace.Trim(),
                xamlTypeName.Trim(),
                clrTypeName.Trim(),
                filePath,
                1,
                1));
        }
    }

    private static void ParsePropertyAliases(
        JsonElement root,
        string filePath,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<XamlPropertyAliasRule>.Builder aliases)
    {
        if (!root.TryGetProperty("propertyAliases", out var propertyAliasesElement) ||
            propertyAliasesElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (propertyAliasesElement.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0900",
                "Property 'propertyAliases' must be an array.",
                filePath,
                1,
                1,
                false));
            return;
        }

        foreach (var aliasElement in propertyAliasesElement.EnumerateArray())
        {
            if (aliasElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Each property alias entry must be an object.",
                    filePath,
                    1,
                    1,
                    false));
                continue;
            }

            var targetTypeName = ReadString(aliasElement, "targetType") ?? "*";
            var xamlPropertyName = ReadString(aliasElement, "xamlProperty") ?? ReadString(aliasElement, "xamlPropertyName");
            var clrPropertyName = ReadString(aliasElement, "clrProperty") ?? ReadString(aliasElement, "clrPropertyName");
            var avaloniaPropertyOwnerTypeName = ReadString(aliasElement, "avaloniaPropertyOwnerType") ?? ReadString(aliasElement, "avaloniaPropertyOwnerTypeName");
            var avaloniaPropertyFieldName = ReadString(aliasElement, "avaloniaPropertyField") ?? ReadString(aliasElement, "avaloniaPropertyFieldName");

            if (string.IsNullOrWhiteSpace(xamlPropertyName) ||
                (string.IsNullOrWhiteSpace(clrPropertyName) &&
                 (string.IsNullOrWhiteSpace(avaloniaPropertyOwnerTypeName) ||
                  string.IsNullOrWhiteSpace(avaloniaPropertyFieldName))))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Property alias entries require 'xamlProperty' and either 'clrProperty' or both 'avaloniaPropertyOwnerType' and 'avaloniaPropertyField'.",
                    filePath,
                    1,
                    1,
                    false));
                continue;
            }

            aliases.Add(new XamlPropertyAliasRule(
                targetTypeName.Trim(),
                xamlPropertyName.Trim(),
                string.IsNullOrWhiteSpace(clrPropertyName) ? null : clrPropertyName.Trim(),
                string.IsNullOrWhiteSpace(avaloniaPropertyOwnerTypeName) ? null : avaloniaPropertyOwnerTypeName.Trim(),
                string.IsNullOrWhiteSpace(avaloniaPropertyFieldName) ? null : avaloniaPropertyFieldName.Trim(),
                filePath,
                1,
                1));
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}

internal sealed record TransformRuleFileInput(string FilePath, string Text);

internal sealed record TransformRuleFileResult(
    string FilePath,
    XamlTransformConfiguration Configuration,
    ImmutableArray<DiagnosticInfo> Diagnostics);

internal sealed class TransformRuleAggregateResult
{
    public TransformRuleAggregateResult(
        XamlTransformConfiguration configuration,
        ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Configuration = configuration;
        Diagnostics = diagnostics;
    }

    public XamlTransformConfiguration Configuration { get; }

    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
}
