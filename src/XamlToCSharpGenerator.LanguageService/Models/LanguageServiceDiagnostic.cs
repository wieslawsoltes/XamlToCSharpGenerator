namespace XamlToCSharpGenerator.LanguageService.Models;

public sealed record LanguageServiceDiagnostic(
    string Code,
    string Message,
    SourceRange Range,
    LanguageServiceDiagnosticSeverity Severity,
    string? Source = null);
