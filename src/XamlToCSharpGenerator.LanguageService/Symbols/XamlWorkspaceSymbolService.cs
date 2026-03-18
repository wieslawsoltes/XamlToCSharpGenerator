using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed class XamlWorkspaceSymbolService
{
    public ImmutableArray<XamlWorkspaceSymbol> GetWorkspaceSymbols(XamlAnalysisResult analysis)
    {
        if (analysis.ParsedDocument?.RootObject is null)
        {
            return ImmutableArray<XamlWorkspaceSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlWorkspaceSymbol>();
        AddObjectSymbols(
            analysis.ParsedDocument.RootObject,
            analysis.Document.Uri,
            containerName: null,
            builder);
        return builder.ToImmutable();
    }

    private static void AddObjectSymbols(
        XamlObjectNode node,
        string uri,
        string? containerName,
        ImmutableArray<XamlWorkspaceSymbol>.Builder builder)
    {
        var objectRange = CreateRange(node.Line, node.Column, Math.Max(1, node.XmlTypeName.Length));
        var displayName = node.Name is null
            ? node.XmlTypeName
            : node.XmlTypeName + " [" + node.Name + "]";

        builder.Add(new XamlWorkspaceSymbol(
            displayName,
            XamlDocumentSymbolKind.Object,
            uri,
            objectRange,
            containerName));

        foreach (var propertyElement in node.PropertyElements)
        {
            var propertyRange = CreateRange(
                propertyElement.Line,
                propertyElement.Column,
                Math.Max(1, propertyElement.PropertyName.Length));
            builder.Add(new XamlWorkspaceSymbol(
                propertyElement.PropertyName,
                XamlDocumentSymbolKind.Property,
                uri,
                propertyRange,
                displayName));

            foreach (var value in propertyElement.ObjectValues)
            {
                AddObjectSymbols(value, uri, propertyElement.PropertyName, builder);
            }
        }

        foreach (var child in node.ChildObjects)
        {
            AddObjectSymbols(child, uri, displayName, builder);
        }
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
