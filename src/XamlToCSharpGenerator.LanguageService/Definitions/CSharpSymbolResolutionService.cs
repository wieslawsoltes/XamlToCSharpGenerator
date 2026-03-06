using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal sealed record CSharpResolvedSymbol(
    CompilationSnapshot Snapshot,
    ISymbol Symbol,
    string FilePath);

internal sealed class CSharpSymbolResolutionService
{
    private readonly ICompilationProvider _compilationProvider;

    public CSharpSymbolResolutionService(ICompilationProvider compilationProvider)
    {
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
    }

    public async Task<CSharpResolvedSymbol?> ResolveSymbolAtPositionAsync(
        string uri,
        SourcePosition position,
        string? documentTextOverride,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        options ??= XamlLanguageServiceOptions.Default;

        var filePath = UriPathHelper.ToFilePath(uri);
        var snapshot = await _compilationProvider
            .GetCompilationAsync(filePath, options.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);

        var symbol = snapshot.Project is not null
            ? await ResolveWorkspaceSymbolAsync(snapshot.Project, filePath, position, documentTextOverride, cancellationToken).ConfigureAwait(false)
            : ResolveCompilationSymbol(snapshot.Compilation, filePath, position, documentTextOverride);

        if (symbol is null)
        {
            return null;
        }

        symbol = symbol switch
        {
            IAliasSymbol aliasSymbol => aliasSymbol.Target,
            _ => symbol
        };

        return new CSharpResolvedSymbol(snapshot, symbol, filePath);
    }

    private static async Task<ISymbol?> ResolveWorkspaceSymbolAsync(
        Project project,
        string filePath,
        SourcePosition position,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        var solution = ApplyDocumentTextOverride(project.Solution, filePath, documentTextOverride);
        var document = solution.GetDocumentIdsWithFilePath(filePath)
            .Select(solution.GetDocument)
            .FirstOrDefault(static candidate => candidate is not null);
        if (document is null)
        {
            return null;
        }

        var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (syntaxRoot is null)
        {
            return null;
        }

        var offset = sourceText.Lines.GetPosition(new LinePosition(position.Line, position.Character));
        return await ResolveSymbolAtOffsetAsync(document, syntaxRoot, sourceText, offset, cancellationToken).ConfigureAwait(false);
    }

    private static ISymbol? ResolveCompilationSymbol(
        Compilation? compilation,
        string filePath,
        SourcePosition position,
        string? documentTextOverride)
    {
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            return null;
        }

        var text = documentTextOverride;
        if (string.IsNullOrWhiteSpace(text))
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            text = File.ReadAllText(filePath);
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(text, path: filePath);
        var existingTree = csharpCompilation.SyntaxTrees.FirstOrDefault(tree =>
            string.Equals(tree.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        var workingCompilation = existingTree is null
            ? csharpCompilation.AddSyntaxTrees(syntaxTree)
            : csharpCompilation.ReplaceSyntaxTree(existingTree, syntaxTree);
        var semanticModel = workingCompilation.GetSemanticModel(syntaxTree);
        var syntaxRoot = syntaxTree.GetRoot(CancellationToken.None);
        var sourceText = syntaxTree.GetText(CancellationToken.None);
        var offset = sourceText.Lines.GetPosition(new LinePosition(position.Line, position.Character));
        var token = FindToken(syntaxRoot, sourceText, offset);

        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node);
            if (declaredSymbol is not null)
            {
                return declaredSymbol;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(node).Symbol;
            if (symbolInfo is not null)
            {
                return symbolInfo;
            }

            var memberGroup = semanticModel.GetMemberGroup(node);
            if (memberGroup.Length > 0)
            {
                return memberGroup[0];
            }
        }

        return null;
    }

    private static async Task<ISymbol?> ResolveSymbolAtOffsetAsync(
        Document document,
        SyntaxNode syntaxRoot,
        SourceText sourceText,
        int offset,
        CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return null;
        }

        var token = FindToken(syntaxRoot, sourceText, offset);
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
            if (declaredSymbol is not null)
            {
                return declaredSymbol;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
            if (symbolInfo is not null)
            {
                return symbolInfo;
            }

            var memberGroup = semanticModel.GetMemberGroup(node, cancellationToken);
            if (memberGroup.Length > 0)
            {
                return memberGroup[0];
            }
        }

        return null;
    }

    private static SyntaxToken FindToken(SyntaxNode syntaxRoot, SourceText sourceText, int offset)
    {
        var boundedOffset = Math.Max(0, Math.Min(offset, sourceText.Length == 0 ? 0 : sourceText.Length - 1));
        return syntaxRoot.FindToken(boundedOffset);
    }

    private static Solution ApplyDocumentTextOverride(Solution solution, string filePath, string? documentTextOverride)
    {
        if (string.IsNullOrWhiteSpace(documentTextOverride))
        {
            return solution;
        }

        var sourceText = SourceText.From(documentTextOverride);

        foreach (var documentId in solution.GetDocumentIdsWithFilePath(filePath).ToImmutableArray())
        {
            if (solution.GetDocument(documentId) is null)
            {
                continue;
            }

            solution = solution.WithDocumentText(documentId, sourceText, PreservationMode.PreserveIdentity);
        }

        foreach (var documentId in GetAdditionalDocumentIdsWithFilePath(solution, filePath))
        {
            if (solution.GetAdditionalDocument(documentId) is null)
            {
                continue;
            }

            solution = solution.WithAdditionalDocumentText(documentId, sourceText, PreservationMode.PreserveIdentity);
        }

        return solution;
    }

    private static ImmutableArray<DocumentId> GetAdditionalDocumentIdsWithFilePath(Solution solution, string filePath)
    {
        var builder = ImmutableArray.CreateBuilder<DocumentId>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.AdditionalDocuments)
            {
                if (string.Equals(document.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Add(document.Id);
                }
            }
        }

        return builder.ToImmutable();
    }
}
