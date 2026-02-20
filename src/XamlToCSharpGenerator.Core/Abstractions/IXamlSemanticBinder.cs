using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Core.Abstractions;

public interface IXamlSemanticBinder
{
    (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options,
        XamlTransformConfiguration transformConfiguration);
}
