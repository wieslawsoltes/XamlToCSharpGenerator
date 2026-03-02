using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

public sealed class XamlReferenceService
{
    public ImmutableArray<XamlReferenceLocation> GetReferences(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (analysis.ParsedDocument is null)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var offset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var identifier = XamlNavigationTextSemantics.ExtractIdentifierAtOffset(analysis.Document.Text, offset);
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var hasNamedDeclaration = HasNamedElementDeclaration(analysis, identifier);
        var hasResourceDeclaration = HasResourceDeclaration(analysis, identifier);
        if (!hasNamedDeclaration && !hasResourceDeclaration)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var symbolKind = XamlNavigationTextSemantics.DetectSymbolKindAtOffset(
            analysis.Document.Text,
            offset,
            identifier,
            hasNamedDeclaration,
            hasResourceDeclaration);
        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.Unknown)
        {
            return ImmutableArray<XamlReferenceLocation>.Empty;
        }

        var resultBuilder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        var seen = new HashSet<SourceRange>();

        var declarationBuilder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            AddNamedElementDeclarations(analysis, identifier, declarationBuilder);
        }
        else if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
        {
            AddResourceDeclarations(analysis, identifier, declarationBuilder);
        }

        foreach (var declaration in declarationBuilder)
        {
            if (seen.Add(declaration.Range))
            {
                resultBuilder.Add(declaration);
            }
        }

        if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.NamedElement)
        {
            foreach (var range in XamlNavigationTextSemantics.FindElementReferenceRanges(analysis.Document.Text, identifier))
            {
                if (seen.Add(range))
                {
                    resultBuilder.Add(new XamlReferenceLocation(
                        UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                        range,
                        IsDeclaration: false));
                }
            }
        }
        else if (symbolKind == XamlNavigationTextSemantics.NavigationSymbolKind.ResourceKey)
        {
            foreach (var range in XamlNavigationTextSemantics.FindResourceReferenceRanges(analysis.Document.Text, identifier))
            {
                if (seen.Add(range))
                {
                    resultBuilder.Add(new XamlReferenceLocation(
                        UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                        range,
                        IsDeclaration: false));
                }
            }
        }

        return resultBuilder.ToImmutable();
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

    private static int AddNamedElementDeclarations(
        XamlAnalysisResult analysis,
        string identifier,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        var added = 0;
        foreach (var namedElement in analysis.ParsedDocument!.NamedElements)
        {
            if (!string.Equals(namedElement.Name, identifier, StringComparison.Ordinal))
            {
                continue;
            }

            var range = CreateRange(namedElement.Line, namedElement.Column, identifier.Length);
            builder.Add(new XamlReferenceLocation(
                UriPathHelper.ToDocumentUri(analysis.Document.FilePath),
                range,
                IsDeclaration: true));
            added++;
        }

        return added;
    }

    private static int AddResourceDeclarations(
        XamlAnalysisResult analysis,
        string identifier,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        var added = 0;

        foreach (var resource in analysis.ParsedDocument!.Resources)
        {
            if (TryAddDeclaration(resource.Key, resource.Line, resource.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var template in analysis.ParsedDocument.Templates)
        {
            if (TryAddDeclaration(template.Key, template.Line, template.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var style in analysis.ParsedDocument.Styles)
        {
            if (TryAddDeclaration(style.Key, style.Line, style.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        foreach (var controlTheme in analysis.ParsedDocument.ControlThemes)
        {
            if (TryAddDeclaration(controlTheme.Key, controlTheme.Line, controlTheme.Column, identifier, analysis.Document.FilePath, builder))
            {
                added++;
            }
        }

        return added;
    }

    private static bool TryAddDeclaration(
        string? key,
        int line,
        int column,
        string identifier,
        string filePath,
        ImmutableArray<XamlReferenceLocation>.Builder builder)
    {
        if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, identifier, StringComparison.Ordinal))
        {
            return false;
        }

        var range = CreateRange(line, column, key.Length);
        builder.Add(new XamlReferenceLocation(
            UriPathHelper.ToDocumentUri(filePath),
            range,
            IsDeclaration: true));
        return true;
    }

    private static SourceRange CreateRange(int line, int column, int length)
    {
        var start = new SourcePosition(
            Math.Max(0, line - 1),
            Math.Max(0, column - 1));
        var end = new SourcePosition(start.Line, start.Character + Math.Max(1, length));
        return new SourceRange(start, end);
    }
}
