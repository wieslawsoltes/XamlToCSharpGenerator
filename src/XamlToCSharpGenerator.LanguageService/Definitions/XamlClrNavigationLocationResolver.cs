using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlClrNavigationLocationResolver
{
    public static readonly SourceRange MetadataNavigationRange = new(
        new SourcePosition(0, 0),
        new SourcePosition(0, 1));

    public static AvaloniaSymbolSourceLocation ResolveTypeLocation(
        XamlAnalysisResult analysis,
        XamlResolvedTypeReference typeReference)
    {
        if (typeReference.SourceLocation is { } sourceLocation)
        {
            return sourceLocation;
        }

        if (XamlSourceLinkResolver.TryResolveTypeLocation(
                analysis,
                typeReference.FullTypeName,
                typeReference.AssemblyName,
                out var sourceLinkLocation))
        {
            return sourceLinkLocation;
        }

        return new AvaloniaSymbolSourceLocation(
            XamlMetadataSymbolUri.CreateTypeUri(typeReference.FullTypeName),
            MetadataNavigationRange);
    }

    public static AvaloniaSymbolSourceLocation ResolveTypeLocation(
        XamlAnalysisResult analysis,
        AvaloniaTypeInfo typeInfo)
    {
        if (typeInfo.SourceLocation is { } sourceLocation)
        {
            return sourceLocation;
        }

        if (XamlSourceLinkResolver.TryResolveTypeLocation(
                analysis,
                typeInfo.FullTypeName,
                typeInfo.AssemblyName,
                out var sourceLinkLocation))
        {
            return sourceLinkLocation;
        }

        return new AvaloniaSymbolSourceLocation(
            XamlMetadataSymbolUri.CreateTypeUri(typeInfo.FullTypeName),
            MetadataNavigationRange);
    }

    public static AvaloniaSymbolSourceLocation ResolveTypeLocation(
        XamlAnalysisResult analysis,
        ITypeSymbol typeSymbol)
    {
        if (TryCreateSourceLocation(typeSymbol) is { } sourceLocation)
        {
            return sourceLocation;
        }

        var fullTypeName = GetClrTypeName(typeSymbol);
        if (XamlSourceLinkResolver.TryResolveTypeLocation(
                analysis,
                fullTypeName,
                typeSymbol.ContainingAssembly?.Identity.Name,
                out var sourceLinkLocation))
        {
            return sourceLinkLocation;
        }

        return new AvaloniaSymbolSourceLocation(
            XamlMetadataSymbolUri.CreateTypeUri(fullTypeName),
            MetadataNavigationRange);
    }

    private static AvaloniaSymbolSourceLocation? TryCreateSourceLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree?.FilePath is null)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();
            var start = new SourcePosition(
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character);
            var end = new SourcePosition(
                lineSpan.EndLinePosition.Line,
                lineSpan.EndLinePosition.Character);
            return new AvaloniaSymbolSourceLocation(
                UriPathHelper.ToDocumentUri(location.SourceTree.FilePath),
                new SourceRange(start, end));
        }

        return null;
    }

    private static string GetClrTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol.SpecialType switch
        {
            SpecialType.System_Boolean => "System.Boolean",
            SpecialType.System_Byte => "System.Byte",
            SpecialType.System_Char => "System.Char",
            SpecialType.System_Decimal => "System.Decimal",
            SpecialType.System_Double => "System.Double",
            SpecialType.System_Int16 => "System.Int16",
            SpecialType.System_Int32 => "System.Int32",
            SpecialType.System_Int64 => "System.Int64",
            SpecialType.System_Object => "System.Object",
            SpecialType.System_SByte => "System.SByte",
            SpecialType.System_Single => "System.Single",
            SpecialType.System_String => "System.String",
            SpecialType.System_UInt16 => "System.UInt16",
            SpecialType.System_UInt32 => "System.UInt32",
            SpecialType.System_UInt64 => "System.UInt64",
            SpecialType.System_Void => "System.Void",
            _ => typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
        };
    }
}
