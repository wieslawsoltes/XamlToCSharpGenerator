using System;
using System.Collections.Immutable;
using System.Linq;
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
        var resources = CollectResources(
            parseContext.RootElement,
            parseContext.IgnoredNamespaces,
            parseContext.ConditionalNamespacesByRawUri);
        var templates = CollectTemplates(
            parseContext.RootElement,
            parseContext.IgnoredNamespaces,
            parseContext.ConditionalNamespacesByRawUri);
        var styles = CollectStyles(
            parseContext.RootElement,
            parseContext.IgnoredNamespaces,
            parseContext.ConditionalNamespacesByRawUri);
        var controlThemes = CollectControlThemes(
            parseContext.RootElement,
            parseContext.IgnoredNamespaces,
            parseContext.ConditionalNamespacesByRawUri);
        var includes = CollectIncludes(
            parseContext.RootElement,
            parseContext.IgnoredNamespaces,
            parseContext.ConditionalNamespacesByRawUri);

        var enriched = document with
        {
            Resources = resources,
            Templates = templates,
            Styles = styles,
            ControlThemes = controlThemes,
            Includes = includes
        };
        return (enriched, ImmutableArray<DiagnosticInfo>.Empty);
    }

    private static ImmutableArray<XamlResourceDefinition> CollectResources(
        XElement root,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var resources = ImmutableArray.CreateBuilder<XamlResourceDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var key = TryGetDirectiveValue(element, "Key");
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)element;
            resources.Add(new XamlResourceDefinition(
                Key: key!,
                XmlNamespace: XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName),
                XmlTypeName: element.Name.LocalName,
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    element.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return resources.ToImmutable();
    }

    private static ImmutableArray<XamlTemplateDefinition> CollectTemplates(
        XElement root,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var templates = ImmutableArray.CreateBuilder<XamlTemplateDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            if (!IsTemplateElement(element.Name.LocalName))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)element;
            templates.Add(new XamlTemplateDefinition(
                Kind: element.Name.LocalName,
                Key: TryGetDirectiveValue(element, "Key"),
                TargetType: element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value,
                DataType: TryGetDirectiveValue(element, "DataType"),
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    element.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return templates.ToImmutable();
    }

    private static ImmutableArray<XamlStyleDefinition> CollectStyles(
        XElement root,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var styles = ImmutableArray.CreateBuilder<XamlStyleDefinition>();

        foreach (var element in root.DescendantsAndSelf().Where(x => x.Name.LocalName == "Style"))
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var selectorAttribute = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Selector");
            var selector = selectorAttribute?.Value;

            var lineInfo = (IXmlLineInfo)element;
            var selectorLineInfo = selectorAttribute is null
                ? lineInfo
                : (IXmlLineInfo)selectorAttribute;
            styles.Add(new XamlStyleDefinition(
                Key: TryGetDirectiveValue(element, "Key"),
                Selector: selector ?? string.Empty,
                SelectorLine: selectorLineInfo.HasLineInfo()
                    ? selectorLineInfo.LineNumber
                    : (lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1),
                SelectorColumn: selectorLineInfo.HasLineInfo()
                    ? selectorLineInfo.LinePosition
                    : (lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1),
                DataType: TryGetDirectiveValue(element, "DataType"),
                CompileBindings: TryGetBoolDirectiveValue(element, "CompileBindings"),
                Setters: CollectSetters(element, conditionalNamespacesByRawUri),
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    element.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return styles.ToImmutable();
    }

    private static ImmutableArray<XamlControlThemeDefinition> CollectControlThemes(
        XElement root,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var themes = ImmutableArray.CreateBuilder<XamlControlThemeDefinition>();

        foreach (var element in root.DescendantsAndSelf().Where(x => x.Name.LocalName == "ControlTheme"))
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var targetType = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value;
            var basedOn = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "BasedOn")?.Value;
            var themeVariant = element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "ThemeVariant")?.Value;

            var lineInfo = (IXmlLineInfo)element;
            themes.Add(new XamlControlThemeDefinition(
                Key: TryGetDirectiveValue(element, "Key"),
                TargetType: targetType,
                BasedOn: basedOn,
                ThemeVariant: themeVariant,
                DataType: TryGetDirectiveValue(element, "DataType"),
                CompileBindings: TryGetBoolDirectiveValue(element, "CompileBindings"),
                Setters: CollectSetters(element, conditionalNamespacesByRawUri),
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    element.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return themes.ToImmutable();
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
                foreach (var nestedSetter in element.Elements().Where(x => x.Name.LocalName == "Setter"))
                {
                    AddSetterDefinition(nestedSetter, setters, conditionalNamespacesByRawUri);
                }
            }
        }

        return setters.ToImmutable();
    }

    private static bool IsSettersPropertyElement(XElement scope, XElement element)
    {
        return element.Name.LocalName.Equals(scope.Name.LocalName + ".Setters", StringComparison.Ordinal);
    }

    private static void AddSetterDefinition(
        XElement setter,
        ImmutableArray<XamlSetterDefinition>.Builder setters,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var propertyName = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Property")?.Value;

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        var value = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Value")?.Value;

        if (value is null)
        {
            var firstValueElement = setter.Elements().FirstOrDefault();
            if (firstValueElement is not null &&
                firstValueElement.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal) &&
                firstValueElement.Elements().FirstOrDefault() is { } innerValueElement)
            {
                value = innerValueElement.ToString(SaveOptions.DisableFormatting);
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

    private static ImmutableArray<XamlIncludeDefinition> CollectIncludes(
        XElement root,
        ImmutableHashSet<string> ignoredNamespaces,
        ImmutableDictionary<string, ConditionalXamlExpression> conditionalNamespacesByRawUri)
    {
        var includes = ImmutableArray.CreateBuilder<XamlIncludeDefinition>();

        foreach (var element in root.DescendantsAndSelf())
        {
            if (ShouldIgnoreElement(element, ignoredNamespaces))
            {
                continue;
            }

            var kind = element.Name.LocalName;
            if (kind != "ResourceInclude" && kind != "StyleInclude" && kind != "MergeResourceInclude")
            {
                continue;
            }

            var source = element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Source")
                ?.Value;
            var sourceValue = string.IsNullOrWhiteSpace(source) ? string.Empty : source;

            var lineInfo = (IXmlLineInfo)element;
            includes.Add(new XamlIncludeDefinition(
                Kind: kind,
                Source: sourceValue!,
                MergeTarget: ResolveMergeTarget(element),
                Condition: XamlConditionalNamespaceUtilities.TryGetConditionalExpression(
                    element.Name.NamespaceName,
                    conditionalNamespacesByRawUri),
                RawXaml: element.ToString(SaveOptions.DisableFormatting),
                Line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                Column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1));
        }

        return includes.ToImmutable();
    }

    private static string ResolveMergeTarget(XElement includeElement)
    {
        XElement? current = includeElement;
        while (current is not null)
        {
            var localName = current.Name.LocalName;
            if (localName.EndsWith(".MergedDictionaries", StringComparison.Ordinal) || localName == "MergedDictionaries")
            {
                return "MergedDictionaries";
            }

            if (localName.EndsWith(".Styles", StringComparison.Ordinal) || localName == "Styles")
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

    private static bool ShouldIgnoreElement(XElement element, ImmutableHashSet<string> ignoredNamespaces)
    {
        return ignoredNamespaces.Contains(XamlConditionalNamespaceUtilities.NormalizeXmlNamespace(element.Name.NamespaceName));
    }

    private static XAttribute? TryGetDirectiveAttribute(XElement element, string directiveName)
    {
        var xaml2006 = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml";
        return element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == xaml2006 && attribute.Name.LocalName == directiveName);
    }

    private static string? TryGetDirectiveValue(XElement element, string directiveName)
    {
        return TryGetDirectiveAttribute(element, directiveName)?.Value;
    }

    private static bool? TryGetBoolDirectiveValue(XElement element, string directiveName)
    {
        var value = TryGetDirectiveValue(element, directiveName);
        if (value is null)
        {
            return null;
        }

        return bool.TryParse(value, out var result) ? result : null;
    }
}
