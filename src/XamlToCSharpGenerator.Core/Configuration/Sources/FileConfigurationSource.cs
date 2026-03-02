using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace XamlToCSharpGenerator.Core.Configuration.Sources;

public sealed class FileConfigurationSource : IXamlSourceGenConfigurationSource
{
    public const string DefaultConfigurationFileName = "xaml-sourcegen.config.json";

    private readonly string? _path;
    private readonly string? _content;
    private readonly bool _useProjectDefaultFile;

    public FileConfigurationSource(
        string path,
        string content,
        int precedence = 100,
        string? name = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(path));
        }

        _path = path;
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _useProjectDefaultFile = false;

        Precedence = precedence;
        Name = string.IsNullOrWhiteSpace(name) ? "File:" + path : name!;
    }

    private FileConfigurationSource(int precedence, string? name)
    {
        _useProjectDefaultFile = true;
        Precedence = precedence;
        Name = string.IsNullOrWhiteSpace(name) ? "File:ProjectDefault" : name!;
    }

    public string Name { get; }

    public int Precedence { get; }

    public static FileConfigurationSource CreateProjectDefault(int precedence = 90, string? name = null)
    {
        return new FileConfigurationSource(precedence, name);
    }

    public static bool IsSupportedConfigurationFileName(string fileName)
    {
        return string.Equals(fileName, DefaultConfigurationFileName, StringComparison.OrdinalIgnoreCase);
    }

    public XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context)
    {
        if (_content is not null && _path is not null)
        {
            return ParseDocument(_path, _content);
        }

        if (!_useProjectDefaultFile ||
            string.IsNullOrWhiteSpace(context.ProjectDirectory))
        {
            return XamlSourceGenConfigurationSourceResult.Empty;
        }

        var configPath = Path.Combine(context.ProjectDirectory!, DefaultConfigurationFileName);
        if (!File.Exists(configPath))
        {
            return XamlSourceGenConfigurationSourceResult.Empty;
        }

        try
        {
            var content = File.ReadAllText(configPath);
            return ParseDocument(configPath, content);
        }
        catch (Exception ex)
        {
            return new XamlSourceGenConfigurationSourceResult
            {
                Issues = ImmutableArray.Create(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0913",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Error,
                    Message: "Failed to read configuration file '" + configPath + "': " + ex.Message,
                    SourceName: Name))
            };
        }
    }

    private XamlSourceGenConfigurationSourceResult ParseDocument(string path, string content)
    {
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        if (string.IsNullOrWhiteSpace(content))
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0914",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Configuration file '" + path + "' is empty.",
                SourceName: Name));
            return new XamlSourceGenConfigurationSourceResult
            {
                Issues = issues.ToImmutable()
            };
        }

        try
        {
            using var document = JsonDocument.Parse(content, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0915",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Error,
                    Message: "Configuration file '" + path + "' must contain a JSON object at the root.",
                    SourceName: Name));
                return new XamlSourceGenConfigurationSourceResult
                {
                    Issues = issues.ToImmutable()
                };
            }

            ValidateSchemaVersion(path, document.RootElement, issues);

            var buildPatch = ParseBuildPatch(path, document.RootElement, issues);
            var parserPatch = ParseParserPatch(path, document.RootElement, issues);
            var semanticPatch = ParseSemanticContractPatch(path, document.RootElement, issues);
            var bindingPatch = ParseBindingPatch(path, document.RootElement, issues);
            var emitterPatch = ParseEmitterPatch(path, document.RootElement, issues);
            var transformPatch = ParseTransformPatch(path, document.RootElement, issues);
            var diagnosticsPatch = ParseDiagnosticsPatch(path, document.RootElement, issues);
            var frameworkExtrasPatch = ParseFrameworkExtrasPatch(path, document.RootElement, issues);

            return new XamlSourceGenConfigurationSourceResult
            {
                Patch = new XamlSourceGenConfigurationPatch
                {
                    Build = buildPatch,
                    Parser = parserPatch,
                    SemanticContract = semanticPatch,
                    Binding = bindingPatch,
                    Emitter = emitterPatch,
                    Transform = transformPatch,
                    Diagnostics = diagnosticsPatch,
                    FrameworkExtras = frameworkExtrasPatch
                },
                Issues = issues.ToImmutable()
            };
        }
        catch (JsonException ex)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0916",
                Severity: XamlSourceGenConfigurationIssueSeverity.Error,
                Message: "Invalid JSON in configuration file '" + path + "': " + ex.Message,
                SourceName: Name));
            return new XamlSourceGenConfigurationSourceResult
            {
                Issues = issues.ToImmutable()
            };
        }
    }

    private static void ValidateSchemaVersion(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(root, "schemaVersion", out var schemaVersionElement))
        {
            return;
        }

        var isVersionOne = false;
        if (schemaVersionElement.ValueKind == JsonValueKind.Number)
        {
            isVersionOne = schemaVersionElement.TryGetInt32(out var numericValue) && numericValue == 1;
        }
        else if (schemaVersionElement.ValueKind == JsonValueKind.String)
        {
            var text = schemaVersionElement.GetString();
            isVersionOne = string.Equals(text, "1", StringComparison.Ordinal);
        }

        if (!isVersionOne)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0917",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Configuration file '" + path + "' uses unsupported schemaVersion. Expected '1'.",
                SourceName: DefaultConfigurationFileName));
        }
    }

    private XamlSourceGenBuildOptionsPatch ParseBuildPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "build", issues, out var section))
        {
            return XamlSourceGenBuildOptionsPatch.Empty;
        }

        var additionalProperties = ReadStringMap(path, section, "additionalProperties", issues);

        return new XamlSourceGenBuildOptionsPatch
        {
            IsEnabled = ReadBoolean(path, section, "isEnabled", issues),
            Backend = ReadString(path, section, "backend", issues),
            StrictMode = ReadBoolean(path, section, "strictMode", issues),
            HotReloadEnabled = ReadBoolean(path, section, "hotReloadEnabled", issues),
            HotReloadErrorResilienceEnabled = ReadBoolean(path, section, "hotReloadErrorResilienceEnabled", issues),
            IdeHotReloadEnabled = ReadBoolean(path, section, "ideHotReloadEnabled", issues),
            HotDesignEnabled = ReadBoolean(path, section, "hotDesignEnabled", issues),
            IosHotReloadEnabled = ReadBoolean(path, section, "iosHotReloadEnabled", issues),
            IosHotReloadUseInterpreter = ReadBoolean(path, section, "iosHotReloadUseInterpreter", issues),
            DotNetWatchBuild = ReadBoolean(path, section, "dotNetWatchBuild", issues),
            BuildingInsideVisualStudio = ReadBoolean(path, section, "buildingInsideVisualStudio", issues),
            BuildingByReSharper = ReadBoolean(path, section, "buildingByReSharper", issues),
            AdditionalProperties = additionalProperties
        };
    }

    private XamlSourceGenParserOptionsPatch ParseParserPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "parser", issues, out var section))
        {
            return XamlSourceGenParserOptionsPatch.Empty;
        }

        return new XamlSourceGenParserOptionsPatch
        {
            AllowImplicitXmlnsDeclaration = ReadBoolean(path, section, "allowImplicitXmlnsDeclaration", issues),
            ImplicitStandardXmlnsPrefixesEnabled = ReadBoolean(path, section, "implicitStandardXmlnsPrefixesEnabled", issues),
            ImplicitDefaultXmlns = ReadString(path, section, "implicitDefaultXmlns", issues),
            InferClassFromPath = ReadBoolean(path, section, "inferClassFromPath", issues),
            ImplicitProjectNamespacesEnabled = ReadBoolean(path, section, "implicitProjectNamespacesEnabled", issues),
            GlobalXmlnsPrefixes = ReadStringMap(path, section, "globalXmlnsPrefixes", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenSemanticContractOptionsPatch ParseSemanticContractPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "semanticContract", issues, out var section))
        {
            return XamlSourceGenSemanticContractOptionsPatch.Empty;
        }

        return new XamlSourceGenSemanticContractOptionsPatch
        {
            TypeContracts = ReadStringMap(path, section, "typeContracts", issues),
            PropertyContracts = ReadStringMap(path, section, "propertyContracts", issues),
            EventContracts = ReadStringMap(path, section, "eventContracts", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenBindingOptionsPatch ParseBindingPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "binding", issues, out var section))
        {
            return XamlSourceGenBindingOptionsPatch.Empty;
        }

        return new XamlSourceGenBindingOptionsPatch
        {
            UseCompiledBindingsByDefault = ReadBoolean(path, section, "useCompiledBindingsByDefault", issues),
            CSharpExpressionsEnabled = ReadBoolean(path, section, "cSharpExpressionsEnabled", issues),
            ImplicitCSharpExpressionsEnabled = ReadBoolean(path, section, "implicitCSharpExpressionsEnabled", issues),
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled = ReadBoolean(path, section, "markupParserLegacyInvalidNamedArgumentFallbackEnabled", issues),
            TypeResolutionCompatibilityFallbackEnabled = ReadBoolean(path, section, "typeResolutionCompatibilityFallbackEnabled", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenEmitterOptionsPatch ParseEmitterPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "emitter", issues, out var section))
        {
            return XamlSourceGenEmitterOptionsPatch.Empty;
        }

        return new XamlSourceGenEmitterOptionsPatch
        {
            CreateSourceInfo = ReadBoolean(path, section, "createSourceInfo", issues),
            TracePasses = ReadBoolean(path, section, "tracePasses", issues),
            MetricsEnabled = ReadBoolean(path, section, "metricsEnabled", issues),
            MetricsDetailed = ReadBoolean(path, section, "metricsDetailed", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenTransformOptionsPatch ParseTransformPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "transform", issues, out var section))
        {
            return XamlSourceGenTransformOptionsPatch.Empty;
        }

        return new XamlSourceGenTransformOptionsPatch
        {
            RawTransformDocuments = ReadStringMap(path, section, "rawTransformDocuments", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenDiagnosticsOptionsPatch ParseDiagnosticsPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "diagnostics", issues, out var section))
        {
            return XamlSourceGenDiagnosticsOptionsPatch.Empty;
        }

        return new XamlSourceGenDiagnosticsOptionsPatch
        {
            TreatWarningsAsErrors = ReadBoolean(path, section, "treatWarningsAsErrors", issues),
            SeverityOverrides = ReadSeverityMap(path, section, "severityOverrides", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private XamlSourceGenFrameworkExtrasPatch ParseFrameworkExtrasPatch(
        string path,
        JsonElement root,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetObjectSection(path, root, "frameworkExtras", issues, out var section))
        {
            return XamlSourceGenFrameworkExtrasPatch.Empty;
        }

        return new XamlSourceGenFrameworkExtrasPatch
        {
            Sections = ReadSectionMap(path, section, "sections", issues),
            AdditionalProperties = ReadStringMap(path, section, "additionalProperties", issues)
        };
    }

    private static bool TryGetObjectSection(
        string path,
        JsonElement root,
        string sectionName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues,
        out JsonElement section)
    {
        if (!TryGetProperty(root, sectionName, out section))
        {
            return false;
        }

        if (section.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        if (section.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0918",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Section '" + sectionName + "' in '" + path + "' must be a JSON object.",
                SourceName: DefaultConfigurationFileName));
            return false;
        }

        return true;
    }

    private static ConfigValue<bool> ReadBoolean(
        string path,
        JsonElement section,
        string propertyName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(section, propertyName, out var property))
        {
            return default;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            return property.GetBoolean();
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            var text = property.GetString();
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        issues.Add(new XamlSourceGenConfigurationIssue(
            Code: "AXSG0919",
            Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
            Message: "Property '" + propertyName + "' in '" + path + "' must be a boolean value.",
            SourceName: DefaultConfigurationFileName));
        return default;
    }

    private static ConfigValue<string> ReadString(
        string path,
        JsonElement section,
        string propertyName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(section, propertyName, out var property))
        {
            return default;
        }

        if (TryConvertToString(property, out var value))
        {
            return value ?? string.Empty;
        }

        issues.Add(new XamlSourceGenConfigurationIssue(
            Code: "AXSG0920",
            Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
            Message: "Property '" + propertyName + "' in '" + path + "' must be a string-compatible value.",
            SourceName: DefaultConfigurationFileName));
        return default;
    }

    private static ImmutableDictionary<string, string?> ReadStringMap(
        string path,
        JsonElement section,
        string propertyName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(section, propertyName, out var property))
        {
            return XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0921",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Property '" + propertyName + "' in '" + path + "' must be a JSON object.",
                SourceName: DefaultConfigurationFileName));
            return XamlSourceGenConfigurationCollections.EmptyNullableStringMap;
        }

        var mapBuilder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (var entry in property.EnumerateObject())
        {
            if (TryConvertToString(entry.Value, out var value))
            {
                mapBuilder[entry.Name] = value;
                continue;
            }

            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0922",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Entry '" + propertyName + "." + entry.Name + "' in '" + path + "' must be string-compatible.",
                SourceName: DefaultConfigurationFileName));
        }

        return mapBuilder.Count == 0
            ? XamlSourceGenConfigurationCollections.EmptyNullableStringMap
            : mapBuilder.ToImmutable();
    }

    private static ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?> ReadSeverityMap(
        string path,
        JsonElement section,
        string propertyName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(section, propertyName, out var property))
        {
            return XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0923",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Property '" + propertyName + "' in '" + path + "' must be a JSON object.",
                SourceName: DefaultConfigurationFileName));
            return XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap;
        }

        var mapBuilder = ImmutableDictionary.CreateBuilder<string, XamlSourceGenConfigurationIssueSeverity?>(StringComparer.Ordinal);
        foreach (var entry in property.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Null)
            {
                mapBuilder[entry.Name] = null;
                continue;
            }

            if (TryParseSeverity(entry.Value, out var severity))
            {
                mapBuilder[entry.Name] = severity;
                continue;
            }

            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0924",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Diagnostics severity value for '" + entry.Name + "' in '" + path +
                         "' must be 'Info', 'Warning', or 'Error'.",
                SourceName: DefaultConfigurationFileName));
        }

        return mapBuilder.Count == 0
            ? XamlSourceGenConfigurationCollections.EmptyNullableSeverityMap
            : mapBuilder.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableDictionary<string, string?>> ReadSectionMap(
        string path,
        JsonElement section,
        string propertyName,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (!TryGetProperty(section, propertyName, out var property))
        {
            return XamlSourceGenConfigurationCollections.EmptySectionPatchMap;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return XamlSourceGenConfigurationCollections.EmptySectionPatchMap;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                Code: "AXSG0925",
                Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                Message: "Property '" + propertyName + "' in '" + path + "' must be a JSON object.",
                SourceName: DefaultConfigurationFileName));
            return XamlSourceGenConfigurationCollections.EmptySectionPatchMap;
        }

        var sectionBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var sectionEntry in property.EnumerateObject())
        {
            if (sectionEntry.Value.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0926",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                    Message: "Framework extras section '" + sectionEntry.Name + "' in '" + path + "' must be a JSON object.",
                    SourceName: DefaultConfigurationFileName));
                continue;
            }

            var valueBuilder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            foreach (var item in sectionEntry.Value.EnumerateObject())
            {
                if (TryConvertToString(item.Value, out var text))
                {
                    valueBuilder[item.Name] = text;
                    continue;
                }

                issues.Add(new XamlSourceGenConfigurationIssue(
                    Code: "AXSG0927",
                    Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
                    Message: "Framework extras entry '" + sectionEntry.Name + "." + item.Name +
                             "' in '" + path + "' must be string-compatible.",
                    SourceName: DefaultConfigurationFileName));
            }

            sectionBuilder[sectionEntry.Name] = valueBuilder.ToImmutable();
        }

        return sectionBuilder.Count == 0
            ? XamlSourceGenConfigurationCollections.EmptySectionPatchMap
            : sectionBuilder.ToImmutable();
    }

    private static bool TryParseSeverity(JsonElement value, out XamlSourceGenConfigurationIssueSeverity severity)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (Enum.TryParse(text, ignoreCase: true, out severity))
            {
                return true;
            }
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var numericSeverity) &&
            Enum.IsDefined(typeof(XamlSourceGenConfigurationIssueSeverity), numericSeverity))
        {
            severity = (XamlSourceGenConfigurationIssueSeverity)numericSeverity;
            return true;
        }

        severity = default;
        return false;
    }

    private static bool TryConvertToString(JsonElement value, out string? text)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Null:
                text = null;
                return true;
            case JsonValueKind.String:
                text = value.GetString();
                return true;
            case JsonValueKind.Number:
                text = value.GetRawText();
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                text = value.GetBoolean().ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                text = null;
                return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
