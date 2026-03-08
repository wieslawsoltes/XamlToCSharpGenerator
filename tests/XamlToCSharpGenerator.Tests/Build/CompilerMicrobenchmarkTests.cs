using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit.Abstractions;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.NoUi.Framework;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public sealed class CompilerMicrobenchmarkTests
{
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    private readonly ITestOutputHelper _output;

    public CompilerMicrobenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [PerfFact]
    public Task Parser_ObjectNode_AttributeScan_Outperforms_Baseline()
    {
        var element = XElement.Parse(
            """
            <TextBlock xmlns="https://github.com/avaloniaui"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                       x:Key="PrimaryText"
                       x:Name="TitleBlock"
                       x:FieldModifier="Public"
                       x:DataType="vm:MainViewModel"
                       x:CompileBindings="True"
                       x:FactoryMethod="Create"
                       x:TypeArguments="x:String"
                       Name="IgnoredFallbackName"
                       Width="240"
                       Height="32"
                       HorizontalAlignment="Center">
                Hello
                <Run />
                World
            </TextBlock>
            """,
            LoadOptions.PreserveWhitespace);

        const int iterations = 20_000;
        _ = BaselineParserHotPath(element);
        _ = OptimizedParserHotPath(element);

        var baseline = Measure(iterations, static target => BaselineParserHotPath(target), element);
        var optimized = Measure(iterations, static target => OptimizedParserHotPath(target), element);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("parser-object-node", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("parser-object-node", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_DiagnosticFilter_CommonPath_Outperforms_Baseline()
    {
        var diagnostics = Enumerable.Range(0, 10_000)
            .Select(static index => new DiagnosticInfo(
                "AXSG9999",
                "No suppression path " + index,
                "MainView.axaml",
                index + 1,
                1,
                false))
            .ToImmutableArray();
        var keys = ImmutableHashSet.Create(StringComparer.Ordinal, "Theme.Primary");
        const int iterations = 4_000;

        _ = BaselineApplyGlobalParityDiagnosticFilters(diagnostics, keys);
        _ = OptimizedApplyGlobalParityDiagnosticFilters(diagnostics, keys);

        var baseline = MeasureBestOf(5, iterations, static state => BaselineApplyGlobalParityDiagnosticFilters(state.Diagnostics, state.Keys).Length, (Diagnostics: diagnostics, Keys: keys));
        var optimized = MeasureBestOf(5, iterations, static state => OptimizedApplyGlobalParityDiagnosticFilters(state.Diagnostics, state.Keys).Length, (Diagnostics: diagnostics, Keys: keys));

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("compiler-host-diagnostic-filter", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-diagnostic-filter", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_UriInvalidation_Stamp_Outperforms_Baseline_Scan()
    {
        var uri = "file:///tmp/Active.axaml";
        var allUris = CreateMockUris(uri, totalUris: 2_000, matchingEvery: 5);
        var generationState = new GenerationState(uri);
        const int iterations = 20_000;

        _ = BaselineInvalidate(allUris, uri);
        _ = OptimizedInvalidate(generationState);

        var baseline = Measure(iterations, static state => BaselineInvalidate(state.Uris, state.Uri), (Uris: allUris, Uri: uri));
        var optimized = Measure(iterations, static state => OptimizedInvalidate(state), generationState);

        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.20,
            FormatFailure("language-service-invalidate", baseline, optimized, 0.20));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-invalidate", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task AvaloniaFeatureEnricher_SinglePass_Outperforms_Baseline()
    {
        var root = XElement.Parse(CreateFeatureInventoryXaml(20), LoadOptions.PreserveWhitespace);
        var state = root;
        const int iterations = 1_000;

        _ = BaselineFeatureEnricherHotPath(state);
        _ = OptimizedFeatureEnricherHotPath(state);

        var baseline = Measure(iterations, static value => BaselineFeatureEnricherHotPath(value), state);
        var optimized = Measure(iterations, static value => OptimizedFeatureEnricherHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.75,
            FormatFailure("avalonia-feature-enricher", baseline, optimized, 0.75));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("avalonia-feature-enricher", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_ClrMemberResolver_Outperforms_Baseline()
    {
        var compilation = CreateClrMemberResolutionCompilation();
        var typeSymbol = compilation.GetTypeByMetadataName("PerfModel.Level4");
        Assert.NotNull(typeSymbol);
        const int iterations = 50_000;

        _ = BaselineClrMemberResolverHotPath(typeSymbol!);
        _ = OptimizedClrMemberResolverHotPath(typeSymbol!);

        var baseline = MeasureBestOf(5, iterations, static symbol => BaselineClrMemberResolverHotPath(symbol), typeSymbol!);
        var optimized = MeasureBestOf(5, iterations, static symbol => OptimizedClrMemberResolverHotPath(symbol), typeSymbol!);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.85,
            FormatFailure("language-service-clr-member-resolver", baseline, optimized, 0.85));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-clr-member-resolver", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_ProjectSourceSnapshot_WarmScan_Outperforms_Baseline()
    {
        using var workspace = CreateReferenceScanWorkspace(fileCount: 72);
        var state = CreateProjectSourceScanState(workspace.RootPath, workspace.ProjectPath, workspace.CurrentFilePath);
        const int iterations = 4_000;

        _ = BaselineProjectSourceScanHotPath(state);
        _ = OptimizedProjectSourceScanHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineProjectSourceScanHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedProjectSourceScanHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.85,
            FormatFailure("language-service-project-source-scan", baseline, optimized, 0.85));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-project-source-scan", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_ProjectIncludeMetadata_WarmCache_Outperforms_Baseline()
    {
        using var workspace = CreateProjectIncludeScanWorkspace(patternCount: 10, filesPerPattern: 12, noiseFilesPerDirectory: 40);
        var state = CreateProjectIncludeScanState(workspace.ProjectPath);
        const int iterations = 5_000;

        _ = BaselineProjectIncludeMetadataHotPath(state);
        _ = OptimizedProjectIncludeMetadataHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineProjectIncludeMetadataHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedProjectIncludeMetadataHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.35,
            FormatFailure("language-service-project-include-metadata", baseline, optimized, 0.35));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-project-include-metadata", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_ProjectPathResolution_Cache_Outperforms_Baseline()
    {
        using var workspace = CreateProjectResolutionWorkspace(depth: 10);
        var state = (WorkspaceRoot: workspace.RootPath, CurrentFilePath: workspace.CurrentFilePath);
        const int iterations = 30_000;

        XamlReferenceService.ClearCachesForTesting();
        _ = BaselineResolveProjectPath(state.WorkspaceRoot, state.CurrentFilePath);
        _ = XamlReferenceService.ResolveProjectPath(null, state.CurrentFilePath);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineResolveProjectPath(value.WorkspaceRoot, value.CurrentFilePath)?.Length ?? 0, state);

        XamlReferenceService.ClearCachesForTesting();
        _ = XamlReferenceService.ResolveProjectPath(null, state.CurrentFilePath);
        var optimized = MeasureBestOf(5, iterations, static value => XamlReferenceService.ResolveProjectPath(null, value.CurrentFilePath)?.Length ?? 0, state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.30,
            FormatFailure("language-service-project-path-resolution", baseline, optimized, 0.30));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-project-path-resolution", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_DiagnosticFilter_NoSuppression_Avoids_Rebuild()
    {
        var diagnostics = CreateLanguageServiceDiagnostics(count: 10_000, source: "Other");
        var options = new XamlLanguageServiceOptions(
            WorkspaceRoot: "/tmp",
            IncludeCompilationDiagnostics: false,
            IncludeSemanticDiagnostics: false);
        const int iterations = 4_000;

        _ = BaselineLanguageServiceDiagnosticFilter(diagnostics, options);
        _ = OptimizedLanguageServiceDiagnosticFilter(diagnostics, options);

        var baseline = MeasureBestOf(5, iterations, static state => BaselineLanguageServiceDiagnosticFilter(state.Diagnostics, state.Options).Length, (Diagnostics: diagnostics, Options: options));
        var optimized = MeasureBestOf(5, iterations, static state => OptimizedLanguageServiceDiagnosticFilter(state.Diagnostics, state.Options).Length, (Diagnostics: diagnostics, Options: options));

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.55,
            FormatFailure("language-service-diagnostic-filter", baseline, optimized, 0.55));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.05,
            FormatAllocationFailure("language-service-diagnostic-filter", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_InlayHintRangeFilter_FullCoverage_Avoids_Rebuild()
    {
        var hints = CreateInlayHints(count: 10_000);
        var requestedRange = new SourceRange(new SourcePosition(0, 0), new SourcePosition(10_000, 8));
        const int iterations = 4_000;

        _ = BaselineInlayHintRangeFilter(hints, requestedRange);
        _ = OptimizedInlayHintRangeFilter(hints, requestedRange);

        var baseline = MeasureBestOf(5, iterations, static state => BaselineInlayHintRangeFilter(state.Hints, state.Range).Length, (Hints: hints, Range: requestedRange));
        var optimized = MeasureBestOf(5, iterations, static state => OptimizedInlayHintRangeFilter(state.Hints, state.Range).Length, (Hints: hints, Range: requestedRange));

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.35,
            FormatFailure("language-service-inlay-filter", baseline, optimized, 0.35));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.05,
            FormatAllocationFailure("language-service-inlay-filter", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_ReferenceSort_ArraySort_Outperforms_Baseline()
    {
        var references = CreateReferenceSortState(count: 12_000);
        const int iterations = 2_000;

        _ = BaselineSortReferences(references);
        _ = OptimizedSortReferences(references);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineSortReferences(value).Length, references);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedSortReferences(value).Length, references);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.75,
            FormatFailure("language-service-reference-sort", baseline, optimized, 0.75));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-reference-sort", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_SharedAnalysisOptionsCache_Reduces_Allocations()
    {
        var options = new XamlLanguageServiceOptions(
            WorkspaceRoot: "/tmp/solution",
            IncludeCompilationDiagnostics: false,
            IncludeSemanticDiagnostics: false);
        var state = new SharedAnalysisOptionsState(
            options,
            new ConcurrentDictionary<string, XamlLanguageServiceOptions>(StringComparer.Ordinal));
        const int iterations = 50_000;

        _ = BaselineSharedAnalysisOptions(options);
        _ = OptimizedSharedAnalysisOptions(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineSharedAnalysisOptions(value).GetHashCode(), options);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedSharedAnalysisOptions(value).GetHashCode(), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 1.75,
            FormatFailure("language-service-shared-analysis-options", baseline, optimized, 1.75));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.05,
            FormatAllocationFailure("language-service-shared-analysis-options", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_XamlInputSnapshot_Normalization_Outperforms_Baseline()
    {
        var state = CreateCompilerHostSnapshotState(totalInputs: 6_000);
        const int iterations = 800;

        _ = BaselineBuildUniqueXamlInputSnapshot(state.XamlInputs);
        _ = XamlSourceGeneratorCompilerHost.BuildUniqueXamlInputSnapshot(state.XamlInputs);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineBuildUniqueXamlInputSnapshot(value.XamlInputs).Length, state);
        var optimized = MeasureBestOf(5, iterations, static value => XamlSourceGeneratorCompilerHost.BuildUniqueXamlInputSnapshot(value.XamlInputs).Length, state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.65,
            FormatFailure("compiler-host-xaml-input-snapshot", baseline, optimized, 0.65));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-xaml-input-snapshot", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_PathNormalization_Outperforms_Baseline()
    {
        var paths = CreateCompilerHostPathNormalizationInputs(totalInputs: 4_000);
        const int iterations = 1_200;

        _ = BaselineNormalizePathBatch(paths);
        _ = OptimizedNormalizePathBatch(paths);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineNormalizePathBatch(value), paths);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedNormalizePathBatch(value), paths);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("compiler-host-path-normalization", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.45,
            FormatAllocationFailure("compiler-host-path-normalization", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_IncludePathNormalization_Outperforms_Baseline()
    {
        var paths = CreateCompilerHostIncludePathNormalizationInputs(totalInputs: 4_000);
        const int iterations = 1_200;

        _ = BaselineNormalizeIncludePathBatch(paths);
        _ = OptimizedNormalizeIncludePathBatch(paths);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineNormalizeIncludePathBatch(value), paths);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedNormalizeIncludePathBatch(value), paths);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.82,
            FormatFailure("compiler-host-include-path-normalization", baseline, optimized, 0.82));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.40,
            FormatAllocationFailure("compiler-host-include-path-normalization", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_IncludeSourceNormalization_Outperforms_Baseline()
    {
        var inputs = CreateCompilerHostIncludeSourceNormalizationInputs(totalInputs: 4_000);
        const int iterations = 1_200;

        _ = BaselineNormalizeIncludeSourceBatch(inputs);
        _ = OptimizedNormalizeIncludeSourceBatch(inputs);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineNormalizeIncludeSourceBatch(value), inputs);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedNormalizeIncludeSourceBatch(value), inputs);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("compiler-host-include-source-normalization", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.65,
            FormatAllocationFailure("compiler-host-include-source-normalization", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_IncludeUriResolution_Outperforms_Baseline()
    {
        var inputs = CreateCompilerHostIncludeUriResolutionInputs(totalInputs: 4_000);
        const int iterations = 1_200;

        _ = BaselineResolveIncludeUriBatch(inputs);
        _ = OptimizedResolveIncludeUriBatch(inputs);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineResolveIncludeUriBatch(value), inputs);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedResolveIncludeUriBatch(value), inputs);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.75,
            FormatFailure("compiler-host-include-uri-resolution", baseline, optimized, 0.75));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.35,
            FormatAllocationFailure("compiler-host-include-uri-resolution", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_ClassInference_Normalization_Outperforms_Baseline()
    {
        var state = CreateCompilerHostConventionInferenceState(totalInputs: 4_000);
        const int iterations = 1_200;

        _ = BaselineInferClassNames(state);
        _ = OptimizedInferClassNames(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineInferClassNames(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedInferClassNames(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.75,
            FormatFailure("compiler-host-class-inference", baseline, optimized, 0.75));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-class-inference", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_GlobalDocumentGraph_Outperforms_Baseline()
    {
        var state = CreateCompilerHostGlobalGraphState(totalDocuments: 320);
        const int iterations = 400;

        _ = BaselineAnalyzeGlobalDocumentGraph(state.Documents, state.Options);
        _ = XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph(state.Documents, state.Options);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineAnalyzeGlobalDocumentGraph(value.Documents, value.Options).Length, state);
        var optimized = MeasureBestOf(5, iterations, static value => XamlSourceGeneratorCompilerHost.AnalyzeGlobalDocumentGraph(value.Documents, value.Options).Length, state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 1.05,
            FormatFailure("compiler-host-global-document-graph", baseline, optimized, 1.05));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-global-document-graph", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_TransformConfigurationAggregation_Outperforms_Baseline()
    {
        var state = CreateCompilerHostTransformConfigurationState(aliasCount: 1_500, rawDocumentCount: 320);
        const int iterations = 700;

        _ = BaselineTransformConfigurationAggregationHotPath(state);
        _ = OptimizedTransformConfigurationAggregationHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineTransformConfigurationAggregationHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedTransformConfigurationAggregationHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("compiler-host-transform-configuration", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-transform-configuration", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_ConfigurationPrecedenceParsing_Outperforms_Baseline()
    {
        var rawValues = CreateConfigurationPrecedenceInputs(2_000);
        const int iterations = 800;

        _ = BaselineConfigurationPrecedenceHotPath(rawValues);
        _ = OptimizedConfigurationPrecedenceHotPath(rawValues);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineConfigurationPrecedenceHotPath(value), rawValues);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedConfigurationPrecedenceHotPath(value), rawValues);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("compiler-host-configuration-precedence", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.40,
            FormatAllocationFailure("compiler-host-configuration-precedence", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task CompilerHost_FrameworkServiceReuse_Outperforms_Baseline()
    {
        var state = CreateCompilerHostFrameworkServiceState();
        const int iterations = 40_000;

        _ = BaselineCompilerHostFrameworkServiceHotPath(state);
        _ = OptimizedCompilerHostFrameworkServiceHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineCompilerHostFrameworkServiceHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedCompilerHostFrameworkServiceHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.55,
            FormatFailure("compiler-host-framework-service-reuse", baseline, optimized, 0.55));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("compiler-host-framework-service-reuse", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_XmlDocumentCacheReuse_Outperforms_Baseline_Reparse()
    {
        using var workspace = CreateXmlCacheWorkspace(fileCount: 48);
        var state = CreateXmlCacheState(workspace.ProjectPath);
        const int iterations = 2_000;

        _ = BaselineXmlCacheHotPath(state);
        _ = OptimizedXmlCacheHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineXmlCacheHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedXmlCacheHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 1.05,
            FormatFailure("language-service-xml-cache-reuse", baseline, optimized, 1.05));
        Assert.True(
            optimized.AllocatedBytes < baseline.AllocatedBytes,
            FormatAllocationFailure("language-service-xml-cache-reuse", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task LanguageService_XmlCache_WeakRetention_Releases_Documents_After_Gc()
    {
        using var workspace = CreateXmlCacheWorkspace(fileCount: 48);
        var state = CreateXmlCacheState(workspace.ProjectPath);

        var baseline = MeasureStrongXmlCacheRetention(state);
        var optimized = MeasureWeakXmlCacheRetention(state);

        Assert.True(
            optimized.LiveDocumentCount <= baseline.LiveDocumentCount / 8,
            $"language-service-xml-cache-weak-retention failed:{Environment.NewLine}" +
            $"baseline live documents={baseline.LiveDocumentCount}{Environment.NewLine}" +
            $"optimized live documents={optimized.LiveDocumentCount}");

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task Emitter_StringAssembly_HotPaths_Outperform_Baseline()
    {
        var state = CreateEmitterStringAssemblyState();
        const int iterations = 5_000;

        Assert.Equal(
            BaselineBuildTypeofArgumentListExpression(state.KnownTypeNames),
            OptimizedBuildTypeofArgumentListExpression(state.KnownTypeNames));
        Assert.Equal(
            BaselineBuildParentStackExpression(state.ParentStackReferences),
            OptimizedBuildParentStackExpression(state.ParentStackReferences));
        Assert.Equal(
            BaselineBuildHotReloadCollectionCleanupDescriptorArrayExpression(state.CollectionMembers, state.RootTypeName),
            OptimizedBuildHotReloadCollectionCleanupDescriptorArrayExpression(state.CollectionMembers, state.RootTypeName));
        Assert.Equal(
            BaselineBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(state.ClrMembers, state.RootTypeName),
            OptimizedBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(state.ClrMembers, state.RootTypeName));
        Assert.Equal(
            BaselineBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(state.AvaloniaPropertyExpressions),
            OptimizedBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(state.AvaloniaPropertyExpressions));
        Assert.Equal(
            BaselineBuildHotReloadEventCleanupDescriptorArrayExpression(state.EventSubscriptions, state.RootTypeName),
            OptimizedBuildHotReloadEventCleanupDescriptorArrayExpression(state.EventSubscriptions, state.RootTypeName));

        _ = BaselineEmitterStringAssemblyHotPath(state);
        _ = OptimizedEmitterStringAssemblyHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineEmitterStringAssemblyHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedEmitterStringAssemblyHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 0.80,
            FormatFailure("emitter-string-assembly", baseline, optimized, 0.80));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes,
            FormatAllocationFailure("emitter-string-assembly", baseline, optimized));

        return Task.CompletedTask;
    }

    [PerfFact]
    public Task Emitter_EventBinding_Builders_Outperform_Baseline()
    {
        var state = CreateEmitterEventBindingState();
        const int iterations = 20_000;

        Assert.True(
            BaselineTryBuildEventBindingMethodInvocationExpression(
                state.SourceExpression,
                state.MethodCallPlan,
                state.SenderExpression,
                state.EventArgsExpression,
                out var baselineInvocation,
                out var baselineRequiresParameter));
        Assert.True(
            OptimizedTryBuildEventBindingMethodInvocationExpression(
                state.SourceExpression,
                state.MethodCallPlan,
                state.SenderExpression,
                state.EventArgsExpression,
                out var optimizedInvocation,
                out var optimizedRequiresParameter));
        Assert.Equal(baselineInvocation, optimizedInvocation);
        Assert.Equal(baselineRequiresParameter, optimizedRequiresParameter);
        Assert.Equal(
            BaselineIsSimpleEventBindingMemberPath(state.MethodCallPlan.TargetPath),
            OptimizedIsSimpleEventBindingMemberPath(state.MethodCallPlan.TargetPath));

        _ = BaselineEmitterEventBindingHotPath(state);
        _ = OptimizedEmitterEventBindingHotPath(state);

        var baseline = MeasureBestOf(5, iterations, static value => BaselineEmitterEventBindingHotPath(value), state);
        var optimized = MeasureBestOf(5, iterations, static value => OptimizedEmitterEventBindingHotPath(value), state);

        Assert.Equal(baseline.Checksum, optimized.Checksum);
        Assert.True(
            optimized.Elapsed <= baseline.Elapsed * 1.75,
            FormatFailure("emitter-event-binding", baseline, optimized, 1.75));
        Assert.True(
            optimized.AllocatedBytes <= baseline.AllocatedBytes * 0.70,
            FormatAllocationFailure("emitter-event-binding", baseline, optimized));

        return Task.CompletedTask;
    }

    private static ImmutableArray<string> CreateMockUris(string matchingUri, int totalUris, int matchingEvery)
    {
        var builder = ImmutableArray.CreateBuilder<string>(totalUris);
        for (var index = 0; index < totalUris; index++)
        {
            builder.Add(index % matchingEvery == 0
                ? matchingUri
                : "file:///tmp/Other" + index.ToString() + ".axaml");
        }

        return builder.ToImmutable();
    }

    private static string CreateFeatureInventoryXaml(int repeatCount)
    {
        var builder = new StringBuilder();
        builder.Append(
            """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:Class="Demo.FeatureInventory">
            """);

        for (var index = 0; index < repeatCount; index++)
        {
            builder.Append(
                $@"  <UserControl.Resources>
    <SolidColorBrush x:Key=""Brush{index}"" Color=""Blue"" />
  </UserControl.Resources>
  <UserControl.Styles>
    <Style x:Key=""Style{index}"" Selector=""TextBlock.card{index}"" x:DataType=""x:String"" x:CompileBindings=""True"">
      <Setter Property=""Text"" Value=""Hello{index}"" />
      <Setter Property=""Background"">
        <Setter.Value>
          <SolidColorBrush Color=""Green"" />
        </Setter.Value>
      </Setter>
    </Style>
    <StyleInclude Source=""/Styles/Shared{index}.axaml"" />
  </UserControl.Styles>
  <UserControl.ControlTheme>
    <ControlTheme x:Key=""Theme{index}"" TargetType=""Border"" BasedOn=""{{StaticResource BaseTheme}}"" ThemeVariant=""Dark"">
      <Setter Property=""Background"" Value=""Red"" />
    </ControlTheme>
  </UserControl.ControlTheme>
  <UserControl.DataTemplates>
    <DataTemplate x:Key=""Template{index}"" x:DataType=""x:String"">
      <TextBlock Text=""{{Binding}}"" />
    </DataTemplate>
  </UserControl.DataTemplates>
");
        }

        builder.Append("</UserControl>");
        return builder.ToString();
    }

    private static int BaselineInvalidate(ImmutableArray<string> uris, string targetUri)
    {
        var removed = 0;
        for (var cacheIndex = 0; cacheIndex < 5; cacheIndex++)
        {
            for (var index = 0; index < uris.Length; index++)
            {
                if (string.Equals(uris[index], targetUri, StringComparison.Ordinal))
                {
                    removed++;
                }
            }
        }

        return removed;
    }

    private static int OptimizedInvalidate(GenerationState state)
    {
        if (!state.Generations.TryGetValue(state.Uri, out var generation))
        {
            generation = 0;
        }

        generation++;
        state.Generations[state.Uri] = generation;
        return generation;
    }

    private static ReferenceScanWorkspace CreateReferenceScanWorkspace(int fileCount)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-reference-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "PerfRefs.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"**/*.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        var currentFilePath = Path.Combine(rootPath, "MainView.axaml");
        File.WriteAllText(currentFilePath, CreateReferenceScanXaml("MainWindowViewModel", 0));

        for (var index = 1; index < fileCount; index++)
        {
            var filePath = Path.Combine(rootPath, "Pages", "Page" + index.ToString() + ".axaml");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, CreateReferenceScanXaml("MainWindowViewModel", index));
        }

        return new ReferenceScanWorkspace(rootPath, projectPath, currentFilePath);
    }

    private static string CreateReferenceScanXaml(string dataTypeName, int index)
    {
        return "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
               "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
               "xmlns:vm=\"using:TestApp.Controls\" " +
               "x:Class=\"TestApp.Views.Page" + index.ToString() + "\" " +
               "x:DataType=\"vm:" + dataTypeName + "\">\n" +
               "  <StackPanel>\n" +
               "    <TextBlock Text=\"{Binding FirstName}\" />\n" +
               "    <TextBlock Text=\"{Binding Customer.DisplayName}\" />\n" +
               "    <TextBlock Text=\"{= Count + 1}\" />\n" +
               "  </StackPanel>\n" +
               "</UserControl>\n";
    }

    private static ProjectSourceScanState CreateProjectSourceScanState(
        string workspaceRoot,
        string projectPath,
        string currentFilePath)
    {
        var candidatePaths = Directory
            .EnumerateFiles(workspaceRoot, "*.axaml", SearchOption.AllDirectories)
            .Select(static path => Path.GetFullPath(path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToImmutableArray();

        var sourceCache = new Dictionary<string, CachedProjectSourceEntry>(StringComparer.Ordinal);
        var snapshotBuilder = ImmutableArray.CreateBuilder<ProjectSourceSnapshotEntry>(candidatePaths.Length);

        foreach (var filePath in candidatePaths)
        {
            var text = File.ReadAllText(filePath);
            var documentUri = ToDocumentUri(filePath);
            var entry = new CachedProjectSourceEntry(documentUri, text, XmlParsed: true);
            sourceCache[filePath] = entry;
            snapshotBuilder.Add(new ProjectSourceSnapshotEntry(filePath, documentUri, text, XmlParsed: true));
        }

        var currentText = File.ReadAllText(currentFilePath);
        return new ProjectSourceScanState(
            workspaceRoot,
            projectPath,
            currentFilePath,
            ToDocumentUri(currentFilePath),
            currentText,
            candidatePaths,
            sourceCache,
            snapshotBuilder.MoveToImmutable());
    }

    private static ProjectIncludeScanWorkspace CreateProjectIncludeScanWorkspace(
        int patternCount,
        int filesPerPattern,
        int noiseFilesPerDirectory)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-project-include-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(rootPath, "project");
        var linkedRoot = Path.Combine(rootPath, "linked");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(linkedRoot);

        var projectPath = Path.Combine(projectDir, "PerfInclude.csproj");
        var itemBuilder = new StringBuilder();
        for (var patternIndex = 0; patternIndex < patternCount; patternIndex++)
        {
            var includeDirectory = Path.Combine(linkedRoot, "Set" + patternIndex.ToString(), "Nested");
            Directory.CreateDirectory(includeDirectory);

            itemBuilder.Append("    <AvaloniaXaml Include=\"../linked/Set");
            itemBuilder.Append(patternIndex);
            itemBuilder.Append("/**/*.axaml\" />\n");

            for (var fileIndex = 0; fileIndex < filesPerPattern; fileIndex++)
            {
                var xamlPath = Path.Combine(includeDirectory, "View" + fileIndex.ToString() + ".axaml");
                File.WriteAllText(xamlPath, "<Path xmlns=\"https://github.com/avaloniaui\" />");
            }

            for (var noiseIndex = 0; noiseIndex < noiseFilesPerDirectory; noiseIndex++)
            {
                var noisePath = Path.Combine(includeDirectory, "Noise" + noiseIndex.ToString() + ".txt");
                File.WriteAllText(noisePath, "noise");
            }
        }

        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            itemBuilder +
            "  </ItemGroup>\n" +
            "</Project>\n");

        return new ProjectIncludeScanWorkspace(rootPath, projectPath);
    }

    private static ProjectResolutionWorkspace CreateProjectResolutionWorkspace(int depth)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-project-resolution-bench-" + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(rootPath, "src", "App");
        Directory.CreateDirectory(projectDirectory);

        var projectPath = Path.Combine(projectDirectory, "PerfRefs.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var currentDirectory = projectDirectory;
        for (var index = 0; index < depth; index++)
        {
            currentDirectory = Path.Combine(currentDirectory, "Level" + index.ToString("D2"));
        }

        Directory.CreateDirectory(currentDirectory);
        var currentFilePath = Path.Combine(currentDirectory, "MainView.axaml");
        File.WriteAllText(currentFilePath, "<UserControl />");

        return new ProjectResolutionWorkspace(rootPath, currentFilePath);
    }

    private static XmlCacheWorkspace CreateXmlCacheWorkspace(int fileCount)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-xml-cache-bench-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "PerfXmlCache.csproj");
        File.WriteAllText(
            projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"Views/**/*.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>\n");

        var viewsDirectory = Path.Combine(rootPath, "Views");
        Directory.CreateDirectory(viewsDirectory);
        for (var index = 0; index < fileCount; index++)
        {
            var filePath = Path.Combine(viewsDirectory, "View" + index.ToString("D3") + ".axaml");
            File.WriteAllText(
                filePath,
                """
                <UserControl xmlns="https://github.com/avaloniaui">
                  <StackPanel>
                    <TextBlock Text="Hello" />
                    <Border />
                  </StackPanel>
                </UserControl>
                """);
        }

        return new XmlCacheWorkspace(rootPath, projectPath);
    }

    private static ProjectIncludeScanState CreateProjectIncludeScanState(string projectPath)
    {
        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var projectDirectory = Path.GetDirectoryName(normalizedProjectPath)!;
        var cachedPatterns = OptimizedBuildProjectIncludePatterns(normalizedProjectPath);
        var regexCache = new Dictionary<string, Regex>(StringComparer.Ordinal);
        foreach (var pattern in cachedPatterns)
        {
            var normalizedPattern = NormalizeIncludePattern(pattern);
            regexCache[normalizedPattern] = OptimizedBuildGlobRegex(normalizedPattern);
        }

        return new ProjectIncludeScanState(normalizedProjectPath, projectDirectory, cachedPatterns, regexCache);
    }

    private static ImmutableArray<XamlReferenceLocation> CreateReferenceSortState(int count)
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>(count);
        for (var index = 0; index < count; index++)
        {
            var uri = (index % 5) switch
            {
                0 => "file:///Views/B.axaml",
                1 => "file:///Views/A.axaml",
                2 => "file:///Views/C.axaml",
                3 => "file:///Views/A.axaml",
                _ => "file:///Views/D.axaml"
            };
            var line = (index * 17) % 240;
            var character = (index * 11) % 80;
            var endCharacter = character + 1 + (index % 7);
            builder.Add(new XamlReferenceLocation(
                uri,
                new SourceRange(
                    new SourcePosition(line, character),
                    new SourcePosition(line, endCharacter)),
                IsDeclaration: (index % 9) == 0));
        }

        return builder.MoveToImmutable();
    }

    private static CompilerHostSnapshotState CreateCompilerHostSnapshotState(int totalInputs)
    {
        var xamlBuilder = ImmutableArray.CreateBuilder<XamlFileInput>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            var logicalIndex = index % 2_000;
            var sourcePath = (index % 2) == 0
                ? "./Views/Page" + logicalIndex.ToString() + ".axaml"
                : "Views/Page" + logicalIndex.ToString() + ".axaml";
            var targetPath = (index % 3) == 0
                ? "Views/Page" + logicalIndex.ToString() + ".axaml"
                : "/tmp/build/AvaloniaResource/Views/Page" + logicalIndex.ToString() + ".axaml";

            xamlBuilder.Add(new XamlFileInput(
                sourcePath,
                targetPath,
                "AvaloniaXaml",
                "<UserControl x:Class=\"Perf.Page" + logicalIndex.ToString() + "\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />"));
        }

        return new CompilerHostSnapshotState(xamlBuilder.MoveToImmutable());
    }

    private static int BaselineProjectSourceScanHotPath(ProjectSourceScanState state)
    {
        var resolvedProjectPath = BaselineResolveProjectPath(state.WorkspaceRoot, state.CurrentFilePath);
        if (resolvedProjectPath is null)
        {
            return 0;
        }

        var checksum = resolvedProjectPath.Length + state.CurrentDocumentUri.Length + state.CurrentText.Length;
        var seen = new HashSet<string>(StringComparer.Ordinal) { state.CurrentFilePath };

        foreach (var filePath in state.CandidatePaths)
        {
            if (!seen.Add(filePath) ||
                !state.SourceCache.TryGetValue(filePath, out var cached))
            {
                continue;
            }

            var source = new ProjectSourceSnapshotEntry(filePath, cached.DocumentUri, cached.Text, cached.XmlParsed);
            checksum += source.DocumentUri.Length + source.Text.Length + (source.XmlParsed ? 1 : 0);
        }

        return checksum;
    }

    private static int OptimizedProjectSourceScanHotPath(ProjectSourceScanState state)
    {
        var resolvedProjectPath = OptimizedResolveProjectPath(state.WorkspaceRoot, state.CurrentFilePath);
        if (resolvedProjectPath is null)
        {
            return 0;
        }

        var checksum = resolvedProjectPath.Length + state.CurrentDocumentUri.Length + state.CurrentText.Length;
        foreach (var source in state.ProjectSnapshot)
        {
            if (string.Equals(source.FilePath, state.CurrentFilePath, StringComparison.Ordinal))
            {
                continue;
            }

            checksum += source.DocumentUri.Length + source.Text.Length + (source.XmlParsed ? 1 : 0);
        }

        return checksum;
    }

    private static int BaselineProjectIncludeMetadataHotPath(ProjectIncludeScanState state)
    {
        var checksum = 0;
        foreach (var includePattern in BaselineBuildProjectIncludePatterns(state.ProjectPath))
        {
            var normalizedPattern = NormalizeIncludePattern(includePattern);
            checksum += normalizedPattern.Length;

            var searchRoot = ResolveProjectIncludeSearchRoot(state.ProjectDirectory, normalizedPattern);
            checksum += searchRoot?.Length ?? 0;

            if (normalizedPattern.IndexOfAny(['*', '?']) >= 0)
            {
                checksum += BaselineBuildGlobRegex(normalizedPattern).ToString().Length;
            }
        }

        return checksum;
    }

    private static int OptimizedProjectIncludeMetadataHotPath(ProjectIncludeScanState state)
    {
        var checksum = 0;
        foreach (var includePattern in state.CachedPatterns)
        {
            var normalizedPattern = NormalizeIncludePattern(includePattern);
            checksum += normalizedPattern.Length;

            var searchRoot = ResolveProjectIncludeSearchRoot(state.ProjectDirectory, normalizedPattern);
            checksum += searchRoot?.Length ?? 0;

            if (normalizedPattern.IndexOfAny(['*', '?']) >= 0)
            {
                checksum += state.RegexCache[normalizedPattern].ToString().Length;
            }
        }

        return checksum;
    }

    private static ImmutableArray<XamlFileInput> BaselineBuildUniqueXamlInputSnapshot(
        ImmutableArray<XamlFileInput> inputs)
    {
        if (inputs.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlFileInput>.Empty;
        }

        var byPath = new Dictionary<string, XamlFileInput>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs
                     .OrderBy(static value => value.FilePath, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static value => value.TargetPath, StringComparer.OrdinalIgnoreCase))
        {
            var dedupeKey = NormalizeDedupePathForBenchmarks(input.FilePath);
            if (!byPath.TryGetValue(dedupeKey, out var existing))
            {
                byPath[dedupeKey] = input;
                continue;
            }

            if (!BaselineShouldPreferTargetPath(input.TargetPath, existing.TargetPath))
            {
                continue;
            }

            byPath[dedupeKey] = input;
        }

        return byPath.Values
            .OrderBy(static value => value.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static int BaselineNormalizePathBatch(ImmutableArray<string> paths)
    {
        var checksum = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            checksum += NormalizeDedupePathForBenchmarks(paths[index]).Length;
        }

        return checksum;
    }

    private static int BaselineNormalizeIncludePathBatch(ImmutableArray<string> paths)
    {
        var checksum = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            checksum += NormalizeIncludePathForBenchmarks(paths[index]).Length;
        }

        return checksum;
    }

    private static int BaselineNormalizeIncludeSourceBatch(ImmutableArray<string> sources)
    {
        var checksum = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            checksum += NormalizeIncludeSourceForBenchmarks(sources[index]).Length;
        }

        return checksum;
    }

    private static int BaselineResolveIncludeUriBatch(ImmutableArray<CompilerHostIncludeResolutionInput> inputs)
    {
        var checksum = 0;
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            if (TryResolveIncludeUriForBenchmarks(
                    input.IncludeSource,
                    input.CurrentTargetPath,
                    input.AssemblyName,
                    out var resolvedUri,
                    out var isProjectLocal))
            {
                checksum += resolvedUri.Length;
                if (isProjectLocal)
                {
                    checksum++;
                }
            }
        }

        return checksum;
    }

    private static ImmutableArray<XamlReferenceLocation> BaselineSortReferences(
        ImmutableArray<XamlReferenceLocation> references)
    {
        return references
            .OrderBy(static item => item.Uri, StringComparer.Ordinal)
            .ThenBy(static item => item.Range.Start.Line)
            .ThenBy(static item => item.Range.Start.Character)
            .ThenBy(static item => item.Range.End.Line)
            .ThenBy(static item => item.Range.End.Character)
            .ThenByDescending(static item => item.IsDeclaration)
            .ToImmutableArray();
    }

    private static ImmutableArray<XamlReferenceLocation> OptimizedSortReferences(
        ImmutableArray<XamlReferenceLocation> references)
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>(references.Length);
        builder.AddRange(references);
        return XamlReferenceService.SortReferencesDeterministically(builder);
    }

    private static int OptimizedNormalizePathBatch(ImmutableArray<string> paths)
    {
        var checksum = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            checksum += XamlSourceGeneratorCompilerHost.NormalizeDedupePath(paths[index]).Length;
        }

        return checksum;
    }

    private static int OptimizedNormalizeIncludePathBatch(ImmutableArray<string> paths)
    {
        var checksum = 0;
        for (var index = 0; index < paths.Length; index++)
        {
            checksum += XamlSourceGeneratorCompilerHost.NormalizeIncludePath(paths[index]).Length;
        }

        return checksum;
    }

    private static int OptimizedNormalizeIncludeSourceBatch(ImmutableArray<string> sources)
    {
        var checksum = 0;
        for (var index = 0; index < sources.Length; index++)
        {
            checksum += XamlSourceGeneratorCompilerHost.NormalizeIncludeSource(sources[index]).Length;
        }

        return checksum;
    }

    private static int OptimizedResolveIncludeUriBatch(ImmutableArray<CompilerHostIncludeResolutionInput> inputs)
    {
        var checksum = 0;
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            if (XamlSourceGeneratorCompilerHost.TryResolveIncludeUri(
                    input.IncludeSource,
                    input.CurrentTargetPath,
                    input.AssemblyName,
                    out var resolvedUri,
                    out var isProjectLocal))
            {
                checksum += resolvedUri.Length;
                if (isProjectLocal)
                {
                    checksum++;
                }
            }
        }

        return checksum;
    }

    private static int BaselineInferClassNames(CompilerHostConventionInferenceState state)
    {
        var checksum = 0;
        for (var index = 0; index < state.TargetPaths.Length; index++)
        {
            checksum += BaselineTryInferClassNameFromTargetPath(state.TargetPaths[index], state.Options)?.Length ?? 0;
        }

        return checksum;
    }

    private static int OptimizedInferClassNames(CompilerHostConventionInferenceState state)
    {
        var checksum = 0;
        for (var index = 0; index < state.TargetPaths.Length; index++)
        {
            checksum += XamlSourceGeneratorCompilerHost.TryInferClassNameFromTargetPath(state.TargetPaths[index], state.Options)?.Length ?? 0;
        }

        return checksum;
    }

    private static string? BaselineTryInferClassNameFromTargetPath(string targetPath, GeneratorOptions options)
    {
        var rootNamespace = BaselineNormalizeRootNamespace(options.RootNamespace ?? options.AssemblyName);
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            return null;
        }

        var effectiveTargetPath = targetPath;
        if (Path.IsPathRooted(effectiveTargetPath))
        {
            effectiveTargetPath = Path.GetFileName(effectiveTargetPath);
        }

        effectiveTargetPath = effectiveTargetPath.Replace('\\', '/').Trim();
        if (effectiveTargetPath.StartsWith("./", StringComparison.Ordinal))
        {
            effectiveTargetPath = effectiveTargetPath.Substring(2);
        }

        if (effectiveTargetPath.StartsWith("/", StringComparison.Ordinal))
        {
            effectiveTargetPath = effectiveTargetPath.Substring(1);
        }

        var fileName = Path.GetFileNameWithoutExtension(effectiveTargetPath);
        var normalizedClassName = BaselineNormalizeIdentifier(fileName, "GeneratedView");
        if (string.IsNullOrWhiteSpace(normalizedClassName))
        {
            return null;
        }

        var namespaceSegments = BaselineNormalizeNamespaceSegments(rootNamespace).ToList();
        var directory = Path.GetDirectoryName(effectiveTargetPath)?
            .Replace('\\', '/');

        if (directory is not null)
        {
            var directoryValue = directory.Trim();
            if (directoryValue.Length > 0)
            {
                foreach (var rawSegment in directoryValue.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var segment = rawSegment.Trim();
                    var normalizedSegment = BaselineNormalizeIdentifier(segment, null);
                    if (normalizedSegment is null || normalizedSegment.Length == 0)
                    {
                        continue;
                    }

                    namespaceSegments.Add(normalizedSegment);
                }
            }
        }

        return namespaceSegments.Count == 0
            ? normalizedClassName
            : string.Join(".", namespaceSegments) + "." + normalizedClassName;
    }

    private static string BaselineNormalizeRootNamespace(string? rootNamespace)
    {
        if (rootNamespace is null)
        {
            return string.Empty;
        }

        var trimmedRootNamespace = rootNamespace.Trim();
        if (trimmedRootNamespace.Length == 0)
        {
            return string.Empty;
        }

        var segments = BaselineNormalizeNamespaceSegments(trimmedRootNamespace);
        return segments.Length == 0
            ? string.Empty
            : string.Join(".", segments);
    }

    private static ImmutableArray<string> BaselineNormalizeNamespaceSegments(string rawNamespace)
    {
        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return ImmutableArray<string>.Empty;
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        foreach (var rawSegment in rawNamespace.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            var normalizedSegment = BaselineNormalizeIdentifier(segment, null);
            if (normalizedSegment is null || normalizedSegment.Length == 0)
            {
                continue;
            }

            segments.Add(normalizedSegment);
        }

        return segments.ToImmutable();
    }

    private static string? BaselineNormalizeIdentifier(string? token, string? fallback)
    {
        if (token is null)
        {
            return fallback;
        }

        var trimmedToken = token.Trim();
        if (trimmedToken.Length == 0)
        {
            return fallback;
        }

        Span<char> buffer = stackalloc char[trimmedToken.Length];
        var length = 0;
        foreach (var ch in trimmedToken)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                buffer[length++] = ch;
            }
        }

        if (length == 0)
        {
            return fallback;
        }

        var normalized = new string(buffer.Slice(0, length).ToArray());
        if (char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
    }

    private static string? BaselineResolveProjectPath(string workspaceRoot, string currentFilePath)
    {
        if (File.Exists(workspaceRoot))
        {
            return workspaceRoot;
        }

        if (Directory.Exists(workspaceRoot))
        {
            var directoryProject = Directory
                .EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (directoryProject is not null)
            {
                return Path.GetFullPath(directoryProject);
            }
        }

        var currentDirectory = Path.GetDirectoryName(currentFilePath);
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var projectFile = Directory
                .EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (projectFile is not null)
            {
                return Path.GetFullPath(projectFile);
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return null;
    }

    private static ImmutableArray<string> CreateCompilerHostPathNormalizationInputs(int totalInputs)
    {
        var builder = ImmutableArray.CreateBuilder<string>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            builder.Add((index % 8) switch
            {
                0 => $"Views\\Section{index % 23}\\Inner\\..\\Main{index % 17}.axaml",
                1 => $".\\Views\\Section{index % 19}\\.\\Main{index % 13}.axaml",
                2 => $"../Views/Section{index % 11}/../Main{index % 29}.axaml",
                3 => $"/tmp/work/Section{index % 31}/../Main{index % 7}.axaml",
                4 => $"//server/share/Section{index % 37}/Inner/../Main{index % 5}.axaml",
                5 => $"Views/Section{index % 41}//Nested///Main{index % 3}.axaml",
                6 => $"Styles/./Theme{index % 9}/../Shared/Palette{index % 27}.axaml",
                _ => $"..\\..\\Assets\\Section{index % 15}\\..\\Theme{index % 21}.axaml"
            });
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<string> CreateCompilerHostIncludePathNormalizationInputs(int totalInputs)
    {
        var builder = ImmutableArray.CreateBuilder<string>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            builder.Add((index % 8) switch
            {
                0 => $"./Views/Shared/../Cards//Card{index % 17}.axaml",
                1 => $@"Views\Shared\.\Card{index % 19}.axaml",
                2 => $"Themes/../Views/./Nested/Card{index % 23}.axaml",
                3 => $"../External/Views/../Cards/Card{index % 11}.axaml",
                4 => $"Views////Section{index % 13}//Card{index % 7}.axaml",
                5 => $"././Views/Section{index % 29}/../Card{index % 5}.axaml",
                6 => $"Views/Section{index % 31}/Sub/../../Card{index % 3}.axaml",
                _ => $"  ./Views/Section{index % 37}/Card{index % 2}.axaml  "
            });
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<string> CreateCompilerHostIncludeSourceNormalizationInputs(int totalInputs)
    {
        var builder = ImmutableArray.CreateBuilder<string>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            builder.Add((index % 8) switch
            {
                0 => "{x:Uri /Views/Theme.axaml}",
                1 => "{x:Uri Uri='/Views/Theme.axaml', Relative=true}",
                2 => "{Uri Value=\"/Views/Palette.axaml\"}",
                3 => "  {x:Uri   '/Views/Controls.axaml' }  ",
                4 => "{Binding ThemeSource}",
                5 => "/Views/Plain.axaml",
                6 => " avares://Demo/Views/Absolute.axaml ",
                _ => "{x:Uri Value='/Views/Nested/Theme.axaml', Extra='ignored'}"
            });
        }

        return builder.MoveToImmutable();
    }

    private static ImmutableArray<CompilerHostIncludeResolutionInput> CreateCompilerHostIncludeUriResolutionInputs(int totalInputs)
    {
        var builder = ImmutableArray.CreateBuilder<CompilerHostIncludeResolutionInput>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            builder.Add((index % 8) switch
            {
                0 => new CompilerHostIncludeResolutionInput(
                    $"/Views/Section{index % 17}/Theme{index % 13}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                1 => new CompilerHostIncludeResolutionInput(
                    $"../Shared/Theme{index % 23}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                2 => new CompilerHostIncludeResolutionInput(
                    $"avares://BenchmarkAssembly/Views/Section{index % 11}/Theme{index % 7}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                3 => new CompilerHostIncludeResolutionInput(
                    $"avares://External.Library/Views/Section{index % 11}/Theme{index % 7}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                4 => new CompilerHostIncludeResolutionInput(
                    $"https://example.com/theme/{index % 29}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                5 => new CompilerHostIncludeResolutionInput(
                    $"  {{x:Uri   '../Shared/Theme{index % 31}.axaml' }}  ",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                6 => new CompilerHostIncludeResolutionInput(
                    $"{{Uri Value=\"avares://BenchmarkAssembly/Views/Section{index % 13}/Theme{index % 5}.axaml\"}}",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly"),
                _ => new CompilerHostIncludeResolutionInput(
                    $"Styles/Theme{index % 37}.axaml",
                    $"Views/Pages/Page{index % 19}.axaml",
                    "BenchmarkAssembly")
            });
        }

        return builder.MoveToImmutable();
    }

    private static CompilerHostConventionInferenceState CreateCompilerHostConventionInferenceState(int totalInputs)
    {
        var targetPaths = ImmutableArray.CreateBuilder<string>(totalInputs);
        for (var index = 0; index < totalInputs; index++)
        {
            targetPaths.Add((index % 8) switch
            {
                0 => $"Views/Sub-View{index % 23}/Main Page{index % 17}.axaml",
                1 => $".\\Views\\Part {index % 19}\\Control-{index % 13}.axaml",
                2 => $"./Screens/9Pane{index % 11}/Inner/View {index % 29}.axaml",
                3 => $"/tmp/generated/Views/Main Page{index % 7}.axaml",
                4 => $"Styles/Theme-{index % 9}/Accent Palette{index % 27}.axaml",
                5 => $"../Views/Section {index % 31}/Nested-Panel/Main-Card {index % 5}.axaml",
                6 => $"Assets\\Dialogs\\Modal_{index % 15}\\Step {index % 21}.axaml",
                _ => $"./Controls/1Special/{index % 25}-Badge.axaml"
            });
        }

        var options = new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: true,
            HotReloadErrorResilienceEnabled: true,
            IdeHotReloadEnabled: true,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: false,
            AllowImplicitXmlnsDeclaration: false,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: true,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: " Demo.App . 9Shell ",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "BenchmarkAssembly");

        return new CompilerHostConventionInferenceState(targetPaths.MoveToImmutable(), options);
    }

    private static CompilerHostGlobalGraphState CreateCompilerHostGlobalGraphState(int totalDocuments)
    {
        var documents = ImmutableArray.CreateBuilder<XamlDocumentModel>(totalDocuments);
        for (var index = 0; index < totalDocuments; index++)
        {
            var targetPath = $"Views/Section{index % 17}/View{index}.axaml";
            var includes = ImmutableArray.CreateBuilder<XamlIncludeDefinition>(3);
            includes.Add(new XamlIncludeDefinition(
                "ResourceInclude",
                $"/Views/Section{(index + 1) % 17}/View{(index + 1) % totalDocuments}.axaml",
                "MergedDictionaries",
                string.Empty,
                4,
                9));
            includes.Add(new XamlIncludeDefinition(
                "ResourceInclude",
                $"../Section{(index + 2) % 17}/View{(index + 2) % totalDocuments}.axaml",
                "MergedDictionaries",
                string.Empty,
                5,
                9));

            if (index % 11 == 0)
            {
                includes.Add(new XamlIncludeDefinition(
                    "ResourceInclude",
                    "/Missing.axaml",
                    "MergedDictionaries",
                    string.Empty,
                    6,
                    9));
            }

            documents.Add(CreateGraphBenchmarkDocument(
                filePath: $"/tmp/project/{targetPath}",
                targetPath: targetPath,
                includes: includes.ToImmutable()));
        }

        var options = new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: false,
            HotReloadEnabled: true,
            HotReloadErrorResilienceEnabled: true,
            IdeHotReloadEnabled: true,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: false,
            AllowImplicitXmlnsDeclaration: false,
            ImplicitStandardXmlnsPrefixesEnabled: true,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Demo",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "BenchmarkAssembly");

        return new CompilerHostGlobalGraphState(documents.MoveToImmutable(), options);
    }

    private static CompilerHostTransformConfigurationState CreateCompilerHostTransformConfigurationState(
        int aliasCount,
        int rawDocumentCount)
    {
        var rawDocuments = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rawDocumentCount; index++)
        {
            var key = index % 2 == 0
                ? "type-" + index.ToString("D4") + ".json"
                : "TYPE-" + index.ToString("D4") + ".json";
            rawDocuments[key] = "{ \"index\": " + index + " }";
        }

        var baseTypeAliases = ImmutableArray.CreateBuilder<XamlTypeAliasRule>(aliasCount);
        var overlayTypeAliases = ImmutableArray.CreateBuilder<XamlTypeAliasRule>(aliasCount);
        var basePropertyAliases = ImmutableArray.CreateBuilder<XamlPropertyAliasRule>(aliasCount);
        var overlayPropertyAliases = ImmutableArray.CreateBuilder<XamlPropertyAliasRule>(aliasCount);

        for (var index = 0; index < aliasCount; index++)
        {
            var typeName = "Type" + index.ToString("D4");
            baseTypeAliases.Add(new XamlTypeAliasRule(
                "urn:test",
                typeName,
                "Legacy." + typeName,
                "legacy.rules",
                index + 1,
                1));

            var overlayTypeName = index % 3 == 0 ? typeName : "Overlay" + typeName;
            overlayTypeAliases.Add(new XamlTypeAliasRule(
                "urn:test",
                overlayTypeName,
                "Unified." + overlayTypeName,
                "config.json",
                index + 1,
                1));

            var propertyName = "Property" + index.ToString("D4");
            basePropertyAliases.Add(new XamlPropertyAliasRule(
                "Target" + index.ToString("D4"),
                propertyName,
                "Legacy" + propertyName,
                "legacy.rules",
                index + 1,
                1));

            var overlayTargetType = index % 4 == 0 ? "Target" + index.ToString("D4") : "OverlayTarget" + index.ToString("D4");
            var overlayPropertyName = index % 4 == 0 ? propertyName : "Overlay" + propertyName;
            overlayPropertyAliases.Add(new XamlPropertyAliasRule(
                overlayTargetType,
                overlayPropertyName,
                "Unified" + overlayPropertyName,
                "config.json",
                index + 1,
                1));
        }

        return new CompilerHostTransformConfigurationState(
            rawDocuments.ToImmutable(),
            new XamlTransformConfiguration(baseTypeAliases.MoveToImmutable(), basePropertyAliases.MoveToImmutable()),
            new XamlTransformConfiguration(overlayTypeAliases.MoveToImmutable(), overlayPropertyAliases.MoveToImmutable()),
            new BenchmarkTransformProvider());
    }

    private static ImmutableArray<string> CreateConfigurationPrecedenceInputs(int count)
    {
        var builder = ImmutableArray.CreateBuilder<string>(count);
        for (var index = 0; index < count; index++)
        {
            builder.Add((index % 4) switch
            {
                0 => "ProjectDefaultFile=80;File=110;MsBuild=220;Code=330",
                1 => " project-default = 90,\n file = 120,\r\n ms_build = 230,\n code = 340 ",
                2 => "default-file=70;BrokenSegment;Code=400;Unknown=999",
                _ => "ProjectDefault=95;File=NaN;MsBuild=205;Code=305"
            });
        }

        return builder.ToImmutable();
    }

    private static CompilerHostFrameworkServiceState CreateCompilerHostFrameworkServiceState()
    {
        var profile = NoUiFrameworkProfile.Instance;
        return new CompilerHostFrameworkServiceState(
            profile,
            profile.CreateSemanticBinder(),
            profile.CreateEmitter());
    }

    private static XmlCacheState CreateXmlCacheState(string projectPath)
    {
        var files = ImmutableArray.CreateBuilder<XmlCacheEntry>();
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.axaml", SearchOption.AllDirectories))
        {
            files.Add(new XmlCacheEntry(filePath, File.ReadAllText(filePath)));
        }

        return new XmlCacheState(files.ToImmutable());
    }

    private static XmlCacheRetentionResult MeasureStrongXmlCacheRetention(XmlCacheState state)
    {
        var documents = new XDocument[state.Files.Length];
        for (var index = 0; index < state.Files.Length; index++)
        {
            documents[index] = XDocument.Parse(state.Files[index].Text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }

        ForceFullCollection();

        var liveDocumentCount = 0;
        for (var index = 0; index < documents.Length; index++)
        {
            if (documents[index] is not null)
            {
                liveDocumentCount++;
            }
        }

        GC.KeepAlive(documents);
        return new XmlCacheRetentionResult(liveDocumentCount);
    }

    private static XmlCacheRetentionResult MeasureWeakXmlCacheRetention(XmlCacheState state)
    {
        XamlReferenceService.ClearCachesForTesting();
        for (var index = 0; index < state.Files.Length; index++)
        {
            var file = state.Files[index];
            _ = XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(file.FilePath, file.Text);
        }

        ForceFullCollection();
        return new XmlCacheRetentionResult(XamlReferenceService.CountLiveCachedXmlDocumentsForTesting());
    }

    private static void ForceFullCollection()
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var pressure = new byte[1024 * 1024 * 8];
            GC.KeepAlive(pressure);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static ImmutableArray<DiagnosticInfo> BaselineAnalyzeGlobalDocumentGraph(
        ImmutableArray<XamlDocumentModel> documents,
        GeneratorOptions options)
    {
        if (documents.IsDefaultOrEmpty)
        {
            return ImmutableArray<DiagnosticInfo>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? "UnknownAssembly";
        var entriesByUri = new Dictionary<string, GraphEntryForBenchmarks>(StringComparer.OrdinalIgnoreCase);
        var entriesByTargetPath = new Dictionary<string, GraphEntryForBenchmarks>(StringComparer.OrdinalIgnoreCase);
        var orderedEntries = new List<GraphEntryForBenchmarks>(documents.Length);

        foreach (var document in documents.OrderBy(static value => value.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (document.Precompile == false)
            {
                continue;
            }

            var normalizedTargetPath = NormalizeIncludePathForBenchmarks(document.TargetPath);
            if (normalizedTargetPath.Length == 0)
            {
                continue;
            }

            var buildUri = BuildUriForBenchmarks(assemblyName, normalizedTargetPath);
            var entry = new GraphEntryForBenchmarks(document, buildUri, normalizedTargetPath);
            if (entriesByTargetPath.ContainsKey(normalizedTargetPath))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0601",
                    $"Generated URI target '{normalizedTargetPath}' is produced by multiple AXAML files. Source-generated URI registration would conflict.",
                    document.FilePath,
                    document.RootObject.Line,
                    document.RootObject.Column,
                    true));
            }
            else
            {
                entriesByTargetPath[normalizedTargetPath] = entry;
            }

            if (entriesByUri.ContainsKey(buildUri))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0601",
                    $"Generated URI '{buildUri}' is registered by multiple AXAML files.",
                    document.FilePath,
                    document.RootObject.Line,
                    document.RootObject.Column,
                    true));
            }
            else
            {
                entriesByUri[buildUri] = entry;
                orderedEntries.Add(entry);
            }
        }

        var edgesBySource = new Dictionary<string, List<GraphEdgeForBenchmarks>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in orderedEntries)
        {
            foreach (var include in entry.Document.Includes)
            {
                if (!TryResolveIncludeUriForBenchmarks(
                        include.Source,
                        entry.NormalizedTargetPath,
                        assemblyName,
                        out var resolvedUri,
                        out var isProjectLocal) ||
                    !isProjectLocal)
                {
                    continue;
                }

                if (!entriesByUri.ContainsKey(resolvedUri))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0403",
                        $"Include source '{include.Source}' resolves to '{resolvedUri}', but no source-generated AXAML file was found for that URI.",
                        entry.Document.FilePath,
                        include.Line,
                        include.Column,
                        options.StrictMode));
                    continue;
                }

                if (!edgesBySource.TryGetValue(entry.BuildUri, out var edges))
                {
                    edges = new List<GraphEdgeForBenchmarks>();
                    edgesBySource[entry.BuildUri] = edges;
                }

                edges.Add(new GraphEdgeForBenchmarks(entry.BuildUri, resolvedUri, include.Source, include.Line, include.Column));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cycleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in edgesBySource.Keys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            DetectCycle(uri, new Stack<string>());
        }

        return diagnostics.ToImmutable();

        void DetectCycle(string sourceUri, Stack<string> path)
        {
            if (state.TryGetValue(sourceUri, out var currentState))
            {
                if (currentState == 1 || currentState == 2)
                {
                    return;
                }
            }

            state[sourceUri] = 1;
            path.Push(sourceUri);

            if (edgesBySource.TryGetValue(sourceUri, out var edges))
            {
                foreach (var edge in edges.OrderBy(static value => value.TargetUri, StringComparer.OrdinalIgnoreCase))
                {
                    if (state.TryGetValue(edge.TargetUri, out var targetState) && targetState == 1)
                    {
                        var cycleKey = edge.SourceUri + "->" + edge.TargetUri;
                        if (cycleKeys.Add(cycleKey) &&
                            entriesByUri.TryGetValue(edge.SourceUri, out var sourceEntry) &&
                            entriesByUri.TryGetValue(edge.TargetUri, out var targetEntry))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0404",
                                $"Include source '{edge.SourceText}' forms a cycle between '{sourceEntry.Document.TargetPath}' and '{targetEntry.Document.TargetPath}'.",
                                sourceEntry.Document.FilePath,
                                edge.Line,
                                edge.Column,
                                true));
                        }

                        continue;
                    }

                    if (!state.TryGetValue(edge.TargetUri, out targetState) || targetState == 0)
                    {
                        DetectCycle(edge.TargetUri, path);
                    }
                }
            }

            path.Pop();
            state[sourceUri] = 2;
        }
    }

    private static int BaselineTransformConfigurationAggregationHotPath(CompilerHostTransformConfigurationState state)
    {
        var checksum = 0;
        checksum += BaselineParseConfigurationTransformRuleInputs(state.RawTransformDocuments, state.TransformProvider).Length;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var merged = BaselineMergeTransformConfigurations(
            state.BaseConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles,
            state.OverlayConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.UnifiedConfigurationTypedObject,
            diagnostics);

        checksum += merged.TypeAliases.Length * 31;
        checksum += merged.PropertyAliases.Length * 17;
        checksum += diagnostics.Count;
        return checksum;
    }

    private static int OptimizedTransformConfigurationAggregationHotPath(CompilerHostTransformConfigurationState state)
    {
        var checksum = 0;
        checksum += XamlSourceGeneratorCompilerHost.ParseConfigurationTransformRuleInputs(state.RawTransformDocuments, state.TransformProvider).Length;

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var merged = XamlSourceGeneratorCompilerHost.MergeTransformConfigurations(
            state.BaseConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles,
            state.OverlayConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.UnifiedConfigurationTypedObject,
            diagnostics);

        checksum += merged.TypeAliases.Length * 31;
        checksum += merged.PropertyAliases.Length * 17;
        checksum += diagnostics.Count;
        return checksum;
    }

    private static ImmutableArray<XamlFrameworkTransformRuleResult> BaselineParseConfigurationTransformRuleInputs(
        ImmutableDictionary<string, string> rawTransformDocuments,
        IXamlFrameworkTransformProvider transformProvider)
    {
        if (rawTransformDocuments.Count == 0)
        {
            return ImmutableArray<XamlFrameworkTransformRuleResult>.Empty;
        }

        var results = ImmutableArray.CreateBuilder<XamlFrameworkTransformRuleResult>(rawTransformDocuments.Count);
        foreach (var pair in rawTransformDocuments.OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var filePath = BaselineBuildConfigurationTransformRuleDocumentPath(pair.Key);
            var parsed = transformProvider.ParseTransformRule(new XamlFrameworkTransformRuleInput(filePath, pair.Value));
            results.Add(parsed);
        }

        return results.ToImmutable();
    }

    private static XamlTransformConfiguration BaselineMergeTransformConfigurations(
        XamlTransformConfiguration baseConfiguration,
        XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind baseSourceKind,
        XamlTransformConfiguration overlayConfiguration,
        XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind overlaySourceKind,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (overlayConfiguration.TypeAliases.IsDefaultOrEmpty &&
            overlayConfiguration.PropertyAliases.IsDefaultOrEmpty)
        {
            return baseConfiguration;
        }

        var typeAliases = new Dictionary<string, BenchmarkTransformTypeAliasEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in baseConfiguration.TypeAliases)
        {
            typeAliases[BenchmarkBuildTypeAliasKey(alias)] = new BenchmarkTransformTypeAliasEntry(alias, baseSourceKind);
        }

        foreach (var alias in overlayConfiguration.TypeAliases)
        {
            var key = BenchmarkBuildTypeAliasKey(alias);
            if (typeAliases.TryGetValue(key, out var existing) &&
                existing.SourceKind == XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles &&
                overlaySourceKind != XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    "Type alias '" + alias.XmlNamespace + ":" + alias.XamlTypeName +
                    "' is declared in both legacy transform rule files and unified configuration. " +
                    "Unified configuration declaration from '" + alias.Source +
                    "' overrides legacy declaration from '" + existing.Alias.Source + "'.",
                    alias.Source,
                    alias.Line,
                    alias.Column,
                    false));
            }

            typeAliases[key] = new BenchmarkTransformTypeAliasEntry(alias, overlaySourceKind);
        }

        var propertyAliases = new Dictionary<string, BenchmarkTransformPropertyAliasEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var alias in baseConfiguration.PropertyAliases)
        {
            propertyAliases[BenchmarkBuildPropertyAliasKey(alias)] = new BenchmarkTransformPropertyAliasEntry(alias, baseSourceKind);
        }

        foreach (var alias in overlayConfiguration.PropertyAliases)
        {
            var key = BenchmarkBuildPropertyAliasKey(alias);
            if (propertyAliases.TryGetValue(key, out var existing) &&
                existing.SourceKind == XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles &&
                overlaySourceKind != XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    "Property alias '" + alias.TargetTypeName + ":" + alias.XamlPropertyName +
                    "' is declared in both legacy transform rule files and unified configuration. " +
                    "Unified configuration declaration from '" + alias.Source +
                    "' overrides legacy declaration from '" + existing.Alias.Source + "'.",
                    alias.Source,
                    alias.Line,
                    alias.Column,
                    false));
            }

            propertyAliases[key] = new BenchmarkTransformPropertyAliasEntry(alias, overlaySourceKind);
        }

        return new XamlTransformConfiguration(
            typeAliases
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.Value.Alias)
                .ToImmutableArray(),
            propertyAliases
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.Value.Alias)
                .ToImmutableArray());
    }

    private static string BaselineBuildConfigurationTransformRuleDocumentPath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "xaml-sourcegen.config.json::transform.rawTransformDocuments[<empty>]";
        }

        return "xaml-sourcegen.config.json::transform.rawTransformDocuments[" + key.Trim() + "]";
    }

    private static int BaselineCompilerHostFrameworkServiceHotPath(CompilerHostFrameworkServiceState state)
    {
        var binder = state.Profile.CreateSemanticBinder();
        var emitter = state.Profile.CreateEmitter();
        return (binder is not null ? 1 : 0) + (emitter is not null ? 1 : 0);
    }

    private static int OptimizedCompilerHostFrameworkServiceHotPath(CompilerHostFrameworkServiceState state)
    {
        return (state.CachedBinder is not null ? 1 : 0) + (state.CachedEmitter is not null ? 1 : 0);
    }

    private static int BaselineConfigurationPrecedenceHotPath(ImmutableArray<string> rawValues)
    {
        var checksum = 0;
        for (var index = 0; index < rawValues.Length; index++)
        {
            var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();
            var result = BaselineResolveConfigurationSourcePrecedence(rawValues[index], issues);
            checksum += result.ProjectDefaultFile;
            checksum += result.File;
            checksum += result.MsBuild;
            checksum += result.Code;
            checksum += issues.Count;
        }

        return checksum;
    }

    private static int OptimizedConfigurationPrecedenceHotPath(ImmutableArray<string> rawValues)
    {
        var checksum = 0;
        for (var index = 0; index < rawValues.Length; index++)
        {
            var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();
            var result = XamlSourceGeneratorCompilerHost.ResolveConfigurationSourcePrecedence(rawValues[index], issues);
            checksum += result.ProjectDefaultFile;
            checksum += result.File;
            checksum += result.MsBuild;
            checksum += result.Code;
            checksum += issues.Count;
        }

        return checksum;
    }

    private static XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence BaselineResolveConfigurationSourcePrecedence(
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence.Default;
        }

        var result = XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence.Default;
        var segments = rawValue.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawSegment in segments)
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
            {
                continue;
            }

            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    "AXSG0933",
                    XamlSourceGenConfigurationIssueSeverity.Warning,
                    "Invalid configuration precedence segment '" + segment +
                    "'. Expected 'ProjectDefaultFile=90;File=100;MsBuild=200;Code=300'.",
                    "MsBuild"));
                continue;
            }

            var key = segment.Substring(0, separatorIndex).Trim();
            var valueText = segment.Substring(separatorIndex + 1).Trim();
            if (!int.TryParse(valueText, out var precedence))
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    "AXSG0933",
                    XamlSourceGenConfigurationIssueSeverity.Warning,
                    "Invalid precedence value '" + valueText + "' for key '" + key + "'. Expected an integer.",
                    "MsBuild"));
                continue;
            }

            switch (BaselineNormalizeConfigurationPrecedenceKey(key))
            {
                case XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.ProjectDefaultFile:
                    result = result with { ProjectDefaultFile = precedence };
                    break;
                case XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.File:
                    result = result with { File = precedence };
                    break;
                case XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.MsBuild:
                    result = result with { MsBuild = precedence };
                    break;
                case XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.Code:
                    result = result with { Code = precedence };
                    break;
                default:
                    issues.Add(new XamlSourceGenConfigurationIssue(
                        "AXSG0933",
                        XamlSourceGenConfigurationIssueSeverity.Warning,
                        "Unknown configuration precedence key '" + key + "'. Supported keys: ProjectDefaultFile, File, MsBuild, Code.",
                        "MsBuild"));
                    break;
            }
        }

        return result;
    }

    private static XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey BaselineNormalizeConfigurationPrecedenceKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.Unknown;
        }

        var normalized = key.Trim().Replace("_", string.Empty).Replace("-", string.Empty);
        if (normalized.Equals("projectdefaultfile", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("projectdefault", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("defaultfile", StringComparison.OrdinalIgnoreCase))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.ProjectDefaultFile;
        }

        if (normalized.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.File;
        }

        if (normalized.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.MsBuild;
        }

        if (normalized.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.Code;
        }

        return XamlSourceGeneratorCompilerHost.ConfigurationPrecedenceKey.Unknown;
    }

    private static int BaselineXmlCacheHotPath(XmlCacheState state)
    {
        var checksum = 0;
        foreach (var file in state.Files)
        {
            _ = XDocument.Parse(file.Text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            _ = XDocument.Parse(file.Text, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            checksum += 1;
        }

        return checksum;
    }

    private static int OptimizedXmlCacheHotPath(XmlCacheState state)
    {
        XamlReferenceService.ClearCachesForTesting();
        var checksum = 0;
        foreach (var file in state.Files)
        {
            if (XamlReferenceService.ReusesParsedXmlForStaleSourceSnapshot(file.FilePath, file.Text))
            {
                checksum += 1;
            }
        }

        return checksum;
    }

    private static string BenchmarkBuildTypeAliasKey(XamlTypeAliasRule alias)
    {
        return alias.XmlNamespace + ":" + alias.XamlTypeName;
    }

    private static string BenchmarkBuildPropertyAliasKey(XamlPropertyAliasRule alias)
    {
        return alias.TargetTypeName + ":" + alias.XamlPropertyName;
    }

    private static string? OptimizedResolveProjectPath(string workspaceRoot, string currentFilePath)
    {
        if (File.Exists(workspaceRoot))
        {
            return workspaceRoot;
        }

        if (Directory.Exists(workspaceRoot))
        {
            var directoryProject = TryFindFirstProjectFile(workspaceRoot);
            if (directoryProject is not null)
            {
                return directoryProject;
            }
        }

        var currentDirectory = Path.GetDirectoryName(currentFilePath);
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var projectFile = TryFindFirstProjectFile(currentDirectory);
            if (projectFile is not null)
            {
                return projectFile;
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return null;
    }

    private static string? TryFindFirstProjectFile(string directoryPath)
    {
        string? bestMatch = null;
        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.csproj", SearchOption.TopDirectoryOnly))
        {
            if (bestMatch is null ||
                StringComparer.OrdinalIgnoreCase.Compare(filePath, bestMatch) < 0)
            {
                bestMatch = filePath;
            }
        }

        return bestMatch is null
            ? null
            : Path.GetFullPath(bestMatch);
    }

    private static ImmutableArray<string> BaselineBuildProjectIncludePatterns(string projectPath)
    {
        XDocument projectDocument;
        try
        {
            projectDocument = XDocument.Load(projectPath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var itemElement in projectDocument.Descendants())
        {
            if (!BaselineIsXamlItemElement(itemElement.Name.LocalName))
            {
                continue;
            }

            var includeValue = itemElement.Attribute("Include")?.Value
                ?? itemElement.Attribute("Update")?.Value;
            if (!string.IsNullOrWhiteSpace(includeValue))
            {
                builder.Add(includeValue);
            }
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> OptimizedBuildProjectIncludePatterns(string projectPath)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            using var reader = XmlReader.Create(
                projectPath,
                new XmlReaderSettings
                {
                    CloseInput = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true
                });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || !BaselineIsXamlItemElement(reader.LocalName))
                {
                    continue;
                }

                var includeValue = reader.GetAttribute("Include") ?? reader.GetAttribute("Update");
                if (!string.IsNullOrWhiteSpace(includeValue) && seen.Add(includeValue))
                {
                    builder.Add(includeValue);
                }
            }
        }
        catch
        {
            return ImmutableArray<string>.Empty;
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<string> BaselineExpandProjectIncludePattern(string projectDirectory, string includeValue)
    {
        var normalizedPattern = NormalizeIncludePattern(includeValue);
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            yield break;
        }

        if (normalizedPattern.IndexOfAny(['*', '?']) < 0)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedPattern));
            if (BaselineIsXamlFile(candidatePath) && File.Exists(candidatePath))
            {
                yield return candidatePath;
            }

            yield break;
        }

        var searchRoot = ResolveProjectIncludeSearchRoot(projectDirectory, normalizedPattern);
        if (searchRoot is null || !Directory.Exists(searchRoot))
        {
            yield break;
        }

        var patternRegex = BaselineBuildGlobRegex(normalizedPattern);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                yield break;
            }

            if (!BaselineIsXamlFile(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/');
            if (patternRegex.IsMatch(relativePath))
            {
                yield return Path.GetFullPath(filePath);
            }
        }
    }

    private static IEnumerable<string> OptimizedExpandProjectIncludePattern(
        string projectDirectory,
        string normalizedPattern,
        Regex patternRegex)
    {
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            yield break;
        }

        if (normalizedPattern.IndexOfAny(['*', '?']) < 0)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedPattern));
            if (BaselineIsXamlFile(candidatePath) && File.Exists(candidatePath))
            {
                yield return candidatePath;
            }

            yield break;
        }

        var searchRoot = ResolveProjectIncludeSearchRoot(projectDirectory, normalizedPattern);
        if (searchRoot is null || !Directory.Exists(searchRoot))
        {
            yield break;
        }

        foreach (var filePath in EnumerateOptimizedCandidateXamlFiles(searchRoot))
        {
            var relativePath = Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/');
            if (patternRegex.IsMatch(relativePath))
            {
                yield return Path.GetFullPath(filePath);
            }
        }
    }

    private static string NormalizeIncludePattern(string includeValue)
    {
        return includeValue
            .Trim()
            .Replace('\\', '/');
    }

    private static string? ResolveProjectIncludeSearchRoot(string projectDirectory, string includePattern)
    {
        var wildcardIndex = includePattern.IndexOfAny(['*', '?']);
        var basePrefix = wildcardIndex <= 0 ? string.Empty : includePattern.Substring(0, wildcardIndex);
        if (string.IsNullOrWhiteSpace(basePrefix))
        {
            return projectDirectory;
        }

        var normalizedBase = basePrefix.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedBase))
        {
            return Path.GetDirectoryName(normalizedBase);
        }

        var combined = Path.GetFullPath(Path.Combine(projectDirectory, normalizedBase));
        if (Directory.Exists(combined))
        {
            return combined;
        }

        return Path.GetDirectoryName(combined);
    }

    private static Regex BaselineBuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);

        return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static Regex OptimizedBuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);

        return new Regex(
            "^" + escaped + "$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static IEnumerable<string> EnumerateOptimizedCandidateXamlFiles(string rootDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var filePath in EnumerateFilesCore(rootDirectory, "*.axaml"))
            {
                yield return filePath;
            }

            foreach (var filePath in EnumerateFilesCore(rootDirectory, "*.xaml"))
            {
                yield return filePath;
            }

            yield break;
        }

        foreach (var filePath in EnumerateFilesCore(rootDirectory, "*.*"))
        {
            if (BaselineIsXamlFile(filePath))
            {
                yield return filePath;
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesCore(string rootDirectory, string searchPattern)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootDirectory, searchPattern, SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                yield break;
            }

            yield return filePath;
        }
    }

    private static bool BaselineIsXamlItemElement(string localName)
    {
        return string.Equals(localName, "AvaloniaXaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Page", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "None", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Content", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "EmbeddedResource", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "AdditionalFiles", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BaselineIsXamlFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDocumentUri(string filePath)
    {
        return new Uri(Path.GetFullPath(filePath)).AbsoluteUri;
    }

    private static int BaselineFeatureEnricherHotPath(XElement root)
    {
        var resources = root.DescendantsAndSelf()
            .Where(static element => TryGetDirectiveValueBaseline(element, "Key") is not null)
            .Select(static element => element.Name.LocalName.Length + TryGetDirectiveValueBaseline(element, "Key")!.Length)
            .ToArray();
        var templates = root.DescendantsAndSelf()
            .Where(static element => IsTemplateElementBaseline(element.Name.LocalName))
            .Select(static element =>
                element.Name.LocalName.Length +
                (TryGetDirectiveValueBaseline(element, "Key")?.Length ?? 0) +
                (element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value.Length ?? 0) +
                (TryGetDirectiveValueBaseline(element, "DataType")?.Length ?? 0))
            .ToArray();
        var styles = root.DescendantsAndSelf()
            .Where(static element => element.Name.LocalName == "Style")
            .Select(static element =>
                (element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Selector")?.Value.Length ?? 0) +
                CollectSettersBaseline(element))
            .ToArray();
        var themes = root.DescendantsAndSelf()
            .Where(static element => element.Name.LocalName == "ControlTheme")
            .Select(static element =>
                (element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "TargetType")?.Value.Length ?? 0) +
                CollectSettersBaseline(element))
            .ToArray();
        var includes = root.DescendantsAndSelf()
            .Where(static element =>
                element.Name.LocalName == "ResourceInclude" ||
                element.Name.LocalName == "StyleInclude" ||
                element.Name.LocalName == "MergeResourceInclude")
            .Select(static element =>
                element.Name.LocalName.Length +
                (element.Attributes().FirstOrDefault(attribute =>
                    attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Source")?.Value.Length ?? 0))
            .ToArray();

        return resources.Sum() + templates.Sum() + styles.Sum() + themes.Sum() + includes.Sum();
    }

    private static int OptimizedFeatureEnricherHotPath(XElement root)
    {
        var checksum = 0;

        foreach (var element in root.DescendantsAndSelf())
        {
            var info = CollectFeatureElementInfo(element);

            if (!string.IsNullOrWhiteSpace(info.Key))
            {
                checksum += info.Key!.Length + element.Name.LocalName.Length;
            }

            if (IsTemplateElementBaseline(element.Name.LocalName))
            {
                checksum += element.Name.LocalName.Length +
                            (info.Key?.Length ?? 0) +
                            (info.TargetType?.Length ?? 0) +
                            (info.DataType?.Length ?? 0);
            }

            if (element.Name.LocalName == "Style")
            {
                checksum += (info.Selector?.Length ?? 0) + CollectSettersOptimized(element);
            }

            if (element.Name.LocalName == "ControlTheme")
            {
                checksum += (info.TargetType?.Length ?? 0) + CollectSettersOptimized(element);
            }

            if (element.Name.LocalName == "ResourceInclude" ||
                element.Name.LocalName == "StyleInclude" ||
                element.Name.LocalName == "MergeResourceInclude")
            {
                checksum += element.Name.LocalName.Length + (info.Source?.Length ?? 0);
            }
        }

        return checksum;
    }

    private static int CollectSettersBaseline(XElement scope)
    {
        var checksum = 0;
        foreach (var element in scope.Elements())
        {
            if (element.Name.LocalName == "Setter")
            {
                checksum += BaselineSetterChecksum(element);
                continue;
            }

            if (XamlPropertyTokenSemantics.IsPropertyElementName(
                    element.Name.LocalName,
                    "Setters",
                    ownerToken: scope.Name.LocalName))
            {
                foreach (var nestedSetter in element.Elements().Where(x => x.Name.LocalName == "Setter"))
                {
                    checksum += BaselineSetterChecksum(nestedSetter);
                }
            }
        }

        return checksum;
    }

    private static int CollectSettersOptimized(XElement scope)
    {
        var checksum = 0;
        foreach (var element in scope.Elements())
        {
            if (element.Name.LocalName == "Setter")
            {
                checksum += OptimizedSetterChecksum(element);
                continue;
            }

            if (XamlPropertyTokenSemantics.IsPropertyElementName(
                    element.Name.LocalName,
                    "Setters",
                    ownerToken: scope.Name.LocalName))
            {
                foreach (var nestedSetter in element.Elements())
                {
                    if (nestedSetter.Name.LocalName == "Setter")
                    {
                        checksum += OptimizedSetterChecksum(nestedSetter);
                    }
                }
            }
        }

        return checksum;
    }

    private static int BaselineSetterChecksum(XElement setter)
    {
        var propertyName = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Property")?.Value;
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return 0;
        }

        var value = setter.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Value")?.Value;
        if (value is null)
        {
            var firstValueElement = setter.Elements().FirstOrDefault();
            if (firstValueElement is not null &&
                XamlPropertyTokenSemantics.IsPropertyElementName(
                    firstValueElement.Name.LocalName,
                    "Value",
                    ownerToken: setter.Name.LocalName) &&
                firstValueElement.Elements().FirstOrDefault() is { } innerValueElement)
            {
                value = innerValueElement.ToString(SaveOptions.DisableFormatting);
            }
            else
            {
                value = firstValueElement?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
            }
        }

        return propertyName.Length + value.Length;
    }

    private static int OptimizedSetterChecksum(XElement setter)
    {
        string? propertyName = null;
        string? value = null;
        XElement? firstValueElement = null;

        foreach (var attribute in setter.Attributes())
        {
            if (attribute.Name.NamespaceName.Length != 0)
            {
                continue;
            }

            if (attribute.Name.LocalName == "Property")
            {
                propertyName = attribute.Value;
            }
            else if (attribute.Name.LocalName == "Value")
            {
                value = attribute.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return 0;
        }

        if (value is null)
        {
            foreach (var child in setter.Elements())
            {
                firstValueElement = child;
                break;
            }

            if (firstValueElement is not null &&
                XamlPropertyTokenSemantics.IsPropertyElementName(
                    firstValueElement.Name.LocalName,
                    "Value",
                    ownerToken: setter.Name.LocalName))
            {
                XElement? innerValueElement = null;
                foreach (var child in firstValueElement.Elements())
                {
                    innerValueElement = child;
                    break;
                }

                value = innerValueElement is not null
                    ? innerValueElement.ToString(SaveOptions.DisableFormatting)
                    : string.Empty;
            }
            else
            {
                value = firstValueElement?.ToString(SaveOptions.DisableFormatting) ?? string.Empty;
            }
        }

        return propertyName.Length + value.Length;
    }

    private static string? TryGetDirectiveValueBaseline(XElement element, string directiveName)
    {
        return element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == directiveName)?.Value;
    }

    private static bool IsTemplateElementBaseline(string localName)
    {
        return localName == "DataTemplate"
               || localName == "ControlTemplate"
               || localName == "ItemsPanelTemplate"
               || localName == "TreeDataTemplate";
    }

    private static BenchmarkFeatureElementInfo CollectFeatureElementInfo(XElement element)
    {
        string? key = null;
        string? dataType = null;
        string? selector = null;
        string? targetType = null;
        string? source = null;

        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.NamespaceName.Length == 0)
            {
                switch (attribute.Name.LocalName)
                {
                    case "Selector":
                        selector = attribute.Value;
                        break;
                    case "TargetType":
                        targetType = attribute.Value;
                        break;
                    case "Source":
                        source = attribute.Value;
                        break;
                }

                continue;
            }

            if (attribute.Name.Namespace == Xaml2006)
            {
                switch (attribute.Name.LocalName)
                {
                    case "Key":
                        key = attribute.Value;
                        break;
                    case "DataType":
                        dataType = attribute.Value;
                        break;
                }
            }
        }

        return new BenchmarkFeatureElementInfo(key, dataType, selector, targetType, source);
    }

    private static CSharpCompilation CreateClrMemberResolutionCompilation()
    {
        const string source = """
                              using System.Collections.Generic;

                              namespace PerfModel
                              {
                                  public class BaseLevel
                                  {
                                      public string DeepProp { get; set; } = string.Empty;
                                      public List<string> Names { get; } = new List<string>();
                                      public string BuildLabel() => DeepProp;
                                  }

                                  public class Level1 : BaseLevel { }
                                  public class Level2 : Level1 { }
                                  public class Level3 : Level2 { }
                                  public class Level4 : Level3 { }
                              }
                              """;

        return CSharpCompilation.Create(
            "PerfModel",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static int BaselineClrMemberResolverHotPath(INamedTypeSymbol typeSymbol)
    {
        var property = BaselineResolveInstanceProperty(typeSymbol, "deepprop");
        var method = BaselineResolveParameterlessMethod(typeSymbol, "buildlabel");
        var indexed = property is null ? null : BaselineResolveIndexedElementType(property.Type);

        return (property?.Name.Length ?? 0) +
               (method?.Name.Length ?? 0) +
               (indexed?.Name.Length ?? 0);
    }

    private static int OptimizedClrMemberResolverHotPath(INamedTypeSymbol typeSymbol)
    {
        var property = OptimizedResolveInstanceProperty(typeSymbol, "deepprop");
        var method = OptimizedResolveParameterlessMethod(typeSymbol, "buildlabel");
        var indexed = property is null ? null : OptimizedResolveIndexedElementType(property.Type);

        return (property?.Name.Length ?? 0) +
               (method?.Name.Length ?? 0) +
               (indexed?.Name.Length ?? 0);
    }

    private static IPropertySymbol? BaselineResolveInstanceProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var exact = current.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(static property => !property.IsStatic && !property.IsIndexer && property.GetMethod is not null);
            if (exact is not null)
            {
                return exact;
            }

            var fallback = current.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property =>
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null &&
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null)
            {
                return fallback;
            }
        }

        return null;
    }

    private static IMethodSymbol? BaselineResolveParameterlessMethod(INamedTypeSymbol? typeSymbol, string methodName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var exact = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(static method =>
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid);
            if (exact is not null)
            {
                return exact;
            }

            var fallback = current.GetMembers()
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method =>
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid &&
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase));
            if (fallback is not null)
            {
                return fallback;
            }
        }

        return null;
    }

    private static ITypeSymbol? BaselineResolveIndexedElementType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return null;
        }

        var indexer = namedType.GetMembers().OfType<IPropertySymbol>()
            .FirstOrDefault(static property => property.IsIndexer && property.Parameters.Length > 0);
        if (indexer?.Type is not null)
        {
            return indexer.Type;
        }

        var listInterface = namedType.AllInterfaces
            .FirstOrDefault(static candidate =>
                candidate is { Name: "IList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IReadOnlyList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IEnumerable", TypeArguments.Length: 1 });
        return listInterface?.TypeArguments.FirstOrDefault();
    }

    private static IPropertySymbol? OptimizedResolveInstanceProperty(INamedTypeSymbol? typeSymbol, string propertyName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol property &&
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null)
                {
                    return property;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol property &&
                    !property.IsStatic &&
                    !property.IsIndexer &&
                    property.GetMethod is not null &&
                    string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property;
                }
            }
        }

        return null;
    }

    private static IMethodSymbol? OptimizedResolveParameterlessMethod(INamedTypeSymbol? typeSymbol, string methodName)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(methodName))
            {
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid)
                {
                    return method;
                }
            }

            foreach (var member in current.GetMembers())
            {
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    !method.IsImplicitlyDeclared &&
                    method.MethodKind == MethodKind.Ordinary &&
                    method.Parameters.Length == 0 &&
                    !method.ReturnsVoid &&
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static ITypeSymbol? OptimizedResolveIndexedElementType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return null;
        }

        foreach (var member in namedType.GetMembers())
        {
            if (member is IPropertySymbol property &&
                property.IsIndexer &&
                property.Parameters.Length > 0 &&
                property.Type is not null)
            {
                return property.Type;
            }
        }

        foreach (var candidate in namedType.AllInterfaces)
        {
            if (candidate is { Name: "IList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IReadOnlyList", TypeArguments.Length: 1 } ||
                candidate is { Name: "IEnumerable", TypeArguments.Length: 1 })
            {
                return candidate.TypeArguments[0];
            }
        }

        return null;
    }

    private static EmitterStringAssemblyState CreateEmitterStringAssemblyState()
    {
        var knownTypeBuilder = ImmutableArray.CreateBuilder<string>(32);
        for (var index = 0; index < 32; index++)
        {
            knownTypeBuilder.Add("global::Demo.Generated.Type" + index.ToString("D2"));
        }

        var parentStackBuilder = ImmutableArray.CreateBuilder<string>(18);
        for (var index = 17; index >= 0; index--)
        {
            parentStackBuilder.Add(index == 0 ? "__root" : "__n" + index.ToString());
        }

        var collectionMembersBuilder = ImmutableArray.CreateBuilder<string>(16);
        var clrMembersBuilder = ImmutableArray.CreateBuilder<string>(16);
        var avaloniaPropertyBuilder = ImmutableArray.CreateBuilder<string>(16);
        for (var index = 0; index < 16; index++)
        {
            collectionMembersBuilder.Add("CollectionSlot" + index.ToString("D2"));
            clrMembersBuilder.Add("NamedField" + index.ToString("D2"));
            avaloniaPropertyBuilder.Add("global::Demo.Controls.Control" + index.ToString("D2") + ".TrackedProperty");
        }

        var eventBuilder = ImmutableArray.CreateBuilder<ResolvedEventSubscription>(16);
        for (var index = 0; index < 8; index++)
        {
            eventBuilder.Add(new ResolvedEventSubscription(
                "ClrEvent" + index.ToString("D2"),
                "OnClrEvent" + index.ToString("D2"),
                ResolvedEventSubscriptionKind.ClrEvent,
                null,
                null,
                null,
                1,
                1));
            eventBuilder.Add(new ResolvedEventSubscription(
                "RoutedEvent" + index.ToString("D2"),
                "OnRoutedEvent" + index.ToString("D2"),
                ResolvedEventSubscriptionKind.RoutedEvent,
                "global::Avalonia.Input.InputElement",
                "PointerPressedEvent",
                "global::System.EventHandler",
                1,
                1));
        }

        return new EmitterStringAssemblyState(
            knownTypeBuilder.MoveToImmutable(),
            parentStackBuilder.MoveToImmutable(),
            collectionMembersBuilder.MoveToImmutable(),
            clrMembersBuilder.MoveToImmutable(),
            avaloniaPropertyBuilder.MoveToImmutable(),
            eventBuilder.MoveToImmutable());
    }

    private static EmitterEventBindingState CreateEmitterEventBindingState()
    {
        var argumentBuilder = ImmutableArray.CreateBuilder<ResolvedEventBindingMethodArgument>(12);
        for (var index = 0; index < 12; index++)
        {
            var kind = (index % 3) switch
            {
                0 => ResolvedEventBindingMethodArgumentKind.Sender,
                1 => ResolvedEventBindingMethodArgumentKind.EventArgs,
                _ => ResolvedEventBindingMethodArgumentKind.Parameter
            };
            var typeName = kind switch
            {
                ResolvedEventBindingMethodArgumentKind.Sender => "object?",
                ResolvedEventBindingMethodArgumentKind.EventArgs => "global::System.EventArgs",
                _ => "global::System.String"
            };
            argumentBuilder.Add(new ResolvedEventBindingMethodArgument(kind, typeName));
        }

        return new EmitterEventBindingState(
            "source",
            new ResolvedEventBindingMethodCallPlan(
                "Owner.Document.Commands.Current.SavePipeline",
                "SaveWithArgs",
                argumentBuilder.MoveToImmutable()),
            "__sender",
            "__eventArgs");
    }

    private static int BaselineEmitterStringAssemblyHotPath(EmitterStringAssemblyState state)
    {
        var knownTypes = BaselineBuildTypeofArgumentListExpression(state.KnownTypeNames);
        var parentStack = BaselineBuildParentStackExpression(state.ParentStackReferences);
        var collectionDescriptors = BaselineBuildHotReloadCollectionCleanupDescriptorArrayExpression(state.CollectionMembers, state.RootTypeName);
        var clrDescriptors = BaselineBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(state.ClrMembers, state.RootTypeName);
        var avaloniaDescriptors = BaselineBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(state.AvaloniaPropertyExpressions);
        var eventDescriptors = BaselineBuildHotReloadEventCleanupDescriptorArrayExpression(state.EventSubscriptions, state.RootTypeName);

        return knownTypes.Length +
               parentStack.Length +
               collectionDescriptors.Length +
               clrDescriptors.Length +
               avaloniaDescriptors.Length +
               eventDescriptors.Length;
    }

    private static int OptimizedEmitterStringAssemblyHotPath(EmitterStringAssemblyState state)
    {
        var knownTypes = OptimizedBuildTypeofArgumentListExpression(state.KnownTypeNames);
        var parentStack = OptimizedBuildParentStackExpression(state.ParentStackReferences);
        var collectionDescriptors = OptimizedBuildHotReloadCollectionCleanupDescriptorArrayExpression(state.CollectionMembers, state.RootTypeName);
        var clrDescriptors = OptimizedBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(state.ClrMembers, state.RootTypeName);
        var avaloniaDescriptors = OptimizedBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(state.AvaloniaPropertyExpressions);
        var eventDescriptors = OptimizedBuildHotReloadEventCleanupDescriptorArrayExpression(state.EventSubscriptions, state.RootTypeName);

        return knownTypes.Length +
               parentStack.Length +
               collectionDescriptors.Length +
               clrDescriptors.Length +
               avaloniaDescriptors.Length +
               eventDescriptors.Length;
    }

    private static int BaselineEmitterEventBindingHotPath(EmitterEventBindingState state)
    {
        var invocation = BaselineTryBuildEventBindingMethodInvocationExpression(
            state.SourceExpression,
            state.MethodCallPlan,
            state.SenderExpression,
            state.EventArgsExpression,
            out var expression,
            out var requiresParameter)
            ? expression
            : string.Empty;
        var memberPathValid = BaselineIsSimpleEventBindingMemberPath(state.MethodCallPlan.TargetPath);

        return invocation.Length +
               (requiresParameter ? 1 : 0) +
               (memberPathValid ? 1 : 0);
    }

    private static int OptimizedEmitterEventBindingHotPath(EmitterEventBindingState state)
    {
        var invocation = OptimizedTryBuildEventBindingMethodInvocationExpression(
            state.SourceExpression,
            state.MethodCallPlan,
            state.SenderExpression,
            state.EventArgsExpression,
            out var expression,
            out var requiresParameter)
            ? expression
            : string.Empty;
        var memberPathValid = OptimizedIsSimpleEventBindingMemberPath(state.MethodCallPlan.TargetPath);

        return invocation.Length +
               (requiresParameter ? 1 : 0) +
               (memberPathValid ? 1 : 0);
    }

    private static string BaselineBuildTypeofArgumentListExpression(ImmutableArray<string> typeNames)
    {
        return string.Join(", ", typeNames.Select(static typeName => "typeof(" + typeName + ")"));
    }

    private static string OptimizedBuildTypeofArgumentListExpression(ImmutableArray<string> typeNames)
    {
        var builder = new StringBuilder(EstimateDelimitedListCapacity(typeNames, 8));
        for (var index = 0; index < typeNames.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("typeof(");
            builder.Append(typeNames[index]);
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static string BaselineBuildParentStackExpression(ImmutableArray<string> parentStackReferences)
    {
        if (parentStackReferences.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<object>()";
        }

        return "new object[] { " + string.Join(", ", parentStackReferences) + " }";
    }

    private static string OptimizedBuildParentStackExpression(ImmutableArray<string> parentStackReferences)
    {
        if (parentStackReferences.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<object>()";
        }

        var builder = new StringBuilder(24 + EstimateDelimitedListCapacity(parentStackReferences));
        builder.Append("new object[] { ");
        AppendDelimitedList(builder, parentStackReferences);
        builder.Append(" }");
        return builder.ToString();
    }

    private static int EstimateDelimitedListCapacity(ImmutableArray<string> values, int itemWrapperLength = 0)
    {
        if (values.IsDefaultOrEmpty)
        {
            return 0;
        }

        var capacity = 0;
        for (var index = 0; index < values.Length; index++)
        {
            capacity += values[index]?.Length ?? 0;
            capacity += itemWrapperLength;
            if (index > 0)
            {
                capacity += 2;
            }
        }

        return capacity;
    }

    private static void AppendDelimitedList(StringBuilder builder, ImmutableArray<string> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[index]);
        }
    }

    private static string BaselineBuildHotReloadCollectionCleanupDescriptorArrayExpression(
        ImmutableArray<string> members,
        string rootTypeName)
    {
        var descriptors = new List<string>();
        foreach (var member in members)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(memberName))
            {
                continue;
            }

            descriptors.Add(
                "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(" +
                "\"" + memberName + "\", " +
                "static __instance => { if (__instance is " + rootTypeName + " __typed) { global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadStateTracker.TryClearCollection(__typed." + memberName + "); } })");
        }

        if (descriptors.Count == 0)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { " +
               string.Join(", ", descriptors) + " }";
    }

    private static string OptimizedBuildHotReloadCollectionCleanupDescriptorArrayExpression(
        ImmutableArray<string> members,
        string rootTypeName)
    {
        var builder = new StringBuilder();
        var hasDescriptors = false;
        foreach (var member in members)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(memberName))
            {
                continue;
            }

            AppendCleanupDescriptorArraySeparator(builder, ref hasDescriptors);
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(memberName);
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(rootTypeName);
            builder.Append(" __typed) { global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadStateTracker.TryClearCollection(__typed.");
            builder.Append(memberName);
            builder.Append("); } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static string BaselineBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<string> members,
        string rootTypeName)
    {
        var descriptors = new List<string>();
        foreach (var member in members)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(memberName))
            {
                continue;
            }

            descriptors.Add(
                "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(" +
                "\"" + memberName + "\", " +
                "static __instance => { if (__instance is " + rootTypeName + " __typed) { __typed." + memberName + " = default!; } })");
        }

        if (descriptors.Count == 0)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { " +
               string.Join(", ", descriptors) + " }";
    }

    private static string OptimizedBuildHotReloadClrPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<string> members,
        string rootTypeName)
    {
        var builder = new StringBuilder();
        var hasDescriptors = false;
        foreach (var member in members)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(memberName))
            {
                continue;
            }

            AppendCleanupDescriptorArraySeparator(builder, ref hasDescriptors);
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(memberName);
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(rootTypeName);
            builder.Append(" __typed) { __typed.");
            builder.Append(memberName);
            builder.Append(" = default!; } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static string BaselineBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<string> propertyExpressions)
    {
        var descriptors = new List<string>();
        foreach (var propertyExpression in propertyExpressions)
        {
            if (string.IsNullOrWhiteSpace(propertyExpression))
            {
                continue;
            }

            var token = propertyExpression.Trim();
            descriptors.Add(
                "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(" +
                "\"" + token + "\", " +
                "static __instance => { if (__instance is global::Avalonia.AvaloniaObject __avaloniaObject) { __avaloniaObject.ClearValue(" +
                token + "); } })");
        }

        if (descriptors.Count == 0)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { " +
               string.Join(", ", descriptors) + " }";
    }

    private static string OptimizedBuildHotReloadAvaloniaPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<string> propertyExpressions)
    {
        var builder = new StringBuilder();
        var hasDescriptors = false;
        foreach (var propertyExpression in propertyExpressions)
        {
            if (string.IsNullOrWhiteSpace(propertyExpression))
            {
                continue;
            }

            var token = propertyExpression.Trim();
            AppendCleanupDescriptorArraySeparator(builder, ref hasDescriptors);
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(token);
            builder.Append("\", static __instance => { if (__instance is global::Avalonia.AvaloniaObject __avaloniaObject) { __avaloniaObject.ClearValue(");
            builder.Append(token);
            builder.Append("); } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static string BaselineBuildHotReloadEventCleanupDescriptorArrayExpression(
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        string rootTypeName)
    {
        var descriptors = new List<string>();
        foreach (var eventSubscription in eventSubscriptions)
        {
            if (string.IsNullOrWhiteSpace(eventSubscription.HandlerMethodName) ||
                !IsValidIdentifierForGeneratedMemberAccessBenchmark(eventSubscription.HandlerMethodName))
            {
                continue;
            }

            var token = BuildHotReloadEventTokenBenchmark(eventSubscription);
            if (eventSubscription.Kind == ResolvedEventSubscriptionKind.ClrEvent)
            {
                if (string.IsNullOrWhiteSpace(eventSubscription.EventName) ||
                    !IsValidIdentifierForGeneratedMemberAccessBenchmark(eventSubscription.EventName))
                {
                    continue;
                }

                descriptors.Add(
                    "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(" +
                    "\"" + token + "\", " +
                    "static __instance => { if (__instance is " + rootTypeName + " __typed) { __typed." +
                    eventSubscription.EventName + " -= __typed." + eventSubscription.HandlerMethodName + "; } })");
                continue;
            }

            if (string.IsNullOrWhiteSpace(eventSubscription.RoutedEventOwnerTypeName) ||
                string.IsNullOrWhiteSpace(eventSubscription.RoutedEventFieldName) ||
                string.IsNullOrWhiteSpace(eventSubscription.RoutedEventHandlerTypeName))
            {
                continue;
            }

            var routedEventFieldName = eventSubscription.RoutedEventFieldName;
            if (routedEventFieldName is null)
            {
                continue;
            }

            routedEventFieldName = routedEventFieldName.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(routedEventFieldName))
            {
                continue;
            }

            descriptors.Add(
                "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(" +
                "\"" + token + "\", " +
                "static __instance => { if (__instance is " + rootTypeName + " __typed) { __typed.RemoveHandler(" +
                eventSubscription.RoutedEventOwnerTypeName + "." + routedEventFieldName + ", (" +
                eventSubscription.RoutedEventHandlerTypeName + ")__typed." + eventSubscription.HandlerMethodName + "); } })");
        }

        if (descriptors.Count == 0)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        return "new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { " +
               string.Join(", ", descriptors) + " }";
    }

    private static string OptimizedBuildHotReloadEventCleanupDescriptorArrayExpression(
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        string rootTypeName)
    {
        var builder = new StringBuilder();
        var hasDescriptors = false;

        foreach (var eventSubscription in eventSubscriptions)
        {
            if (string.IsNullOrWhiteSpace(eventSubscription.HandlerMethodName) ||
                !IsValidIdentifierForGeneratedMemberAccessBenchmark(eventSubscription.HandlerMethodName))
            {
                continue;
            }

            var token = BuildHotReloadEventTokenBenchmark(eventSubscription);
            if (eventSubscription.Kind == ResolvedEventSubscriptionKind.ClrEvent)
            {
                if (string.IsNullOrWhiteSpace(eventSubscription.EventName) ||
                    !IsValidIdentifierForGeneratedMemberAccessBenchmark(eventSubscription.EventName))
                {
                    continue;
                }

                AppendCleanupDescriptorArraySeparator(builder, ref hasDescriptors);
                builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
                builder.Append(token);
                builder.Append("\", static __instance => { if (__instance is ");
                builder.Append(rootTypeName);
                builder.Append(" __typed) { __typed.");
                builder.Append(eventSubscription.EventName);
                builder.Append(" -= __typed.");
                builder.Append(eventSubscription.HandlerMethodName);
                builder.Append("; } })");
                continue;
            }

            if (string.IsNullOrWhiteSpace(eventSubscription.RoutedEventOwnerTypeName) ||
                string.IsNullOrWhiteSpace(eventSubscription.RoutedEventFieldName) ||
                string.IsNullOrWhiteSpace(eventSubscription.RoutedEventHandlerTypeName))
            {
                continue;
            }

            var routedEventFieldName = eventSubscription.RoutedEventFieldName.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccessBenchmark(routedEventFieldName))
            {
                continue;
            }

            AppendCleanupDescriptorArraySeparator(builder, ref hasDescriptors);
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(token);
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(rootTypeName);
            builder.Append(" __typed) { __typed.RemoveHandler(");
            builder.Append(eventSubscription.RoutedEventOwnerTypeName);
            builder.Append('.');
            builder.Append(routedEventFieldName);
            builder.Append(", (");
            builder.Append(eventSubscription.RoutedEventHandlerTypeName);
            builder.Append(")__typed.");
            builder.Append(eventSubscription.HandlerMethodName);
            builder.Append("); } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static void AppendCleanupDescriptorArraySeparator(StringBuilder builder, ref bool hasDescriptors)
    {
        if (!hasDescriptors)
        {
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
            hasDescriptors = true;
        }
        else
        {
            builder.Append(", ");
        }
    }

    private static bool BaselineTryBuildEventBindingMethodInvocationExpression(
        string sourceExpression,
        ResolvedEventBindingMethodCallPlan methodCallPlan,
        string senderExpression,
        string eventArgsExpression,
        out string invocationExpression,
        out bool requiresParameter)
    {
        invocationExpression = string.Empty;
        requiresParameter = false;

        if (string.IsNullOrWhiteSpace(sourceExpression) ||
            string.IsNullOrWhiteSpace(methodCallPlan.MethodName) ||
            !IsSimpleEventBindingIdentifierBenchmark(methodCallPlan.MethodName))
        {
            return false;
        }

        var targetExpression = sourceExpression;
        if (!string.IsNullOrWhiteSpace(methodCallPlan.TargetPath) &&
            !methodCallPlan.TargetPath.Equals(".", StringComparison.Ordinal))
        {
            if (!BaselineTryBuildEventBindingMemberAccessExpression(sourceExpression, methodCallPlan.TargetPath, out targetExpression))
            {
                return false;
            }
        }

        var argumentExpressions = new List<string>();
        if (!methodCallPlan.Arguments.IsDefaultOrEmpty)
        {
            for (var index = 0; index < methodCallPlan.Arguments.Length; index++)
            {
                var argument = methodCallPlan.Arguments[index];
                var sourceArgumentExpression = argument.Kind switch
                {
                    ResolvedEventBindingMethodArgumentKind.Sender => senderExpression,
                    ResolvedEventBindingMethodArgumentKind.EventArgs => eventArgsExpression,
                    ResolvedEventBindingMethodArgumentKind.Parameter => "__axsgParameter",
                    _ => "null"
                };
                requiresParameter |= argument.Kind == ResolvedEventBindingMethodArgumentKind.Parameter;

                var targetTypeName = string.IsNullOrWhiteSpace(argument.TypeName)
                    ? "object?"
                    : argument.TypeName;
                argumentExpressions.Add("((" + targetTypeName + ")(" + sourceArgumentExpression + "))");
            }
        }

        invocationExpression = targetExpression + "." + methodCallPlan.MethodName + "(" + string.Join(", ", argumentExpressions) + ")";
        return true;
    }

    private static bool OptimizedTryBuildEventBindingMethodInvocationExpression(
        string sourceExpression,
        ResolvedEventBindingMethodCallPlan methodCallPlan,
        string senderExpression,
        string eventArgsExpression,
        out string invocationExpression,
        out bool requiresParameter)
    {
        invocationExpression = string.Empty;
        requiresParameter = false;

        if (string.IsNullOrWhiteSpace(sourceExpression) ||
            string.IsNullOrWhiteSpace(methodCallPlan.MethodName) ||
            !IsSimpleEventBindingIdentifierBenchmark(methodCallPlan.MethodName))
        {
            return false;
        }

        var targetExpression = sourceExpression;
        if (!string.IsNullOrWhiteSpace(methodCallPlan.TargetPath) &&
            !methodCallPlan.TargetPath.Equals(".", StringComparison.Ordinal))
        {
            if (!OptimizedTryBuildEventBindingMemberAccessExpression(sourceExpression, methodCallPlan.TargetPath, out targetExpression))
            {
                return false;
            }
        }

        var argumentCount = methodCallPlan.Arguments.IsDefaultOrEmpty
            ? 0
            : methodCallPlan.Arguments.Length;
        var builder = new StringBuilder(
            targetExpression.Length +
            methodCallPlan.MethodName.Length +
            3 +
            (argumentCount * 24));
        builder.Append(targetExpression);
        builder.Append('.');
        builder.Append(methodCallPlan.MethodName);
        builder.Append('(');
        if (!methodCallPlan.Arguments.IsDefaultOrEmpty)
        {
            for (var index = 0; index < methodCallPlan.Arguments.Length; index++)
            {
                var argument = methodCallPlan.Arguments[index];
                var sourceArgumentExpression = argument.Kind switch
                {
                    ResolvedEventBindingMethodArgumentKind.Sender => senderExpression,
                    ResolvedEventBindingMethodArgumentKind.EventArgs => eventArgsExpression,
                    ResolvedEventBindingMethodArgumentKind.Parameter => "__axsgParameter",
                    _ => "null"
                };
                requiresParameter |= argument.Kind == ResolvedEventBindingMethodArgumentKind.Parameter;

                var targetTypeName = string.IsNullOrWhiteSpace(argument.TypeName)
                    ? "object?"
                    : argument.TypeName;
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("((");
                builder.Append(targetTypeName);
                builder.Append(")(");
                builder.Append(sourceArgumentExpression);
                builder.Append("))");
            }
        }

        builder.Append(')');
        invocationExpression = builder.ToString();
        return true;
    }

    private static bool BaselineTryBuildEventBindingMemberAccessExpression(
        string sourceExpression,
        string path,
        out string memberAccessExpression)
    {
        memberAccessExpression = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceExpression))
        {
            return false;
        }

        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (normalizedPath == ".")
        {
            memberAccessExpression = sourceExpression;
            return true;
        }

        if (!BaselineIsSimpleEventBindingMemberPath(normalizedPath))
        {
            return false;
        }

        memberAccessExpression = sourceExpression + "." + normalizedPath;
        return true;
    }

    private static bool OptimizedTryBuildEventBindingMemberAccessExpression(
        string sourceExpression,
        string path,
        out string memberAccessExpression)
    {
        memberAccessExpression = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceExpression))
        {
            return false;
        }

        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (normalizedPath == ".")
        {
            memberAccessExpression = sourceExpression;
            return true;
        }

        if (!OptimizedIsSimpleEventBindingMemberPath(normalizedPath))
        {
            return false;
        }

        memberAccessExpression = sourceExpression + "." + normalizedPath;
        return true;
    }

    private static bool BaselineIsSimpleEventBindingMemberPath(string path)
    {
        var rawSegments = path.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        var segments = new string[rawSegments.Length];
        for (var index = 0; index < rawSegments.Length; index++)
        {
            segments[index] = rawSegments[index].Trim();
        }

        if (segments.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            if (!IsSimpleEventBindingIdentifierBenchmark(segments[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool OptimizedIsSimpleEventBindingMemberPath(string path)
    {
        var segmentStart = -1;
        for (var index = 0; index < path.Length; index++)
        {
            var current = path[index];
            if (current == '.')
            {
                if (!TryValidateSimpleEventBindingPathSegmentBenchmark(path, segmentStart, index))
                {
                    return false;
                }

                segmentStart = -1;
                continue;
            }

            if (segmentStart < 0)
            {
                segmentStart = index;
            }
        }

        return TryValidateSimpleEventBindingPathSegmentBenchmark(path, segmentStart, path.Length);
    }

    private static bool TryValidateSimpleEventBindingPathSegmentBenchmark(string path, int segmentStart, int segmentEndExclusive)
    {
        if (segmentStart < 0)
        {
            return false;
        }

        while (segmentStart < segmentEndExclusive && char.IsWhiteSpace(path[segmentStart]))
        {
            segmentStart++;
        }

        while (segmentEndExclusive > segmentStart && char.IsWhiteSpace(path[segmentEndExclusive - 1]))
        {
            segmentEndExclusive--;
        }

        if (segmentStart >= segmentEndExclusive)
        {
            return false;
        }

        var first = path[segmentStart];
        if (!(first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var index = segmentStart + 1; index < segmentEndExclusive; index++)
        {
            var current = path[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleEventBindingIdentifierBenchmark(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var first = value[0];
        if (!(first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidIdentifierForGeneratedMemberAccessBenchmark(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        var first = identifier[0];
        if (!(first == '@' || first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var index = 1; index < identifier.Length; index++)
        {
            var current = identifier[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildHotReloadEventTokenBenchmark(ResolvedEventSubscription eventSubscription)
    {
        var kindToken = eventSubscription.Kind == ResolvedEventSubscriptionKind.RoutedEvent ? "R" : "C";
        return kindToken + "|" +
               (eventSubscription.EventName ?? string.Empty) + "|" +
               (eventSubscription.HandlerMethodName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventOwnerTypeName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventFieldName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventHandlerTypeName ?? string.Empty);
    }

    private readonly record struct BenchmarkFeatureElementInfo(
        string? Key,
        string? DataType,
        string? Selector,
        string? TargetType,
        string? Source);

    private static int BaselineParserHotPath(XElement element)
    {
        var key = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "Key")?.Value;

        var xName = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "Name")?.Value;

        var plainName = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Name")?.Value;

        var fieldModifier = XamlAccessibilityModifierSemantics.NormalizeFieldModifier(
            element.Attributes().FirstOrDefault(attribute =>
                attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "FieldModifier")?.Value);

        var dataType = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "DataType")?.Value;

        var compileBindingsValue = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "CompileBindings")?.Value;
        var compileBindings = bool.TryParse(compileBindingsValue, out var parsedCompileBindings)
            ? parsedCompileBindings
            : (bool?)null;

        var factoryMethod = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "FactoryMethod")?.Value;

        var typeArguments = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.Namespace == Xaml2006 && attribute.Name.LocalName == "TypeArguments")?.Value;

        var inlineText = string.Join(
            " ",
            element.Nodes()
                .OfType<XText>()
                .Select(static node => node.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToArray());

        var propertyCount = 0;
        foreach (var attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.Namespace == Xaml2006)
            {
                var localName = attribute.Name.LocalName;
                if (localName == "Class" ||
                    localName == "Name" ||
                    localName == "FieldModifier" ||
                    localName == "ClassModifier" ||
                    localName == "Key" ||
                    localName == "DataType" ||
                    localName == "CompileBindings" ||
                    localName == "Precompile" ||
                    localName == "FactoryMethod" ||
                    localName == "TypeArguments" ||
                    localName == "Arguments" ||
                    localName == "Type")
                {
                    continue;
                }
            }

            propertyCount++;
        }

        var resolvedName = xName ?? plainName;

        return (key?.Length ?? 0) +
               (resolvedName?.Length ?? 0) +
               (fieldModifier?.Length ?? 0) +
               (dataType?.Length ?? 0) +
               (compileBindings == true ? 1 : 0) +
               (factoryMethod?.Length ?? 0) +
               (typeArguments?.Length ?? 0) +
               inlineText.Length +
               propertyCount;
    }

    private static int OptimizedParserHotPath(XElement element)
    {
        string? key = null;
        string? xName = null;
        string? plainName = null;
        string? fieldModifier = null;
        string? dataType = null;
        bool? compileBindings = null;
        string? factoryMethod = null;
        string? typeArguments = null;
        var propertyCount = 0;

        foreach (var attribute in element.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                if (attribute.Name.Namespace == Xaml2006)
                {
                    if (attribute.Name.LocalName == "Key")
                    {
                        key = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "Name")
                    {
                        xName = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "FieldModifier")
                    {
                        fieldModifier = XamlAccessibilityModifierSemantics.NormalizeFieldModifier(attribute.Value);
                    }
                    else if (attribute.Name.LocalName == "DataType")
                    {
                        dataType = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "CompileBindings")
                    {
                        compileBindings = bool.TryParse(attribute.Value, out var parsedCompileBindings)
                            ? parsedCompileBindings
                            : (bool?)null;
                    }
                    else if (attribute.Name.LocalName == "FactoryMethod")
                    {
                        factoryMethod = attribute.Value;
                    }
                    else if (attribute.Name.LocalName == "TypeArguments")
                    {
                        typeArguments = attribute.Value;
                    }
                }
                else if (attribute.Name.NamespaceName.Length == 0 &&
                         attribute.Name.LocalName == "Name")
                {
                    plainName = attribute.Value;
                }
            }

            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.Namespace == Xaml2006)
            {
                var localName = attribute.Name.LocalName;
                if (localName == "Class" ||
                    localName == "Name" ||
                    localName == "FieldModifier" ||
                    localName == "ClassModifier" ||
                    localName == "Key" ||
                    localName == "DataType" ||
                    localName == "CompileBindings" ||
                    localName == "Precompile" ||
                    localName == "FactoryMethod" ||
                    localName == "TypeArguments" ||
                    localName == "Arguments" ||
                    localName == "Type")
                {
                    continue;
                }
            }

            propertyCount++;
        }

        StringBuilder? inlineTextBuilder = null;
        foreach (var node in element.Nodes())
        {
            var textNode = node as XText;
            if (textNode is null)
            {
                continue;
            }

            var trimmed = textNode.Value.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (inlineTextBuilder is null)
            {
                inlineTextBuilder = new StringBuilder(trimmed.Length);
            }
            else if (inlineTextBuilder.Length > 0)
            {
                inlineTextBuilder.Append(' ');
            }

            inlineTextBuilder.Append(trimmed);
        }

        var resolvedName = xName ?? plainName;

        return (key?.Length ?? 0) +
               (resolvedName?.Length ?? 0) +
               (fieldModifier?.Length ?? 0) +
               (dataType?.Length ?? 0) +
               (compileBindings == true ? 1 : 0) +
               (factoryMethod?.Length ?? 0) +
               (typeArguments?.Length ?? 0) +
               (inlineTextBuilder?.Length ?? 0) +
               propertyCount;
    }

    private static ImmutableArray<DiagnosticInfo> BaselineApplyGlobalParityDiagnosticFilters(
        ImmutableArray<DiagnosticInfo> diagnostics,
        ImmutableHashSet<string> globalControlThemeKeys)
    {
        if (diagnostics.IsDefaultOrEmpty ||
            globalControlThemeKeys.Count == 0)
        {
            return diagnostics;
        }

        ImmutableArray<DiagnosticInfo>.Builder? filtered = null;
        var suppressedAny = false;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (ShouldSuppressControlThemeBasedOnDiagnostic(diagnostic, globalControlThemeKeys))
            {
                suppressedAny = true;
                continue;
            }

            filtered ??= ImmutableArray.CreateBuilder<DiagnosticInfo>(diagnostics.Length);
            filtered.Add(diagnostic);
        }

        if (!suppressedAny)
        {
            return diagnostics;
        }

        return filtered is null
            ? ImmutableArray<DiagnosticInfo>.Empty
            : filtered.ToImmutable();
    }

    private static ImmutableArray<DiagnosticInfo> OptimizedApplyGlobalParityDiagnosticFilters(
        ImmutableArray<DiagnosticInfo> diagnostics,
        ImmutableHashSet<string> globalControlThemeKeys)
    {
        if (diagnostics.IsDefaultOrEmpty ||
            globalControlThemeKeys.Count == 0)
        {
            return diagnostics;
        }

        ImmutableArray<DiagnosticInfo>.Builder? filtered = null;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (ShouldSuppressControlThemeBasedOnDiagnostic(diagnostic, globalControlThemeKeys))
            {
                if (filtered is null)
                {
                    filtered = ImmutableArray.CreateBuilder<DiagnosticInfo>(diagnostics.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        filtered.Add(diagnostics[preservedIndex]);
                    }
                }

                continue;
            }

            filtered?.Add(diagnostic);
        }

        return filtered is null
            ? diagnostics
            : filtered.ToImmutable();
    }

    private static bool ShouldSuppressControlThemeBasedOnDiagnostic(
        DiagnosticInfo diagnostic,
        ImmutableHashSet<string> globalControlThemeKeys)
    {
        if (!string.Equals(diagnostic.Id, "AXSG0305", StringComparison.Ordinal))
        {
            return false;
        }

        const string marker = "key '";
        var markerIndex = diagnostic.Message.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var start = markerIndex + marker.Length;
        if (start >= diagnostic.Message.Length)
        {
            return false;
        }

        var end = diagnostic.Message.IndexOf('\'', start);
        if (end <= start)
        {
            return false;
        }

        var key = diagnostic.Message.Substring(start, end - start).Trim();
        return key.Length > 0 && globalControlThemeKeys.Contains(key);
    }

    private static ImmutableArray<LanguageServiceDiagnostic> CreateLanguageServiceDiagnostics(int count, string source)
    {
        var builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(count);
        for (var index = 0; index < count; index++)
        {
            builder.Add(new LanguageServiceDiagnostic(
                Code: "AXSGLS0001",
                Message: "diagnostic " + index.ToString(),
                Range: new SourceRange(
                    new SourcePosition(index, 0),
                    new SourcePosition(index, 10)),
                Severity: LanguageServiceDiagnosticSeverity.Warning,
                Source: source));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlInlayHint> CreateInlayHints(int count)
    {
        var builder = ImmutableArray.CreateBuilder<XamlInlayHint>(count);
        for (var index = 0; index < count; index++)
        {
            builder.Add(new XamlInlayHint(
                Position: new SourcePosition(index, 4),
                Label: ": string",
                Kind: XamlInlayHintKind.Type,
                Tooltip: "hint " + index.ToString()));
        }

        return builder.ToImmutable();
    }

    private static XamlLanguageServiceOptions BaselineSharedAnalysisOptions(XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        return options with
        {
            IncludeCompilationDiagnostics = true,
            IncludeSemanticDiagnostics = true
        };
    }

    private static XamlLanguageServiceOptions OptimizedSharedAnalysisOptions(SharedAnalysisOptionsState state)
    {
        var options = state.Options ?? XamlLanguageServiceOptions.Default;
        if (options.IncludeCompilationDiagnostics && options.IncludeSemanticDiagnostics)
        {
            return options;
        }

        var workspaceRoot = options.WorkspaceRoot ?? string.Empty;
        return state.Cache.GetOrAdd(
            workspaceRoot,
            static root => new XamlLanguageServiceOptions(
                WorkspaceRoot: root.Length == 0 ? null : root,
                IncludeCompilationDiagnostics: true,
                IncludeSemanticDiagnostics: true));
    }

    private static ImmutableArray<LanguageServiceDiagnostic> BaselineLanguageServiceDiagnosticFilter(
        ImmutableArray<LanguageServiceDiagnostic> diagnostics,
        XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        if (diagnostics.IsDefaultOrEmpty ||
            (options.IncludeCompilationDiagnostics && options.IncludeSemanticDiagnostics))
        {
            return diagnostics;
        }

        var builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            if (!options.IncludeCompilationDiagnostics &&
                string.Equals(diagnostic.Source, "MSBuildWorkspace", StringComparison.Ordinal))
            {
                continue;
            }

            if (!options.IncludeSemanticDiagnostics &&
                string.Equals(diagnostic.Source, "AXSG.Semantic", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Add(diagnostic);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<LanguageServiceDiagnostic> OptimizedLanguageServiceDiagnosticFilter(
        ImmutableArray<LanguageServiceDiagnostic> diagnostics,
        XamlLanguageServiceOptions options)
    {
        options ??= XamlLanguageServiceOptions.Default;
        if (diagnostics.IsDefaultOrEmpty ||
            (options.IncludeCompilationDiagnostics && options.IncludeSemanticDiagnostics))
        {
            return diagnostics;
        }

        ImmutableArray<LanguageServiceDiagnostic>.Builder? builder = null;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            var diagnostic = diagnostics[index];
            if (!options.IncludeCompilationDiagnostics &&
                string.Equals(diagnostic.Source, "MSBuildWorkspace", StringComparison.Ordinal))
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(diagnostics[preservedIndex]);
                    }
                }

                continue;
            }

            if (!options.IncludeSemanticDiagnostics &&
                string.Equals(diagnostic.Source, "AXSG.Semantic", StringComparison.Ordinal))
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>(diagnostics.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(diagnostics[preservedIndex]);
                    }
                }

                continue;
            }

            builder?.Add(diagnostic);
        }

        return builder?.ToImmutable() ?? diagnostics;
    }

    private static ImmutableArray<XamlInlayHint> BaselineInlayHintRangeFilter(
        ImmutableArray<XamlInlayHint> hints,
        SourceRange requestedRange)
    {
        if (hints.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlInlayHint>();
        foreach (var hint in hints)
        {
            if (ComparePositions(hint.Position, requestedRange.Start) < 0 ||
                ComparePositions(hint.Position, requestedRange.End) > 0)
            {
                continue;
            }

            builder.Add(hint);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<XamlInlayHint> OptimizedInlayHintRangeFilter(
        ImmutableArray<XamlInlayHint> hints,
        SourceRange requestedRange)
    {
        if (hints.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlInlayHint>.Empty;
        }

        ImmutableArray<XamlInlayHint>.Builder? builder = null;
        for (var index = 0; index < hints.Length; index++)
        {
            var hint = hints[index];
            if (ComparePositions(hint.Position, requestedRange.Start) < 0 ||
                ComparePositions(hint.Position, requestedRange.End) > 0)
            {
                if (builder is null)
                {
                    builder = ImmutableArray.CreateBuilder<XamlInlayHint>(hints.Length - 1);
                    for (var preservedIndex = 0; preservedIndex < index; preservedIndex++)
                    {
                        builder.Add(hints[preservedIndex]);
                    }
                }

                continue;
            }

            builder?.Add(hint);
        }

        return builder?.ToImmutable() ?? hints;
    }

    private static bool BaselineShouldPreferTargetPath(string candidateTargetPath, string currentTargetPath)
    {
        var candidateRooted = Path.IsPathRooted(candidateTargetPath);
        var currentRooted = Path.IsPathRooted(currentTargetPath);
        if (candidateRooted != currentRooted)
        {
            return !candidateRooted;
        }

        if (candidateTargetPath.Length != currentTargetPath.Length)
        {
            return candidateTargetPath.Length < currentTargetPath.Length;
        }

        return string.Compare(candidateTargetPath, currentTargetPath, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static string NormalizeDedupePathForBenchmarks(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        var hasUncPrefix = normalized.StartsWith("//", StringComparison.Ordinal);

        try
        {
            if (!hasUncPrefix && Path.IsPathRooted(path))
            {
                normalized = Path.GetFullPath(path).Replace('\\', '/');
            }
        }
        catch
        {
            // Benchmark baseline matches compiler-host fallback behavior.
        }

        return NormalizePathSegmentsForBenchmarks(normalized);
    }

    private static string NormalizePathSegmentsForBenchmarks(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');
        var hasUncPrefix = normalized.StartsWith("//", StringComparison.Ordinal);
        var hasUnixRoot = !hasUncPrefix && normalized.StartsWith("/", StringComparison.Ordinal);
        var isRooted = Path.IsPathRooted(normalized) || hasUncPrefix || hasUnixRoot;
        var parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0 &&
                    !string.Equals(stack[stack.Count - 1], "..", StringComparison.Ordinal) &&
                    !IsDriveSegmentForBenchmarks(stack[stack.Count - 1]))
                {
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                if (!isRooted)
                {
                    stack.Add(part);
                }

                continue;
            }

            stack.Add(part);
        }

        var collapsed = string.Join("/", stack);
        if (hasUncPrefix)
        {
            return "//" + collapsed;
        }

        if (hasUnixRoot)
        {
            return "/" + collapsed;
        }

        return collapsed;
    }

    private static bool IsDriveSegmentForBenchmarks(string value)
    {
        return value.Length == 2 &&
               value[1] == ':' &&
               char.IsLetter(value[0]);
    }

    private static XamlDocumentModel CreateGraphBenchmarkDocument(
        string filePath,
        string targetPath,
        ImmutableArray<XamlIncludeDefinition> includes)
    {
        return new XamlDocumentModel(
            FilePath: filePath,
            TargetPath: targetPath,
            ClassFullName: "Demo." + Path.GetFileNameWithoutExtension(filePath),
            ClassModifier: "public",
            Precompile: true,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: new XamlObjectNode(
                XmlNamespace: "https://github.com/avaloniaui",
                XmlTypeName: "UserControl",
                Key: null,
                Name: null,
                FieldModifier: null,
                DataType: null,
                CompileBindings: null,
                FactoryMethod: null,
                TypeArguments: ImmutableArray<string>.Empty,
                ArrayItemType: null,
                ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
                TextContent: null,
                PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
                ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
                PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
                Line: 1,
                Column: 1),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: includes,
            IsValid: true);
    }

    private static bool TryResolveIncludeUriForBenchmarks(
        string includeSource,
        string currentTargetPath,
        string assemblyName,
        out string resolvedUri,
        out bool isProjectLocal)
    {
        resolvedUri = string.Empty;
        isProjectLocal = false;
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return false;
        }

        var trimmedSource = NormalizeIncludeSourceForBenchmarks(includeSource);
        if (trimmedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var rootedPath = NormalizeIncludePathForBenchmarks(trimmedSource.TrimStart('/'));
            if (rootedPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildUriForBenchmarks(assemblyName, rootedPath);
            isProjectLocal = true;
            return true;
        }

        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var absoluteSource))
        {
            if (!absoluteSource.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                resolvedUri = absoluteSource.ToString();
                return true;
            }

            if (!absoluteSource.Host.Equals(assemblyName, StringComparison.OrdinalIgnoreCase))
            {
                resolvedUri = absoluteSource.ToString();
                return true;
            }

            var normalizedPath = NormalizeIncludePathForBenchmarks(absoluteSource.AbsolutePath.TrimStart('/'));
            if (normalizedPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildUriForBenchmarks(assemblyName, normalizedPath);
            isProjectLocal = true;
            return true;
        }

        var includePath = NormalizeIncludePathForBenchmarks(
            CombineIncludePathForBenchmarks(GetIncludeDirectoryForBenchmarks(currentTargetPath), trimmedSource));
        if (includePath.Length == 0)
        {
            return false;
        }

        resolvedUri = BuildUriForBenchmarks(assemblyName, includePath);
        isProjectLocal = true;
        return true;
    }

    private static string NormalizeIncludeSourceForBenchmarks(string includeSource)
    {
        var trimmed = includeSource.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return trimmed;
        }

        var separatorIndex = inner.IndexOfAny(new[] { ' ', ',' });
        var markupName = separatorIndex >= 0 ? inner.Substring(0, separatorIndex) : inner;
        if (!markupName.Equals("x:Uri", StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var arguments = separatorIndex >= 0 ? inner.Substring(separatorIndex + 1).Trim() : string.Empty;
        if (arguments.Length == 0)
        {
            return trimmed;
        }

        var argumentSegment = arguments;
        var commaIndex = argumentSegment.IndexOf(',');
        if (commaIndex >= 0)
        {
            argumentSegment = argumentSegment.Substring(0, commaIndex).Trim();
        }

        var equalsIndex = argumentSegment.IndexOf('=');
        if (equalsIndex > 0)
        {
            var key = argumentSegment.Substring(0, equalsIndex).Trim();
            var value = argumentSegment.Substring(equalsIndex + 1).Trim();
            if (key.Equals("Uri", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Value", StringComparison.OrdinalIgnoreCase))
            {
                return UnquoteIncludeSourceForBenchmarks(value);
            }

            return trimmed;
        }

        return UnquoteIncludeSourceForBenchmarks(argumentSegment);
    }

    private static string UnquoteIncludeSourceForBenchmarks(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }
        }

        return value;
    }

    private static string NormalizeIncludePathForBenchmarks(string path)
    {
        var normalizedSeparators = path.Replace('\\', '/');
        var parts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join("/", stack);
    }

    private static string BuildUriForBenchmarks(string assemblyName, string normalizedTargetPath)
    {
        return "avares://" + assemblyName + "/" + normalizedTargetPath;
    }

    private static string GetIncludeDirectoryForBenchmarks(string path)
    {
        var lastSeparator = path.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return string.Empty;
        }

        return path.Substring(0, lastSeparator);
    }

    private static string CombineIncludePathForBenchmarks(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        return baseDirectory + "/" + relativePath;
    }

    private static int ComparePositions(SourcePosition left, SourcePosition right)
    {
        var lineComparison = left.Line.CompareTo(right.Line);
        if (lineComparison != 0)
        {
            return lineComparison;
        }

        return left.Character.CompareTo(right.Character);
    }

    private MeasurementResult Measure<TState>(int iterations, Func<TState, int> action, TState state)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            checksum += action(state);
        }

        stopwatch.Stop();
        var after = GC.GetAllocatedBytesForCurrentThread();

        var result = new MeasurementResult(stopwatch.Elapsed.TotalMilliseconds, after - before, checksum);
        _output.WriteLine("iterations={0} elapsedMs={1:F2} allocatedBytes={2} checksum={3}", iterations, result.Elapsed, result.AllocatedBytes, result.Checksum);
        return result;
    }

    private MeasurementResult MeasureBestOf<TState>(int runs, int iterations, Func<TState, int> action, TState state)
    {
        MeasurementResult? best = null;
        for (var runIndex = 0; runIndex < runs; runIndex++)
        {
            var current = Measure(iterations, action, state);
            if (best is null || current.Elapsed < best.Value.Elapsed)
            {
                best = current;
            }
        }

        return best!.Value;
    }

    private static string FormatFailure(string scenario, MeasurementResult baseline, MeasurementResult optimized, double targetRatio)
    {
        return scenario +
               ": optimized elapsed " + optimized.Elapsed.ToString("F2") +
               "ms exceeded ratio target " + targetRatio.ToString("F2") +
               " of baseline " + baseline.Elapsed.ToString("F2") + "ms.";
    }

    private static string FormatAllocationFailure(string scenario, MeasurementResult baseline, MeasurementResult optimized)
    {
        return scenario +
               ": optimized allocated " + optimized.AllocatedBytes.ToString() +
               " bytes, baseline allocated " + baseline.AllocatedBytes.ToString() + " bytes.";
    }

    private readonly record struct MeasurementResult(double Elapsed, long AllocatedBytes, int Checksum);

    private sealed class GenerationState
    {
        public GenerationState(string uri)
        {
            Uri = uri;
            Generations = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        public string Uri { get; }

        public Dictionary<string, int> Generations { get; }
    }

    private sealed class ReferenceScanWorkspace : IDisposable
    {
        public ReferenceScanWorkspace(string rootPath, string projectPath, string currentFilePath)
        {
            RootPath = rootPath;
            ProjectPath = projectPath;
            CurrentFilePath = currentFilePath;
        }

        public string RootPath { get; }

        public string ProjectPath { get; }

        public string CurrentFilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class ProjectIncludeScanWorkspace : IDisposable
    {
        public ProjectIncludeScanWorkspace(string rootPath, string projectPath)
        {
            RootPath = rootPath;
            ProjectPath = projectPath;
        }

        public string RootPath { get; }

        public string ProjectPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class ProjectResolutionWorkspace : IDisposable
    {
        public ProjectResolutionWorkspace(string rootPath, string currentFilePath)
        {
            RootPath = rootPath;
            CurrentFilePath = currentFilePath;
        }

        public string RootPath { get; }

        public string CurrentFilePath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private sealed class XmlCacheWorkspace : IDisposable
    {
        public XmlCacheWorkspace(string rootPath, string projectPath)
        {
            RootPath = rootPath;
            ProjectPath = projectPath;
        }

        public string RootPath { get; }

        public string ProjectPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }
        }
    }

    private readonly record struct CachedProjectSourceEntry(
        string DocumentUri,
        string Text,
        bool XmlParsed);

    private readonly record struct ProjectSourceSnapshotEntry(
        string FilePath,
        string DocumentUri,
        string Text,
        bool XmlParsed);

    private readonly record struct ProjectSourceScanState(
        string WorkspaceRoot,
        string ProjectPath,
        string CurrentFilePath,
        string CurrentDocumentUri,
        string CurrentText,
        ImmutableArray<string> CandidatePaths,
        IReadOnlyDictionary<string, CachedProjectSourceEntry> SourceCache,
        ImmutableArray<ProjectSourceSnapshotEntry> ProjectSnapshot);

    private readonly record struct ProjectIncludeScanState(
        string ProjectPath,
        string ProjectDirectory,
        ImmutableArray<string> CachedPatterns,
        IReadOnlyDictionary<string, Regex> RegexCache);

    private readonly record struct SharedAnalysisOptionsState(
        XamlLanguageServiceOptions Options,
        ConcurrentDictionary<string, XamlLanguageServiceOptions> Cache);

    private readonly record struct CompilerHostSnapshotState(
        ImmutableArray<XamlFileInput> XamlInputs);

    private readonly record struct CompilerHostConventionInferenceState(
        ImmutableArray<string> TargetPaths,
        GeneratorOptions Options);

    private readonly record struct CompilerHostGlobalGraphState(
        ImmutableArray<XamlDocumentModel> Documents,
        GeneratorOptions Options);

    private readonly record struct CompilerHostTransformConfigurationState(
        ImmutableDictionary<string, string> RawTransformDocuments,
        XamlTransformConfiguration BaseConfiguration,
        XamlTransformConfiguration OverlayConfiguration,
        IXamlFrameworkTransformProvider TransformProvider);

    private readonly record struct CompilerHostFrameworkServiceState(
        IXamlFrameworkProfile Profile,
        IXamlFrameworkSemanticBinder CachedBinder,
        IXamlFrameworkEmitter CachedEmitter);

    private readonly record struct XmlCacheState(
        ImmutableArray<XmlCacheEntry> Files);

    private readonly record struct XmlCacheEntry(
        string FilePath,
        string Text);

    private readonly record struct XmlCacheRetentionResult(
        int LiveDocumentCount);

    private readonly record struct EmitterStringAssemblyState(
        ImmutableArray<string> KnownTypeNames,
        ImmutableArray<string> ParentStackReferences,
        ImmutableArray<string> CollectionMembers,
        ImmutableArray<string> ClrMembers,
        ImmutableArray<string> AvaloniaPropertyExpressions,
        ImmutableArray<ResolvedEventSubscription> EventSubscriptions)
    {
        public string RootTypeName => "global::Demo.MainView";
    }

    private readonly record struct EmitterEventBindingState(
        string SourceExpression,
        ResolvedEventBindingMethodCallPlan MethodCallPlan,
        string SenderExpression,
        string EventArgsExpression);

    private readonly record struct CompilerHostIncludeResolutionInput(
        string IncludeSource,
        string CurrentTargetPath,
        string AssemblyName);

    private sealed class GraphEntryForBenchmarks
    {
        public GraphEntryForBenchmarks(XamlDocumentModel document, string buildUri, string normalizedTargetPath)
        {
            Document = document;
            BuildUri = buildUri;
            NormalizedTargetPath = normalizedTargetPath;
        }

        public XamlDocumentModel Document { get; }

        public string BuildUri { get; }

        public string NormalizedTargetPath { get; }
    }

    private sealed class GraphEdgeForBenchmarks
    {
        public GraphEdgeForBenchmarks(string sourceUri, string targetUri, string sourceText, int line, int column)
        {
            SourceUri = sourceUri;
            TargetUri = targetUri;
            SourceText = sourceText;
            Line = line;
            Column = column;
        }

        public string SourceUri { get; }

        public string TargetUri { get; }

        public string SourceText { get; }

        public int Line { get; }

        public int Column { get; }
    }

    private readonly record struct BenchmarkTransformTypeAliasEntry(
        XamlTypeAliasRule Alias,
        XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind SourceKind);

    private readonly record struct BenchmarkTransformPropertyAliasEntry(
        XamlPropertyAliasRule Alias,
        XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind SourceKind);

    private sealed class BenchmarkTransformProvider : IXamlFrameworkTransformProvider
    {
        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
        {
            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
            ImmutableArray<XamlFrameworkTransformRuleResult> files)
        {
            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }
    }
}
