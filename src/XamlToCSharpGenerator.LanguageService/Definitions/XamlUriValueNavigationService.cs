using System;
using System.IO;
using System.Linq;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlUriValueNavigationService
{
    public static bool TryResolveDefinitionAtOffset(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlDefinitionLocation definitionLocation)
    {
        definitionLocation = default!;
        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out _,
                out var attribute,
                out _,
                out var attributeValueRange) ||
            !string.Equals(attribute.Name.LocalName, "Source", StringComparison.Ordinal) ||
            !ContainsPosition(analysis.Document.Text, attributeValueRange, position))
        {
            return false;
        }

        if (!TryResolveTargetFilePath(analysis, attribute.Value, out var targetFilePath))
        {
            return false;
        }

        definitionLocation = new XamlDefinitionLocation(
            UriPathHelper.ToDocumentUri(targetFilePath),
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(0, 0)));
        return true;
    }

    private static bool TryResolveTargetFilePath(
        XamlAnalysisResult analysis,
        string sourceValue,
        out string targetFilePath)
    {
        targetFilePath = string.Empty;
        var normalizedIncludeSource = NormalizeIncludeSource(sourceValue);
        if (!LooksLikeXamlUri(normalizedIncludeSource))
        {
            return false;
        }

        if (TryResolveAbsoluteFileUri(normalizedIncludeSource, out targetFilePath))
        {
            return true;
        }

        if (TryResolveAbsoluteAvaresTargetPath(analysis, normalizedIncludeSource, out var avaresTargetPath) &&
            XamlProjectFileDiscoveryService.TryResolveProjectXamlFileByTargetPath(
                analysis.ProjectPath,
                analysis.Document.FilePath,
                avaresTargetPath,
                out targetFilePath))
        {
            return true;
        }

        if (normalizedIncludeSource.StartsWith("/", StringComparison.Ordinal))
        {
            var rootedTargetPath = XamlIncludePathSemantics.NormalizePath(normalizedIncludeSource.TrimStart('/'));
            return XamlProjectFileDiscoveryService.TryResolveProjectXamlFileByTargetPath(
                analysis.ProjectPath,
                analysis.Document.FilePath,
                rootedTargetPath,
                out targetFilePath);
        }

        if (TryResolveRelativeTargetPath(analysis, normalizedIncludeSource, out var relativeTargetPath))
        {
            return XamlProjectFileDiscoveryService.TryResolveProjectXamlFileByTargetPath(
                analysis.ProjectPath,
                analysis.Document.FilePath,
                relativeTargetPath,
                out targetFilePath);
        }

        return false;
    }

    private static bool TryResolveRelativeTargetPath(
        XamlAnalysisResult analysis,
        string includeSource,
        out string targetPath)
    {
        targetPath = string.Empty;
        if (!XamlProjectFileDiscoveryService.TryResolveProjectXamlEntryByFilePath(
                analysis.ProjectPath,
                analysis.Document.FilePath,
                analysis.Document.FilePath,
                out var currentEntry))
        {
            return false;
        }

        var currentDirectory = XamlIncludePathSemantics.GetDirectory(currentEntry.TargetPath);
        targetPath = XamlIncludePathSemantics.NormalizePath(
            XamlIncludePathSemantics.CombinePath(currentDirectory, includeSource));
        return targetPath.Length > 0;
    }

    private static bool TryResolveAbsoluteFileUri(string includeSource, out string filePath)
    {
        filePath = string.Empty;
        if (!Uri.TryCreate(includeSource, UriKind.Absolute, out var absoluteUri) || !absoluteUri.IsFile)
        {
            return false;
        }

        filePath = absoluteUri.LocalPath;
        return File.Exists(filePath) && IsXamlFile(filePath);
    }

    private static bool TryResolveAbsoluteAvaresTargetPath(
        XamlAnalysisResult analysis,
        string includeSource,
        out string targetPath)
    {
        targetPath = string.Empty;
        if (!Uri.TryCreate(includeSource, UriKind.Absolute, out var absoluteUri) ||
            !absoluteUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TryGetCurrentBuildAssemblyName(analysis, out var currentAssemblyName) &&
            !string.Equals(absoluteUri.Host, currentAssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        targetPath = XamlIncludePathSemantics.NormalizePath(absoluteUri.AbsolutePath.TrimStart('/'));
        return targetPath.Length > 0;
    }

    private static bool LooksLikeXamlUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
        {
            return HasXamlExtension(value);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.IsFile && IsXamlFile(absoluteUri.LocalPath);
        }

        return HasXamlExtension(value);
    }

    private static bool HasXamlExtension(string path)
    {
        return IsXamlFile(path) ||
               path.EndsWith(".xaml\"", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".axaml\"", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".xaml'", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".axaml'", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXamlFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.GetExtension(path), ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsPosition(string text, SourceRange range, SourcePosition position)
    {
        var offset = TextCoordinateHelper.GetOffset(text, position);
        var startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        return offset >= startOffset && offset <= endOffset;
    }

    private static string NormalizeIncludeSource(string includeSource)
    {
        var trimmed = includeSource.AsSpan().Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
        {
            return trimmed.ToString();
        }

        var inner = trimmed.Slice(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return trimmed.ToString();
        }

        var separatorIndex = IndexOfWhitespaceOrComma(inner);
        var markupName = separatorIndex >= 0
            ? inner.Slice(0, separatorIndex)
            : inner;
        if (!markupName.Equals("x:Uri".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToString();
        }

        var arguments = separatorIndex >= 0
            ? inner.Slice(separatorIndex + 1).Trim()
            : ReadOnlySpan<char>.Empty;
        if (arguments.Length == 0)
        {
            return trimmed.ToString();
        }

        var argumentSegment = arguments;
        var commaIndex = argumentSegment.IndexOf(',');
        if (commaIndex >= 0)
        {
            argumentSegment = argumentSegment.Slice(0, commaIndex).Trim();
        }

        var equalsIndex = argumentSegment.IndexOf('=');
        if (equalsIndex > 0)
        {
            var key = argumentSegment.Slice(0, equalsIndex).Trim();
            var value = argumentSegment.Slice(equalsIndex + 1).Trim();
            if (key.Equals("Uri".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Value".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return Unquote(value);
            }

            return trimmed.ToString();
        }

        return Unquote(argumentSegment);
    }

    private static string Unquote(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value.Slice(1, value.Length - 2).ToString();
        }

        return value.ToString();
    }

    private static int IndexOfWhitespaceOrComma(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == ',' || char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool TryGetCurrentBuildAssemblyName(XamlAnalysisResult analysis, out string assemblyName)
    {
        assemblyName = string.Empty;
        if (analysis.ViewModel is null ||
            !Uri.TryCreate(analysis.ViewModel.BuildUri, UriKind.Absolute, out var buildUri) ||
            !buildUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(buildUri.Host))
        {
            return false;
        }

        assemblyName = buildUri.Host;
        return true;
    }
}
