using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Models;
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
            analysis.ViewModel is null ||
            analysis.XmlDocument is null ||
            string.IsNullOrWhiteSpace(analysis.Document.Text))
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlInlayHint>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var xmlLocationIndex = XmlLocationIndex.Create(analysis.XmlDocument);

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

            var inlineTypeName = FormatInlineTypeName(compiledBinding, options.TypeDisplayStyle);
            if (inlineTypeName.Length == 0)
            {
                continue;
            }

            var label = ": " + inlineTypeName;
            var identity = valueRange.End.Line + ":" + valueRange.End.Character + ":" + label;
            if (!seen.Add(identity))
            {
                continue;
            }

            builder.Add(new XamlInlayHint(
                Position: valueRange.End,
                Label: label,
                Kind: XamlInlayHintKind.Type,
                Tooltip: BuildTooltip(compiledBinding),
                PaddingLeft: true,
                PaddingRight: false));
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

    private static string FormatInlineTypeName(
        ResolvedCompiledBindingDefinition compiledBinding,
        XamlInlayHintTypeDisplayStyle displayStyle)
    {
        var typeName = compiledBinding.ResultTypeName!;
        return displayStyle == XamlInlayHintTypeDisplayStyle.Qualified
            ? NormalizeQualifiedTypeName(typeName)
            : ShortenTypeName(typeName);
    }

    private static string BuildTooltip(ResolvedCompiledBindingDefinition compiledBinding)
    {
        var sourceTypeName = NormalizeQualifiedTypeName(compiledBinding.SourceTypeName);
        var resultTypeName = string.IsNullOrWhiteSpace(compiledBinding.ResultTypeName)
            ? "<unavailable>"
            : NormalizeQualifiedTypeName(compiledBinding.ResultTypeName!);

        return string.Join(
            "\n",
            "**Compiled Binding**",
            string.Empty,
            "- Target: `" + NormalizeQualifiedTypeName(compiledBinding.TargetTypeName) + "." + compiledBinding.TargetPropertyName + "`",
            "- Path: `" + compiledBinding.Path + "`",
            "- Source type: `" + sourceTypeName + "`",
            "- Result type: `" + resultTypeName + "`");
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
