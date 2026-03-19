using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlBindingMarkupRefactoringProvider : IXamlRefactoringProvider
{
    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlBindingMarkupRefactoringProvider(
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
                    IncludeSemanticDiagnostics = true
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (analysis.XmlDocument is null ||
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

        int attributeValueStartOffset = TextCoordinateHelper.GetOffset(document.Text, attributeValueRange.Start);
        if (attributeValueStartOffset < 0 ||
            !XamlMarkupExtensionSpanParser.TryParse(attribute.Value, attributeValueStartOffset, out var markupSpanInfo))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlMarkupExtensionKind extensionKind = XamlMarkupExtensionNameSemantics.Classify(markupSpanInfo.ExtensionName);
        if (extensionKind is not (XamlMarkupExtensionKind.Binding or XamlMarkupExtensionKind.CompiledBinding or XamlMarkupExtensionKind.ReflectionBinding))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        var actions = new List<XamlRefactoringAction>(2);
        bool hasMissingDataTypeDiagnostic = extensionKind is XamlMarkupExtensionKind.CompiledBinding &&
                                            HasDiagnosticForAttribute(
                                                analysis,
                                                "AXSG0110",
                                                attributeNameRange,
                                                attributeValueRange);
        bool hasInvalidCompiledBindingPathDiagnostic = extensionKind is XamlMarkupExtensionKind.CompiledBinding &&
                                                       HasDiagnosticForAttribute(
                                                           analysis,
                                                           "AXSG0111",
                                                           attributeNameRange,
                                                           attributeValueRange);
        if (extensionKind is XamlMarkupExtensionKind.Binding or XamlMarkupExtensionKind.ReflectionBinding)
        {
            if (XamlSemanticSourceTypeResolver.TryResolveAmbientDataType(analysis, element, out _, out _))
            {
                actions.Add(CreateRewriteAction(document.Uri, document.Text, markupSpanInfo, "CompiledBinding"));
            }
        }

        if (extensionKind is XamlMarkupExtensionKind.CompiledBinding or XamlMarkupExtensionKind.ReflectionBinding)
        {
            if (hasMissingDataTypeDiagnostic)
            {
                actions.Add(CreateQuickFixAction(
                    document.Uri,
                    document.Text,
                    markupSpanInfo,
                    replacementExtensionName: "Binding",
                    title: "AXSG: Fix missing x:DataType by converting to Binding"));
            }

            if (hasInvalidCompiledBindingPathDiagnostic)
            {
                actions.Add(CreateQuickFixAction(
                    document.Uri,
                    document.Text,
                    markupSpanInfo,
                    replacementExtensionName: "Binding",
                    title: "AXSG: Fix invalid compiled binding path by converting to Binding"));
            }

            actions.Add(CreateRewriteAction(document.Uri, document.Text, markupSpanInfo, "Binding"));
        }

        return actions.Count == 0
            ? ImmutableArray<XamlRefactoringAction>.Empty
            : ImmutableArray.CreateRange(actions);
    }

    private static bool HasDiagnosticForAttribute(
        XamlAnalysisResult analysis,
        string code,
        SourceRange attributeNameRange,
        SourceRange attributeValueRange)
    {
        return analysis.Diagnostics.Any(diagnostic =>
            string.Equals(diagnostic.Code, code, System.StringComparison.Ordinal) &&
            IntersectsAttributeSpan(diagnostic.Range.Start, attributeNameRange, attributeValueRange));
    }

    private static bool IntersectsAttributeSpan(
        SourcePosition position,
        SourceRange attributeNameRange,
        SourceRange attributeValueRange)
    {
        if (position.Line < attributeNameRange.Start.Line ||
            position.Line > attributeValueRange.End.Line)
        {
            return false;
        }

        if (position.Line == attributeNameRange.Start.Line &&
            position.Character < attributeNameRange.Start.Character)
        {
            return false;
        }

        if (position.Line == attributeValueRange.End.Line &&
            position.Character > attributeValueRange.End.Character)
        {
            return false;
        }

        return true;
    }

    private static XamlRefactoringAction CreateRewriteAction(
        string uri,
        string sourceText,
        MarkupSpanInfo markupSpanInfo,
        string replacementExtensionName)
    {
        SourceRange range = new(
            TextCoordinateHelper.GetPosition(sourceText, markupSpanInfo.ExtensionNameStart),
            TextCoordinateHelper.GetPosition(sourceText, markupSpanInfo.ExtensionNameStart + markupSpanInfo.ExtensionNameLength));

        ImmutableArray<XamlDocumentTextEdit> edits = ImmutableArray.Create(
            new XamlDocumentTextEdit(range, replacementExtensionName));
        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(uri, edits));

        return new XamlRefactoringAction(
            Title: "AXSG: Convert to " + replacementExtensionName,
            Kind: "refactor.rewrite",
            IsPreferred: false,
            Edit: workspaceEdit,
            Command: null);
    }

    private static XamlRefactoringAction CreateQuickFixAction(
        string uri,
        string sourceText,
        MarkupSpanInfo markupSpanInfo,
        string replacementExtensionName,
        string title)
    {
        SourceRange range = new(
            TextCoordinateHelper.GetPosition(sourceText, markupSpanInfo.ExtensionNameStart),
            TextCoordinateHelper.GetPosition(sourceText, markupSpanInfo.ExtensionNameStart + markupSpanInfo.ExtensionNameLength));

        ImmutableArray<XamlDocumentTextEdit> edits = ImmutableArray.Create(
            new XamlDocumentTextEdit(range, replacementExtensionName));
        XamlWorkspaceEdit workspaceEdit = new(
            ImmutableDictionary<string, ImmutableArray<XamlDocumentTextEdit>>.Empty.Add(uri, edits));

        return new XamlRefactoringAction(
            Title: title,
            Kind: "quickfix",
            IsPreferred: true,
            Edit: workspaceEdit,
            Command: null);
    }
}
