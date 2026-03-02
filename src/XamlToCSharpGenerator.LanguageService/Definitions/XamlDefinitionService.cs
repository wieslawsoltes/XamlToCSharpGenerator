using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

public sealed class XamlDefinitionService
{
    public ImmutableArray<XamlDefinitionLocation> GetDefinitions(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (analysis.ParsedDocument is null)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var hasNamedDeclaration = HasNamedElementDeclaration(analysis, identifier);
        var hasResourceDeclaration = HasResourceDeclaration(analysis, identifier);
        if (!hasNamedDeclaration && !hasResourceDeclaration)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        var symbolKind = XamlNavigationTextSemantics.DetectSymbolKindAtOffset(
            analysis.Document.Text,
            offset,
            identifier,
            hasNamedDeclaration,
            hasResourceDeclaration);

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.Unknown)
        {
            return ImmutableArray<XamlDefinitionLocation>.Empty;
        }

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            return CollectNamedElementDefinitions(analysis, identifier);
        }

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
        {
            return CollectResourceDefinitions(analysis, identifier);
        }

        return ImmutableArray<XamlDefinitionLocation>.Empty;
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectNamedElementDefinitions(
        XamlAnalysisResult analysis,
        string identifier)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (!string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var start = new SourcePosition(
                Math.Max(0, namedElement.Line - 1),
                Math.Max(0, namedElement.Column - 1));
            var end = new SourcePosition(start.Line, start.Character + Math.Max(1, namedElement.Name.Length));

            builder.Add(new XamlDefinitionLocation(
                UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                new SourceRange(start, end)));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlDefinitionLocation> CollectResourceDefinitions(
        XamlAnalysisResult analysis,
        string identifier)
    {
        var builder = ImmutableArray.CreateBuilder<XamlDefinitionLocation>();

        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            TryAddDefinition(resource.Key, resource.Line, resource.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            TryAddDefinition(template.Key, template.Line, template.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            TryAddDefinition(style.Key, style.Line, style.Column, identifier, analysis.Document.FilePath, builder);
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            TryAddDefinition(controlTheme.Key, controlTheme.Line, controlTheme.Column, identifier, analysis.Document.FilePath, builder);
        }

        return builder.ToImmutable();
    }

    private static bool TryAddDefinition(
        string? key,
        int line,
        int column,
        string identifier,
        string filePath,
        ImmutableArray<XamlDefinitionLocation>.Builder builder)
    {
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, identifier, StringComparison.Ordinal))
        {
            return false;
        }

        var start = new SourcePosition(
            Math.Max(0, line - 1),
            Math.Max(0, column - 1));
        var end = new SourcePosition(start.Line, start.Character + Math.Max(1, key.Length));
        builder.Add(new XamlDefinitionLocation(
            UriPathHelper.ToDocumentUri(filePath),
            new SourceRange(start, end)));
        return true;
    }

    private static bool HasNamedElementDeclaration(XamlAnalysisResult analysis, string identifier)
    {
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResourceDeclaration(XamlAnalysisResult analysis, string identifier)
    {
        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            if (string.Equals(resource.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            if (string.Equals(template.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (string.Equals(style.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (string.Equals(controlTheme.Key, identifier, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
