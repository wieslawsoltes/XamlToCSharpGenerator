namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkDocumentUriResolver
{
    string BuildDocumentUri(string assemblyName, string normalizedTargetPath);

    bool TryResolveIncludeUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        out string resolvedUri,
        out bool isProjectLocal);
}
