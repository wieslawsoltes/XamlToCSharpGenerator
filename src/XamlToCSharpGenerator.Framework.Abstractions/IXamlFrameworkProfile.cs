using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkProfile
{
    string Id { get; }

    XamlSourceGenConfiguration BaseConfiguration { get; }

    XamlFrameworkMsBuildSettings MsBuildSettings { get; }

    SemanticContractMap SemanticContractMap { get; }

    XamlFrameworkSemanticConventions SemanticConventions { get; }

    IXamlFrameworkBuildContract BuildContract { get; }

    IXamlFrameworkDocumentUriResolver DocumentUriResolver { get; }

    IXamlFrameworkTransformProvider TransformProvider { get; }

    IXamlFrameworkSemanticBinder CreateSemanticBinder();

    IXamlFrameworkEmitter CreateEmitter();

    ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers();

    XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options);

    string? BuildHotReloadAssemblyMetadataHandlerSource(bool hasXamlInputs, GeneratorOptions options);
}
