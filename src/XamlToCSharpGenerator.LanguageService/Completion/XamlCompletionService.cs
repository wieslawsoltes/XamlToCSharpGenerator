using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Completion;

public sealed class XamlCompletionService
{
    private static readonly ImmutableArray<XamlCompletionItem> BaseDirectiveCompletions =
    [
        new("x:Class", "x:Class=\"$0\"", XamlCompletionItemKind.Keyword, "XAML class directive", InsertTextIsSnippet: true),
        new("x:Name", "x:Name=\"$0\"", XamlCompletionItemKind.Keyword, "Element name", InsertTextIsSnippet: true),
        new("x:Key", "x:Key=\"$0\"", XamlCompletionItemKind.Keyword, "Resource key", InsertTextIsSnippet: true)
    ];

    private static readonly ImmutableArray<XamlCompletionItem> BaseMarkupExtensionCompletions =
    [
        new("Binding", "{Binding $0}", XamlCompletionItemKind.MarkupExtension, "Binding", InsertTextIsSnippet: true),
        new("StaticResource", "{StaticResource $0}", XamlCompletionItemKind.MarkupExtension, "Static resource lookup", InsertTextIsSnippet: true),
        new("DynamicResource", "{DynamicResource $0}", XamlCompletionItemKind.MarkupExtension, "Dynamic resource lookup", InsertTextIsSnippet: true),
        new("TemplateBinding", "{TemplateBinding $0}", XamlCompletionItemKind.MarkupExtension, "Template binding", InsertTextIsSnippet: true),
        new("RelativeSource", "{RelativeSource $0}", XamlCompletionItemKind.MarkupExtension, "Relative source", InsertTextIsSnippet: true),
        new("x:Reference", "{x:Reference $0}", XamlCompletionItemKind.MarkupExtension, "Named element reference", InsertTextIsSnippet: true),
        new("x:Static", "{x:Static $0}", XamlCompletionItemKind.MarkupExtension, "Static member reference", InsertTextIsSnippet: true),
        new("x:Type", "{x:Type $0}", XamlCompletionItemKind.MarkupExtension, "Type extension", InsertTextIsSnippet: true),
        new("x:Null", "{x:Null}", XamlCompletionItemKind.MarkupExtension, "Null extension")
    ];

    public ImmutableArray<XamlCompletionItem> GetCompletions(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (XamlInlineCSharpCompletionService.TryGetCompletions(analysis, position, out var inlineCodeCompletions))
        {
            return inlineCodeCompletions
                .DistinctBy(static completion => completion.Label, StringComparer.Ordinal)
                .OrderBy(static completion => completion.Label, StringComparer.Ordinal)
                .ToImmutableArray();
        }

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

            case XamlCompletionContextKind.QualifiedPropertyElement:
                AddQualifiedPropertyElementCompletions(builder, analysis.Document.Text, context, prefixMap, analysis.TypeIndex);
                break;

            case XamlCompletionContextKind.AttributeName:
                AddAttributeCompletions(builder, context, analysis);
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
                : typeIndex.DefaultXmlNamespace;

            AppendElementTypeCompletions(completions, typeIndex, xmlNamespace, string.Empty, typedName);
            return;
        }

        if (!prefixMap.TryGetValue(prefix, out var prefixedNamespace))
        {
            return;
        }

        AppendElementTypeCompletions(completions, typeIndex, prefixedNamespace, prefix + ":", typedName);
    }

    private static void AddQualifiedPropertyElementCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        string documentText,
        XamlCompletionContext context,
        ImmutableDictionary<string, string> prefixMap,
        AvaloniaTypeIndex? typeIndex)
    {
        if (typeIndex is null ||
            !XamlPropertyElementSemantics.TrySplitOwnerQualifiedPropertyFragment(
                context.Token,
                out var ownerToken,
                out var propertyPrefix) ||
            !XamlClrSymbolResolver.TryResolveTypeInfo(typeIndex, prefixMap, ownerToken, out var ownerTypeInfo) ||
            ownerTypeInfo is null)
        {
            return;
        }

        foreach (var property in ownerTypeInfo.Properties)
        {
            if (!property.Name.StartsWith(propertyPrefix ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var kind = property.IsAttached
                ? XamlCompletionItemKind.AttachedProperty
                : XamlCompletionItemKind.Property;
            completions.Add(new XamlCompletionItem(
                property.Name,
                ownerToken + "." + property.Name,
                kind,
                property.TypeName,
                ReplaceRange: new SourceRange(
                    XamlToCSharpGenerator.LanguageService.Text.TextCoordinateHelper.GetPosition(documentText, context.TokenStartOffset),
                    XamlToCSharpGenerator.LanguageService.Text.TextCoordinateHelper.GetPosition(documentText, context.TokenEndOffset))));
        }
    }

    private static void AddAttributeCompletions(
        ImmutableArray<XamlCompletionItem>.Builder completions,
        XamlCompletionContext context,
        XamlAnalysisResult analysis)
    {
        completions.AddRange(GetDirectiveCompletions(analysis.Framework));

        if (string.IsNullOrWhiteSpace(context.CurrentElementName) || analysis.TypeIndex is null)
        {
            return;
        }

        if (!XamlXmlNamespaceResolver.TryResolveXmlNamespace(
                analysis.PrefixMap,
                context.CurrentElementName,
                out var elementXmlNamespace,
                out var elementTypeName,
                analysis.Framework.DefaultXmlNamespace))
        {
            return;
        }

        if (!analysis.TypeIndex.TryGetType(elementXmlNamespace, elementTypeName, out var typeInfo) || typeInfo is null)
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
                property.TypeName,
                InsertTextIsSnippet: true));
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

        completions.AddRange(CreateMarkupExtensionCompletions(analysis.Document.Text, context, analysis.Framework));

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

    private static ImmutableArray<XamlCompletionItem> CreateMarkupExtensionCompletions(
        string documentText,
        XamlCompletionContext context,
        XamlLanguageFrameworkInfo framework)
    {
        var markupExtensionCompletions = GetMarkupExtensionCompletions(framework);
        if (string.IsNullOrEmpty(context.Token) ||
            context.TokenStartOffset < 0 ||
            context.TokenEndOffset < context.TokenStartOffset)
        {
            return markupExtensionCompletions;
        }

        var replaceRange = new SourceRange(
            XamlToCSharpGenerator.LanguageService.Text.TextCoordinateHelper.GetPosition(documentText, context.TokenStartOffset),
            XamlToCSharpGenerator.LanguageService.Text.TextCoordinateHelper.GetPosition(documentText, context.TokenEndOffset));

        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>(markupExtensionCompletions.Length);
        foreach (var completion in markupExtensionCompletions)
        {
            builder.Add(completion with { ReplaceRange = replaceRange });
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlCompletionItem> GetDirectiveCompletions(XamlLanguageFrameworkInfo framework)
    {
        return BaseDirectiveCompletions.AddRange(MapFrameworkCompletions(
            framework.DirectiveCompletions,
            XamlCompletionItemKind.Keyword));
    }

    private static ImmutableArray<XamlCompletionItem> GetMarkupExtensionCompletions(XamlLanguageFrameworkInfo framework)
    {
        return BaseMarkupExtensionCompletions.AddRange(MapFrameworkCompletions(
            framework.MarkupExtensionCompletions,
            XamlCompletionItemKind.MarkupExtension));
    }

    private static ImmutableArray<XamlCompletionItem> MapFrameworkCompletions(
        ImmutableArray<XamlLanguageFrameworkCompletion> completions,
        XamlCompletionItemKind itemKind)
    {
        if (completions.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlCompletionItem>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>(completions.Length);
        foreach (var completion in completions)
        {
            builder.Add(new XamlCompletionItem(
                completion.Label,
                completion.InsertText,
                itemKind,
                completion.Detail,
                InsertTextIsSnippet: completion.InsertTextIsSnippet));
        }

        return builder.ToImmutable();
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
