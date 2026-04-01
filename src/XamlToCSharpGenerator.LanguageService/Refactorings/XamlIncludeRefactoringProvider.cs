using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlIncludeRefactoringProvider : IXamlRefactoringProvider
{
    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlIncludeRefactoringProvider(
        XamlDocumentStore documentStore,
        XamlCompilerAnalysisService analysisService,
        XamlLanguageFrameworkRegistry frameworkRegistry)
    {
        _documentStore = documentStore;
        _analysisService = analysisService;
        _ = frameworkRegistry ?? throw new ArgumentNullException(nameof(frameworkRegistry));
    }

    public async Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        XamlRefactoringContext context,
        CancellationToken cancellationToken)
    {
        LanguageServiceDocument? document = await XamlRefactoringDocumentResolver
            .ResolveDocumentAsync(_documentStore, context, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlAnalysisResult analysis = await _analysisService
            .AnalyzeAsync(
                document,
                context.Options with
                {
                    IncludeCompilationDiagnostics = false,
                    IncludeSemanticDiagnostics = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (analysis.XmlDocument?.Root is null ||
            !TryFindIncludeElementContext(
                document.Text,
                analysis.XmlDocument,
                context.Position,
                out XElement includeElement,
                out SourceRange includeRange))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlRefactoringAction>();
        if (TryBuildRemoveInvalidIncludeAction(
                document,
                analysis,
                includeRange,
                out XamlRefactoringAction removeAction))
        {
            builder.Add(removeAction);
        }

        XAttribute? sourceAttribute = includeElement.Attributes().FirstOrDefault(static item =>
            string.Equals(item.Name.LocalName, "Source", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(analysis.ProjectPath) &&
            sourceAttribute is not null &&
            TryBuildAddProjectIncludeAction(document, analysis, sourceAttribute.Value, out XamlRefactoringAction resolvedAction))
        {
            builder.Add(resolvedAction);
        }

        return builder.ToImmutable();
    }

    private static bool TryBuildAddProjectIncludeAction(
        LanguageServiceDocument document,
        XamlAnalysisResult analysis,
        string sourceValue,
        out XamlRefactoringAction action)
    {
        action = default!;
        string projectPath = analysis.ProjectPath!;
        bool resolvedIncludeFile = TryResolveProjectLocalIncludeFilePath(analysis, sourceValue, out string targetFilePath);
        bool includeFileExists = resolvedIncludeFile && File.Exists(targetFilePath);
        bool projectExists = File.Exists(projectPath);
        bool alreadyExplicitlyIncluded = includeFileExists &&
            projectExists &&
            XamlProjectFileDiscoveryService.TryResolveExplicitProjectXamlEntryByFilePath(
                projectPath,
                document.FilePath,
                targetFilePath,
                analysis.FrameworkRegistry,
                out _);
        if (!resolvedIncludeFile ||
            !includeFileExists ||
            !projectExists ||
            alreadyExplicitlyIncluded)
        {
            return false;
        }

        string projectText = File.ReadAllText(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return false;
        }

        string includePath = Path.GetRelativePath(projectDirectory, targetFilePath)
            .Replace('\\', '/');
        bool hasIncludePath = !string.IsNullOrWhiteSpace(includePath);
        bool alreadyIncludesPath = hasIncludePath && ProjectAlreadyIncludesPath(projectText, includePath, analysis.FrameworkRegistry);
        XamlDocumentTextEdit edit = default!;
        bool createdEdit = hasIncludePath &&
            !alreadyIncludesPath &&
            TryCreateProjectFileInsertionEdit(
                projectText,
                includePath,
                analysis.Framework.PreferredProjectXamlItemName,
                out edit);
        if (!hasIncludePath ||
            alreadyIncludesPath ||
            !createdEdit)
        {
            return false;
        }

        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(
                UriPathHelper.ToDocumentUri(projectPath),
                ImmutableArray.Create(edit)));

        action = new XamlRefactoringAction(
            Title: "AXSG: Add included XAML file to project",
            Kind: "quickfix",
            IsPreferred: true,
            Edit: workspaceEdit,
            Command: null);
        return true;
    }

    private static bool TryBuildRemoveInvalidIncludeAction(
        LanguageServiceDocument document,
        XamlAnalysisResult analysis,
        SourceRange includeRange,
        out XamlRefactoringAction action)
    {
        action = default!;
        if (!HasIncludeRemovalDiagnostic(analysis, includeRange))
        {
            return false;
        }

        SourceRange removalRange = CreateIncludeRemovalRange(document.Text, includeRange);
        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(
                document.Uri,
                ImmutableArray.Create(new XamlDocumentTextEdit(removalRange, string.Empty))));

        action = new XamlRefactoringAction(
            Title: "AXSG: Remove invalid include",
            Kind: "quickfix",
            IsPreferred: false,
            Edit: workspaceEdit,
            Command: null);
        return true;
    }

    private static bool HasIncludeRemovalDiagnostic(
        XamlAnalysisResult analysis,
        SourceRange includeRange)
    {
        return analysis.Diagnostics.Any(diagnostic =>
            IsIncludeRemovalDiagnosticCode(diagnostic.Code) &&
            IntersectsRange(diagnostic.Range.Start, includeRange));
    }

    private static bool IsIncludeRemovalDiagnosticCode(string? code)
    {
        return string.Equals(code, "AXSG0400", StringComparison.Ordinal) ||
               string.Equals(code, "AXSG0401", StringComparison.Ordinal) ||
               string.Equals(code, "AXSG0402", StringComparison.Ordinal) ||
               string.Equals(code, "AXSG0404", StringComparison.Ordinal);
    }

    private static bool IntersectsRange(SourcePosition position, SourceRange range)
    {
        if (position.Line < range.Start.Line || position.Line > range.End.Line)
        {
            return false;
        }

        if (position.Line == range.Start.Line && position.Character < range.Start.Character)
        {
            return false;
        }

        if (position.Line == range.End.Line && position.Character > range.End.Character)
        {
            return false;
        }

        return true;
    }

    private static SourceRange CreateIncludeRemovalRange(string sourceText, SourceRange includeRange)
    {
        int startOffset = TextCoordinateHelper.GetOffset(sourceText, includeRange.Start);
        int endOffset = TextCoordinateHelper.GetOffset(sourceText, includeRange.End);

        int lineStartOffset = startOffset;
        while (lineStartOffset > 0 && sourceText[lineStartOffset - 1] != '\n')
        {
            lineStartOffset--;
        }

        bool onlyWhitespaceBefore = IsWhitespaceOnly(sourceText.AsSpan(lineStartOffset, startOffset - lineStartOffset));

        int lineEndOffset = endOffset;
        while (lineEndOffset < sourceText.Length &&
               sourceText[lineEndOffset] != '\n' &&
               sourceText[lineEndOffset] != '\r')
        {
            lineEndOffset++;
        }

        bool onlyWhitespaceAfter = IsWhitespaceOnly(sourceText.AsSpan(endOffset, lineEndOffset - endOffset));

        int removalStart = startOffset;
        int removalEnd = endOffset;
        if (onlyWhitespaceBefore && onlyWhitespaceAfter)
        {
            removalStart = lineStartOffset;
            removalEnd = lineEndOffset;
            if (removalEnd < sourceText.Length)
            {
                if (sourceText[removalEnd] == '\r' &&
                    removalEnd + 1 < sourceText.Length &&
                    sourceText[removalEnd + 1] == '\n')
                {
                    removalEnd += 2;
                }
                else
                {
                    removalEnd++;
                }
            }
            else if (removalStart > 0)
            {
                int previousLineBreakOffset = removalStart - 1;
                if (sourceText[previousLineBreakOffset] == '\n')
                {
                    removalStart = previousLineBreakOffset;
                    if (removalStart > 0 && sourceText[removalStart - 1] == '\r')
                    {
                        removalStart--;
                    }
                }
            }
        }

        return new SourceRange(
            TextCoordinateHelper.GetPosition(sourceText, removalStart),
            TextCoordinateHelper.GetPosition(sourceText, removalEnd));
    }

    private static bool IsWhitespaceOnly(ReadOnlySpan<char> value)
    {
        foreach (char character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindIncludeElementContext(
        string sourceText,
        XDocument xmlDocument,
        SourcePosition position,
        out XElement includeElement,
        out SourceRange includeRange)
    {
        includeElement = null!;
        includeRange = default;

        if (XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                sourceText,
                xmlDocument,
                position,
                out XElement attributedElement,
                out _,
                out _,
                out _) &&
            IsIncludeElement(attributedElement) &&
            XamlXmlSourceRangeService.TryCreateElementRange(sourceText, attributedElement, out includeRange))
        {
            includeElement = attributedElement;
            return true;
        }

        if (XamlXmlSourceRangeService.TryFindElementNameAtPosition(
                sourceText,
                xmlDocument,
                position,
                out XElement namedElement,
                out _) &&
            IsIncludeElement(namedElement) &&
            XamlXmlSourceRangeService.TryCreateElementRange(sourceText, namedElement, out includeRange))
        {
            includeElement = namedElement;
            return true;
        }

        if (XamlXmlSourceRangeService.TryFindInnermostElementAtPosition(
                sourceText,
                xmlDocument,
                position,
                out XElement candidateElement,
                out SourceRange candidateRange) &&
            IsIncludeElement(candidateElement))
        {
            includeElement = candidateElement;
            includeRange = candidateRange;
            return true;
        }

        return false;
    }

    private static bool TryResolveProjectLocalIncludeFilePath(
        XamlAnalysisResult analysis,
        string sourceValue,
        out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(analysis.ProjectPath) ||
            string.IsNullOrWhiteSpace(analysis.Document.FilePath))
        {
            return false;
        }

        string normalizedSource = NormalizeIncludeSource(sourceValue);
        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            return false;
        }

        string projectDirectory = Path.GetDirectoryName(analysis.ProjectPath) ?? string.Empty;
        string documentDirectory = Path.GetDirectoryName(analysis.Document.FilePath) ?? projectDirectory;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return false;
        }

        if (normalizedSource.StartsWith("/", StringComparison.Ordinal))
        {
            string rootedRelativePath = normalizedSource.TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);
            if (!LooksLikeXamlFile(rootedRelativePath))
            {
                return false;
            }

            filePath = Path.GetFullPath(Path.Combine(projectDirectory, rootedRelativePath));
            return true;
        }

        if (Uri.TryCreate(normalizedSource, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (absoluteUri.IsFile)
            {
                string localFilePath = UriPathHelper.ToFilePath(absoluteUri.ToString());
                if (!LooksLikeXamlFile(localFilePath))
                {
                    return false;
                }

                filePath = Path.GetFullPath(localFilePath);
                return true;
            }

            if (TryResolvePackTargetPath(analysis, normalizedSource, projectDirectory, out filePath) ||
                TryResolveMsAppxTargetPath(normalizedSource, projectDirectory, out filePath))
            {
                return true;
            }

            if (!analysis.Framework.SupportsAssemblyResourceUris ||
                !absoluteUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(absoluteUri.Host, analysis.Compilation?.AssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string rootedTargetPath = absoluteUri.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            if (!LooksLikeXamlFile(rootedTargetPath))
            {
                return false;
            }

            filePath = Path.GetFullPath(Path.Combine(projectDirectory, rootedTargetPath));
            return true;
        }
        string relativePath = Path.Combine(documentDirectory, normalizedSource.Replace('/', Path.DirectorySeparatorChar));
        if (!LooksLikeXamlFile(relativePath))
        {
            return false;
        }

        filePath = Path.GetFullPath(relativePath);
        return true;
    }

    private static bool TryResolvePackTargetPath(
        XamlAnalysisResult analysis,
        string normalizedSource,
        string projectDirectory,
        out string filePath)
    {
        const string applicationPrefix = "pack://application:,,,/";

        filePath = string.Empty;
        if (!normalizedSource.StartsWith(applicationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = normalizedSource.Substring(applicationPrefix.Length).Trim();
        var componentSeparator = payload.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
        if (componentSeparator >= 0)
        {
            var assemblyName = payload.Substring(0, componentSeparator).Trim();
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                !string.Equals(assemblyName, analysis.Compilation?.AssemblyName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            payload = payload.Substring(componentSeparator + ";component/".Length);
        }

        if (!LooksLikeXamlFile(payload))
        {
            return false;
        }

        filePath = Path.GetFullPath(Path.Combine(projectDirectory, payload.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        return true;
    }

    private static bool TryResolveMsAppxTargetPath(
        string normalizedSource,
        string projectDirectory,
        out string filePath)
    {
        filePath = string.Empty;
        if (!Uri.TryCreate(normalizedSource, UriKind.Absolute, out var absoluteUri) ||
            !absoluteUri.Scheme.Equals("ms-appx", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = absoluteUri.AbsolutePath.TrimStart('/');
        if (!LooksLikeXamlFile(payload))
        {
            return false;
        }

        filePath = Path.GetFullPath(Path.Combine(projectDirectory, payload.Replace('/', Path.DirectorySeparatorChar)));
        return true;
    }

    private static bool LooksLikeXamlFile(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ProjectAlreadyIncludesPath(
        string projectText,
        string includePath,
        XamlLanguageFrameworkRegistry frameworkRegistry)
    {
        try
        {
            XDocument document = XDocument.Parse(projectText, LoadOptions.PreserveWhitespace);
            return document
                .Descendants()
                .Where(element => frameworkRegistry.IsKnownProjectXamlItemName(element.Name.LocalName))
                .Attributes("Include")
                .Any(attribute => string.Equals(
                    NormalizeProjectIncludePath(attribute.Value),
                    NormalizeProjectIncludePath(includePath),
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return projectText.IndexOf($"Include=\"{includePath}\"", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    private static string NormalizeProjectIncludePath(string value)
    {
        return value
            .Replace('\\', '/')
            .Trim();
    }

    private static bool TryCreateProjectFileInsertionEdit(
        string projectText,
        string includePath,
        string projectItemName,
        out XamlDocumentTextEdit edit)
    {
        edit = default!;
        int projectClosingTagOffset = projectText.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
        if (projectClosingTagOffset < 0)
        {
            return false;
        }

        string newline = projectText.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string escapedIncludePath = SecurityElement.Escape(includePath) ?? includePath;
        string insertionText =
            $"  <ItemGroup>{newline}" +
            $"    <{projectItemName} Include=\"{escapedIncludePath}\" />{newline}" +
            $"  </ItemGroup>{newline}";

        SourcePosition insertionPosition = TextCoordinateHelper.GetPosition(projectText, projectClosingTagOffset);
        edit = new XamlDocumentTextEdit(
            new SourceRange(insertionPosition, insertionPosition),
            insertionText);
        return true;
    }

    private static bool IsIncludeElement(XElement element)
    {
        string localName = element.Name.LocalName;
        return string.Equals(localName, "ResourceInclude", StringComparison.Ordinal) ||
               string.Equals(localName, "StyleInclude", StringComparison.Ordinal) ||
               string.Equals(localName, "MergeResourceInclude", StringComparison.Ordinal) ||
               (string.Equals(localName, "ResourceDictionary", StringComparison.Ordinal) &&
                element.Attributes().Any(static attribute => string.Equals(attribute.Name.LocalName, "Source", StringComparison.Ordinal)));
    }

    private static string NormalizeIncludeSource(string includeSource)
    {
        ReadOnlySpan<char> trimmed = includeSource.AsSpan().Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return trimmed.ToString();
        }

        ReadOnlySpan<char> inner = trimmed.Slice(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return trimmed.ToString();
        }

        int separatorIndex = IndexOfWhitespaceOrComma(inner);
        ReadOnlySpan<char> markupName = separatorIndex >= 0
            ? inner.Slice(0, separatorIndex)
            : inner;
        if (!markupName.Equals("x:Uri".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return trimmed.ToString();
        }

        ReadOnlySpan<char> arguments = separatorIndex >= 0
            ? inner.Slice(separatorIndex + 1).Trim()
            : ReadOnlySpan<char>.Empty;
        if (arguments.Length == 0)
        {
            return trimmed.ToString();
        }

        ReadOnlySpan<char> argumentSegment = arguments;
        int commaIndex = argumentSegment.IndexOf(',');
        if (commaIndex >= 0)
        {
            argumentSegment = argumentSegment.Slice(0, commaIndex).Trim();
        }

        int equalsIndex = argumentSegment.IndexOf('=');
        if (equalsIndex > 0)
        {
            ReadOnlySpan<char> key = argumentSegment.Slice(0, equalsIndex).Trim();
            ReadOnlySpan<char> value = argumentSegment.Slice(equalsIndex + 1).Trim();
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
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == ',' || char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }
}
