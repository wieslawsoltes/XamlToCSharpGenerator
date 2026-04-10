using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static ImmutableArray<ResolvedIncludeDefinition> BindIncludes(
        XamlDocumentModel document,
        Compilation compilation,
        string currentDocumentUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        return IncludeBindingService.BindIncludes(
            document,
            compilation,
            currentDocumentUri,
            diagnostics,
            options);
    }
}
