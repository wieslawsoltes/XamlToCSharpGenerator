using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlNamespaceImportRefactoringProvider : IXamlRefactoringProvider
{
    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;
    private readonly XamlNamespacePrefixSuggestionService _prefixSuggestionService;

    public XamlNamespaceImportRefactoringProvider(
        XamlDocumentStore documentStore,
        XamlCompilerAnalysisService analysisService,
        XamlNamespacePrefixSuggestionService prefixSuggestionService)
    {
        _documentStore = documentStore;
        _analysisService = analysisService;
        _prefixSuggestionService = prefixSuggestionService;
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
            analysis.TypeIndex is null)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        var actions = ImmutableArray.CreateBuilder<XamlRefactoringAction>();

        if (TryCreateElementImportAction(document.Text, analysis, context.Position, out var elementAction))
        {
            actions.Add(elementAction);
        }

        if (TryCreatePropertyOwnerImportAction(document.Text, analysis, context.Position, out var propertyAction))
        {
            if (!actions.Any(existing => string.Equals(existing.Title, propertyAction.Title, StringComparison.Ordinal)))
            {
                actions.Add(propertyAction);
            }
        }

        if (TryCreateSetterPropertyValueImportAction(document.Text, analysis, context.Position, out var setterPropertyAction))
        {
            if (!actions.Any(existing => string.Equals(existing.Title, setterPropertyAction.Title, StringComparison.Ordinal)))
            {
                actions.Add(setterPropertyAction);
            }
        }

        if (TryCreateTypeReferenceValueImportAction(document.Text, analysis, context.Position, out var typeReferenceAction))
        {
            if (!actions.Any(existing => string.Equals(existing.Title, typeReferenceAction.Title, StringComparison.Ordinal)))
            {
                actions.Add(typeReferenceAction);
            }
        }

        return actions.ToImmutable();
    }

    private bool TryCreateElementImportAction(
        string sourceText,
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlRefactoringAction action)
    {
        action = default!;

        if (!XamlXmlSourceRangeService.TryFindElementNameAtPosition(
                sourceText,
                analysis.XmlDocument,
                position,
                out var element,
                out var elementNameRange))
        {
            return false;
        }

        var rawElementName = ReadRangeText(sourceText, elementNameRange);
        NamespaceImportTarget importTarget;
        string replacementText;
        if (TrySplitOwnerQualifiedPropertyToken(rawElementName, out var ownerToken, out var memberName))
        {
            if (XamlClrSymbolResolver.TryResolvePropertyInfo(
                    analysis.TypeIndex!,
                    XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element),
                    currentElementName: null,
                    rawElementName,
                    out _,
                    out _))
            {
                return false;
            }

            if (!TryResolveImportTarget(analysis, element, ownerToken, out importTarget))
            {
                return false;
            }

            replacementText = importTarget.QualifiedName + "." + memberName;
        }
        else
        {
            if (!IsUnresolvedElementType(analysis, element, rawElementName) ||
                !TryResolveImportTarget(analysis, element, rawElementName, out importTarget))
            {
                return false;
            }

            replacementText = importTarget.QualifiedName;
        }

        var edits = BuildElementEdits(
            sourceText,
            analysis.XmlDocument!.Root!,
            element,
            elementNameRange,
            replacementText,
            importTarget);
        if (edits.IsDefaultOrEmpty)
        {
            return false;
        }

        action = CreateImportAction(analysis.Document.Uri, importTarget, edits);
        return true;
    }

    private bool TryCreatePropertyOwnerImportAction(
        string sourceText,
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlRefactoringAction action)
    {
        action = default!;

        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                sourceText,
                analysis.XmlDocument,
                position,
                out var element,
                out var attribute,
                out var attributeNameRange,
                out _))
        {
            return false;
        }

        var rawAttributeName = ReadRangeText(sourceText, attributeNameRange);
        if (attribute.IsNamespaceDeclaration ||
            rawAttributeName.StartsWith("xmlns", StringComparison.Ordinal) ||
            !TrySplitOwnerQualifiedPropertyToken(rawAttributeName, out var ownerToken, out var memberName))
        {
            return false;
        }

        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        if (XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                prefixMap,
                currentElementName: null,
                rawAttributeName,
                out _,
                out _))
        {
            return false;
        }

        if (!TryResolveImportTarget(analysis, element, ownerToken, out var importTarget))
        {
            return false;
        }

        var replacementText = importTarget.QualifiedName + "." + memberName;
        var edits = BuildAttributeEdits(
            sourceText,
            analysis.XmlDocument!.Root!,
            attributeNameRange,
            replacementText,
            importTarget);
        if (edits.IsDefaultOrEmpty)
        {
            return false;
        }

        action = CreateImportAction(analysis.Document.Uri, importTarget, edits);
        return true;
    }

    private bool TryCreateSetterPropertyValueImportAction(
        string sourceText,
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlRefactoringAction action)
    {
        action = default!;

        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                sourceText,
                analysis.XmlDocument,
                position,
                out var element,
                out var attribute,
                out _,
                out var attributeValueRange) ||
            !XamlStyleNavigationSemantics.IsSetterPropertyAttribute(element.Name.LocalName, attribute.Name.LocalName))
        {
            return false;
        }

        var setterPropertyToken = ReadRangeText(sourceText, attributeValueRange);
        if (!TrySplitOwnerQualifiedPropertyToken(setterPropertyToken, out var ownerToken, out var memberName))
        {
            return false;
        }

        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        if (XamlClrSymbolResolver.TryResolvePropertyInfo(
                analysis.TypeIndex!,
                prefixMap,
                element.Name.LocalName,
                setterPropertyToken,
                out _,
                out _))
        {
            return false;
        }

        if (!TryResolveImportTarget(analysis, element, ownerToken, out var importTarget))
        {
            return false;
        }

        var replacementText = importTarget.QualifiedName + "." + memberName;
        var edits = BuildAttributeEdits(
            sourceText,
            analysis.XmlDocument!.Root!,
            attributeValueRange,
            replacementText,
            importTarget);
        if (edits.IsDefaultOrEmpty)
        {
            return false;
        }

        action = CreateImportAction(analysis.Document.Uri, importTarget, edits);
        return true;
    }

    private bool TryCreateTypeReferenceValueImportAction(
        string sourceText,
        XamlAnalysisResult analysis,
        SourcePosition position,
        out XamlRefactoringAction action)
    {
        action = default!;

        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                sourceText,
                analysis.XmlDocument,
                position,
                out var element,
                out var attribute,
                out _,
                out var attributeValueRange))
        {
            return false;
        }

        var rawTypeValue = ReadRangeText(sourceText, attributeValueRange);
        if (!TryResolveTypeReferenceImportAction(
                analysis,
                element,
                attribute,
                rawTypeValue,
                out var importTarget,
                out var replacementText))
        {
            return false;
        }

        var edits = BuildAttributeEdits(
            sourceText,
            analysis.XmlDocument!.Root!,
            attributeValueRange,
            replacementText,
            importTarget);
        if (edits.IsDefaultOrEmpty)
        {
            return false;
        }

        action = CreateImportAction(analysis.Document.Uri, importTarget, edits);
        return true;
    }

    private bool TryResolveTypeReferenceImportAction(
        XamlAnalysisResult analysis,
        XElement element,
        XAttribute attribute,
        string rawTypeValue,
        out NamespaceImportTarget importTarget,
        out string replacementText)
    {
        importTarget = default;
        replacementText = string.Empty;

        if (!TryGetImportableTypeReferenceToken(element, attribute, rawTypeValue, out var typeToken))
        {
            return false;
        }

        var prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        if (XamlClrSymbolResolver.TryResolveTypeInfo(
                analysis.TypeIndex!,
                prefixMap,
                typeToken,
                out _))
        {
            return false;
        }

        if (!TryResolveImportTarget(analysis, element, typeToken, out importTarget) ||
            !XamlTypeReferenceNavigationResolver.TryRewriteTypeReferenceToken(
                rawTypeValue,
                importTarget.QualifiedName,
                out replacementText))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetImportableTypeReferenceToken(
        XElement element,
        XAttribute attribute,
        string rawTypeValue,
        out string typeToken)
    {
        typeToken = string.Empty;

        if (string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal))
        {
            return false;
        }

        if (XamlTypeReferenceNavigationResolver.IsTypeReferenceAttribute(attribute))
        {
            return XamlTypeReferenceNavigationResolver.TryNormalizeTypeReferenceToken(rawTypeValue, out typeToken);
        }

        return string.Equals(element.Name.LocalName, "ControlTheme", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.LocalName, "TargetType", StringComparison.Ordinal) &&
               XamlStyleNavigationSemantics.TryNormalizeControlThemeTargetType(rawTypeValue, out typeToken);
    }

    private bool TryResolveImportTarget(
        XamlAnalysisResult analysis,
        XElement element,
        string rawElementName,
        out NamespaceImportTarget importTarget)
    {
        importTarget = default;
        (_, var xmlTypeName) = XamlXmlNamespaceResolver.SplitQualifiedName(rawElementName);
        if (string.IsNullOrWhiteSpace(xmlTypeName))
        {
            return false;
        }

        ImmutableDictionary<string, string> prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        string currentXmlNamespace = element.Name.NamespaceName;
        var actionableTargets = new List<NamespaceImportTarget>();

        foreach (var groupedCandidate in analysis.TypeIndex!
                     .FindTypesByXmlTypeName(xmlTypeName)
                     .GroupBy(static candidate => candidate.FullTypeName, StringComparer.Ordinal))
        {
            AvaloniaTypeInfo chosenCandidate = ChoosePreferredCandidate(
                groupedCandidate,
                prefixMap,
                currentXmlNamespace,
                analysis.Framework.DefaultXmlNamespace);
            if (!TryCreateImportTarget(
                    analysis,
                    rawElementName,
                    xmlTypeName,
                    chosenCandidate,
                    prefixMap,
                    currentXmlNamespace,
                    out var candidateTarget))
            {
                continue;
            }

            actionableTargets.Add(candidateTarget);
        }

        if (actionableTargets.Count != 1)
        {
            return false;
        }

        importTarget = actionableTargets[0];
        return true;
    }

    private AvaloniaTypeInfo ChoosePreferredCandidate(
        IGrouping<string, AvaloniaTypeInfo> candidates,
        ImmutableDictionary<string, string> prefixMap,
        string currentXmlNamespace,
        string defaultXmlNamespace)
    {
        return candidates
            .OrderByDescending(candidate => HasExistingNonDefaultPrefix(prefixMap, candidate.XmlNamespace))
            .ThenByDescending(candidate => !string.Equals(candidate.XmlNamespace, defaultXmlNamespace, StringComparison.Ordinal))
            .ThenByDescending(candidate => !string.Equals(candidate.XmlNamespace, currentXmlNamespace, StringComparison.Ordinal))
            .ThenBy(candidate => candidate.XmlNamespace, StringComparer.Ordinal)
            .First();
    }

    private bool TryCreateImportTarget(
        XamlAnalysisResult analysis,
        string rawElementName,
        string xmlTypeName,
        AvaloniaTypeInfo candidate,
        ImmutableDictionary<string, string> prefixMap,
        string currentXmlNamespace,
        out NamespaceImportTarget importTarget)
    {
        importTarget = default;
        if (string.Equals(candidate.XmlNamespace, currentXmlNamespace, StringComparison.Ordinal) &&
            !prefixMap.Any(entry => string.Equals(entry.Value, candidate.XmlNamespace, StringComparison.Ordinal) && entry.Key.Length > 0))
        {
            return false;
        }

        string qualifiedName;
        var requiresNamespaceDeclaration = false;
        if (TryGetExistingPrefixForNamespace(prefixMap, candidate.XmlNamespace, out var existingPrefix))
        {
            qualifiedName = string.IsNullOrEmpty(existingPrefix)
                ? xmlTypeName
                : existingPrefix + ":" + xmlTypeName;
        }
        else
        {
            var suggestedPrefix = _prefixSuggestionService.SuggestPrefix(
                analysis.Compilation,
                prefixMap,
                candidate.XmlNamespace,
                candidate.ClrNamespace);
            qualifiedName = suggestedPrefix + ":" + xmlTypeName;
            requiresNamespaceDeclaration = true;
        }

        if (string.Equals(rawElementName, qualifiedName, StringComparison.Ordinal) &&
            !requiresNamespaceDeclaration)
        {
            return false;
        }

        importTarget = new NamespaceImportTarget(
            candidate.XmlNamespace,
            qualifiedName,
            requiresNamespaceDeclaration);
        return true;
    }

    private static bool TryGetExistingPrefixForNamespace(
        ImmutableDictionary<string, string> prefixMap,
        string xmlNamespace,
        out string prefix)
    {
        foreach (var entry in prefixMap)
        {
            if (string.Equals(entry.Value, xmlNamespace, StringComparison.Ordinal))
            {
                prefix = entry.Key;
                return true;
            }
        }

        prefix = string.Empty;
        return false;
    }

    private static bool HasExistingNonDefaultPrefix(ImmutableDictionary<string, string> prefixMap, string xmlNamespace)
    {
        return prefixMap.Any(entry =>
            entry.Key.Length > 0 &&
            string.Equals(entry.Value, xmlNamespace, StringComparison.Ordinal));
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildElementEdits(
        string sourceText,
        XElement root,
        XElement element,
        SourceRange targetRange,
        string replacementText,
        NamespaceImportTarget importTarget)
    {
        var edits = new List<XamlDocumentTextEdit>(3)
        {
            new(targetRange, replacementText)
        };

        if (XamlXmlSourceRangeService.TryCreateClosingElementNameRange(sourceText, element, out var closingElementRange))
        {
            edits.Add(new XamlDocumentTextEdit(closingElementRange, replacementText));
        }

        if (importTarget.RequiresNamespaceDeclaration &&
            TryCreateNamespaceDeclarationEdit(sourceText, root, importTarget.XmlNamespace, importTarget.QualifiedName, out var namespaceEdit))
        {
            edits.Add(namespaceEdit);
        }

        return OrderEdits(edits);
    }

    private static ImmutableArray<XamlDocumentTextEdit> BuildAttributeEdits(
        string sourceText,
        XElement root,
        SourceRange targetRange,
        string replacementText,
        NamespaceImportTarget importTarget)
    {
        var edits = new List<XamlDocumentTextEdit>(2)
        {
            new(targetRange, replacementText)
        };

        if (importTarget.RequiresNamespaceDeclaration &&
            TryCreateNamespaceDeclarationEdit(sourceText, root, importTarget.XmlNamespace, importTarget.QualifiedName, out var namespaceEdit))
        {
            edits.Add(namespaceEdit);
        }

        return OrderEdits(edits);
    }

    private static ImmutableArray<XamlDocumentTextEdit> OrderEdits(List<XamlDocumentTextEdit> edits)
    {
        return edits
            .OrderByDescending(static edit => edit.Range.Start.Line)
            .ThenByDescending(static edit => edit.Range.Start.Character)
            .ToImmutableArray();
    }

    private static XamlRefactoringAction CreateImportAction(
        string documentUri,
        NamespaceImportTarget importTarget,
        ImmutableArray<XamlDocumentTextEdit> edits)
    {
        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(documentUri, edits));

        return new XamlRefactoringAction(
            Title: "AXSG: Import namespace for " + importTarget.QualifiedName,
            Kind: "quickfix",
            IsPreferred: true,
            Edit: workspaceEdit,
            Command: null);
    }

    private static bool TrySplitOwnerQualifiedPropertyToken(
        string propertyToken,
        out string ownerToken,
        out string memberName)
    {
        ownerToken = string.Empty;
        memberName = string.Empty;

        var separatorIndex = propertyToken.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= propertyToken.Length - 1)
        {
            return false;
        }

        ownerToken = propertyToken[..separatorIndex];
        memberName = propertyToken[(separatorIndex + 1)..];
        return ownerToken.Length > 0 && memberName.Length > 0;
    }

    private static bool TryCreateNamespaceDeclarationEdit(
        string sourceText,
        XElement root,
        string xmlNamespace,
        string qualifiedName,
        out XamlDocumentTextEdit edit)
    {
        edit = default!;
        if (!TryGetNamespaceDeclarationPrefix(qualifiedName, out var prefix) ||
            root is not IXmlLineInfo lineInfo ||
            !lineInfo.HasLineInfo())
        {
            return false;
        }

        var rootStartPosition = new SourcePosition(
            Math.Max(0, lineInfo.LineNumber - 1),
            Math.Max(0, lineInfo.LinePosition - 1));
        var rootStartOffset = NormalizeElementStartOffset(sourceText, TextCoordinateHelper.GetOffset(sourceText, rootStartPosition));
        if (rootStartOffset < 0 ||
            !TryFindElementStartTagEnd(sourceText, rootStartOffset, out var startTagEnd))
        {
            return false;
        }

        var insertOffset = startTagEnd > rootStartOffset && sourceText[startTagEnd - 1] == '/'
            ? startTagEnd - 1
            : startTagEnd;
        var isSelfClosingInsert = insertOffset < sourceText.Length && sourceText[insertOffset] == '/';
        var declarationText = "xmlns:" + prefix + "=\"" + xmlNamespace + "\"";
        var startTagText = sourceText.Substring(rootStartOffset, startTagEnd - rootStartOffset + 1);
        var hasLeadingWhitespace = insertOffset > 0 && char.IsWhiteSpace(sourceText[insertOffset - 1]);
        var insertText = startTagText.IndexOf('\n') >= 0 || startTagText.IndexOf('\r') >= 0
            ? DetectNewLine(sourceText) + DetectAttributeIndent(sourceText, rootStartOffset, startTagEnd) + declarationText
            : hasLeadingWhitespace && isSelfClosingInsert
                ? declarationText + " "
                : (hasLeadingWhitespace ? string.Empty : " ") + declarationText;

        var insertPosition = TextCoordinateHelper.GetPosition(sourceText, insertOffset);
        edit = new XamlDocumentTextEdit(new SourceRange(insertPosition, insertPosition), insertText);
        return true;
    }

    private static bool TryGetNamespaceDeclarationPrefix(string qualifiedName, out string prefix)
    {
        (prefix, _) = XamlXmlNamespaceResolver.SplitQualifiedName(qualifiedName);
        return prefix.Length > 0;
    }

    private static bool TryFindElementStartTagEnd(string sourceText, int startOffset, out int startTagEnd)
    {
        var inQuotes = false;
        var quote = '\0';
        for (var index = startOffset; index < sourceText.Length; index++)
        {
            var current = sourceText[index];
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
                startTagEnd = index;
                return true;
            }
        }

        startTagEnd = -1;
        return false;
    }

    private static int NormalizeElementStartOffset(string sourceText, int offset)
    {
        if (offset <= 0 || offset >= sourceText.Length || sourceText[offset] == '<')
        {
            return offset;
        }

        for (var index = offset; index >= 0; index--)
        {
            var current = sourceText[index];
            if (current == '<')
            {
                return index;
            }

            if (current is '\r' or '\n')
            {
                break;
            }
        }

        return offset;
    }

    private static string DetectAttributeIndent(string sourceText, int rootStartOffset, int startTagEnd)
    {
        var rootIndent = GetLineIndent(sourceText, rootStartOffset);
        for (var index = rootStartOffset; index < startTagEnd; index++)
        {
            if (sourceText[index] is not '\r' and not '\n')
            {
                continue;
            }

            var candidateIndentStart = index + 1;
            while (candidateIndentStart < sourceText.Length &&
                   sourceText[candidateIndentStart] is '\r' or '\n')
            {
                candidateIndentStart++;
            }

            var candidateIndentEnd = candidateIndentStart;
            while (candidateIndentEnd < sourceText.Length &&
                   sourceText[candidateIndentEnd] is ' ' or '\t')
            {
                candidateIndentEnd++;
            }

            if (candidateIndentEnd > candidateIndentStart)
            {
                return sourceText.Substring(candidateIndentStart, candidateIndentEnd - candidateIndentStart);
            }
        }

        return rootIndent + "  ";
    }

    private static string GetLineIndent(string sourceText, int offset)
    {
        var lineStart = offset;
        while (lineStart > 0 && sourceText[lineStart - 1] is not '\r' and not '\n')
        {
            lineStart--;
        }

        var indentEnd = lineStart;
        while (indentEnd < sourceText.Length &&
               sourceText[indentEnd] is ' ' or '\t')
        {
            indentEnd++;
        }

        return sourceText.Substring(lineStart, indentEnd - lineStart);
    }

    private static string DetectNewLine(string text)
    {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static bool IsUnresolvedElementType(
        XamlAnalysisResult analysis,
        XElement element,
        string rawElementName)
    {
        ImmutableDictionary<string, string> prefixMap = XamlTypeReferenceNavigationResolver.BuildPrefixMapForElement(element);
        return !XamlClrSymbolResolver.TryResolveTypeInfo(
            analysis.TypeIndex!,
            prefixMap,
            rawElementName,
            out _);
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

    private readonly record struct NamespaceImportTarget(
        string XmlNamespace,
        string QualifiedName,
        bool RequiresNamespaceDeclaration);
}
