using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlClassPartialRefactoringProvider : IXamlRefactoringProvider
{
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly XamlDocumentStore _documentStore;
    private readonly XamlCompilerAnalysisService _analysisService;

    public XamlClassPartialRefactoringProvider(
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
                out _,
                out var attribute,
                out _,
                out _) ||
            !IsXClassAttribute(attribute))
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        INamedTypeSymbol? typeSymbol = ResolveTypeSymbol(analysis.Compilation, attribute.Value.Trim()) as INamedTypeSymbol;
        if (typeSymbol is null)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        XamlWorkspaceEdit workspaceEdit = BuildWorkspaceEdit(typeSymbol, cancellationToken);
        if (!workspaceEdit.HasChanges)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        return ImmutableArray.Create(
            new XamlRefactoringAction(
                Title: "AXSG: Fix x:Class companion type by adding partial",
                Kind: "quickfix",
                IsPreferred: true,
                Edit: workspaceEdit,
                Command: null));
    }

    private static XamlWorkspaceEdit BuildWorkspaceEdit(INamedTypeSymbol typeSymbol, CancellationToken cancellationToken)
    {
        var changesBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XamlDocumentTextEdit>>(StringComparer.Ordinal);

        foreach (var syntaxGroup in typeSymbol.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax(cancellationToken) as TypeDeclarationSyntax)
                     .Where(static declaration => declaration is not null)
                     .Cast<TypeDeclarationSyntax>()
                     .GroupBy(static declaration => declaration.SyntaxTree.FilePath, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(syntaxGroup.Key))
            {
                continue;
            }

            SourceText sourceText = syntaxGroup.First().SyntaxTree.GetText(cancellationToken);
            var edits = ImmutableArray.CreateBuilder<XamlDocumentTextEdit>();
            var seen = new HashSet<int>();

            foreach (TypeDeclarationSyntax declaration in syntaxGroup)
            {
                if (declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                int insertionOffset = declaration.Keyword.SpanStart;
                if (!seen.Add(insertionOffset))
                {
                    continue;
                }

                edits.Add(new XamlDocumentTextEdit(
                    CreateInsertionRange(sourceText, insertionOffset),
                    "partial "));
            }

            if (edits.Count == 0)
            {
                continue;
            }

            changesBuilder[UriPathHelper.ToDocumentUri(syntaxGroup.Key)] = SortEdits(edits.ToImmutable());
        }

        return changesBuilder.Count == 0
            ? XamlWorkspaceEdit.Empty
            : new XamlWorkspaceEdit(changesBuilder.ToImmutable());
    }

    private static bool IsXClassAttribute(XAttribute attribute)
    {
        return string.Equals(attribute.Name.LocalName, "Class", StringComparison.Ordinal) &&
               string.Equals(attribute.Name.NamespaceName, Xaml2006Namespace, StringComparison.Ordinal);
    }

    private static SourceRange CreateInsertionRange(SourceText sourceText, int offset)
    {
        var linePosition = sourceText.Lines.GetLinePosition(offset);
        var position = new SourcePosition(linePosition.Line, linePosition.Character);
        return new SourceRange(position, position);
    }

    private static ImmutableArray<XamlDocumentTextEdit> SortEdits(ImmutableArray<XamlDocumentTextEdit> edits)
    {
        return edits
            .OrderByDescending(static edit => edit.Range.Start.Line)
            .ThenByDescending(static edit => edit.Range.Start.Character)
            .ToImmutableArray();
    }

    private static ISymbol? ResolveTypeSymbol(Compilation? compilation, string fullTypeName)
    {
        if (compilation is null || string.IsNullOrWhiteSpace(fullTypeName))
        {
            return null;
        }

        return compilation.GetTypeByMetadataName(fullTypeName)
               ?? compilation.GetTypeByMetadataName(fullTypeName.Replace('.', '+'));
    }
}
