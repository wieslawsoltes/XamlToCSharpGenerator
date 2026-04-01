namespace XamlToCSharpGenerator.LanguageService;

public sealed record XamlLanguageServiceOptions(
    string? WorkspaceRoot,
    bool IncludeCompilationDiagnostics = true,
    bool IncludeSemanticDiagnostics = true,
    string? AvaloniaVersion = null)
{
    public static XamlLanguageServiceOptions Default { get; } = new(
        WorkspaceRoot: null,
        IncludeCompilationDiagnostics: true,
        IncludeSemanticDiagnostics: true);
}
