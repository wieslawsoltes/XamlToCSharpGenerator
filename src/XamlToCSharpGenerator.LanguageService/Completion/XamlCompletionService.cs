using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Completion;

public sealed class XamlCompletionService
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private static readonly ImmutableArray<XamlCompletionItem> XamlDirectiveCompletions =
    [
        new("x:Class", "x:Class=\"$0\"", XamlCompletionItemKind.Keyword, "XAML class directive"),
        new("x:Name", "x:Name=\"$0\"", XamlCompletionItemKind.Keyword, "Element name"),
        new("x:Key", "x:Key=\"$0\"", XamlCompletionItemKind.Keyword, "Resource key"),
        new("x:DataType", "x:DataType=\"$0\"", XamlCompletionItemKind.Keyword, "Compiled binding data type"),
        new("x:CompileBindings", "x:CompileBindings=\"True\"", XamlCompletionItemKind.Keyword, "Compiled binding toggle")
    ];

    private static readonly ImmutableArray<XamlCompletionItem> MarkupExtensionCompletions =
    [
        new("Binding", "{Binding $0}", XamlCompletionItemKind.MarkupExtension, "Avalonia binding"),
        new("CompiledBinding", "{CompiledBinding $0}", XamlCompletionItemKind.MarkupExtension, "Compiled binding"),
        new("StaticResource", "{StaticResource $0}", XamlCompletionItemKind.MarkupExtension, "Static resource lookup"),
        new("DynamicResource", "{DynamicResource $0}", XamlCompletionItemKind.MarkupExtension, "Dynamic resource lookup"),
        new("TemplateBinding", "{TemplateBinding $0}", XamlCompletionItemKind.MarkupExtension, "Template binding"),
        new("x:Type", "{x:Type $0}", XamlCompletionItemKind.MarkupExtension, "Type extension"),
        new("x:Null", "{x:Null}", XamlCompletionItemKind.MarkupExtension, "Null extension")
    ];

    public ImmutableArray<XamlCompletionItem> GetCompletions(XamlAnalysisResult analysis, SourcePosition position)
    {
        var context = XamlCompletionContextDetector.Detect(analysis.Document.Text, position);
        if (context.Kind == XamlCompletionContextKind.Unknown)
        {
            return ImmutableArray<XamlCompletionItem>.Empty;
        }

        var prefixMap = analysis.PrefixMap;
        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>();

        switch (context.Kind)
        {
            case XamlCompletionContextKind.ElementName:
                AddElementCompletions(builder, context, prefixMap, analysis.TypeIndex);
                break;

            case XamlCompletionContextKind.AttributeName:
                AddAttributeCompletions(builder, context, prefixMap, analysis.TypeIndex);
                break;

            case XamlCompletionContextKind.AttributeValue:
            case XamlCompletionContextKind.MarkupExtension:
                AddAttributeValueCompletions(builder, context, analysis, position);
                break;
        }

        return builder
            .DistinctBy(static completion => completion.Label, StringComparer.Ordinal)
            .OrderBy(static completion => completion.Label, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AddElementCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        XamlCompletionContext context,
        ImmutableDictionary<string, string> prefixMap,
        AvaloniaTypeIndex? typeIndex)
    {
        if (typeIndex is null)
        {
            return;
        }

        var (prefix, typedName) = XamlXmlNamespaceResolver.SplitQualifiedName(context.Token);

        if (string.IsNullOrEmpty(prefix))
        {
            var xmlNamespace = prefixMap.TryGetValue(string.Empty, out var resolvedNamespace) &&
                               !string.IsNullOrWhiteSpace(resolvedNamespace)
                ? resolvedNamespace
                : AvaloniaDefaultXmlNamespace;

            AppendElementTypeCompletions(completions, typeIndex, xmlNamespace, string.Empty, typedName);
            return;
        }

        if (!prefixMap.TryGetValue(prefix, out var prefixedNamespace))
        {
            return;
        }

        AppendElementTypeCompletions(completions, typeIndex, prefixedNamespace, prefix + ":", typedName);
    }

    private static void AddAttributeCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        XamlCompletionContext context,
        ImmutableDictionary<string, string> prefixMap,
        AvaloniaTypeIndex? typeIndex)
    {
        completions.AddRange(XamlDirectiveCompletions);

        if (string.IsNullOrWhiteSpace(context.CurrentElementName) || typeIndex is null)
        {
            return;
        }

        if (!XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                prefixMap,
                context.CurrentElementName,
                out var elementXmlNamespace,
                out var elementTypeName))
        {
            return;
        }

        if (!typeIndex.TryGetType(elementXmlNamespace, elementTypeName, out var typeInfo) || typeInfo is null)
        {
            return;
        }

        foreach (var property in typeInfo.Properties)
        {
            var kind = property.IsAttached
                ? XamlCompletionItemKind.AttachedProperty
                : XamlCompletionItemKind.Property;
            completions.Add(new XamlCompletionItem(
                property.Name,
                property.Name + "=\"$0\"",
                kind,
                property.TypeName));
        }
    }

    private static void AddAttributeValueCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        XamlCompletionContext context,
        XamlAnalysisResult analysis,
        SourcePosition position)
    {
        if (XamlBindingCompletionService.TryGetCompletions(analysis, position, out var bindingCompletions))
        {
            completions.AddRange(bindingCompletions);
            return;
        }

        if (XamlExpressionCompletionService.TryGetCompletions(analysis, position, out var expressionCompletions))
        {
            completions.AddRange(expressionCompletions);
            return;
        }

        completions.AddRange(MarkupExtensionCompletions);

        if (string.Equals(context.CurrentAttributeName, "x:Name", StringComparison.Ordinal))
        {
            return;
        }

        if (analysis.ParsedDocument is null)
        {
            return;
        }

        if (IsElementReferenceValueContext(context))
        {
            foreach (var namedElement in analysis.ParsedDocument.NamedElements)
            {
                completions.Add(new XamlCompletionItem(
                    namedElement.Name,
                    namedElement.Name,
                    XamlCompletionItemKind.Resource,
                    namedElement.XmlTypeName));
            }
        }

        if (IsResourceReferenceValueContext(context))
        {
            AddResourceKeyCompletions(completions, analysis);
        }
    }

    private static void AddResourceKeyCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        XamlAnalysisResult analysis)
    {
        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Key))
            {
                continue;
            }

            completions.Add(new XamlCompletionItem(
                resource.Key,
                resource.Key,
                XamlCompletionItemKind.Resource,
                resource.XmlTypeName));
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            if (string.IsNullOrWhiteSpace(template.Key))
            {
                continue;
            }

            completions.Add(new XamlCompletionItem(
                template.Key,
                template.Key,
                XamlCompletionItemKind.Resource,
                template.Kind));
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (string.IsNullOrWhiteSpace(style.Key))
            {
                continue;
            }

            completions.Add(new XamlCompletionItem(
                style.Key,
                style.Key,
                XamlCompletionItemKind.Resource,
                "Style"));
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (string.IsNullOrWhiteSpace(controlTheme.Key))
            {
                continue;
            }

            completions.Add(new XamlCompletionItem(
                controlTheme.Key,
                controlTheme.Key,
                XamlCompletionItemKind.Resource,
                "ControlTheme"));
        }
    }

    private static bool IsElementReferenceValueContext(XamlCompletionContext context)
    {
        if (context.Token.Contains("ElementName=", StringComparison.Ordinal) ||
            context.Token.Contains("x:Reference", StringComparison.Ordinal))
        {
            return true;
        }

        return context.CurrentAttributeValue?.IndexOf("ElementName=", StringComparison.Ordinal) >= 0 ||
               context.CurrentAttributeValue?.IndexOf("x:Reference", StringComparison.Ordinal) >= 0;
    }

    private static bool IsResourceReferenceValueContext(XamlCompletionContext context)
    {
        if (context.Token.Contains("StaticResource", StringComparison.Ordinal) ||
            context.Token.Contains("DynamicResource", StringComparison.Ordinal))
        {
            return true;
        }

        return context.CurrentAttributeValue?.IndexOf("StaticResource", StringComparison.Ordinal) >= 0 ||
               context.CurrentAttributeValue?.IndexOf("DynamicResource", StringComparison.Ordinal) >= 0;
    }

    private static void AppendElementTypeCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        AvaloniaTypeIndex typeIndex,
        string xmlNamespace,
        string prefix,
        string typedName)
    {
        var candidates = typeIndex.GetTypes(xmlNamespace);
        foreach (var candidate in candidates)
        {
            if (!candidate.XmlTypeName.StartsWith(typedName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            completions.Add(new XamlCompletionItem(
                prefix + candidate.XmlTypeName,
                prefix + candidate.XmlTypeName,
                XamlCompletionItemKind.Element,
                candidate.FullTypeName,
                candidate.Summary));
        }
    }
}
