using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Configuration;

public sealed class XamlJsonTransformProvider : IXamlFrameworkTransformProvider
{
    private readonly string _defaultFrameworkId;
    private readonly string _defaultXmlNamespace;
    private readonly ImmutableArray<string> _legacyFieldPropertyNames;
    private readonly ImmutableArray<string> _legacyOwnerPropertyNames;

    public XamlJsonTransformProvider(
        string defaultFrameworkId,
        string defaultXmlNamespace,
        IEnumerable<string>? legacyOwnerPropertyNames = null,
        IEnumerable<string>? legacyFieldPropertyNames = null)
    {
        _defaultFrameworkId = defaultFrameworkId;
        _defaultXmlNamespace = defaultXmlNamespace;
        _legacyOwnerPropertyNames = legacyOwnerPropertyNames is null
            ? ImmutableArray<string>.Empty
            : legacyOwnerPropertyNames.ToImmutableArray();
        _legacyFieldPropertyNames = legacyFieldPropertyNames is null
            ? ImmutableArray<string>.Empty
            : legacyFieldPropertyNames.ToImmutableArray();
    }

    public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
    {
        try
        {
            using var document = JsonDocument.Parse(input.Text);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure(input.FilePath, "AXSG0900", "Transform rule file must contain a JSON object.");
            }

            var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
            var typeAliases = ParseTypeAliases(document.RootElement, input.FilePath, diagnostics);
            var propertyAliases = ParsePropertyAliases(document.RootElement, input.FilePath, diagnostics);

            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                new XamlTransformConfiguration(typeAliases, propertyAliases),
                diagnostics.ToImmutable());
        }
        catch (JsonException ex)
        {
            return Failure(input.FilePath, "AXSG0900", "Failed to parse transform rule file: " + ex.Message);
        }
    }

    public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
        ImmutableArray<XamlFrameworkTransformRuleResult> files)
    {
        if (files.IsDefaultOrEmpty)
        {
            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var typeAliases = new Dictionary<string, XamlTypeAliasRule>(StringComparer.OrdinalIgnoreCase);
        var propertyAliases = new Dictionary<string, XamlPropertyAliasRule>(StringComparer.OrdinalIgnoreCase);

        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            diagnostics.AddRange(files[fileIndex].Diagnostics);

            foreach (var alias in files[fileIndex].Configuration.TypeAliases)
            {
                var key = alias.XmlNamespace + ":" + alias.XamlTypeName;
                if (typeAliases.TryGetValue(key, out var existing))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0903",
                        "Type alias '" + key + "' from '" + alias.Source +
                        "' overrides the previous declaration from '" + existing.Source + "'.",
                        alias.Source,
                        alias.Line,
                        alias.Column,
                        false));
                }

                typeAliases[key] = alias;
            }

            foreach (var alias in files[fileIndex].Configuration.PropertyAliases)
            {
                var key = alias.TargetTypeName + ":" + alias.XamlPropertyName;
                if (propertyAliases.TryGetValue(key, out var existing))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0903",
                        "Property alias '" + key + "' from '" + alias.Source +
                        "' overrides the previous declaration from '" + existing.Source + "'.",
                        alias.Source,
                        alias.Line,
                        alias.Column,
                        false));
                }

                propertyAliases[key] = alias;
            }
        }

        return new XamlFrameworkTransformRuleAggregateResult(
            new XamlTransformConfiguration(
                typeAliases
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => pair.Value)
                    .ToImmutableArray(),
                propertyAliases
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => pair.Value)
                    .ToImmutableArray()),
            diagnostics.ToImmutable());
    }

    private ImmutableArray<XamlTypeAliasRule> ParseTypeAliases(
        JsonElement root,
        string filePath,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!root.TryGetProperty("typeAliases", out var aliasesElement) ||
            aliasesElement.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<XamlTypeAliasRule>.Empty;
        }

        var aliases = ImmutableArray.CreateBuilder<XamlTypeAliasRule>();
        foreach (var aliasElement in aliasesElement.EnumerateArray())
        {
            if (aliasElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(InvalidEntry(filePath, "typeAliases entry must be an object."));
                continue;
            }

            var xmlNamespace = ReadTrimmedString(aliasElement, "xmlNamespace");
            var xamlType = ReadTrimmedString(aliasElement, "xamlType");
            var clrType = ReadTrimmedString(aliasElement, "clrType");

            if (string.IsNullOrWhiteSpace(xmlNamespace))
            {
                xmlNamespace = _defaultXmlNamespace;
            }

            if (string.IsNullOrWhiteSpace(xmlNamespace) ||
                string.IsNullOrWhiteSpace(xamlType) ||
                string.IsNullOrWhiteSpace(clrType))
            {
                diagnostics.Add(InvalidEntry(filePath, "typeAliases entry requires xmlNamespace, xamlType, and clrType."));
                continue;
            }

            aliases.Add(new XamlTypeAliasRule(
                xmlNamespace!,
                xamlType!,
                clrType!,
                filePath,
                1,
                1));
        }

        return aliases.ToImmutable();
    }

    private ImmutableArray<XamlPropertyAliasRule> ParsePropertyAliases(
        JsonElement root,
        string filePath,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (!root.TryGetProperty("propertyAliases", out var aliasesElement) ||
            aliasesElement.ValueKind != JsonValueKind.Array)
        {
            return ImmutableArray<XamlPropertyAliasRule>.Empty;
        }

        var aliases = ImmutableArray.CreateBuilder<XamlPropertyAliasRule>();
        foreach (var aliasElement in aliasesElement.EnumerateArray())
        {
            if (aliasElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(InvalidEntry(filePath, "propertyAliases entry must be an object."));
                continue;
            }

            var targetType = ReadTrimmedString(aliasElement, "targetType");
            var xamlProperty = ReadTrimmedString(aliasElement, "xamlProperty");
            var clrProperty = ReadTrimmedString(aliasElement, "clrProperty");
            var frameworkId = ReadTrimmedString(aliasElement, "frameworkId") ?? _defaultFrameworkId;
            var propertyOwnerTypeName =
                ReadTrimmedString(aliasElement, "propertyOwnerType") ??
                ReadTrimmedString(aliasElement, "propertyOwnerTypeName") ??
                ReadFirstConfiguredProperty(aliasElement, _legacyOwnerPropertyNames);
            var propertyFieldName =
                ReadTrimmedString(aliasElement, "propertyField") ??
                ReadTrimmedString(aliasElement, "propertyFieldName") ??
                ReadFirstConfiguredProperty(aliasElement, _legacyFieldPropertyNames);

            if (string.IsNullOrWhiteSpace(targetType) ||
                string.IsNullOrWhiteSpace(xamlProperty) ||
                (string.IsNullOrWhiteSpace(clrProperty) &&
                 string.IsNullOrWhiteSpace(propertyOwnerTypeName) &&
                 string.IsNullOrWhiteSpace(propertyFieldName)))
            {
                diagnostics.Add(InvalidEntry(
                    filePath,
                    "propertyAliases entry requires targetType, xamlProperty, and clrProperty or framework property metadata."));
                continue;
            }

            var frameworkPayload =
                string.IsNullOrWhiteSpace(propertyOwnerTypeName) && string.IsNullOrWhiteSpace(propertyFieldName)
                    ? null
                    : new XamlFrameworkPropertyAliasPayload(
                        frameworkId,
                        propertyOwnerTypeName,
                        propertyFieldName);

            aliases.Add(new XamlPropertyAliasRule(
                targetType!,
                xamlProperty!,
                clrProperty,
                filePath,
                1,
                1,
                frameworkPayload));
        }

        return aliases.ToImmutable();
    }

    private static XamlFrameworkTransformRuleResult Failure(string filePath, string id, string message)
    {
        return new XamlFrameworkTransformRuleResult(
            filePath,
            XamlTransformConfiguration.Empty,
            ImmutableArray.Create(new DiagnosticInfo(id, message, filePath, 1, 1, true)));
    }

    private static DiagnosticInfo InvalidEntry(string filePath, string message)
    {
        return new DiagnosticInfo("AXSG0901", message, filePath, 1, 1, true);
    }

    private static string? ReadTrimmedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value!.Trim();
    }

    private static string? ReadFirstConfiguredProperty(JsonElement element, ImmutableArray<string> propertyNames)
    {
        for (var index = 0; index < propertyNames.Length; index++)
        {
            var value = ReadTrimmedString(element, propertyNames[index]);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
