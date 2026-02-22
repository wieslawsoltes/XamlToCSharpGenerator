using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator;

internal static class FrameworkGeneratorTestHarness
{
    public static (
        Compilation UpdatedCompilation,
        ImmutableArray<Diagnostic> Diagnostics,
        GeneratorDriverRunResult RunResult) RunGenerator(
        IIncrementalGenerator generator,
        CSharpCompilation compilation,
        IReadOnlyList<(string Path, string Text, string SourceItemGroup, string TargetPath)> additionalXamlFiles,
        IEnumerable<KeyValuePair<string, string>>? additionalBuildOptions = null)
    {
        var options = new List<KeyValuePair<string, string>>
        {
            new("build_property.XamlSourceGenBackend", "SourceGen"),
            new("build_property.XamlSourceGenEnabled", "true")
        };

        if (additionalBuildOptions is not null)
        {
            options.AddRange(additionalBuildOptions);
        }

        var additionalTexts = additionalXamlFiles
            .Select(static file => new InMemoryAdditionalText(file.Path, file.Text))
            .ToImmutableArray();
        var additionalFileMetadata = additionalXamlFiles
            .Select(static file =>
            {
                var values = new List<KeyValuePair<string, string>>
                {
                    new("build_metadata.AdditionalFiles.SourceItemGroup", file.SourceItemGroup),
                    new("build_metadata.AdditionalFiles.TargetPath", file.TargetPath)
                };

                return (file.Path, Values: (IEnumerable<KeyValuePair<string, string>>)values);
            })
            .ToImmutableArray();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: additionalTexts,
            optionsProvider: new TestAnalyzerConfigOptionsProvider(options, additionalFileMetadata));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out var diagnostics);
        return (updatedCompilation, diagnostics, driver.GetRunResult());
    }
}
