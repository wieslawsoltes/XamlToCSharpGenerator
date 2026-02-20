using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.Avalonia.Binding;
using XamlToCSharpGenerator.Avalonia.Emission;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Diagnostics;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class AvaloniaXamlSourceGenerator : IIncrementalGenerator
{
    private const string SourceItemGroupMetadata = "build_metadata.AdditionalFiles.SourceItemGroup";
    private const string TargetPathMetadata = "build_metadata.AdditionalFiles.TargetPath";
    private const string TransformRuleSourceItemGroup = "AvaloniaSourceGenTransformRule";
    private const string AvaloniaXmlnsPrefixAttributeMetadataName = "Avalonia.Metadata.XmlnsPrefixAttribute";
    private const string SourceGenGlobalXmlnsPrefixAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute";
    private const string SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenAllowImplicitXmlnsDeclarationAttribute";
    private static readonly ConcurrentDictionary<string, CachedGeneratedSource> LastGoodGeneratedSources =
        new(StringComparer.OrdinalIgnoreCase);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var optionsProvider = context.AnalyzerConfigOptionsProvider
            .Combine(context.CompilationProvider.Select(static (compilation, _) => compilation.AssemblyName))
            .Select(static (pair, _) => GeneratorOptions.From(pair.Left.GlobalOptions, pair.Right));

        var xamlInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(optionsProvider)
            .Select(static (pair, cancellationToken) =>
            {
                var text = pair.Left.Left;
                var optionsProvider = pair.Left.Right;
                var generatorOptions = pair.Right;

                if (!generatorOptions.IsEnabled)
                {
                    return null;
                }

                if (!IsXaml(text.Path))
                {
                    return null;
                }

                var metadataOptions = optionsProvider.GetOptions(text);
                metadataOptions.TryGetValue(SourceItemGroupMetadata, out var sourceItemGroup);

                if (sourceItemGroup is { Length: > 0 } &&
                    !sourceItemGroup.Equals("AvaloniaXaml", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                metadataOptions.TryGetValue(TargetPathMetadata, out var targetPath);
                var textContent = text.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    return null;
                }

                var normalizedTargetPath = string.IsNullOrWhiteSpace(targetPath) ? Path.GetFileName(text.Path) : targetPath!;
                var normalizedSourceItemGroup = string.IsNullOrWhiteSpace(sourceItemGroup) ? "AvaloniaXaml" : sourceItemGroup!;

                return new XamlFileInput(
                    FilePath: text.Path,
                    TargetPath: normalizedTargetPath,
                    SourceItemGroup: normalizedSourceItemGroup,
                    Text: textContent!);
            })
            .Where(static input => input is not null)
            .Select(static (input, _) => input!);

        var uniqueXamlInputs = xamlInputs
            .Collect()
            .Select(static (inputs, _) =>
            {
                if (inputs.IsDefaultOrEmpty)
                {
                    return ImmutableArray<XamlFileInput>.Empty;
                }

                var byPath = new Dictionary<string, XamlFileInput>(StringComparer.OrdinalIgnoreCase);

                foreach (var input in inputs
                             .OrderBy(static x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(static x => x.TargetPath, StringComparer.OrdinalIgnoreCase))
                {
                    var dedupeKey = NormalizeDedupePath(input.FilePath);
                    if (!byPath.TryGetValue(dedupeKey, out var existing))
                    {
                        byPath[dedupeKey] = input;
                        continue;
                    }

                    if (!ShouldPreferTargetPath(input.TargetPath, existing.TargetPath))
                    {
                        continue;
                    }

                    byPath[dedupeKey] = input;
                }

                return byPath.Values
                    .OrderBy(static x => x.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static x => x.TargetPath, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();
            });

        var transformRuleInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(optionsProvider)
            .Select(static (pair, cancellationToken) =>
            {
                var text = pair.Left.Left;
                var optionsProvider = pair.Left.Right;
                var generatorOptions = pair.Right;
                if (!generatorOptions.IsEnabled)
                {
                    return null;
                }

                var metadataOptions = optionsProvider.GetOptions(text);
                metadataOptions.TryGetValue(SourceItemGroupMetadata, out var sourceItemGroup);
                if (!string.Equals(sourceItemGroup, TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var textContent = text.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    return null;
                }

                return new TransformRuleFileInput(text.Path, textContent);
            })
            .Where(static input => input is not null)
            .Select(static (input, _) => input!);

        var transformRules = transformRuleInputs
            .Select(static (input, _) => TransformRulesParser.Parse(input))
            .Collect()
            .Select(static (results, _) => MergeTransformRules(results));

        context.RegisterSourceOutput(
            transformRules,
            static (sourceContext, rules) => ReportDiagnostics(sourceContext, rules.Diagnostics));

        var parserNamespaceContext = context.CompilationProvider
            .Combine(optionsProvider)
            .Select(static (pair, _) => BuildParserNamespaceContext(pair.Left, pair.Right));

        var parsedDocuments = uniqueXamlInputs
            .Combine(parserNamespaceContext)
            .SelectMany(static (payload, _) =>
            {
                var (inputs, parserContext) = payload;
                if (inputs.IsDefaultOrEmpty)
                {
                    return ImmutableArray<ParsedDocumentResult>.Empty;
                }

                IXamlDocumentParser parser = new SimpleXamlDocumentParser(
                    parserContext.GlobalXmlnsPrefixes,
                    parserContext.AllowImplicitDefaultXmlns,
                    parserContext.ImplicitDefaultXmlns);
                var results = ImmutableArray.CreateBuilder<ParsedDocumentResult>(inputs.Length);
                foreach (var input in inputs)
                {
                    var parseStart = Stopwatch.GetTimestamp();
                    var (document, diagnostics) = parser.Parse(input);
                    var parseElapsed = Stopwatch.GetElapsedTime(parseStart);
                    results.Add(new ParsedDocumentResult(input, document, diagnostics, parseElapsed));
                }

                return results.ToImmutable();
            });

        var globalGraphDiagnostics = parsedDocuments
            .Select(static (result, _) => result.Document)
            .Where(static document => document is not null)
            .Select(static (document, _) => document!)
            .Collect()
            .Combine(optionsProvider)
            .Select(static (pair, _) =>
            {
                var globalGraphStart = Stopwatch.GetTimestamp();
                var diagnostics = AnalyzeGlobalDocumentGraph(pair.Left, pair.Right);
                var globalGraphElapsed = Stopwatch.GetElapsedTime(globalGraphStart);
                return new GlobalDiagnosticsResult(
                    diagnostics,
                    IsHotReloadErrorResilienceEnabled(pair.Right),
                    pair.Right.MetricsEnabled,
                    pair.Right.MetricsDetailed,
                    pair.Left.Length,
                    globalGraphElapsed);
            });

        context.RegisterSourceOutput(globalGraphDiagnostics,
            static (sourceContext, diagnosticsResult) =>
            {
                ReportDiagnostics(
                    sourceContext,
                    diagnosticsResult.Diagnostics,
                    diagnosticsResult.DemoteErrorsToWarnings);
                ReportGlobalMetrics(sourceContext, diagnosticsResult);
            });

        context.RegisterSourceOutput(
            parsedDocuments.Combine(context.CompilationProvider.Combine(optionsProvider).Combine(transformRules)),
            static (sourceContext, payload) =>
            {
                var (parsedDocument, ((compilation, options), transformRules)) = payload;
                var parseResult = parsedDocument.Document;
                var parseDiagnostics = parsedDocument.Diagnostics;
                var resilienceEnabled = IsHotReloadErrorResilienceEnabled(options);
                var cacheKey = BuildHotReloadCacheKey(
                    options.AssemblyName,
                    parsedDocument.Input.FilePath,
                    parsedDocument.Input.TargetPath);
                var bindElapsed = TimeSpan.Zero;
                var emitElapsed = TimeSpan.Zero;
                var semanticDiagnosticsCount = 0;
                var generatedSource = false;
                var usedFallbackSource = false;
                var duplicateHint = false;
                var status = "parse";
                var totalStartTimestamp = options.MetricsEnabled ? Stopwatch.GetTimestamp() : 0L;

                try
                {
                    ReportDiagnostics(sourceContext, parseDiagnostics, resilienceEnabled);
                    if (parseResult is null)
                    {
                        usedFallbackSource = TryUseCachedSource(sourceContext, cacheKey, parsedDocument.Input.FilePath, resilienceEnabled);
                        status = usedFallbackSource ? "fallback-parse" : "parse-failed";
                        return;
                    }

                    if (parseResult.Precompile == false)
                    {
                        status = "skipped-precompile";
                        return;
                    }

                    status = "bind";
                    var bindStart = Stopwatch.GetTimestamp();
                    IXamlSemanticBinder binder = new AvaloniaSemanticBinder();
                    var (viewModel, semanticDiagnostics) = binder.Bind(parseResult, compilation, options, transformRules.Configuration);
                    bindElapsed = Stopwatch.GetElapsedTime(bindStart);
                    semanticDiagnosticsCount = semanticDiagnostics.Length;
                    ReportDiagnostics(sourceContext, semanticDiagnostics, resilienceEnabled);
                    if (viewModel is null)
                    {
                        usedFallbackSource = TryUseCachedSource(sourceContext, cacheKey, parseResult.FilePath, resilienceEnabled);
                        status = usedFallbackSource ? "fallback-bind" : "bind-failed";
                        return;
                    }

                    status = "emit";
                    var emitStart = Stopwatch.GetTimestamp();
                    try
                    {
                        IXamlCodeEmitter emitter = new AvaloniaCodeEmitter();
                        var (hintName, source) = emitter.Emit(viewModel);
                        if (TryAddSource(sourceContext, hintName, source))
                        {
                            generatedSource = true;
                            status = "generated";
                            if (resilienceEnabled)
                            {
                                LastGoodGeneratedSources[cacheKey] = new CachedGeneratedSource(hintName, source);
                            }
                        }
                        else
                        {
                            duplicateHint = true;
                            status = "duplicate-hint";
                            sourceContext.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticCatalog.DuplicateGeneratedHintName,
                                Location.None,
                                hintName,
                                parseResult.FilePath));
                        }
                    }
                    catch (Exception ex)
                    {
                        usedFallbackSource = TryUseCachedSource(sourceContext, cacheKey, parseResult.FilePath, resilienceEnabled);
                        status = usedFallbackSource ? "fallback-emit" : "emit-failed";
                        if (!usedFallbackSource)
                        {
                            sourceContext.ReportDiagnostic(Diagnostic.Create(
                                DiagnosticCatalog.EmissionFailed,
                                Location.None,
                                parseResult.ClassFullName ?? parseResult.TargetPath,
                                ex.Message));
                        }
                    }
                    finally
                    {
                        emitElapsed = Stopwatch.GetElapsedTime(emitStart);
                    }
                }
                finally
                {
                    ReportFileMetrics(
                        sourceContext,
                        options,
                        parsedDocument,
                        parseResult,
                        parseDiagnostics.Length,
                        semanticDiagnosticsCount,
                        parsedDocument.ParseElapsed,
                        bindElapsed,
                        emitElapsed,
                        options.MetricsEnabled ? Stopwatch.GetElapsedTime(totalStartTimestamp) : TimeSpan.Zero,
                        status,
                        generatedSource,
                        usedFallbackSource,
                        duplicateHint);
                }
            });
    }

    private static void ReportDiagnostics(
        SourceProductionContext context,
        ImmutableArray<DiagnosticInfo> diagnostics,
        bool demoteErrorsToWarnings = false)
    {
        foreach (var diagnostic in diagnostics)
        {
            var descriptor = diagnostic.Id switch
            {
                "AXSG0001" => DiagnosticCatalog.ParseFailed,
                "AXSG0002" => DiagnosticCatalog.MissingClassDirective,
                "AXSG0003" => DiagnosticCatalog.PrecompileDirectiveInvalid,
                "AXSG0100" => DiagnosticCatalog.TypeResolutionFailed,
                "AXSG0101" => DiagnosticCatalog.UnsupportedProperty,
                "AXSG0102" => DiagnosticCatalog.UnsupportedLiteralConversion,
                "AXSG0103" => DiagnosticCatalog.ChildAttachmentConflict,
                "AXSG0104" => DiagnosticCatalog.ClassModifierInvalid,
                "AXSG0105" => DiagnosticCatalog.ClassModifierMismatch,
                "AXSG0106" => DiagnosticCatalog.ConstructionDirectiveInvalid,
                "AXSG0107" => DiagnosticCatalog.ConstructionFactoryNotFound,
                "AXSG0108" => DiagnosticCatalog.ArrayConstructionInvalid,
                "AXSG0110" => DiagnosticCatalog.CompiledBindingRequiresDataType,
                "AXSG0111" => DiagnosticCatalog.CompiledBindingPathInvalid,
                "AXSG0120" => DiagnosticCatalog.ConditionalXamlExpressionInvalid,
                "AXSG0300" => DiagnosticCatalog.StyleSelectorInvalid,
                "AXSG0301" => DiagnosticCatalog.StyleSetterPropertyInvalid,
                "AXSG0302" => DiagnosticCatalog.ControlThemeTargetTypeInvalid,
                "AXSG0303" => DiagnosticCatalog.ControlThemeSetterPropertyInvalid,
                "AXSG0304" => DiagnosticCatalog.DuplicateSetterDetected,
                "AXSG0305" => DiagnosticCatalog.ControlThemeBasedOnNotFound,
                "AXSG0306" => DiagnosticCatalog.ControlThemeBasedOnCycleDetected,
                "AXSG0500" => DiagnosticCatalog.DataTemplateDataTypeRecommended,
                "AXSG0501" => DiagnosticCatalog.ControlTemplateTargetTypeInvalid,
                "AXSG0502" => DiagnosticCatalog.RequiredTemplatePartMissing,
                "AXSG0503" => DiagnosticCatalog.TemplatePartWrongType,
                "AXSG0504" => DiagnosticCatalog.OptionalTemplatePartMissing,
                "AXSG0505" => DiagnosticCatalog.ItemContainerInsideTemplate,
                "AXSG0506" => DiagnosticCatalog.TemplateContentTypeInvalid,
                "AXSG0400" => DiagnosticCatalog.IncludeSourceMissing,
                "AXSG0401" => DiagnosticCatalog.IncludeSourceInvalid,
                "AXSG0402" => DiagnosticCatalog.IncludeMergeTargetUnknown,
                "AXSG0403" => DiagnosticCatalog.IncludeTargetNotFound,
                "AXSG0404" => DiagnosticCatalog.IncludeCycleDetected,
                "AXSG0600" => DiagnosticCatalog.RoutedEventHandlerInvalid,
                "AXSG0601" => DiagnosticCatalog.DuplicateBuildUriRegistration,
                "AXSG0700" => DiagnosticCatalog.HotReloadFallbackUsed,
                "AXSG0701" => DiagnosticCatalog.DuplicateGeneratedHintName,
                "AXSG0900" => DiagnosticCatalog.TransformRuleParseFailed,
                "AXSG0901" => DiagnosticCatalog.TransformRuleEntryInvalid,
                "AXSG0902" => DiagnosticCatalog.TransformRuleTypeResolutionFailed,
                "AXSG0903" => DiagnosticCatalog.TransformRuleDuplicateAlias,
                _ => DiagnosticCatalog.InternalError,
            };

            if (diagnostic.IsError && demoteErrorsToWarnings)
            {
                descriptor = WithSeverity(descriptor, DiagnosticSeverity.Warning);
            }
            else if (diagnostic.IsError && descriptor.DefaultSeverity != DiagnosticSeverity.Error)
            {
                descriptor = WithSeverity(descriptor, DiagnosticSeverity.Error);
            }

            var location = CreateLocation(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);

            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, diagnostic.Message));
        }
    }

    private static void ReportGlobalMetrics(
        SourceProductionContext context,
        GlobalDiagnosticsResult result)
    {
        if (!result.MetricsEnabled)
        {
            return;
        }

        var message = result.MetricsDetailed
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Global XAML graph analysis: documents={0}, diagnostics={1}, elapsed={2}.",
                result.DocumentCount,
                result.Diagnostics.Length,
                FormatMilliseconds(result.Elapsed))
            : string.Format(
                CultureInfo.InvariantCulture,
                "Global XAML graph analysis elapsed={0}.",
                FormatMilliseconds(result.Elapsed));

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticCatalog.CompileMetricsSummary,
            Location.None,
            message));
    }

    private static void ReportFileMetrics(
        SourceProductionContext context,
        GeneratorOptions options,
        ParsedDocumentResult parsedDocument,
        XamlDocumentModel? parseResult,
        int parseDiagnosticsCount,
        int semanticDiagnosticsCount,
        TimeSpan parseElapsed,
        TimeSpan bindElapsed,
        TimeSpan emitElapsed,
        TimeSpan totalElapsed,
        string status,
        bool generatedSource,
        bool usedFallbackSource,
        bool duplicateHint)
    {
        if (!options.MetricsEnabled)
        {
            return;
        }

        var line = parseResult?.RootObject.Line ?? 1;
        var column = parseResult?.RootObject.Column ?? 1;
        var location = CreateLocation(parsedDocument.Input.FilePath, line, column);
        var documentDisplayName = parseResult?.ClassFullName ?? parsedDocument.Input.TargetPath;

        var message = options.MetricsDetailed
            ? string.Format(
                CultureInfo.InvariantCulture,
                "XAML compile metrics for '{0}': total={1}, parse={2}, bind={3}, emit={4}, status={5}, generated={6}, fallback={7}, duplicateHint={8}, parseDiagnostics={9}, semanticDiagnostics={10}.",
                documentDisplayName,
                FormatMilliseconds(totalElapsed),
                FormatMilliseconds(parseElapsed),
                FormatMilliseconds(bindElapsed),
                FormatMilliseconds(emitElapsed),
                status,
                generatedSource,
                usedFallbackSource,
                duplicateHint,
                parseDiagnosticsCount,
                semanticDiagnosticsCount)
            : string.Format(
                CultureInfo.InvariantCulture,
                "XAML compile metrics for '{0}': total={1}, status={2}.",
                documentDisplayName,
                FormatMilliseconds(totalElapsed),
                status);

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticCatalog.CompileMetricsFile,
            location,
            message));
    }

    private static string FormatMilliseconds(TimeSpan elapsed)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000}ms", elapsed.TotalMilliseconds);
    }

    private static Location CreateLocation(string? filePath, int line, int column)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Location.None;
        }

        var lineIndex = Math.Max(0, line - 1);
        var columnIndex = Math.Max(0, column - 1);
        return Location.Create(
            filePath,
            TextSpan.FromBounds(0, 0),
            new LinePositionSpan(
                new LinePosition(lineIndex, columnIndex),
                new LinePosition(lineIndex, columnIndex)));
    }

    private static DiagnosticDescriptor WithSeverity(DiagnosticDescriptor descriptor, DiagnosticSeverity severity)
    {
        return new DiagnosticDescriptor(
            descriptor.Id,
            descriptor.Title,
            descriptor.MessageFormat,
            descriptor.Category,
            severity,
            descriptor.IsEnabledByDefault,
            descriptor.Description,
            descriptor.HelpLinkUri,
            descriptor.CustomTags.ToArray());
    }

    private static bool IsHotReloadErrorResilienceEnabled(GeneratorOptions options)
    {
        if (!options.HotReloadEnabled || !options.HotReloadErrorResilienceEnabled)
        {
            return false;
        }

        if (options.DotNetWatchBuild)
        {
            return true;
        }

        if (!options.IdeHotReloadEnabled)
        {
            return false;
        }

        return options.BuildingInsideVisualStudio || options.BuildingByReSharper;
    }

    private static bool TryUseCachedSource(
        SourceProductionContext sourceContext,
        string cacheKey,
        string filePath,
        bool resilienceEnabled)
    {
        if (!resilienceEnabled ||
            !LastGoodGeneratedSources.TryGetValue(cacheKey, out var cached))
        {
            return false;
        }

        _ = TryAddSource(sourceContext, cached.HintName, cached.Source);

        var location = CreateLocation(filePath, 1, 1);

        sourceContext.ReportDiagnostic(Diagnostic.Create(
            DiagnosticCatalog.HotReloadFallbackUsed,
            location,
            "Hot reload fallback is using the last successfully generated XAML source for this file until current XAML errors are fixed."));
        return true;
    }

    private static string BuildHotReloadCacheKey(string? assemblyName, string filePath, string targetPath)
    {
        var normalizedAssemblyName = string.IsNullOrWhiteSpace(assemblyName)
            ? "UnknownAssembly"
            : assemblyName!;
        return normalizedAssemblyName + "|" + NormalizeDedupePath(filePath) + "|" + targetPath;
    }

    private static bool TryAddSource(SourceProductionContext sourceContext, string hintName, string source)
    {
        try
        {
            sourceContext.AddSource(hintName, source);
            return true;
        }
        catch (ArgumentException ex) when (IsDuplicateHintNameException(ex))
        {
            return false;
        }
    }

    private static bool IsDuplicateHintNameException(ArgumentException ex)
    {
        return string.Equals(ex.ParamName, "hintName", StringComparison.OrdinalIgnoreCase) ||
               (ex.Message.Contains("hintName", StringComparison.OrdinalIgnoreCase) &&
                ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsXaml(string path)
    {
        return path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".paml", StringComparison.OrdinalIgnoreCase);
    }

    private static ParserNamespaceContext BuildParserNamespaceContext(
        Compilation compilation,
        GeneratorOptions options)
    {
        var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (IsXmlnsPrefixAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length < 2 ||
                        attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                        attribute.ConstructorArguments[1].Value is not string prefix ||
                        string.IsNullOrWhiteSpace(prefix) ||
                        string.IsNullOrWhiteSpace(xmlNamespace))
                    {
                        continue;
                    }

                    globalPrefixes[prefix.Trim()] = xmlNamespace.Trim();
                    continue;
                }

                if (IsSourceGenAllowImplicitXmlnsDeclarationAttribute(attribute))
                {
                    if (attribute.ConstructorArguments.Length == 0)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = true };
                    }
                    else if (attribute.ConstructorArguments[0].Value is bool allowImplicit)
                    {
                        options = options with { AllowImplicitXmlnsDeclaration = allowImplicit };
                    }
                }
            }
        }

        foreach (var entry in ParseGlobalXmlnsPrefixesProperty(options.GlobalXmlnsPrefixes))
        {
            globalPrefixes[entry.Key] = entry.Value;
        }

        if (options.AllowImplicitXmlnsDeclaration &&
            !string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns) &&
            !globalPrefixes.ContainsKey(string.Empty))
        {
            globalPrefixes[string.Empty] = options.ImplicitDefaultXmlns;
        }

        return new ParserNamespaceContext(
            globalPrefixes.ToImmutable(),
            options.AllowImplicitXmlnsDeclaration,
            options.ImplicitDefaultXmlns);
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && visited.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }

        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }
    }

    private static bool IsXmlnsPrefixAttribute(AttributeData attribute)
    {
        var metadataName = attribute.AttributeClass?.ToDisplayString();
        return string.Equals(metadataName, AvaloniaXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal) ||
               string.Equals(metadataName, SourceGenGlobalXmlnsPrefixAttributeMetadataName, StringComparison.Ordinal);
    }

    private static bool IsSourceGenAllowImplicitXmlnsDeclarationAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(),
            SourceGenAllowImplicitXmlnsDeclarationAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string> ParseGlobalXmlnsPrefixesProperty(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ImmutableDictionary<string, string>.Empty;
        }

        var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var entries = rawValue
            .Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var separatorIndex = entry.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
            {
                continue;
            }

            var prefix = entry.Substring(0, separatorIndex).Trim();
            var xmlNamespace = entry.Substring(separatorIndex + 1).Trim();
            if (prefix.Length == 0 || xmlNamespace.Length == 0)
            {
                continue;
            }

            map[prefix] = xmlNamespace;
        }

        return map.ToImmutable();
    }

    private static TransformRuleAggregateResult MergeTransformRules(
        ImmutableArray<TransformRuleFileResult> files)
    {
        if (files.IsDefaultOrEmpty)
        {
            return new TransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var typeAliases = new Dictionary<string, XamlTypeAliasRule>(StringComparer.OrdinalIgnoreCase);
        var propertyAliases = new Dictionary<string, XamlPropertyAliasRule>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files.OrderBy(static x => x.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.AddRange(file.Diagnostics);

            foreach (var typeAlias in file.Configuration.TypeAliases)
            {
                var key = BuildTypeAliasKey(typeAlias.XmlNamespace, typeAlias.XamlTypeName);
                if (typeAliases.TryGetValue(key, out var existing))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0903",
                        $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' is declared multiple times. The later declaration from '{typeAlias.Source}' overrides '{existing.Source}'.",
                        typeAlias.Source,
                        typeAlias.Line,
                        typeAlias.Column,
                        false));
                }

                typeAliases[key] = typeAlias;
            }

            foreach (var propertyAlias in file.Configuration.PropertyAliases)
            {
                var key = BuildPropertyAliasKey(propertyAlias.TargetTypeName, propertyAlias.XamlPropertyName);
                if (propertyAliases.TryGetValue(key, out var existing))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0903",
                        $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' is declared multiple times. The later declaration from '{propertyAlias.Source}' overrides '{existing.Source}'.",
                        propertyAlias.Source,
                        propertyAlias.Line,
                        propertyAlias.Column,
                        false));
                }

                propertyAliases[key] = propertyAlias;
            }
        }

        return new TransformRuleAggregateResult(
            new XamlTransformConfiguration(
                typeAliases.Values.ToImmutableArray(),
                propertyAliases.Values.ToImmutableArray()),
            diagnostics.ToImmutable());
    }

    private static string BuildTypeAliasKey(string xmlNamespace, string xamlType)
    {
        return xmlNamespace.Trim() + "|" + xamlType.Trim();
    }

    private static string BuildPropertyAliasKey(string targetType, string xamlProperty)
    {
        return targetType.Trim() + "|" + xamlProperty.Trim();
    }

    private static ImmutableArray<DiagnosticInfo> AnalyzeGlobalDocumentGraph(
        ImmutableArray<XamlDocumentModel> documents,
        GeneratorOptions options)
    {
        if (documents.IsDefaultOrEmpty)
        {
            return ImmutableArray<DiagnosticInfo>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? "UnknownAssembly";
        var entriesByUri = new Dictionary<string, DocumentGraphEntry>(StringComparer.OrdinalIgnoreCase);
        var entriesByTargetPath = new Dictionary<string, DocumentGraphEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents.OrderBy(static document => document.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            if (document.Precompile == false)
            {
                continue;
            }

            var normalizedTargetPath = NormalizeIncludePath(document.TargetPath);
            if (normalizedTargetPath.Length == 0)
            {
                continue;
            }

            var buildUri = BuildUri(assemblyName, normalizedTargetPath);
            var entry = new DocumentGraphEntry(document, buildUri, normalizedTargetPath);
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
            }
        }

        var edgesBySource = new Dictionary<string, List<IncludeGraphEdge>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entriesByUri.Values.OrderBy(static x => x.Document.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var include in entry.Document.Includes)
            {
                if (!TryResolveIncludeUri(
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
                    edges = new List<IncludeGraphEdge>();
                    edgesBySource[entry.BuildUri] = edges;
                }

                edges.Add(new IncludeGraphEdge(
                    entry.BuildUri,
                    resolvedUri,
                    include.Source,
                    include.Line,
                    include.Column));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var cycleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var uri in edgesBySource.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
        {
            DetectCycle(uri, new Stack<string>());
        }

        return diagnostics.ToImmutable();

        void DetectCycle(string sourceUri, Stack<string> path)
        {
            if (state.TryGetValue(sourceUri, out var currentState))
            {
                if (currentState == 1)
                {
                    return;
                }

                if (currentState == 2)
                {
                    return;
                }
            }

            state[sourceUri] = 1;
            path.Push(sourceUri);

            if (edgesBySource.TryGetValue(sourceUri, out var edges))
            {
                foreach (var edge in edges.OrderBy(static edge => edge.TargetUri, StringComparer.OrdinalIgnoreCase))
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

    private static bool TryResolveIncludeUri(
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

        var trimmedSource = NormalizeIncludeSource(includeSource);
        if (trimmedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var rootedPath = NormalizeIncludePath(trimmedSource.TrimStart('/'));
            if (rootedPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildUri(assemblyName, rootedPath);
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

            var normalizedPath = NormalizeIncludePath(absoluteSource.AbsolutePath.TrimStart('/'));
            if (normalizedPath.Length == 0)
            {
                return false;
            }

            resolvedUri = BuildUri(assemblyName, normalizedPath);
            isProjectLocal = true;
            return true;
        }

        var includePath = NormalizeIncludePath(CombineIncludePath(GetIncludeDirectory(currentTargetPath), trimmedSource));

        if (includePath.Length == 0)
        {
            return false;
        }

        resolvedUri = BuildUri(assemblyName, includePath);
        isProjectLocal = true;
        return true;
    }

    private static string NormalizeIncludeSource(string includeSource)
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
        var markupName = separatorIndex >= 0
            ? inner.Substring(0, separatorIndex)
            : inner;
        if (!markupName.Equals("x:Uri", StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        var arguments = separatorIndex >= 0
            ? inner.Substring(separatorIndex + 1).Trim()
            : string.Empty;
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
                return UnquoteIncludeSource(value);
            }

            return trimmed;
        }

        return UnquoteIncludeSource(argumentSegment);
    }

    private static string UnquoteIncludeSource(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') ||
                (value[0] == '\'' && value[^1] == '\''))
            {
                return value.Substring(1, value.Length - 2);
            }
        }

        return value;
    }

    private static string BuildUri(string assemblyName, string normalizedTargetPath)
    {
        return "avares://" + assemblyName + "/" + normalizedTargetPath;
    }

    private static bool ShouldPreferTargetPath(string candidateTargetPath, string currentTargetPath)
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

    private static string NormalizeDedupePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path.Replace('\\', '/');

        try
        {
            if (Path.IsPathRooted(path))
            {
                normalized = Path.GetFullPath(path).Replace('\\', '/');
            }
        }
        catch
        {
            // Keep lexical normalization when physical normalization fails.
        }

        return NormalizePathSegments(normalized);
    }

    private static string NormalizePathSegments(string path)
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
                    !string.Equals(stack[^1], "..", StringComparison.Ordinal) &&
                    !IsDriveSegment(stack[^1]))
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

    private static bool IsDriveSegment(string value)
    {
        return value.Length == 2 &&
               value[1] == ':' &&
               char.IsLetter(value[0]);
    }

    private static string GetIncludeDirectory(string path)
    {
        var lastSeparator = path.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return string.Empty;
        }

        return path.Substring(0, lastSeparator);
    }

    private static string CombineIncludePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        return baseDirectory + "/" + relativePath;
    }

    private static string NormalizeIncludePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

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

    private sealed class DocumentGraphEntry
    {
        public DocumentGraphEntry(XamlDocumentModel document, string buildUri, string normalizedTargetPath)
        {
            Document = document;
            BuildUri = buildUri;
            NormalizedTargetPath = normalizedTargetPath;
        }

        public XamlDocumentModel Document { get; }

        public string BuildUri { get; }

        public string NormalizedTargetPath { get; }
    }

    private sealed class IncludeGraphEdge
    {
        public IncludeGraphEdge(string sourceUri, string targetUri, string sourceText, int line, int column)
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

    private sealed class ParserNamespaceContext
    {
        public ParserNamespaceContext(
            ImmutableDictionary<string, string> globalXmlnsPrefixes,
            bool allowImplicitDefaultXmlns,
            string implicitDefaultXmlns)
        {
            GlobalXmlnsPrefixes = globalXmlnsPrefixes;
            AllowImplicitDefaultXmlns = allowImplicitDefaultXmlns;
            ImplicitDefaultXmlns = implicitDefaultXmlns;
        }

        public ImmutableDictionary<string, string> GlobalXmlnsPrefixes { get; }

        public bool AllowImplicitDefaultXmlns { get; }

        public string ImplicitDefaultXmlns { get; }
    }

    private sealed class ParsedDocumentResult
    {
        public ParsedDocumentResult(
            XamlFileInput input,
            XamlDocumentModel? document,
            ImmutableArray<DiagnosticInfo> diagnostics,
            TimeSpan parseElapsed)
        {
            Input = input;
            Document = document;
            Diagnostics = diagnostics;
            ParseElapsed = parseElapsed;
        }

        public XamlFileInput Input { get; }

        public XamlDocumentModel? Document { get; }

        public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

        public TimeSpan ParseElapsed { get; }
    }

    private sealed class GlobalDiagnosticsResult
    {
        public GlobalDiagnosticsResult(
            ImmutableArray<DiagnosticInfo> diagnostics,
            bool demoteErrorsToWarnings,
            bool metricsEnabled,
            bool metricsDetailed,
            int documentCount,
            TimeSpan elapsed)
        {
            Diagnostics = diagnostics;
            DemoteErrorsToWarnings = demoteErrorsToWarnings;
            MetricsEnabled = metricsEnabled;
            MetricsDetailed = metricsDetailed;
            DocumentCount = documentCount;
            Elapsed = elapsed;
        }

        public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

        public bool DemoteErrorsToWarnings { get; }

        public bool MetricsEnabled { get; }

        public bool MetricsDetailed { get; }

        public int DocumentCount { get; }

        public TimeSpan Elapsed { get; }
    }

    private sealed class CachedGeneratedSource
    {
        public CachedGeneratedSource(string hintName, string source)
        {
            HintName = hintName;
            Source = source;
        }

        public string HintName { get; }

        public string Source { get; }
    }
}
