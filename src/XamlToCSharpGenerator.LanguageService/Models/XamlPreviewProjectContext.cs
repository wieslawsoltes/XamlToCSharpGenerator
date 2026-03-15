namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlPreviewProjectContext(
    string ProjectPath,
    string ProjectDirectory,
    string FilePath,
    string TargetPath);
