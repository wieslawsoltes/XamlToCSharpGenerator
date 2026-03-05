using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal sealed class XamlRefactoringService
{
    private readonly ImmutableArray<IXamlRefactoringProvider> _providers;
    private readonly IXamlRenameProvider _renameProvider;

    public XamlRefactoringService(
        ImmutableArray<IXamlRefactoringProvider> providers,
        IXamlRenameProvider renameProvider)
    {
        _providers = providers.IsDefault ? ImmutableArray<IXamlRefactoringProvider>.Empty : providers;
        _renameProvider = renameProvider ?? throw new ArgumentNullException(nameof(renameProvider));
    }

    public async Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        if (_providers.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlRefactoringAction>.Empty;
        }

        var context = new XamlRefactoringContext(uri, position, options, documentTextOverride);
        var builder = ImmutableArray.CreateBuilder<XamlRefactoringAction>();
        foreach (var provider in _providers)
        {
            builder.AddRange(await provider.GetCodeActionsAsync(context, cancellationToken).ConfigureAwait(false));
        }

        return builder.ToImmutable();
    }

    public Task<XamlPrepareRenameResult?> PrepareRenameAsync(
        string uri,
        SourcePosition position,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _renameProvider.PrepareRenameAsync(uri, position, options, documentTextOverride, cancellationToken);
    }

    public Task<XamlWorkspaceEdit> RenameAsync(
        string uri,
        SourcePosition position,
        string newName,
        XamlLanguageServiceOptions options,
        string? documentTextOverride,
        CancellationToken cancellationToken)
    {
        return _renameProvider.RenameAsync(uri, position, newName, options, documentTextOverride, cancellationToken);
    }
}
