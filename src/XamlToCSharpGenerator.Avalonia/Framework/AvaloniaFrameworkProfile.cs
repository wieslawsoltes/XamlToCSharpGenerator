using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Avalonia.Parsing;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Avalonia.Binding;
using XamlToCSharpGenerator.Avalonia.Emission;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Framework;

public sealed class AvaloniaFrameworkProfile : IXamlFrameworkProfile
{
    private const string AvaloniaXmlnsPrefixAttributeMetadataName = "Avalonia.Metadata.XmlnsPrefixAttribute";
    private const string SourceGenGlobalXmlnsPrefixAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute";
    private const string SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenAllowImplicitXmlnsDeclarationAttribute";
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private static readonly ConcurrentDictionary<string, ImmutableDictionary<string, string>> GlobalXmlnsPrefixPropertyCache =
        new(StringComparer.Ordinal);

    private static readonly IXamlFrameworkBuildContract BuildContractInstance = new AvaloniaFrameworkBuildContract();
    private static readonly IXamlFrameworkTransformProvider TransformProviderInstance = new AvaloniaFrameworkTransformProvider();
    private static readonly IXamlFrameworkSemanticBinder SemanticBinderInstance =
        new AvaloniaFrameworkSemanticBinder(new AvaloniaSemanticBinder());
    private static readonly IXamlFrameworkEmitter EmitterInstance =
        new AvaloniaFrameworkEmitter(new AvaloniaCodeEmitter());
    private static readonly ImmutableArray<IXamlDocumentEnricher> DocumentEnricherInstances =
        [AvaloniaDocumentFeatureEnricher.Instance];

    public static AvaloniaFrameworkProfile Instance { get; } = new();

    private AvaloniaFrameworkProfile()
    {
    }

    public string Id => "Avalonia";

    public IXamlFrameworkBuildContract BuildContract => BuildContractInstance;

    public IXamlFrameworkTransformProvider TransformProvider => TransformProviderInstance;

    public IXamlFrameworkSemanticBinder CreateSemanticBinder()
    {
        return SemanticBinderInstance;
    }

    public IXamlFrameworkEmitter CreateEmitter()
    {
        return EmitterInstance;
    }

    public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers()
    {
        return DocumentEnricherInstances;
    }

    public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
    {
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (IsXmlnsPrefixAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length < 2 ||
                        attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                        attribute.ConstructorArguments[1].Value is not string prefix ||
                        string.IsNullOrWhiteSpace(prefix) ||
                        string.IsNullOrWhiteSpace(xmlNamespace))
                    {
                        continue;
                    }

                    globalPrefixes[prefix.Trim()] = xmlNamespace.Trim();
                    continue;
                }

                if (IsSourceGenAllowImplicitXmlnsDeclarationAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length == 0)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = true };
                    }
                    else if (attribute.ConstructorArguments[0].Value is bool allowImplicit)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = allowImplicit };
                    }
                }
            }
        }

        foreach (var entry in ParseGlobalXmlnsPrefixesProperty(options.GlobalXmlnsPrefixes))
        {
            globalPrefixes[entry.Key] = entry.Value;
        }

        if (options.AllowImplicitXmlnsDeclaration &&
            options.ImplicitStandardXmlnsPrefixesEnabled)
        {
            AddImplicitPrefix(globalPrefixes, "x", Xaml2006Namespace);
            AddImplicitPrefix(globalPrefixes, "d", BlendDesignNamespace);
            AddImplicitPrefix(globalPrefixes, "mc", MarkupCompatibilityNamespace);
        }

        if (options.AllowImplicitXmlnsDeclaration &&
            !string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns) &&
            !globalPrefixes.ContainsKey(string.Empty))
        {
            globalPrefixes[string.Empty] = options.ImplicitDefaultXmlns;
        }

        return new XamlFrameworkParserSettings(
            globalPrefixes.ToImmutable(),
            options.AllowImplicitXmlnsDeclaration,
            options.ImplicitDefaultXmlns);
    }

    private static void AddImplicitPrefix(
        ImmutableDictionary<string, string>.Builder globalPrefixes,
        string prefix,
        string xmlNamespace)
    {
        if (!globalPrefixes.ContainsKey(prefix))
        {
            globalPrefixes[prefix] = xmlNamespace;
        }
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && visited.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }

        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }
    }

    private static bool IsXmlnsPrefixAttribute(AttributeData attribute)
    {
        var metadataName = attribute.AttributeClass?.ToDisplayString();
        return string.Equals(metadataName, AvaloniaXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal) ||
               string.Equals(metadataName, SourceGenGlobalXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal);
    }

    private static bool IsSourceGenAllowImplicitXmlnsDeclarationAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> ParseGlobalXmlnsPrefixesProperty(string? rawValue)
    {
        if (rawValue is null)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var trimmedRawValue = rawValue.Trim();
        if (trimmedRawValue.Length == 0)
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        return GlobalXmlnsPrefixPropertyCache.GetOrAdd(trimmedRawValue, static value => ParseGlobalXmlnsPrefixesCore(value));
    }

    private static ImmutableDictionary<string, string> ParseGlobalXmlnsPrefixesCore(string rawValue)
    {
        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var span = rawValue.AsSpan();
        var index = 0;

        while (index < span.Length)
        {
            while (index < span.Length && IsGlobalPrefixDelimiter(span[index]))
            {
                index++;
            }

            if (index >= span.Length)
            {
                break;
            }

            var entryStart = index;
            while (index < span.Length && !IsGlobalPrefixDelimiter(span[index]))
            {
                index++;
            }

            var entry = span.Slice(entryStart, index - entryStart).Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            var prefix = entry.Slice(0, separatorIndex).Trim();
            var xmlNamespace = entry.Slice(separatorIndex + 1).Trim();
            if (prefix.Length == 0 || xmlNamespace.Length == 0)
            {
                continue;
            }

            map[prefix.ToString()] = xmlNamespace.ToString();
        }

        return map.ToImmutable();
    }

    private static bool IsGlobalPrefixDelimiter(char character)
    {
        return character == ';' || character == ',' || character == '\r' || character == '\n';
    }

    private sealed class AvaloniaFrameworkSemanticBinder : IXamlFrameworkSemanticBinder
    {
        private readonly IXamlSemanticBinder _innerBinder;

        public AvaloniaFrameworkSemanticBinder(IXamlSemanticBinder innerBinder)
        {
            _innerBinder = innerBinder;
        }

        public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration)
        {
            return _innerBinder.Bind(document, compilation, options, transformConfiguration);
        }
    }

    private sealed class AvaloniaFrameworkEmitter : IXamlFrameworkEmitter
    {
        private readonly IXamlCodeEmitter _innerEmitter;

        public AvaloniaFrameworkEmitter(IXamlCodeEmitter innerEmitter)
        {
            _innerEmitter = innerEmitter;
        }

        public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
        {
            return _innerEmitter.Emit(viewModel);
        }
    }

    private sealed class AvaloniaFrameworkBuildContract : IXamlFrameworkBuildContract
    {
        public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

        public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

        public string XamlSourceItemGroup => "AvaloniaXaml";

        public string TransformRuleSourceItemGroup => "AvaloniaSourceGenTransformRule";

        public bool IsXamlPath(string path)
        {
            return path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".paml", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsXamlSourceItemGroup(string? sourceItemGroup)
        {
            if (sourceItemGroup is null)
            {
                return true;
            }

            var normalizedSourceItemGroup = sourceItemGroup.Trim();
            if (normalizedSourceItemGroup.Length == 0)
            {
                return true;
            }

            return normalizedSourceItemGroup.Equals(XamlSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup)
        {
            return string.Equals(sourceItemGroup, TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeSourceItemGroup(string? sourceItemGroup)
        {
            if (sourceItemGroup is null)
            {
                return XamlSourceItemGroup;
            }

            var normalizedSourceItemGroup = sourceItemGroup.Trim();
            return normalizedSourceItemGroup.Length == 0
                ? XamlSourceItemGroup
                : normalizedSourceItemGroup;
        }
    }

    private sealed class AvaloniaFrameworkTransformProvider : IXamlFrameworkTransformProvider
    {
        private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";

        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
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
                    return new XamlFrameworkTransformRuleResult(
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

            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                new XamlTransformConfiguration(typeAliases.ToImmutable(), propertyAliases.ToImmutable()),
                diagnostics.ToImmutable());
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

            foreach (var file in files.OrderBy(static x => x.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.AddRange(file.Diagnostics);

                foreach (var typeAlias in file.Configuration.TypeAliases)
                {
                    var key = BuildTypeAliasKey(typeAlias.XmlNamespace, typeAlias.XamlTypeName);
                    if (typeAliases.TryGetValue(key, out var existing))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0903",
                            $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' is declared multiple times. The later declaration from '{typeAlias.Source}' overrides '{existing.Source}'.",
                            typeAlias.Source,
                            typeAlias.Line,
                            typeAlias.Column,
                            false));
                    }

                    typeAliases[key] = typeAlias;
                }

                foreach (var propertyAlias in file.Configuration.PropertyAliases)
                {
                    var key = BuildPropertyAliasKey(propertyAlias.TargetTypeName, propertyAlias.XamlPropertyName);
                    if (propertyAliases.TryGetValue(key, out var existing))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0903",
                            $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' is declared multiple times. The later declaration from '{propertyAlias.Source}' overrides '{existing.Source}'.",
                            propertyAlias.Source,
                            propertyAlias.Line,
                            propertyAlias.Column,
                            false));
                    }

                    propertyAliases[key] = propertyAlias;
                }
            }

            return new XamlFrameworkTransformRuleAggregateResult(
                new XamlTransformConfiguration(
                    typeAliases.Values.ToImmutableArray(),
                    propertyAliases.Values.ToImmutableArray()),
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

                var xmlNamespace = ReadString(aliasElement, "xmlNamespace");
                var xamlTypeName = ReadString(aliasElement, "xamlType") ?? ReadString(aliasElement, "xamlTypeName");
                var clrTypeName = ReadString(aliasElement, "clrType") ?? ReadString(aliasElement, "clrTypeName");
                if (xamlTypeName is null || xamlTypeName.Trim().Length == 0 ||
                    clrTypeName is null || clrTypeName.Trim().Length == 0)
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

                var resolvedXmlNamespace = string.IsNullOrWhiteSpace(xmlNamespace)
                    ? AvaloniaDefaultXmlNamespace
                    : (xmlNamespace is null ? AvaloniaDefaultXmlNamespace : xmlNamespace.Trim());
                var resolvedXamlTypeName = xamlTypeName.Trim();
                var resolvedClrTypeName = clrTypeName.Trim();
                aliases.Add(new XamlTypeAliasRule(
                    resolvedXmlNamespace,
                    resolvedXamlTypeName,
                    resolvedClrTypeName,
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

                if (xamlPropertyName is null || xamlPropertyName.Trim().Length == 0 ||
                    ((clrPropertyName is null || clrPropertyName.Trim().Length == 0) &&
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

                var resolvedTargetTypeName = string.IsNullOrWhiteSpace(targetTypeName)
                    ? "*"
                    : targetTypeName.Trim();
                var resolvedXamlPropertyName = xamlPropertyName.Trim();
                var resolvedClrPropertyName = string.IsNullOrWhiteSpace(clrPropertyName)
                    ? null
                    : (clrPropertyName is null ? null : clrPropertyName.Trim());
                aliases.Add(new XamlPropertyAliasRule(
                    resolvedTargetTypeName,
                    resolvedXamlPropertyName,
                    resolvedClrPropertyName,
                    filePath,
                    1,
                    1,
                    CreateAvaloniaAliasPayload(
                        avaloniaPropertyOwnerTypeName,
                        avaloniaPropertyFieldName)));
            }
        }

        private static XamlFrameworkPropertyAliasPayload? CreateAvaloniaAliasPayload(
            string? ownerTypeName,
            string? propertyFieldName)
        {
            if (string.IsNullOrWhiteSpace(ownerTypeName) &&
                string.IsNullOrWhiteSpace(propertyFieldName))
            {
                return null;
            }

            var normalizedOwnerTypeName = string.IsNullOrWhiteSpace(ownerTypeName)
                ? null
                : (ownerTypeName is null ? null : ownerTypeName.Trim());
            var normalizedPropertyFieldName = string.IsNullOrWhiteSpace(propertyFieldName)
                ? null
                : (propertyFieldName is null ? null : propertyFieldName.Trim());
            return new XamlFrameworkPropertyAliasPayload(
                FrameworkProfileIds.Avalonia,
                normalizedOwnerTypeName,
                normalizedPropertyFieldName);
        }

        private static string BuildTypeAliasKey(string xmlNamespace, string xamlType)
        {
            return xmlNamespace.Trim() + "|" + xamlType.Trim();
        }

        private static string BuildPropertyAliasKey(string targetType, string xamlProperty)
        {
            return targetType.Trim() + "|" + xamlProperty.Trim();
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
}
