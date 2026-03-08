using System;
using System.Collections.Immutable;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Parsing;

public sealed class AvaloniaDocumentFeatureEnricher : IXamlDocumentEnricher
{
    public static AvaloniaDocumentFeatureEnricher Instance { get; } = new();

    private AvaloniaDocumentFeatureEnricher()
    {
    }

    public (XamlDocumentModel Document, ImmutableArray<DiagnosticInfo> Diagnostics) Enrich(
        XamlDocumentModel document,
        XamlDocumentParseContext parseContext)
    {
        var resources = ImmutableArray.CreateBuilder<XamlResourceDefinition>();
        var templates = ImmutableArray.CreateBuilder<XamlTemplateDefinition>();
        var styles = ImmutableArray.CreateBuilder<XamlStyleDefinition>();
        var controlThemes = ImmutableArray.CreateBuilder<XamlControlThemeDefinition>();
        var includes = ImmutableArray.CreateBuilder<XamlIncludeDefinition>();

        foreach (var element in parseContext.RootElement.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, parseContext.IgnoredNamespaces))
            {
                continue;
            }

            AddElementFeatures(
                element,
                parseContext.ConditionalNamespacesByRawUri,
                resources,
                templates,
                styles,
                controlThemes,
                includes);
        }

        var enriched = document with
        {
            Resources = resources.ToImmutable(),
            Templates = templates.ToImmutable(),
            Styles = styles.ToImmutable(),
            ControlThemes = controlThemes.ToImmutable(),
            Includes = includes.ToImmutable()
        };
        return (enriched, ImmutableArray<DiagnosticInfo>.Empty);
    }

    private static void AddElementFeatures(
        XElement element,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri,
        ImmutableArray<XamlResourceDefinition>.Builder resources,
        ImmutableArray<XamlTemplateDefinition>.Builder templates,
        ImmutableArray<XamlStyleDefinition>.Builder styles,
        ImmutableArray<XamlControlThemeDefinition>.Builder controlThemes,
        ImmutableArray<XamlIncludeDefinition>.Builder includes)
    {
        var info = CollectElementInfo(element);
        var condition = XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
            element.Name.NamespaceName,
            conditionalNamespacesByRawUri);
        var lineInfo = (IXmlLineInfo)element;
        var line = lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1;
        var column = lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1;
        string? rawXaml = null;

        if (!string.IsNullOrWhiteSpace(info.Key))
        {
            rawXaml ??= element.ToString(SaveOptions.DisableFormatting);
            resources.Add(new XamlResourceDefinition(
                Key: info.Key!,
                XmlNamespace: XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName),
                XmlTypeName: element.Name.LocalName,
                Condition: condition,
                RawXaml: rawXaml,
                Line: line,
                Column: column));
        }

        if (IsTemplateElement(element.Name.LocalName))
        {
            rawXaml ??= element.ToString(SaveOptions.DisableFormatting);
            templates.Add(new XamlTemplateDefinition(
                Kind: element.Name.LocalName,
                Key: info.Key,
                TargetType: info.TargetType,
                DataType: info.DataType,
                Condition: condition,
                RawXaml: rawXaml,
                Line: line,
                Column: column));
        }

        if (element.Name.LocalName == "Style")
        {
            rawXaml ??= element.ToString(SaveOptions.DisableFormatting);
            var selectorLineInfo = info.SelectorAttribute is null
                ? lineInfo
                : (IXmlLineInfo)info.SelectorAttribute;
            styles.Add(new XamlStyleDefinition(
                Key: info.Key,
                Selector: info.Selector ?? string.Empty,
                SelectorLine: selectorLineInfo.HasLineInfo()
                    ? selectorLineInfo.LineNumber
                    : line,
                SelectorColumn: selectorLineInfo.HasLineInfo()
                    ? selectorLineInfo.LinePosition
                    : column,
                DataType: info.DataType,
                CompileBindings: info.CompileBindings,
                Setters: CollectSetters(element, conditionalNamespacesByRawUri),
                Condition: condition,
                RawXaml: rawXaml,
                Line: line,
                Column: column));
        }

        if (element.Name.LocalName == "ControlTheme")
        {
            rawXaml ??= element.ToString(SaveOptions.DisableFormatting);
            controlThemes.Add(new XamlControlThemeDefinition(
                Key: info.Key,
                TargetType: info.TargetType,
                BasedOn: info.BasedOn,
                ThemeVariant: info.ThemeVariant,
                DataType: info.DataType,
                CompileBindings: info.CompileBindings,
                Setters: CollectSetters(element, conditionalNamespacesByRawUri),
                Condition: condition,
                RawXaml: rawXaml,
                Line: line,
                Column: column));
        }

        if (IsIncludeElement(element.Name.LocalName))
        {
            rawXaml ??= element.ToString(SaveOptions.DisableFormatting);
            includes.Add(new XamlIncludeDefinition(
                Kind: element.Name.LocalName,
                Source: string.IsNullOrWhiteSpace(info.Source) ? string.Empty : info.Source!,
                MergeTarget: ResolveMergeTarget(element),
                Condition: condition,
                RawXaml: rawXaml,
                Line: line,
                Column: column));
        }
    }

    private static bool IsSettersPropertyElement(XElement scope, XElement element)
    {
        return XamlPropertyTokenSemantics.IsPropertyElementName(
            element.Name.LocalName,
            "Setters",
            ownerToken: scope.Name.LocalName);
    }

    private static void AddSetterDefinition(
        XElement setter,
        ImmutableArray<XamlSetterDefinition>.Builder setters,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        string? propertyName = null;
        string? value = null;
        XElement? firstValueElement = null;

        foreach (var attribute in setter.Attributes())
        {
            if (attribute.Name.NamespaceName.Length != 0)
            {
                continue;
            }

            if (attribute.Name.LocalName == "Property")
            {
                propertyName = attribute.Value;
            }
            else if (attribute.Name.LocalName == "Value")
            {
                value = attribute.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        if (value is null)
        {
            foreach (var child in setter.Elements())
            {
                firstValueElement = child;
                break;
            }

            if (firstValueElement is not null &&
                XamlPropertyTokenSemantics.IsPropertyElementName(
                    firstValueElement.Name.LocalName,
                    "Value",
                    ownerToken: setter.Name.LocalName))
            {
                XElement? innerValueElement = null;
                foreach (var child in firstValueElement.Elements())
                {
                    innerValueElement = child;
                    break;
                }

                value = innerValueElement is not null
                    ? innerValueElement.ToString(SaveOptions.DisableFormatting)
                    : string.Empty;
            }
            else
            {
                value = firstValueElement?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
            }
        }

        var lineInfo = (IXmlLineInfo)setter;
        setters.Add(new XamlSetterDefinition(
            PropertyName: propertyName!,
            Value: value,
            Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                setter.Name.NamespaceName,
                conditionalNamespacesByRawUri),
            Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
            Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
    }

    private static ImmutableArray<XamlSetterDefinition> CollectSetters(
        XElement scope,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var setters = ImmutableArray.CreateBuilder<XamlSetterDefinition>();

        foreach (var element in scope.Elements())
        {
            if (element.Name.LocalName == "Setter")
            {
                AddSetterDefinition(element, setters, conditionalNamespacesByRawUri);
                continue;
            }

            if (IsSettersPropertyElement(scope, element))
            {
                foreach (var nestedSetter in element.Elements())
                {
                    if (nestedSetter.Name.LocalName == "Setter")
                    {
                        AddSetterDefinition(nestedSetter, setters, conditionalNamespacesByRawUri);
                    }
                }
            }
        }

        return setters.ToImmutable();
    }

    private static string ResolveMergeTarget(XElement includeElement)
    {
        XElement? current = includeElement;
        while (current is not null)
        {
            var localName = current.Name.LocalName;
            if (XamlPropertyTokenSemantics.IsPropertyElementName(localName, "MergedDictionaries"))
            {
                return "MergedDictionaries";
            }

            if (XamlPropertyTokenSemantics.IsPropertyElementName(localName, "Styles"))
            {
                return "Styles";
            }

            current = current.Parent;
        }

        return "Unknown";
    }

    private static bool IsTemplateElement(string localName)
    {
        return localName == "DataTemplate"
               || localName == "ControlTemplate"
               || localName == "ItemsPanelTemplate"
               || localName == "TreeDataTemplate";
    }

    private static bool IsIncludeElement(string localName)
    {
        return localName == "ResourceInclude" || localName == "StyleInclude" || localName == "MergeResourceInclude";
    }

    private static bool ShouldIgnoreElement(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName));
    }

    private static bool? TryGetBoolDirectiveValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var result) ? result : null;
    }

    private static ElementFeatureInfo CollectElementInfo(XElement element)
    {
        var key = default(string);
        var dataType = default(string);
        var compileBindingsText = default(string);
        var selector = default(string);
        var selectorAttribute = default(XAttribute);
        var targetType = default(string);
        var basedOn = default(string);
        var themeVariant = default(string);
        var source = default(string);

        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.NamespaceName.Length == 0)
            {
                switch (attribute.Name.LocalName)
                {
                    case "Selector":
                        selector = attribute.Value;
                        selectorAttribute = attribute;
                        break;
                    case "TargetType":
                        targetType = attribute.Value;
                        break;
                    case "BasedOn":
                        basedOn = attribute.Value;
                        break;
                    case "ThemeVariant":
                        themeVariant = attribute.Value;
                        break;
                    case "Source":
                        source = attribute.Value;
                        break;
                }

                continue;
            }

            if (attribute.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml")
            {
                switch (attribute.Name.LocalName)
                {
                    case "Key":
                        key = attribute.Value;
                        break;
                    case "DataType":
                        dataType = attribute.Value;
                        break;
                    case "CompileBindings":
                        compileBindingsText = attribute.Value;
                        break;
                }
            }
        }

        return new ElementFeatureInfo(
            Key: key,
            DataType: dataType,
            CompileBindings: TryGetBoolDirectiveValue(compileBindingsText),
            Selector: selector,
            SelectorAttribute: selectorAttribute,
            TargetType: targetType,
            BasedOn: basedOn,
            ThemeVariant: themeVariant,
            Source: source);
    }

    private readonly record struct ElementFeatureInfo(
        string? Key,
        string? DataType,
        bool? CompileBindings,
        string? Selector,
        XAttribute? SelectorAttribute,
        string? TargetType,
        string? BasedOn,
        string? ThemeVariant,
        string? Source);
}
