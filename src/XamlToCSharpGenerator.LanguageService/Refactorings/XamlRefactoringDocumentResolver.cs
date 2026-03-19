using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService.Documents;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Refactorings;

internal static class XamlRefactoringDocumentResolver
{
    public static async Task<LanguageServiceDocument?> ResolveDocumentAsync(
        XamlDocumentStore documentStore,
        XamlRefactoringContext context,
        CancellationToken cancellationToken)
    {
        string? documentText = context.DocumentTextOverride;
        LanguageServiceDocument? openDocument = documentStore.Get(context.Uri);
        int version = openDocument?.Version ?? 0;
        string filePath = openDocument?.FilePath ?? UriPathHelper.ToFilePath(context.Uri);

        if (documentText is null)
        {
            if (openDocument is not null)
            {
                documentText = openDocument.Text;
            }
            else if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                documentText = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
        }

        return documentText is null
            ? null
            : new LanguageServiceDocument(context.Uri, filePath, documentText, version);
    }
}
