using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Xunit.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

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
}
