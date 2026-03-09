using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.InlayHints;

public sealed class XamlInlayHintService
{
    public ImmutableArray<XamlInlayHint> GetInlayHints(
        XamlAnalysisResult analysis,
        XamlInlayHintOptions options)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        options ??= XamlInlayHintOptions.Default;

        if (!options.EnableBindingTypeHints ||
            analysis.XmlDocument is null ||
            string.IsNullOrWhiteSpace(analysis.Document.Text))
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlInlayHint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var xmlLocationIndex = XmlLocationIndex.Create(analysis.XmlDocument);

        if (analysis.ViewModel is not null)
        {
            foreach (var compiledBinding in analysis.ViewModel.CompiledBindings)
            {
                if (string.IsNullOrWhiteSpace(compiledBinding.ResultTypeName))
                {
                    continue;
                }

                if (!TryResolveBindingValueRange(
                        analysis.Document.Text,
                        xmlLocationIndex,
                        compiledBinding,
                        out var valueRange))
                {
                    continue;
                }

                AddInlayHint(
                    builder,
                    seen,
                    valueRange,
                    options,
                    compiledBinding.ResultTypeName!,
                    BuildTooltip(
                        heading: IsExpressionBindingPath(compiledBinding.Path)
                            ? "**Expression Binding**"
                            : "**Compiled Binding**",
                        targetTypeName: compiledBinding.TargetTypeName,
                        targetPropertyName: compiledBinding.TargetPropertyName,
                        path: compiledBinding.Path,
                        sourceTypeName: compiledBinding.SourceTypeName,
                        resultTypeName: compiledBinding.ResultTypeName!),
                    TryResolveTypeLocation(analysis, compiledBinding.ResultTypeName!));
            }
        }

        foreach (var element in analysis.XmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
        {
            foreach (var attribute in element.Attributes())
            {
                if (XamlBindingNavigationService.TryResolveInlayHintTarget(
                        analysis,
                        analysis.Document.Text,
                        element,
                        attribute,
                        out var bindingHint))
                {
                    AddInlayHint(
                        builder,
                        seen,
                        bindingHint.HintAnchorRange,
                        options,
                        bindingHint.ResultTypeName,
                        BuildTooltip(
                            heading: "**Binding Type**",
                            targetTypeName: null,
                            targetPropertyName: null,
                            path: bindingHint.Path,
                            sourceTypeName: bindingHint.SourceTypeName,
                            resultTypeName: bindingHint.ResultTypeName),
                        bindingHint.ResultTypeLocation);
                }

                if (XamlExpressionBindingNavigationService.TryResolveInlayHintTarget(
                        analysis,
                        analysis.Document.Text,
                        element,
                        attribute,
                        out var expressionHint))
                {
                    AddInlayHint(
                        builder,
                        seen,
                        expressionHint.HintAnchorRange,
                        options,
                        expressionHint.ResultTypeName,
                        BuildTooltip(
                            heading: "**Expression Binding**",
                            targetTypeName: null,
                            targetPropertyName: null,
                            path: expressionHint.Expression,
                            sourceTypeName: expressionHint.SourceTypeName,
                            resultTypeName: expressionHint.ResultTypeName),
                        expressionHint.ResultTypeLocation);
                }

                if (XamlInlineCSharpNavigationService.TryResolveInlayHintTarget(
                        analysis,
                        analysis.Document.Text,
                        element,
                        attribute,
                        out var inlineAttributeHint))
                {
                    AddInlayHint(
                        builder,
                        seen,
                        inlineAttributeHint.HintAnchorRange,
                        options,
                        inlineAttributeHint.ResultTypeName,
                        BuildTooltip(
                            heading: "**Inline C#**",
                            targetTypeName: null,
                            targetPropertyName: null,
                            path: inlineAttributeHint.Code,
                            sourceTypeName: inlineAttributeHint.ContextTypeName,
                            resultTypeName: inlineAttributeHint.ResultTypeName),
                        inlineAttributeHint.ResultTypeLocation);
                }
            }
        }

        foreach (var element in analysis.XmlDocument.Root?.DescendantsAndSelf() ?? Enumerable.Empty<XElement>())
        {
            if (!XamlInlineCSharpNavigationService.TryResolveInlayHintTarget(
                    analysis,
                    analysis.Document.Text,
                    element,
                    out var inlineElementHint))
            {
                continue;
            }

            AddInlayHint(
                builder,
                seen,
                inlineElementHint.HintAnchorRange,
                options,
                inlineElementHint.ResultTypeName,
                BuildTooltip(
                    heading: "**Inline C#**",
                    targetTypeName: null,
                    targetPropertyName: null,
                    path: inlineElementHint.Code,
                    sourceTypeName: inlineElementHint.ContextTypeName,
                    resultTypeName: inlineElementHint.ResultTypeName),
                inlineElementHint.ResultTypeLocation);
        }

        return builder
            .ToImmutable()
            .OrderBy(static hint => hint.Position.Line)
            .ThenBy(static hint => hint.Position.Character)
            .ToImmutableArray();
    }

    private static bool TryResolveBindingValueRange(
        string text,
        XmlLocationIndex xmlLocationIndex,
        ResolvedCompiledBindingDefinition compiledBinding,
        out SourceRange range)
    {
        range = default;

        if (compiledBinding.IsSetterBinding)
        {
            if (!xmlLocationIndex.TryGetElement(compiledBinding.Line, compiledBinding.Column, out var setterElement) ||
                !string.Equals(setterElement.Name.LocalName, "Setter", StringComparison.Ordinal))
            {
                return false;
            }

            var valueAttribute = setterElement.Attributes()
                .FirstOrDefault(static attribute => string.Equals(attribute.Name.LocalName, "Value", StringComparison.Ordinal));
            return valueAttribute is not null &&
                   XamlXmlSourceRangeService.TryCreateAttributeValueRange(text, valueAttribute, out range);
        }

        return xmlLocationIndex.TryGetAttribute(compiledBinding.Line, compiledBinding.Column, out var attribute) &&
               XamlXmlSourceRangeService.TryCreateAttributeValueRange(text, attribute, out range);
    }

    private static void AddInlayHint(
        ImmutableArray<XamlInlayHint>.Builder builder,
        HashSet<string> seen,
        SourceRange valueRange,
        XamlInlayHintOptions options,
        string resultTypeName,
        string tooltip,
        AvaloniaSymbolSourceLocation? typeLocation)
    {
        var inlineTypeName = FormatInlineTypeName(resultTypeName, options.TypeDisplayStyle);
        if (inlineTypeName.Length == 0)
        {
            return;
        }

        var label = ": " + inlineTypeName;
        var identity = valueRange.End.Line + ":" + valueRange.End.Character + ":" + label;
        if (!seen.Add(identity))
        {
            return;
        }

        builder.Add(new XamlInlayHint(
            Position: valueRange.End,
            Label: label,
            Kind: XamlInlayHintKind.Type,
            Tooltip: tooltip,
            PaddingLeft: true,
            PaddingRight: false,
            LabelParts:
            [
                new XamlInlayHintLabelPart(": "),
                new XamlInlayHintLabelPart(inlineTypeName, tooltip, typeLocation)
            ]));
    }

    private static string FormatInlineTypeName(
        string typeName,
        XamlInlayHintTypeDisplayStyle displayStyle)
    {
        return displayStyle == XamlInlayHintTypeDisplayStyle.Qualified
            ? NormalizeQualifiedTypeName(typeName)
            : ShortenTypeName(typeName);
    }

    private static string BuildTooltip(
        string heading,
        string? targetTypeName,
        string? targetPropertyName,
        string path,
        string sourceTypeName,
        string resultTypeName)
    {
        var lines = new List<string>
        {
            heading,
            string.Empty
        };

        if (!string.IsNullOrWhiteSpace(targetTypeName) &&
            !string.IsNullOrWhiteSpace(targetPropertyName))
        {
            lines.Add("- Target: `" + NormalizeQualifiedTypeName(targetTypeName) + "." + targetPropertyName + "`");
        }

        lines.Add("- Path: `" + path + "`");
        lines.Add("- Source type: `" + NormalizeQualifiedTypeName(sourceTypeName) + "`");
        lines.Add("- Result type: `" + NormalizeQualifiedTypeName(resultTypeName) + "`");
        return string.Join("\n", lines);
    }

    private static bool IsExpressionBindingPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               path.TrimStart().StartsWith("{=", StringComparison.Ordinal);
    }

    private static AvaloniaSymbolSourceLocation? TryResolveTypeLocation(
        XamlAnalysisResult analysis,
        string resultTypeName)
    {
        var normalizedTypeName = NormalizeQualifiedTypeName(resultTypeName);
        if (analysis.TypeIndex?.TryGetTypeByFullTypeName(normalizedTypeName, out var typeInfo) == true &&
            typeInfo is not null)
        {
            return XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, typeInfo);
        }

        if (TryResolveClrTypeSymbol(analysis.Compilation, normalizedTypeName) is { } typeSymbol)
        {
            return XamlClrNavigationLocationResolver.ResolveTypeLocation(analysis, typeSymbol);
        }

        return string.IsNullOrWhiteSpace(normalizedTypeName)
            ? null
            : new AvaloniaSymbolSourceLocation(
                XamlMetadataSymbolUri.CreateTypeUri(normalizedTypeName),
                XamlClrNavigationLocationResolver.MetadataNavigationRange);
    }

    private static ITypeSymbol? TryResolveClrTypeSymbol(Compilation? compilation, string typeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        return typeName switch
        {
            "bool" or "System.Boolean" => compilation.GetSpecialType(SpecialType.System_Boolean),
            "byte" or "System.Byte" => compilation.GetSpecialType(SpecialType.System_Byte),
            "char" or "System.Char" => compilation.GetSpecialType(SpecialType.System_Char),
            "decimal" or "System.Decimal" => compilation.GetSpecialType(SpecialType.System_Decimal),
            "double" or "System.Double" => compilation.GetSpecialType(SpecialType.System_Double),
            "short" or "System.Int16" => compilation.GetSpecialType(SpecialType.System_Int16),
            "int" or "System.Int32" => compilation.GetSpecialType(SpecialType.System_Int32),
            "long" or "System.Int64" => compilation.GetSpecialType(SpecialType.System_Int64),
            "object" or "System.Object" => compilation.GetSpecialType(SpecialType.System_Object),
            "sbyte" or "System.SByte" => compilation.GetSpecialType(SpecialType.System_SByte),
            "float" or "System.Single" => compilation.GetSpecialType(SpecialType.System_Single),
            "string" or "System.String" => compilation.GetSpecialType(SpecialType.System_String),
            "ushort" or "System.UInt16" => compilation.GetSpecialType(SpecialType.System_UInt16),
            "uint" or "System.UInt32" => compilation.GetSpecialType(SpecialType.System_UInt32),
            "ulong" or "System.UInt64" => compilation.GetSpecialType(SpecialType.System_UInt64),
            "void" or "System.Void" => compilation.GetSpecialType(SpecialType.System_Void),
            _ => compilation.GetTypeByMetadataName(typeName)
        };
    }

    private static string NormalizeQualifiedTypeName(string typeName)
    {
        return typeName.Replace("global::", string.Empty, StringComparison.Ordinal);
    }

    private static string ShortenTypeName(string typeName)
    {
        var normalized = NormalizeQualifiedTypeName(typeName);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        var token = new StringBuilder();

        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
            {
                token.Append(ch);
                continue;
            }

            FlushToken(token, builder);
            builder.Append(ch);
        }

        FlushToken(token, builder);
        return builder.ToString();
    }

    private static void FlushToken(StringBuilder token, StringBuilder builder)
    {
        if (token.Length == 0)
        {
            return;
        }

        var value = token.ToString();
        var separator = value.LastIndexOf('.');
        var shortName = separator >= 0 ? value.Substring(separator + 1) : value;
        builder.Append(MapAlias(shortName));
        token.Clear();
    }

    private static string MapAlias(string typeName)
    {
        return typeName switch
        {
            "Boolean" => "bool",
            "Byte" => "byte",
            "Char" => "char",
            "Decimal" => "decimal",
            "Double" => "double",
            "Int16" => "short",
            "Int32" => "int",
            "Int64" => "long",
            "Object" => "object",
            "SByte" => "sbyte",
            "Single" => "float",
            "String" => "string",
            "UInt16" => "ushort",
            "UInt32" => "uint",
            "UInt64" => "ulong",
            "Void" => "void",
            _ => typeName
        };
    }

    private sealed class XmlLocationIndex
    {
        private readonly ImmutableDictionary<(int Line, int Column), XAttribute> _attributesByLocation;
        private readonly ImmutableDictionary<(int Line, int Column), XElement> _elementsByLocation;

        private XmlLocationIndex(
            ImmutableDictionary<(int Line, int Column), XAttribute> attributesByLocation,
            ImmutableDictionary<(int Line, int Column), XElement> elementsByLocation)
        {
            _attributesByLocation = attributesByLocation;
            _elementsByLocation = elementsByLocation;
        }

        public static XmlLocationIndex Create(XDocument document)
        {
            var attributes = ImmutableDictionary.CreateBuilder<(int Line, int Column), XAttribute>();
            var elements = ImmutableDictionary.CreateBuilder<(int Line, int Column), XElement>();

            var elementsToIndex = document.Root is null
                ? Enumerable.Empty<XElement>()
                : document.Root.DescendantsAndSelf();
            foreach (var element in elementsToIndex)
            {
                if (element is IXmlLineInfo elementLineInfo && elementLineInfo.HasLineInfo())
                {
                    elements[(elementLineInfo.LineNumber, elementLineInfo.LinePosition)] = element;
                }

                foreach (var attribute in element.Attributes())
                {
                    if (attribute is not IXmlLineInfo attributeLineInfo || !attributeLineInfo.HasLineInfo())
                    {
                        continue;
                    }

                    attributes[(attributeLineInfo.LineNumber, attributeLineInfo.LinePosition)] = attribute;
                }
            }

            return new XmlLocationIndex(attributes.ToImmutable(), elements.ToImmutable());
        }

        public bool TryGetAttribute(int line, int column, out XAttribute attribute)
        {
            return _attributesByLocation.TryGetValue((line, column), out attribute!);
        }

        public bool TryGetElement(int line, int column, out XElement element)
        {
            return _elementsByLocation.TryGetValue((line, column), out element!);
        }
    }
}
