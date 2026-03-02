namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record XamlReferenceLocation(string Uri, SourceRange Range, bool IsDeclaration);
