using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlRenameRefactoringProvider : IXamlRefactoringProvider, IXamlRenameProvider
{
    private readonly XamlRenameService _renameService;

    public XamlRenameRefactoringProvider(XamlRenameService renameService)
    {
        _renameService = renameService;
    }

    public async Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        XamlRefactoringContext context,
        CancellationToken cancellationToken)
    {
        var prepareResult = await _renameService
            .PrepareRenameAsync(
                context.Uri,
                context.Position,
                context.Options,
                context.DocumentTextOverride,
                cancellationToken)
            .ConfigureAwait(false);
        if (prepareResult is null)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        return ImmutableArray.Create(
            new XamlRefactoringAction(
                Title: "AXSG: Rename Symbol Across C# and XAML",
                Kind: "refactor.rename",
                IsPreferred: true,
                Edit: null,
                Command: new XamlRefactoringCommand(
                    Name: "axsg.refactor.renameSymbol",
                    Uri: context.Uri,
                    Position: context.Position,
                    RefactoringId: "rename")));
    }

    public Task<XamlPrepareRenameResult?> PrepareRenameAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _renameService.PrepareRenameAsync(uri, position, options, documentTextOverride, cancellationToken);
    }

    public Task<XamlWorkspaceEdit> RenameAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _renameService.RenameAsync(uri, position, newName, options, documentTextOverride, cancellationToken);
    }

    public Task<XamlWorkspaceEdit> GetCSharpRenamePropagationEditsAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _renameService.GetCSharpRenamePropagationEditsAsync(
            uri,
            position,
            newName,
            options,
            documentTextOverride,
            cancellationToken);
    }
}
