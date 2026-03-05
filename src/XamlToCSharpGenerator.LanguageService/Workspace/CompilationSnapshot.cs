using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

public sealed record CompilationSnapshot(
    string? ProjectPath,
    Project? Project,
    Compilation? Compilation,
    ImmutableArray<LanguageServiceDiagnostic> Diagnostics);
