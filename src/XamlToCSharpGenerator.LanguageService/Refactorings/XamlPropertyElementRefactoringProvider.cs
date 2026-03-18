using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlPropertyElementRefactoringProvider : IXamlRefactoringProvider
{
    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlPropertyElementRefactoringProvider(
        XamlDocumentStore documentStore,
        XamlCompilerAnalysisService analysisService)
    {
        _documentStore = documentStore;
        _analysisService = analysisService;
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
                    IncludeSemanticDiagnostics = false
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (analysis.XmlDocument is null ||
            analysis.TypeIndex is null ||
            !XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                document.Text,
                analysis.XmlDocument,
                context.Position,
                out var element,
                out var attribute,
                out var attributeNameRange,
                out var attributeValueRange))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        string rawAttributeName = ReadRangeText(document.Text, attributeNameRange);
        if (!IsSupportedAttribute(attribute, rawAttributeName) ||
            !XamlXmlSourceRangeService.TryCreateElementNameRange(document.Text, element, out var elementNameRange))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        string rawElementName = ReadRangeText(document.Text, elementNameRange);
        if (!XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex,
                analysis.PrefixMap,
                rawElementName,
                rawAttributeName,
                out _,
                out _))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        string propertyElementName = rawAttributeName.Contains('.', StringComparison.Ordinal)
            ? rawAttributeName
            : rawElementName + "." + rawAttributeName;
        if (!TryCreateConversionEdit(
                document.Text,
                element,
                rawElementName,
                propertyElementName,
                attribute,
                attributeValueRange,
                out var edit))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(
                document.Uri,
                ImmutableArray.Create(edit)));

        return ImmutableArray.Create(
            new XamlRefactoringAction(
                Title: "AXSG: Convert attribute to property element",
                Kind: "refactor.rewrite",
                IsPreferred: false,
                Edit: workspaceEdit,
                Command: null));
    }

    private static bool IsSupportedAttribute(XAttribute attribute, string rawAttributeName)
    {
        if (attribute.IsNamespaceDeclaration ||
            string.IsNullOrWhiteSpace(rawAttributeName) ||
            rawAttributeName.StartsWith("xmlns", StringComparison.Ordinal))
        {
            return false;
        }

        int colonIndex = rawAttributeName.IndexOf(':');
        int dotIndex = rawAttributeName.IndexOf('.');
        return colonIndex < 0 || (dotIndex >= 0 && colonIndex < dotIndex);
    }

    private static bool TryCreateConversionEdit(
        string sourceText,
        XElement element,
        string rawElementName,
        string propertyElementName,
        XAttribute attribute,
        SourceRange attributeValueRange,
        out XamlDocumentTextEdit edit)
    {
        edit = default!;
        if (!XamlXmlSourceRangeService.TryCreateElementRange(sourceText, element, out var elementRange) ||
            !XamlXmlSourceRangeService.TryCreateAttributeRange(sourceText, attribute, out var attributeRange))
        {
            return false;
        }

        int elementStartOffset = TextCoordinateHelper.GetOffset(sourceText, elementRange.Start);
        int elementEndOffset = TextCoordinateHelper.GetOffset(sourceText, elementRange.End);
        int attributeStartOffset = TextCoordinateHelper.GetOffset(sourceText, attributeRange.Start);
        int attributeEndOffset = TextCoordinateHelper.GetOffset(sourceText, attributeRange.End);
        if (elementStartOffset < 0 ||
            elementEndOffset <= elementStartOffset ||
            attributeStartOffset < elementStartOffset ||
            attributeEndOffset <= attributeStartOffset)
        {
            return false;
        }

        string originalElementText = sourceText.Substring(elementStartOffset, elementEndOffset - elementStartOffset);
        if (!TryFindStartTagEnd(originalElementText, out var startTagEnd))
        {
            return false;
        }

        string startTagText = originalElementText.Substring(0, startTagEnd + 1);
        int relativeAttributeStart = attributeStartOffset - elementStartOffset;
        int relativeAttributeEnd = attributeEndOffset - elementStartOffset;
        if (relativeAttributeEnd > startTagText.Length)
        {
            return false;
        }

        string updatedStartTagText = RemoveAttributeSegment(startTagText, relativeAttributeStart, relativeAttributeEnd);
        bool isSelfClosing = IsSelfClosingTag(startTagText);
        if (isSelfClosing)
        {
            updatedStartTagText = ExpandSelfClosingTag(updatedStartTagText);
        }

        string newline = DetectNewLine(sourceText);
        string baseIndent = GetLineIndent(sourceText, elementStartOffset);
        string indentUnit = DetectIndentUnit(originalElementText, startTagEnd, baseIndent);
        string propertyIndent = baseIndent + indentUnit;
        string rawValue = ReadRangeText(sourceText, attributeValueRange);
        string propertyElementText = propertyIndent + "<" + propertyElementName + ">" + rawValue + "</" + propertyElementName + ">";

        string rewrittenElementText = RewriteElementText(
            originalElementText,
            updatedStartTagText,
            startTagEnd,
            isSelfClosing,
            rawElementName,
            baseIndent,
            newline,
            propertyElementText);
        if (string.IsNullOrEmpty(rewrittenElementText))
        {
            return false;
        }

        edit = new XamlDocumentTextEdit(elementRange, rewrittenElementText);
        return true;
    }

    private static string RewriteElementText(
        string originalElementText,
        string updatedStartTagText,
        int startTagEnd,
        bool isSelfClosing,
        string rawElementName,
        string baseIndent,
        string newline,
        string propertyElementText)
    {
        if (isSelfClosing)
        {
            return updatedStartTagText +
                   newline +
                   propertyElementText +
                   newline +
                   baseIndent +
                   "</" + rawElementName + ">";
        }

        int closingTagStart = originalElementText.LastIndexOf("</", StringComparison.Ordinal);
        if (closingTagStart < 0 || closingTagStart < startTagEnd)
        {
            return string.Empty;
        }

        string innerContent = originalElementText.Substring(startTagEnd + 1, closingTagStart - startTagEnd - 1);
        string closingTagText = originalElementText.Substring(closingTagStart);
        if (string.IsNullOrWhiteSpace(innerContent))
        {
            return updatedStartTagText +
                   newline +
                   propertyElementText +
                   newline +
                   baseIndent +
                   closingTagText;
        }

        if (StartsWithLineBreak(innerContent))
        {
            return updatedStartTagText +
                   newline +
                   propertyElementText +
                   innerContent +
                   closingTagText;
        }

        return updatedStartTagText +
               newline +
               propertyElementText +
               newline +
               innerContent +
               closingTagText;
    }

    private static string ReadRangeText(string text, SourceRange range)
    {
        int startOffset = TextCoordinateHelper.GetOffset(text, range.Start);
        int endOffset = TextCoordinateHelper.GetOffset(text, range.End);
        if (startOffset < 0 ||
            endOffset < startOffset ||
            endOffset > text.Length)
        {
            return string.Empty;
        }

        return text.Substring(startOffset, endOffset - startOffset);
    }

    private static bool TryFindStartTagEnd(string text, out int index)
    {
        index = -1;
        bool inQuotes = false;
        char quote = '\0';
        for (int currentIndex = 0; currentIndex < text.Length; currentIndex++)
        {
            char current = text[currentIndex];
            if (inQuotes)
            {
                if (current == quote)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                inQuotes = true;
                quote = current;
                continue;
            }

            if (current == '>')
            {
                index = currentIndex;
                return true;
            }
        }

        return false;
    }

    private static bool IsSelfClosingTag(string startTagText)
    {
        int closeIndex = startTagText.LastIndexOf('>');
        if (closeIndex <= 0)
        {
            return false;
        }

        for (int index = closeIndex - 1; index >= 0; index--)
        {
            char current = startTagText[index];
            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            return current == '/';
        }

        return false;
    }

    private static string ExpandSelfClosingTag(string startTagText)
    {
        int closeIndex = startTagText.LastIndexOf('>');
        if (closeIndex <= 0)
        {
            return startTagText;
        }

        int slashIndex = closeIndex - 1;
        while (slashIndex >= 0 && char.IsWhiteSpace(startTagText[slashIndex]))
        {
            slashIndex--;
        }

        if (slashIndex < 0 || startTagText[slashIndex] != '/')
        {
            return startTagText;
        }

        string expandedStartTag = startTagText.Remove(slashIndex, 1);
        closeIndex = expandedStartTag.LastIndexOf('>');
        if (closeIndex <= 0)
        {
            return expandedStartTag;
        }

        int whitespaceStart = closeIndex;
        while (whitespaceStart > 0 && char.IsWhiteSpace(expandedStartTag[whitespaceStart - 1]))
        {
            whitespaceStart--;
        }

        return whitespaceStart < closeIndex
            ? expandedStartTag.Remove(whitespaceStart, closeIndex - whitespaceStart)
            : expandedStartTag;
    }

    private static string RemoveAttributeSegment(string startTagText, int attributeStart, int attributeEnd)
    {
        int removalStart = attributeStart;
        while (removalStart > 0 && char.IsWhiteSpace(startTagText[removalStart - 1]))
        {
            removalStart--;
            if (startTagText[removalStart] == '\n')
            {
                if (removalStart > 0 && startTagText[removalStart - 1] == '\r')
                {
                    removalStart--;
                }

                break;
            }

            if (startTagText[removalStart] == '\r')
            {
                break;
            }
        }

        return startTagText.Remove(removalStart, attributeEnd - removalStart);
    }

    private static string DetectNewLine(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static string GetLineIndent(string text, int offset)
    {
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        int indentEnd = lineStart;
        while (indentEnd < text.Length && (text[indentEnd] == ' ' || text[indentEnd] == '\t'))
        {
            indentEnd++;
        }

        return text.Substring(lineStart, indentEnd - lineStart);
    }

    private static string DetectIndentUnit(string originalElementText, int startTagEnd, string baseIndent)
    {
        if (TryFindIndentUnit(originalElementText.Substring(0, startTagEnd + 1), baseIndent, out var indentUnit))
        {
            return indentUnit;
        }

        if (startTagEnd + 1 < originalElementText.Length &&
            TryFindIndentUnit(originalElementText.Substring(startTagEnd + 1), baseIndent, out indentUnit))
        {
            return indentUnit;
        }

        return "  ";
    }

    private static bool TryFindIndentUnit(string text, string baseIndent, out string indentUnit)
    {
        indentUnit = string.Empty;
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not '\r' and not '\n')
            {
                continue;
            }

            int lineStart = index + 1;
            if (text[index] == '\r' && lineStart < text.Length && text[lineStart] == '\n')
            {
                lineStart++;
            }

            int current = lineStart;
            while (current < text.Length && (text[current] == ' ' || text[current] == '\t'))
            {
                current++;
            }

            if (current == lineStart)
            {
                continue;
            }

            string indent = text.Substring(lineStart, current - lineStart);
            if (indent.Length <= baseIndent.Length ||
                !indent.StartsWith(baseIndent, StringComparison.Ordinal))
            {
                continue;
            }

            indentUnit = indent.Substring(baseIndent.Length);
            return !string.IsNullOrEmpty(indentUnit);
        }

        return false;
    }

    private static bool StartsWithLineBreak(string text)
    {
        return text.StartsWith("\r\n", StringComparison.Ordinal) ||
               text.StartsWith("\n", StringComparison.Ordinal) ||
               text.StartsWith("\r", StringComparison.Ordinal);
    }
}
