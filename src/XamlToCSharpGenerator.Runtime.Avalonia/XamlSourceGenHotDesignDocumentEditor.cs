using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Runtime;

internal sealed class XamlSourceGenHotDesignDocumentEditor
{
    private readonly string _text;
    private readonly XDocument _document;
    private readonly string _lineEnding;
    private readonly string _indentUnit;

    private XamlSourceGenHotDesignDocumentEditor(string text, XDocument document)
    {
        _text = text;
        _document = document;
        _lineEnding = DetectLineEnding(text);
        _indentUnit = DetectIndentUnit(text);
    }

    public static bool TryCreate(
        string? xamlText,
        out XamlSourceGenHotDesignDocumentEditor? editor,
        out string? error)
    {
        editor = null;
        error = null;

        if (string.IsNullOrWhiteSpace(xamlText))
        {
            error = "Target document has no source text available.";
            return false;
        }

        try
        {
            var document = XDocument.Parse(
                xamlText,
                LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            if (document.Root is null)
            {
                error = "Target document does not have a root element.";
                return false;
            }

            editor = new XamlSourceGenHotDesignDocumentEditor(xamlText, document);
            return true;
        }
        catch (Exception ex)
        {
            error = "Failed to parse target XAML document: " + ex.Message;
            return false;
        }
    }

    public bool TryApplyPropertyUpdate(
        string elementId,
        string propertyName,
        string? propertyValue,
        bool removeProperty,
        out string updatedText,
        out string? error)
    {
        updatedText = _text;
        error = null;

        var targetElement = TryFindElementById(_document.Root!, elementId);
        if (targetElement is null)
        {
            error = "Could not locate element '" + elementId + "' in the document.";
            return false;
        }

        var normalizedPropertyName = propertyName.Trim();
        var attributeName = ResolveAttributeName(targetElement, normalizedPropertyName);
        var existingAttribute = targetElement.Attributes(attributeName).FirstOrDefault();
        if (existingAttribute is null)
        {
            existingAttribute = targetElement.Attributes()
                .FirstOrDefault(attribute =>
                    string.Equals(
                        GetQualifiedAttributeName(targetElement, attribute),
                        normalizedPropertyName,
                        StringComparison.Ordinal));
        }

        if (removeProperty)
        {
            if (existingAttribute is null)
            {
                return true;
            }

            if (!TryCreateAttributeRemovalEdit(existingAttribute, out var removeEdit))
            {
                error = "Could not locate property '" + normalizedPropertyName + "' in the source text.";
                return false;
            }

            updatedText = ApplyEdit(removeEdit);
            return true;
        }

        var serializedValue = EscapeAttributeValue(propertyValue ?? string.Empty, existingAttribute is null
            ? InferAttributeQuoteCharacter(targetElement)
            : GetAttributeQuoteCharacter(existingAttribute));

        if (existingAttribute is not null)
        {
            if (!TryCreateAttributeValueReplaceEdit(existingAttribute, serializedValue, out var replaceEdit))
            {
                error = "Could not locate property '" + normalizedPropertyName + "' in the source text.";
                return false;
            }

            updatedText = ApplyEdit(replaceEdit);
            return true;
        }

        if (!TryCreateAttributeInsertEdit(targetElement, normalizedPropertyName, serializedValue, out var insertEdit))
        {
            error = "Could not insert property '" + normalizedPropertyName + "' into the source text.";
            return false;
        }

        updatedText = ApplyEdit(insertEdit);
        return true;
    }

    public bool TryInsertElement(
        string parentElementId,
        string? elementName,
        string? xamlFragment,
        out string updatedText,
        out string? error)
    {
        updatedText = _text;
        error = null;

        var parentElement = TryFindElementById(_document.Root!, parentElementId);
        if (parentElement is null)
        {
            error = "Could not locate parent element '" + parentElementId + "' for insert.";
            return false;
        }

        var rawMarkup = BuildInsertedMarkup(parentElement, elementName, xamlFragment, out error);
        if (rawMarkup is null)
        {
            return false;
        }

        if (!TryGetElementTagInfo(parentElement, out var parentTag))
        {
            error = "Could not locate parent element '" + parentElementId + "' in the source text.";
            return false;
        }

        if (parentTag.IsSelfClosing)
        {
            var childIndent = DetermineChildIndent(parentElement, parentTag, parentTag.StartTagEnd);
            var formattedMarkup = FormatMarkupForIndent(rawMarkup, childIndent);
            var trailingWhitespaceStart = FindTrailingWhitespaceStartBeforeClose(parentTag.CloseTokenStart);
            var replacement = ">" + _lineEnding + formattedMarkup + _lineEnding + GetElementIndent(parentTag.TagStart) +
                              "</" + parentTag.QualifiedName + ">";
            updatedText = ApplyEdit(new TextEdit(trailingWhitespaceStart, parentTag.StartTagEnd + 1, replacement));
            return true;
        }

        if (!TryFindMatchingClosingTag(parentTag, out var closingTag))
        {
            error = "Could not locate parent closing tag for insert.";
            return false;
        }

        var useMultilineLayout = rawMarkup.IndexOf('\n') >= 0 ||
                                 rawMarkup.IndexOf('\r') >= 0 ||
                                 ContainsLineBreak(parentTag.StartTagEnd + 1, closingTag.Start) ||
                                 IsClosingTagOnOwnLine(closingTag.Start);
        if (!useMultilineLayout)
        {
            updatedText = ApplyEdit(new TextEdit(closingTag.Start, closingTag.Start, rawMarkup));
            return true;
        }

        var childLineIndent = DetermineChildIndent(parentElement, parentTag, closingTag.Start);
        var multilineMarkup = FormatMarkupForIndent(rawMarkup, childLineIndent);
        var insertionStart = IsClosingTagOnOwnLine(closingTag.Start)
            ? FindLineStart(closingTag.Start)
            : closingTag.Start;
        var insertionText = (IsClosingTagOnOwnLine(closingTag.Start) ? string.Empty : _lineEnding) +
                            multilineMarkup +
                            _lineEnding;
        updatedText = ApplyEdit(new TextEdit(insertionStart, insertionStart, insertionText));
        return true;
    }

    public bool TryRemoveElement(
        string elementId,
        out string updatedText,
        out string? error)
    {
        updatedText = _text;
        error = null;

        var element = TryFindElementById(_document.Root!, elementId);
        if (element is null)
        {
            error = "Could not locate element '" + elementId + "' for remove.";
            return false;
        }

        if (string.Equals(elementId, "0", StringComparison.Ordinal))
        {
            error = "Cannot remove the root element.";
            return false;
        }

        if (!TryCreateElementRemovalEdit(element, out var removeEdit))
        {
            error = "Could not locate element '" + elementId + "' in the source text.";
            return false;
        }

        updatedText = ApplyEdit(removeEdit);
        return true;
    }

    public bool TryGetElementRange(
        string elementId,
        out SourceGenHotDesignTextRange? range)
    {
        range = null;

        var element = TryFindElementById(_document.Root!, elementId);
        if (element is null)
        {
            return false;
        }

        if (!TryGetExactElementBounds(element, out var startOffset, out var endOffsetExclusive))
        {
            return false;
        }

        var endOffset = Math.Max(startOffset, endOffsetExclusive - 1);
        GetLinePosition(startOffset, out var startLine, out var startColumn);
        GetLinePosition(endOffset, out var endLine, out var endColumn);
        range = new SourceGenHotDesignTextRange(
            StartLine: startLine,
            StartColumn: startColumn,
            EndLine: endLine,
            EndColumn: endColumn,
            StartOffset: startOffset,
            EndOffset: endOffsetExclusive);
        return true;
    }

    private string? BuildInsertedMarkup(
        XElement parentElement,
        string? elementName,
        string? xamlFragment,
        out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(xamlFragment))
        {
            var trimmedFragment = TrimOuterBlankLines(xamlFragment!);
            try
            {
                _ = XElement.Parse(trimmedFragment, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
                return trimmedFragment;
            }
            catch (Exception ex)
            {
                error = "Could not parse XAML fragment: " + ex.Message;
                return null;
            }
        }

        if (string.IsNullOrWhiteSpace(elementName))
        {
            error = "ElementName or XamlFragment is required for insert.";
            return null;
        }

        return "<" + ResolveElementName(parentElement, elementName.Trim()) + " />";
    }

    private bool TryCreateAttributeValueReplaceEdit(
        XAttribute attribute,
        string serializedValue,
        out TextEdit edit)
    {
        edit = default;
        if (!TryGetAttributeValueBounds(attribute, out var valueStart, out var valueEnd, out _))
        {
            return false;
        }

        edit = new TextEdit(valueStart, valueEnd, serializedValue);
        return true;
    }

    private bool TryCreateAttributeRemovalEdit(XAttribute attribute, out TextEdit edit)
    {
        edit = default;
        if (!TryGetAttributeBounds(attribute, out var nameStart, out _, out _, out var attributeEnd, out _))
        {
            return false;
        }

        var lineStart = FindLineStart(nameStart);
        var lineEnd = FindLineEnd(attributeEnd);
        if (IsWhiteSpaceOnly(lineStart, nameStart) && IsWhiteSpaceOnly(attributeEnd, lineEnd))
        {
            var removeEnd = lineEnd + GetLineBreakLength(lineEnd);
            edit = new TextEdit(lineStart, removeEnd, string.Empty);
            return true;
        }

        var removeStart = nameStart;
        while (removeStart > 0 && IsSameLineWhitespace(_text[removeStart - 1]))
        {
            removeStart--;
        }

        edit = new TextEdit(removeStart, attributeEnd, string.Empty);
        return true;
    }

    private bool TryCreateAttributeInsertEdit(
        XElement element,
        string propertyName,
        string serializedValue,
        out TextEdit edit)
    {
        edit = default;
        if (!TryGetElementTagInfo(element, out var tagInfo))
        {
            return false;
        }

        var quoteCharacter = InferAttributeQuoteCharacter(element);
        var renderedPropertyName = ResolveAttributeNameForInsertion(element, propertyName);
        var renderedAttribute = renderedPropertyName + "=" + quoteCharacter + serializedValue + quoteCharacter;
        var insertionStart = FindTrailingWhitespaceStartBeforeClose(tagInfo.CloseTokenStart);
        if (!ContainsLineBreak(tagInfo.TagStart, tagInfo.StartTagEnd))
        {
            edit = new TextEdit(insertionStart, insertionStart, " " + renderedAttribute);
            return true;
        }

        var attributeIndent = DetermineAttributeIndent(element, tagInfo);
        edit = new TextEdit(insertionStart, insertionStart, _lineEnding + attributeIndent + renderedAttribute);
        return true;
    }

    private bool TryCreateElementRemovalEdit(XElement element, out TextEdit edit)
    {
        edit = default;
        if (!TryGetExactElementBounds(element, out var elementStart, out var elementEnd))
        {
            return false;
        }

        var lineStart = FindLineStart(elementStart);
        var lineEnd = FindLineEnd(elementEnd);
        if (IsWhiteSpaceOnly(lineStart, elementStart) && IsWhiteSpaceOnly(elementEnd, lineEnd))
        {
            var removeEnd = lineEnd + GetLineBreakLength(lineEnd);
            edit = new TextEdit(lineStart, removeEnd, string.Empty);
            return true;
        }

        edit = new TextEdit(elementStart, elementEnd, string.Empty);
        return true;
    }

    private bool TryGetExactElementBounds(XElement element, out int start, out int end)
    {
        start = 0;
        end = 0;
        if (!TryGetElementTagInfo(element, out var tagInfo))
        {
            return false;
        }

        start = tagInfo.TagStart;
        if (tagInfo.IsSelfClosing)
        {
            end = tagInfo.StartTagEnd + 1;
            return true;
        }

        if (!TryFindMatchingClosingTag(tagInfo, out var closingTag))
        {
            return false;
        }

        end = closingTag.End + 1;
        return true;
    }

    private bool TryGetElementTagInfo(XElement element, out ElementTagInfo tagInfo)
    {
        tagInfo = default;
        if (element is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var sourceOffset = GetOffset(lineInfo.LineNumber, lineInfo.LinePosition);
        var tagStart = FindElementTagStart(sourceOffset);
        if (tagStart < 0 || tagStart >= _text.Length)
        {
            return false;
        }

        if (!TryReadOpeningTag(tagStart, out var qualifiedName, out var startTagEnd, out var closeTokenStart, out var isSelfClosing))
        {
            return false;
        }

        tagInfo = new ElementTagInfo(tagStart, startTagEnd, closeTokenStart, qualifiedName, isSelfClosing);
        return true;
    }

    private int FindElementTagStart(int sourceOffset)
    {
        if (sourceOffset < 0 || sourceOffset >= _text.Length)
        {
            return -1;
        }

        var lineStart = FindLineStart(sourceOffset);
        var cursor = sourceOffset;
        while (cursor > lineStart && _text[cursor] != '<')
        {
            cursor--;
        }

        if (_text[cursor] == '<')
        {
            return cursor;
        }

        cursor = sourceOffset;
        while (cursor < _text.Length && char.IsWhiteSpace(_text[cursor]))
        {
            cursor++;
        }

        return cursor < _text.Length && _text[cursor] == '<'
            ? cursor
            : -1;
    }

    private bool TryReadOpeningTag(
        int tagStart,
        out string qualifiedName,
        out int startTagEnd,
        out int closeTokenStart,
        out bool isSelfClosing)
    {
        qualifiedName = string.Empty;
        startTagEnd = -1;
        closeTokenStart = -1;
        isSelfClosing = false;

        if (tagStart < 0 || tagStart >= _text.Length || _text[tagStart] != '<')
        {
            return false;
        }

        var nameStart = tagStart + 1;
        while (nameStart < _text.Length && char.IsWhiteSpace(_text[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < _text.Length &&
               !char.IsWhiteSpace(_text[nameEnd]) &&
               _text[nameEnd] != '>' &&
               _text[nameEnd] != '/')
        {
            nameEnd++;
        }

        if (nameEnd <= nameStart)
        {
            return false;
        }

        qualifiedName = _text.Substring(nameStart, nameEnd - nameStart);
        startTagEnd = FindTagEnd(tagStart);
        if (startTagEnd < 0)
        {
            return false;
        }

        closeTokenStart = startTagEnd;
        while (closeTokenStart > tagStart && char.IsWhiteSpace(_text[closeTokenStart - 1]))
        {
            closeTokenStart--;
        }

        if (closeTokenStart > tagStart && _text[closeTokenStart - 1] == '/')
        {
            closeTokenStart--;
            isSelfClosing = true;
        }

        return true;
    }

    private bool TryFindMatchingClosingTag(ElementTagInfo tagInfo, out ClosingTagInfo closingTag)
    {
        closingTag = default;
        var depth = 1;
        var cursor = tagInfo.StartTagEnd + 1;
        while (cursor < _text.Length)
        {
            var nextTag = _text.IndexOf('<', cursor);
            if (nextTag < 0)
            {
                return false;
            }

            if (StartsWith(nextTag, "<!--"))
            {
                var commentEnd = _text.IndexOf("-->", nextTag, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    return false;
                }

                cursor = commentEnd + 3;
                continue;
            }

            if (StartsWith(nextTag, "<![CDATA["))
            {
                var cdataEnd = _text.IndexOf("]]>", nextTag, StringComparison.Ordinal);
                if (cdataEnd < 0)
                {
                    return false;
                }

                cursor = cdataEnd + 3;
                continue;
            }

            if (StartsWith(nextTag, "<?"))
            {
                var processingEnd = _text.IndexOf("?>", nextTag, StringComparison.Ordinal);
                if (processingEnd < 0)
                {
                    return false;
                }

                cursor = processingEnd + 2;
                continue;
            }

            if (StartsWith(nextTag, "<!"))
            {
                var declarationEnd = _text.IndexOf('>', nextTag + 2);
                if (declarationEnd < 0)
                {
                    return false;
                }

                cursor = declarationEnd + 1;
                continue;
            }

            if (StartsWith(nextTag, "</"))
            {
                if (!TryReadClosingTag(nextTag, out var closingName, out var closingEnd))
                {
                    return false;
                }

                if (string.Equals(closingName, tagInfo.QualifiedName, StringComparison.Ordinal))
                {
                    depth--;
                    if (depth == 0)
                    {
                        closingTag = new ClosingTagInfo(nextTag, closingEnd);
                        return true;
                    }
                }

                cursor = closingEnd + 1;
                continue;
            }

            if (!TryReadOpeningTag(nextTag, out var openingName, out var openingEnd, out _, out var isSelfClosing))
            {
                return false;
            }

            if (!isSelfClosing && string.Equals(openingName, tagInfo.QualifiedName, StringComparison.Ordinal))
            {
                depth++;
            }

            cursor = openingEnd + 1;
        }

        return false;
    }

    private bool TryReadClosingTag(int tagStart, out string qualifiedName, out int tagEnd)
    {
        qualifiedName = string.Empty;
        tagEnd = -1;
        if (tagStart < 0 || tagStart + 1 >= _text.Length || _text[tagStart] != '<' || _text[tagStart + 1] != '/')
        {
            return false;
        }

        var nameStart = tagStart + 2;
        while (nameStart < _text.Length && char.IsWhiteSpace(_text[nameStart]))
        {
            nameStart++;
        }

        var nameEnd = nameStart;
        while (nameEnd < _text.Length &&
               !char.IsWhiteSpace(_text[nameEnd]) &&
               _text[nameEnd] != '>')
        {
            nameEnd++;
        }

        if (nameEnd <= nameStart)
        {
            return false;
        }

        qualifiedName = _text.Substring(nameStart, nameEnd - nameStart);
        tagEnd = _text.IndexOf('>', nameEnd);
        return tagEnd >= 0;
    }

    private static XElement? TryFindElementById(XElement root, string elementId)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            return null;
        }

        var segments = elementId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !string.Equals(segments[0], "0", StringComparison.Ordinal))
        {
            return null;
        }

        var current = root;
        for (var index = 1; index < segments.Length; index++)
        {
            if (!int.TryParse(segments[index], out var childIndex) || childIndex < 0)
            {
                return null;
            }

            var children = current.Elements().ToArray();
            if (childIndex >= children.Length)
            {
                return null;
            }

            current = children[childIndex];
        }

        return current;
    }

    private bool TryGetAttributeBounds(
        XAttribute attribute,
        out int nameStart,
        out int nameEnd,
        out int valueStart,
        out int attributeEnd,
        out char quoteCharacter)
    {
        nameStart = 0;
        nameEnd = 0;
        valueStart = 0;
        attributeEnd = 0;
        quoteCharacter = '"';

        if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
        {
            return false;
        }

        var offset = GetOffset(lineInfo.LineNumber, lineInfo.LinePosition);
        if (offset < 0 || offset >= _text.Length)
        {
            return false;
        }

        nameStart = offset;
        while (nameStart < _text.Length && char.IsWhiteSpace(_text[nameStart]))
        {
            nameStart++;
        }

        nameEnd = nameStart;
        while (nameEnd < _text.Length &&
               !char.IsWhiteSpace(_text[nameEnd]) &&
               _text[nameEnd] != '=' &&
               _text[nameEnd] != '>' &&
               _text[nameEnd] != '/')
        {
            nameEnd++;
        }

        if (nameEnd <= nameStart)
        {
            return false;
        }

        var equalsIndex = _text.IndexOf('=', nameEnd);
        if (equalsIndex < 0)
        {
            return false;
        }

        var quoteIndex = equalsIndex + 1;
        while (quoteIndex < _text.Length && char.IsWhiteSpace(_text[quoteIndex]))
        {
            quoteIndex++;
        }

        if (quoteIndex >= _text.Length || (_text[quoteIndex] != '"' && _text[quoteIndex] != '\''))
        {
            return false;
        }

        quoteCharacter = _text[quoteIndex];
        valueStart = quoteIndex + 1;
        var valueEnd = _text.IndexOf(quoteCharacter, valueStart);
        if (valueEnd < valueStart)
        {
            return false;
        }

        attributeEnd = valueEnd + 1;
        return true;
    }

    private bool TryGetAttributeValueBounds(
        XAttribute attribute,
        out int valueStart,
        out int valueEnd,
        out char quoteCharacter)
    {
        valueStart = 0;
        valueEnd = 0;
        quoteCharacter = '"';
        if (!TryGetAttributeBounds(attribute, out _, out _, out valueStart, out var attributeEnd, out quoteCharacter))
        {
            return false;
        }

        valueEnd = attributeEnd - 1;
        return true;
    }

    private char GetAttributeQuoteCharacter(XAttribute attribute)
    {
        return TryGetAttributeValueBounds(attribute, out _, out _, out var quoteCharacter)
            ? quoteCharacter
            : InferAttributeQuoteCharacter(attribute.Parent);
    }

    private char InferAttributeQuoteCharacter(XElement? element)
    {
        if (element is not null)
        {
            foreach (var attribute in element.Attributes())
            {
                if (TryGetAttributeValueBounds(attribute, out _, out _, out var quoteCharacter))
                {
                    return quoteCharacter;
                }
            }
        }

        return '"';
    }

    private string DetermineAttributeIndent(XElement element, ElementTagInfo tagInfo)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
            {
                continue;
            }

            var attributeOffset = GetOffset(lineInfo.LineNumber, lineInfo.LinePosition);
            var lineStart = FindLineStart(attributeOffset);
            var indent = _text.Substring(lineStart, Math.Max(0, attributeOffset - lineStart));
            if (!string.IsNullOrEmpty(indent) && indent.All(char.IsWhiteSpace))
            {
                return indent;
            }
        }

        return GetElementIndent(tagInfo.TagStart) + _indentUnit;
    }

    private string DetermineChildIndent(XElement parentElement, ElementTagInfo tagInfo, int closingTagStart)
    {
        foreach (var child in parentElement.Elements())
        {
            if (child is not IXmlLineInfo lineInfo || !lineInfo.HasLineInfo())
            {
                continue;
            }

            var childOffset = GetOffset(lineInfo.LineNumber, lineInfo.LinePosition);
            var lineStart = FindLineStart(childOffset);
            var indent = _text.Substring(lineStart, Math.Max(0, childOffset - lineStart));
            if (!string.IsNullOrEmpty(indent) && indent.All(char.IsWhiteSpace))
            {
                return indent;
            }
        }

        if (IsClosingTagOnOwnLine(closingTagStart))
        {
            var closeIndent = _text.Substring(FindLineStart(closingTagStart), closingTagStart - FindLineStart(closingTagStart));
            if (closeIndent.All(char.IsWhiteSpace))
            {
                return closeIndent + _indentUnit;
            }
        }

        return GetElementIndent(tagInfo.TagStart) + _indentUnit;
    }

    private string FormatMarkupForIndent(string rawMarkup, string indent)
    {
        var normalized = NormalizeLineEndings(TrimOuterBlankLines(rawMarkup));
        if (normalized.IndexOf('\n') < 0)
        {
            return indent + normalized;
        }

        var lines = normalized.Split('\n');
        var commonIndent = GetCommonIndent(lines);
        var formattedLines = new string[lines.Length];
        for (var index = 0; index < lines.Length; index++)
        {
            var line = StripIndent(lines[index], commonIndent);
            formattedLines[index] = string.IsNullOrWhiteSpace(line)
                ? string.Empty
                : indent + line;
        }

        return string.Join(_lineEnding, formattedLines);
    }

    private string ApplyEdit(TextEdit edit)
    {
        return _text.Substring(0, edit.Start) + edit.NewText + _text.Substring(edit.End);
    }

    private int GetOffset(int lineNumber, int linePosition)
    {
        var targetLine = Math.Max(0, lineNumber - 1);
        var targetCharacter = Math.Max(0, linePosition - 1);
        var offset = 0;
        var currentLine = 0;
        while (offset < _text.Length && currentLine < targetLine)
        {
            if (_text[offset++] == '\n')
            {
                currentLine++;
            }
        }

        return Math.Min(_text.Length, offset + targetCharacter);
    }

    private void GetLinePosition(int offset, out int lineNumber, out int linePosition)
    {
        var normalizedOffset = Math.Max(0, Math.Min(offset, _text.Length));
        lineNumber = 1;
        linePosition = 1;

        for (var index = 0; index < normalizedOffset; index++)
        {
            var current = _text[index];
            if (current == '\n')
            {
                lineNumber++;
                linePosition = 1;
                continue;
            }

            if (current == '\r')
            {
                if (index + 1 < normalizedOffset && _text[index + 1] == '\n')
                {
                    index++;
                }

                lineNumber++;
                linePosition = 1;
                continue;
            }

            linePosition++;
        }
    }

    private int FindTagEnd(int tagStart)
    {
        var inQuotes = false;
        var quoteCharacter = '\0';
        for (var index = tagStart; index < _text.Length; index++)
        {
            var current = _text[index];
            if (inQuotes)
            {
                if (current == quoteCharacter)
                {
                    inQuotes = false;
                }

                continue;
            }

            if (current is '"' or '\'')
            {
                inQuotes = true;
                quoteCharacter = current;
                continue;
            }

            if (current == '>')
            {
                return index;
            }
        }

        return -1;
    }

    private int FindTrailingWhitespaceStartBeforeClose(int closeTokenStart)
    {
        var cursor = closeTokenStart;
        while (cursor > 0 && char.IsWhiteSpace(_text[cursor - 1]))
        {
            cursor--;
        }

        return cursor;
    }

    private int FindLineStart(int offset)
    {
        var start = Math.Max(0, Math.Min(offset, _text.Length));
        while (start > 0 && _text[start - 1] != '\n' && _text[start - 1] != '\r')
        {
            start--;
        }

        return start;
    }

    private int FindLineEnd(int offset)
    {
        var end = Math.Max(0, Math.Min(offset, _text.Length));
        while (end < _text.Length && _text[end] != '\n' && _text[end] != '\r')
        {
            end++;
        }

        return end;
    }

    private int GetLineBreakLength(int offset)
    {
        if (offset < 0 || offset >= _text.Length)
        {
            return 0;
        }

        if (_text[offset] == '\r')
        {
            return offset + 1 < _text.Length && _text[offset + 1] == '\n' ? 2 : 1;
        }

        return _text[offset] == '\n' ? 1 : 0;
    }

    private bool ContainsLineBreak(int start, int endExclusive)
    {
        var normalizedStart = Math.Max(0, start);
        var normalizedEnd = Math.Min(_text.Length, Math.Max(normalizedStart, endExclusive));
        for (var index = normalizedStart; index < normalizedEnd; index++)
        {
            if (_text[index] == '\n' || _text[index] == '\r')
            {
                return true;
            }
        }

        return false;
    }

    private bool IsClosingTagOnOwnLine(int closingTagStart)
    {
        var lineStart = FindLineStart(closingTagStart);
        return IsWhiteSpaceOnly(lineStart, closingTagStart);
    }

    private bool IsWhiteSpaceOnly(int start, int endExclusive)
    {
        var normalizedStart = Math.Max(0, start);
        var normalizedEnd = Math.Min(_text.Length, Math.Max(normalizedStart, endExclusive));
        for (var index = normalizedStart; index < normalizedEnd; index++)
        {
            if (!char.IsWhiteSpace(_text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSameLineWhitespace(char value)
    {
        return char.IsWhiteSpace(value) && value != '\n' && value != '\r';
    }

    private string GetElementIndent(int tagStart)
    {
        var lineStart = FindLineStart(tagStart);
        var indentLength = Math.Max(0, tagStart - lineStart);
        return indentLength == 0 ? string.Empty : _text.Substring(lineStart, indentLength);
    }

    private string ResolveAttributeNameForInsertion(XElement element, string propertyName)
    {
        if (propertyName.Contains(':', StringComparison.Ordinal))
        {
            return propertyName;
        }

        var attributeName = ResolveAttributeName(element, propertyName);
        if (attributeName.Namespace == XNamespace.None)
        {
            return attributeName.LocalName;
        }

        var prefix = element.GetPrefixOfNamespace(attributeName.Namespace);
        return string.IsNullOrWhiteSpace(prefix)
            ? propertyName
            : prefix + ":" + attributeName.LocalName;
    }

    private static XName ResolveAttributeName(XElement element, string propertyName)
    {
        if (propertyName.Contains(':', StringComparison.Ordinal))
        {
            var parts = propertyName.Split(':', 2);
            var prefix = parts[0];
            var localName = parts[1];
            var ns = element.GetNamespaceOfPrefix(prefix);
            return ns is null ? XName.Get(propertyName) : ns + localName;
        }

        return XName.Get(propertyName);
    }

    private static string ResolveElementName(XElement parent, string elementName)
    {
        if (elementName.Contains(':', StringComparison.Ordinal))
        {
            var parts = elementName.Split(':', 2);
            var prefix = parts[0];
            var localName = parts[1];
            var ns = parent.GetNamespaceOfPrefix(prefix);
            return ns is null ? elementName : prefix + ":" + localName;
        }

        return elementName;
    }

    private static string GetQualifiedAttributeName(XElement element, XAttribute attribute)
    {
        if (attribute.Name.Namespace == XNamespace.None)
        {
            return attribute.Name.LocalName;
        }

        var prefix = element.GetPrefixOfNamespace(attribute.Name.Namespace);
        return string.IsNullOrWhiteSpace(prefix)
            ? attribute.Name.LocalName
            : prefix + ":" + attribute.Name.LocalName;
    }

    private static string EscapeAttributeValue(string value, char quoteCharacter)
    {
        var escaped = value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

        return quoteCharacter == '\''
            ? escaped.Replace("'", "&apos;", StringComparison.Ordinal)
            : escaped.Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string TrimOuterBlankLines(string value)
    {
        var normalized = NormalizeLineEndings(value);
        var lines = normalized.Split('\n');
        var start = 0;
        while (start < lines.Length && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        var end = lines.Length - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        if (start > end)
        {
            return string.Empty;
        }

        return string.Join("\n", lines[start..(end + 1)]);
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
    }

    private static string DetectLineEnding(string value)
    {
        return value.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static string DetectIndentUnit(string value)
    {
        var normalized = NormalizeLineEndings(value);
        var lines = normalized.Split('\n');
        string? bestIndent = null;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indentLength = 0;
            while (indentLength < line.Length && (line[indentLength] == ' ' || line[indentLength] == '\t'))
            {
                indentLength++;
            }

            if (indentLength > 0)
            {
                var indent = line.Substring(0, indentLength);
                if (bestIndent is null || indent.Length < bestIndent.Length)
                {
                    bestIndent = indent;
                }
            }
        }

        if (string.IsNullOrEmpty(bestIndent))
        {
            return "  ";
        }

        return bestIndent;
    }

    private static int GetCommonIndent(IReadOnlyList<string> lines)
    {
        var commonIndent = int.MaxValue;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var indent = 0;
            while (indent < line.Length && char.IsWhiteSpace(line[indent]) && line[indent] != '\n' && line[indent] != '\r')
            {
                indent++;
            }

            commonIndent = Math.Min(commonIndent, indent);
        }

        return commonIndent == int.MaxValue ? 0 : commonIndent;
    }

    private static string StripIndent(string line, int commonIndent)
    {
        if (commonIndent <= 0 || string.IsNullOrEmpty(line))
        {
            return line;
        }

        var removable = Math.Min(commonIndent, line.TakeWhile(static character => character == ' ' || character == '\t').Count());
        return removable == 0 ? line : line.Substring(removable);
    }

    private bool StartsWith(int startIndex, string value)
    {
        return startIndex >= 0 &&
               startIndex + value.Length <= _text.Length &&
               string.Compare(_text, startIndex, value, 0, value.Length, StringComparison.Ordinal) == 0;
    }

    private readonly record struct TextEdit(int Start, int End, string NewText);

    private readonly record struct ElementTagInfo(
        int TagStart,
        int StartTagEnd,
        int CloseTokenStart,
        string QualifiedName,
        bool IsSelfClosing);

    private readonly record struct ClosingTagInfo(int Start, int End);
}
