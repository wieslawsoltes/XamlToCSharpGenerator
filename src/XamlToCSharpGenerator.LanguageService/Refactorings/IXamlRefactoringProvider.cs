using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal interface IXamlRefactoringProvider
{
    Task<ImmutableArray<XamlRefactoringAction>> GetCodeActionsAsync(
        XamlRefactoringContext context,
        CancellationToken cancellationToken);
}
