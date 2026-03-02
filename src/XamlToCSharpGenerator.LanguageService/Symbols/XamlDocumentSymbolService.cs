using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed class XamlDocumentSymbolService
{
    public ImmutableArray<XamlDocumentSymbol> GetDocumentSymbols(XamlAnalysisResult analysis)
    {
        if (analysis.ParsedDocument?.RootObject is null)
        {
            return ImmutableArray<XamlDocumentSymbol>.Empty;
        }

        return [CreateObjectSymbol(analysis.ParsedDocument.RootObject)];
    }

    private static XamlDocumentSymbol CreateObjectSymbol(XamlObjectNode node)
    {
        var children = ImmutableArray.CreateBuilder<XamlDocumentSymbol>();
        foreach (var child in node.ChildObjects)
        {
            children.Add(CreateObjectSymbol(child));
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            var propertyChildren = ImmutableArray.CreateBuilder<XamlDocumentSymbol>();
            foreach (var value in propertyElement.ObjectValues)
            {
                propertyChildren.Add(CreateObjectSymbol(value));
            }

            var propertyStart = new SourcePosition(
                Math.Max(0, propertyElement.Line - 1),
                Math.Max(0, propertyElement.Column - 1));
            var propertyEnd = new SourcePosition(propertyStart.Line, propertyStart.Character + propertyElement.PropertyName.Length);

            children.Add(new XamlDocumentSymbol(
                Name: propertyElement.PropertyName,
                Kind: XamlDocumentSymbolKind.Property,
                Range: new SourceRange(propertyStart, propertyEnd),
                SelectionRange: new SourceRange(propertyStart, propertyEnd),
                Children: propertyChildren.ToImmutable()));
        }

        var start = new SourcePosition(Math.Max(0, node.Line - 1), Math.Max(0, node.Column - 1));
        var displayName = node.Name is null
            ? node.XmlTypeName
            : node.XmlTypeName + " [" + node.Name + "]";
        var length = Math.Max(1, node.XmlTypeName.Length);
        var end = new SourcePosition(start.Line, start.Character + length);

        return new XamlDocumentSymbol(
            Name: displayName,
            Kind: XamlDocumentSymbolKind.Object,
            Range: new SourceRange(start, end),
            SelectionRange: new SourceRange(start, end),
            Children: children.ToImmutable());
    }
}
