using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Diagnostics;

internal static class DiagnosticConversion
{
    public static ImmutableArray<LanguageServiceDiagnostic> FromCoreDiagnostics(
        ImmutableArray<DiagnosticInfo> diagnostics)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return ImmutableArray<LanguageServiceDiagnostic>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            var line = Math.Max(0, diagnostic.Line - 1);
            var character = Math.Max(0, diagnostic.Column - 1);
            var range = new SourceRange(
                new SourcePosition(line, character),
                new SourcePosition(line, character + 1));

            builder.Add(new LanguageServiceDiagnostic(
                diagnostic.Id,
                diagnostic.Message,
                range,
                diagnostic.IsError
                    ? LanguageServiceDiagnosticSeverity.Error
                    : LanguageServiceDiagnosticSeverity.Warning,
                Source: "AXSG"));
        }

        return builder.ToImmutable();
    }
}
