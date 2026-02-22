using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkProfile
{
    string Id { get; }

    IXamlFrameworkBuildContract BuildContract { get; }

    IXamlFrameworkTransformProvider TransformProvider { get; }

    IXamlFrameworkSemanticBinder CreateSemanticBinder();

    IXamlFrameworkEmitter CreateEmitter();

    ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers();

    XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options);
}
