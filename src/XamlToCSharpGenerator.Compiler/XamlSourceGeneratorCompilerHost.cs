using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Configuration.Sources;
using XamlToCSharpGenerator.Core.Diagnostics;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Compiler;

public static class XamlSourceGeneratorCompilerHost
{
    private const string PersistentCacheVersionHeader = "AXSG-HOTRELOAD-CACHE-V1";

    private static readonly ConcurrentDictionary<string, CachedGeneratedSource> LastGoodGeneratedSources =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void ClearHotReloadFallbackCacheForTesting()
    {
        LastGoodGeneratedSources.Clear();
    }

    public static void Initialize(
        IncrementalGeneratorInitializationContext context,
        IXamlFrameworkProfile frameworkProfile)
    {
        var transformProvider = frameworkProfile.TransformProvider;
        var configurationFileInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select((pair, cancellationToken) =>
            {
                var text = pair.Left;
                var optionsProvider = pair.Right;
                var metadataOptions = optionsProvider.GetOptions(text);
                metadataOptions.TryGetValue(frameworkProfile.BuildContract.SourceItemGroupMetadataName, out var sourceItemGroup);
                var fileName = Path.GetFileName(text.Path);

                if (!FileConfigurationSource.IsSupportedConfigurationFileName(fileName) &&
                    !string.Equals(sourceItemGroup, "XamlSourceGenConfiguration", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var content = text.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                return new ConfigurationFileInput(text.Path, content!);
            })
            .Where(static input => input is not null)
            .Select(static (input, _) => input!);

        var configurationFileSnapshot = configurationFileInputs
            .Collect()
            .Select(static (inputs, _) => BuildConfigurationFileSnapshot(inputs));

        var configurationProvider = context.CompilationProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(configurationFileSnapshot)
            .Select(static (payload, _) =>
            {
                var compilation = payload.Left.Left;
                var optionsProvider = payload.Left.Right;
                var configurationFiles = payload.Right;
                var globalOptions = optionsProvider.GlobalOptions;
                var projectDirectory = GetNullableAnalyzerOption(globalOptions, "build_property.MSBuildProjectDirectory");
                var precedenceIssues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();
                var sourcePrecedence = ResolveConfigurationSourcePrecedence(globalOptions, precedenceIssues);
                var hasDefaultConfigurationFile = configurationFiles.Any(static file =>
                    FileConfigurationSource.IsSupportedConfigurationFileName(Path.GetFileName(file.Path)));

                var configurationBuilder = new XamlSourceGenConfigurationBuilder()
                    .AddSource(new MsBuildConfigurationSource(globalOptions, sourcePrecedence.MsBuild));

                foreach (var configurationFile in configurationFiles)
                {
                    configurationBuilder.AddSource(new FileConfigurationSource(
                        configurationFile.Path,
                        configurationFile.Text,
                        sourcePrecedence.File));
                }

                if (!hasDefaultConfigurationFile)
                {
                    configurationBuilder.AddSource(FileConfigurationSource.CreateProjectDefault(sourcePrecedence.ProjectDefaultFile));
                }

                configurationBuilder.AddSource(new CodeConfigurationSource(compilation, sourcePrecedence.Code));

                var configurationResult = configurationBuilder.Build(new XamlSourceGenConfigurationSourceContext
                {
                    ProjectDirectory = projectDirectory,
                    AssemblyName = compilation.AssemblyName
                });
                if (precedenceIssues.Count != 0)
                {
                    configurationResult = configurationResult with
                    {
                        Issues = configurationResult.Issues.AddRange(precedenceIssues.ToImmutable())
                    };
                }

                var generatorOptions = GeneratorOptions.FromConfiguration(
                    configurationResult.Configuration,
                    globalOptions,
                    compilation.AssemblyName);

                return new GeneratorConfigurationSnapshot(generatorOptions, configurationResult);
            });

        context.RegisterSourceOutput(
            configurationProvider,
            static (sourceContext, snapshot) => ReportConfigurationIssues(sourceContext, snapshot.ConfigurationResult.Issues));

        var optionsProvider = configurationProvider
            .Select(static (snapshot, _) => snapshot.Options);
        var semanticBinder = frameworkProfile.CreateSemanticBinder();
        var emitter = frameworkProfile.CreateEmitter();

        var xamlInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(optionsProvider)
            .Select((pair, cancellationToken) =>
            {
                var text = pair.Left.Left;
                var optionsProvider = pair.Left.Right;
                var generatorOptions = pair.Right;

                if (!generatorOptions.IsEnabled)
                {
                    return null;
                }

                if (!frameworkProfile.BuildContract.IsXamlPath(text.Path))
                {
                    return null;
                }

                var metadataOptions = optionsProvider.GetOptions(text);
                metadataOptions.TryGetValue(frameworkProfile.BuildContract.SourceItemGroupMetadataName, out var sourceItemGroup);

                if (!frameworkProfile.BuildContract.IsXamlSourceItemGroup(sourceItemGroup))
                {
                    return null;
                }

                metadataOptions.TryGetValue(frameworkProfile.BuildContract.TargetPathMetadataName, out var targetPath);
                var textContent = text.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(textContent))
                {
                    return null;
                }

                var normalizedTargetPath = string.IsNullOrWhiteSpace(targetPath) ? Path.GetFileName(text.Path) : targetPath!;
                var normalizedSourceItemGroup = frameworkProfile.BuildContract.NormalizeSourceItemGroup(sourceItemGroup);

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
            .Select(static (inputs, _) => BuildUniqueXamlInputSnapshot(inputs));

        var transformRuleInputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Combine(optionsProvider)
            .Select((pair, cancellationToken) =>
            {
                var text = pair.Left.Left;
                var optionsProvider = pair.Left.Right;
                var generatorOptions = pair.Right;
                if (!generatorOptions.IsEnabled)
                {
                    return null;
                }

                var metadataOptions = optionsProvider.GetOptions(text);
                metadataOptions.TryGetValue(frameworkProfile.BuildContract.SourceItemGroupMetadataName, out var sourceItemGroup);
                if (!frameworkProfile.BuildContract.IsTransformRuleSourceItemGroup(sourceItemGroup))
                {
                    return null;
                }

                var textContent = text.GetText(cancellationToken)?.ToString();
                if (textContent is null)
                {
                    return null;
                }

                if (textContent.Trim().Length == 0)
                {
                    return null;
                }

                return new XamlFrameworkTransformRuleInput(text.Path, textContent);
            })
            .Where(static input => input is not null)
            .Select(static (input, _) => input!);

        var uniqueTransformRuleInputs = transformRuleInputs
            .Collect()
            .Select(static (inputs, _) => BuildUniqueTransformRuleInputSnapshot(inputs));

        var legacyTransformRules = uniqueTransformRuleInputs
            .Select((inputs, _) =>
            {
                if (inputs.IsDefaultOrEmpty)
                {
                    return new XamlFrameworkTransformRuleAggregateResult(
                        XamlTransformConfiguration.Empty,
                        ImmutableArray<DiagnosticInfo>.Empty);
                }

                var parsed = inputs
                    .Select(transformProvider.ParseTransformRule)
                    .ToImmutableArray();
                return transformProvider.MergeTransformRules(parsed);
            });

        var transformRules = legacyTransformRules
            .Combine(configurationProvider)
            .Select((pair, _) =>
            {
                var legacy = pair.Left;
                var configuration = pair.Right.ConfigurationResult.Configuration;
                return BuildEffectiveTransformRules(
                    legacy,
                    configuration,
                    transformProvider);
            });

        context.RegisterSourceOutput(
            transformRules,
            static (sourceContext, rules) => ReportDiagnostics(sourceContext, rules.Diagnostics));

        var parserNamespaceContext = context.CompilationProvider
            .Combine(optionsProvider)
            .Select((pair, _) => frameworkProfile.BuildParserSettings(pair.Left, pair.Right));
        var parserDocumentEnrichers = frameworkProfile.CreateDocumentEnrichers();

        var parsedDocuments = uniqueXamlInputs
            .Combine(parserNamespaceContext)
            .SelectMany((payload, _) =>
            {
                var (inputs, parserContext) = payload;
                if (inputs.IsDefaultOrEmpty)
                {
                    return ImmutableArray<ParsedDocumentResult>.Empty;
                }

                IXamlDocumentParser parser = new SimpleXamlDocumentParser(
                    parserContext.GlobalXmlnsPrefixes,
                    parserContext.AllowImplicitDefaultXmlns,
                    parserContext.ImplicitDefaultXmlns,
                    parserDocumentEnrichers);
                var results = ImmutableArray.CreateBuilder<ParsedDocumentResult>(inputs.Length);
                foreach (var input in inputs)
                {
                    var parseStart = Stopwatch.GetTimestamp();
                    var (document, diagnostics) = parser.Parse(input);
                    var parseElapsed = GetElapsedTimeSince(parseStart);
                    results.Add(new ParsedDocumentResult(input, document, diagnostics, parseElapsed));
                }

                return results.ToImmutable();
            });
        var parsedDocumentsSnapshot = parsedDocuments.Collect();
        var globalControlThemeKeys = parsedDocumentsSnapshot
            .Select(static (documents, _) => BuildGlobalControlThemeKeySet(documents));

        var hotReloadAssemblyHandlerSource = uniqueXamlInputs
            .Combine(optionsProvider)
            .Select((pair, _) =>
            {
                var (inputs, options) = pair;
                return BuildHotReloadAssemblyMetadataHandlerSource(
                    frameworkProfile.Id,
                    !inputs.IsDefaultOrEmpty,
                    options);
            });

        context.RegisterSourceOutput(
            hotReloadAssemblyHandlerSource,
            (sourceContext, source) =>
            {
                if (source is null)
                {
                    return;
                }

                if (source.Trim().Length == 0)
                {
                    return;
                }

                var hintName = NormalizeFrameworkHintName(
                    frameworkProfile.Id,
                    "__SourceGenHotReloadAssemblyMetadataHandler.g.cs");
                _ = TryAddSource(sourceContext, hintName, source);
            });

        var globalGraphDiagnostics = parsedDocuments
            .Collect()
            .Combine(optionsProvider)
            .Select((pair, _) =>
            {
                var globalGraphStart = Stopwatch.GetTimestamp();
                var documents = ImmutableArray.CreateBuilder<XamlDocumentModel>(pair.Left.Length);
                foreach (var parsed in pair.Left)
                {
                    if (parsed.Document is not null)
                    {
                        documents.Add(parsed.Document);
                    }
                }

                var diagnostics = AnalyzeGlobalDocumentGraph(documents.ToImmutable(), pair.Right);
                var globalGraphElapsed = GetElapsedTimeSince(globalGraphStart);
                return new GlobalDiagnosticsResult(
                    diagnostics,
                    IsHotReloadErrorResilienceEnabled(pair.Right),
                    pair.Right.MetricsEnabled,
                    pair.Right.MetricsDetailed,
                    pair.Left.Length,
                    globalGraphElapsed);
            });

        context.RegisterSourceOutput(globalGraphDiagnostics,
            (sourceContext, diagnosticsResult) =>
            {
                ReportDiagnostics(
                    sourceContext,
                    diagnosticsResult.Diagnostics,
                    diagnosticsResult.DemoteErrorsToWarnings);
                ReportGlobalMetrics(sourceContext, diagnosticsResult);
            });

        context.RegisterSourceOutput(
            parsedDocuments
                .Combine(globalControlThemeKeys)
                .Combine(context.CompilationProvider.Combine(optionsProvider).Combine(transformRules)),
            (sourceContext, payload) =>
            {
                var parsedAndSnapshot = payload.Left;
                var parsedDocument = parsedAndSnapshot.Left;
                var controlThemeKeys = parsedAndSnapshot.Right;
                var compilationAndRules = payload.Right;
                var compilationAndOptions = compilationAndRules.Left;
                var compilation = compilationAndOptions.Left;
                var options = compilationAndOptions.Right;
                var transformRules = compilationAndRules.Right;
                var parseResult = parsedDocument.Document;
                var parseDiagnostics = ApplyGlobalParityDiagnosticFilters(
                    parsedDocument.Diagnostics,
                    controlThemeKeys);
                parseDiagnostics = ApplyDefaultDiagnosticPolicy(parseDiagnostics, options);
                var resilienceEnabled = IsHotReloadErrorResilienceEnabled(options);
                var cacheKey = BuildHotReloadCacheKey(
                    frameworkProfile.Id,
                    options.AssemblyName,
                    parsedDocument.Input.FilePath,
                    parsedDocument.Input.TargetPath,
                    options.ProjectDirectory);
                var bindElapsed = TimeSpan.Zero;
                var emitElapsed = TimeSpan.Zero;
                var semanticDiagnosticsCount = 0;
                var typeResolutionFallbackCount = 0;
                var generatedSource = false;
                var usedFallbackSource = false;
                var duplicateHint = false;
                var status = "parse";
                var totalStartTimestamp = options.MetricsEnabled ? Stopwatch.GetTimestamp() : 0L;

                try
                {
                    (parseResult, parseDiagnostics) = ApplyDocumentConventions(
                        parsedDocument.Input,
                        parseResult,
                        parseDiagnostics,
                        compilation,
                        options);

                    ReportDiagnostics(sourceContext, parseDiagnostics, resilienceEnabled);
                    if (parseResult is null)
                    {
                        usedFallbackSource = TryUseCachedSource(
                            sourceContext,
                            cacheKey,
                            parsedDocument.Input.FilePath,
                            resilienceEnabled,
                            options,
                            frameworkProfile.Id);
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
                    var (viewModel, semanticDiagnostics) = semanticBinder.Bind(parseResult, compilation, options, transformRules.Configuration);
                    bindElapsed = GetElapsedTimeSince(bindStart);
                    semanticDiagnostics = ApplyGlobalParityDiagnosticFilters(
                        semanticDiagnostics,
                        controlThemeKeys);
                    semanticDiagnostics = ApplyDefaultDiagnosticPolicy(semanticDiagnostics, options);
                    semanticDiagnosticsCount = semanticDiagnostics.Length;
                    typeResolutionFallbackCount = semanticDiagnostics.Count(static diagnostic => diagnostic.Id == "AXSG0113");
                    ReportDiagnostics(sourceContext, semanticDiagnostics, resilienceEnabled);
                    if (viewModel is null)
                    {
                        usedFallbackSource = TryUseCachedSource(
                            sourceContext,
                            cacheKey,
                            parseResult.FilePath,
                            resilienceEnabled,
                            options,
                            frameworkProfile.Id);
                        status = usedFallbackSource ? "fallback-bind" : "bind-failed";
                        return;
                    }

                    status = "emit";
                    var emitStart = Stopwatch.GetTimestamp();
                    try
                    {
                        var (hintName, source) = emitter.Emit(viewModel);
                        hintName = NormalizeFrameworkHintName(frameworkProfile.Id, hintName);
                        if (TryAddSource(sourceContext, hintName, source))
                        {
                            generatedSource = true;
                            status = "generated";
                            if (resilienceEnabled)
                            {
                                var cachedSource = new CachedGeneratedSource(hintName, source);
                                LastGoodGeneratedSources[cacheKey] = cachedSource;
                                PersistCachedSource(cachedSource, cacheKey, options, frameworkProfile.Id);
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
                        usedFallbackSource = TryUseCachedSource(
                            sourceContext,
                            cacheKey,
                            parseResult.FilePath,
                            resilienceEnabled,
                            options,
                            frameworkProfile.Id);
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
                        emitElapsed = GetElapsedTimeSince(emitStart);
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
                        typeResolutionFallbackCount,
                        parsedDocument.ParseElapsed,
                        bindElapsed,
                        emitElapsed,
                        options.MetricsEnabled ? GetElapsedTimeSince(totalStartTimestamp) : TimeSpan.Zero,
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
                "AXSG0004" => DiagnosticCatalog.SharedDirectiveInvalid,
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
                "AXSG0112" => DiagnosticCatalog.TypeResolutionAmbiguous,
                "AXSG0113" => DiagnosticCatalog.TypeResolutionFallbackUsed,
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

    private static void ReportConfigurationIssues(
        SourceProductionContext context,
        ImmutableArray<XamlSourceGenConfigurationIssue> issues)
    {
        if (issues.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var issue in issues)
        {
            var severity = issue.Severity switch
            {
                XamlSourceGenConfigurationIssueSeverity.Error => DiagnosticSeverity.Error,
                XamlSourceGenConfigurationIssueSeverity.Warning => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info
            };

            var descriptor = new DiagnosticDescriptor(
                id: issue.Code,
                title: "Source generator configuration issue",
                messageFormat: "{0}",
                category: "AXSG.Configuration",
                defaultSeverity: severity,
                isEnabledByDefault: true);

            var sourceName = string.IsNullOrWhiteSpace(issue.SourceName) ? "Configuration" : issue.SourceName!;
            var message = "[" + sourceName + "] " + issue.Message;
            context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, message));
        }
    }

    internal static ImmutableArray<ConfigurationFileInput> BuildConfigurationFileSnapshot(
        ImmutableArray<ConfigurationFileInput> inputs)
    {
        if (inputs.IsDefaultOrEmpty)
        {
            return ImmutableArray<ConfigurationFileInput>.Empty;
        }

        var byPath = new Dictionary<string, ConfigurationFileInput>(inputs.Length, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            var dedupeKey = NormalizeDedupePath(input.Path);
            if (!byPath.TryGetValue(dedupeKey, out var existing) ||
                ShouldReplaceConfigurationFile(input, existing))
            {
                byPath[dedupeKey] = input;
            }
        }

        return SortConfigurationFileInputs(byPath.Values);
    }

    internal static ImmutableArray<XamlFileInput> BuildUniqueXamlInputSnapshot(
        ImmutableArray<XamlFileInput> inputs)
    {
        if (inputs.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlFileInput>.Empty;
        }

        var byPath = new Dictionary<string, XamlFileInput>(inputs.Length, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            var dedupeKey = NormalizeDedupePath(input.FilePath);
            if (!byPath.TryGetValue(dedupeKey, out var existing) ||
                ShouldReplaceXamlInput(input, existing))
            {
                byPath[dedupeKey] = input;
            }
        }

        return SortXamlFileInputs(byPath.Values);
    }

    internal static ImmutableArray<XamlFrameworkTransformRuleInput> BuildUniqueTransformRuleInputSnapshot(
        ImmutableArray<XamlFrameworkTransformRuleInput> inputs)
    {
        if (inputs.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlFrameworkTransformRuleInput>.Empty;
        }

        var byPath = new Dictionary<string, XamlFrameworkTransformRuleInput>(inputs.Length, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < inputs.Length; index++)
        {
            var input = inputs[index];
            var dedupeKey = NormalizeDedupePath(input.FilePath);
            if (!byPath.TryGetValue(dedupeKey, out var existing) ||
                ShouldReplaceTransformRuleInput(input, existing))
            {
                byPath[dedupeKey] = input;
            }
        }

        return SortTransformRuleInputs(byPath.Values);
    }

    private static XamlFrameworkTransformRuleAggregateResult BuildEffectiveTransformRules(
        XamlFrameworkTransformRuleAggregateResult legacyTransformRules,
        XamlSourceGenConfiguration configuration,
        IXamlFrameworkTransformProvider transformProvider)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        diagnostics.AddRange(legacyTransformRules.Diagnostics);

        var configurationTransformRuleResults = ParseConfigurationTransformRuleInputs(
            configuration.Transform.RawTransformDocuments,
            transformProvider);
        var configurationTransformRules = transformProvider.MergeTransformRules(configurationTransformRuleResults);
        diagnostics.AddRange(configurationTransformRules.Diagnostics);

        var mergedConfiguration = MergeTransformConfigurations(
            legacyTransformRules.Configuration,
            TransformConfigurationSourceKind.LegacyRuleFiles,
            configurationTransformRules.Configuration,
            TransformConfigurationSourceKind.UnifiedConfigurationRawDocuments,
            diagnostics);

        mergedConfiguration = MergeTransformConfigurations(
            mergedConfiguration,
            TransformConfigurationSourceKind.UnifiedConfigurationRawDocuments,
            configuration.Transform.Configuration,
            TransformConfigurationSourceKind.UnifiedConfigurationTypedObject,
            diagnostics);

        return new XamlFrameworkTransformRuleAggregateResult(
            mergedConfiguration,
            diagnostics.ToImmutable());
    }

    internal static ImmutableArray<XamlFrameworkTransformRuleResult> ParseConfigurationTransformRuleInputs(
        ImmutableDictionary<string, string> rawTransformDocuments,
        IXamlFrameworkTransformProvider transformProvider)
    {
        if (rawTransformDocuments.Count == 0)
        {
            return ImmutableArray<XamlFrameworkTransformRuleResult>.Empty;
        }

        var orderedDocuments = new KeyValuePair<string, string>[rawTransformDocuments.Count];
        var orderedIndex = 0;
        foreach (var pair in rawTransformDocuments)
        {
            orderedDocuments[orderedIndex++] = pair;
        }

        Array.Sort(
            orderedDocuments,
            static (left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));

        var results = new XamlFrameworkTransformRuleResult[orderedDocuments.Length];
        for (var index = 0; index < orderedDocuments.Length; index++)
        {
            var pair = orderedDocuments[index];
            var filePath = BuildConfigurationTransformRuleDocumentPath(pair.Key);
            results[index] = transformProvider.ParseTransformRule(new XamlFrameworkTransformRuleInput(filePath, pair.Value));
        }

        return ImmutableArray.Create(results);
    }

    private static string BuildConfigurationTransformRuleDocumentPath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "xaml-sourcegen.config.json::transform.rawTransformDocuments[<empty>]";
        }

        return "xaml-sourcegen.config.json::transform.rawTransformDocuments[" + key.Trim() + "]";
    }

    internal static XamlTransformConfiguration MergeTransformConfigurations(
        XamlTransformConfiguration baseConfiguration,
        TransformConfigurationSourceKind baseSourceKind,
        XamlTransformConfiguration overlayConfiguration,
        TransformConfigurationSourceKind overlaySourceKind,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        if (overlayConfiguration.TypeAliases.IsDefaultOrEmpty &&
            overlayConfiguration.PropertyAliases.IsDefaultOrEmpty)
        {
            return baseConfiguration;
        }

        var typeAliases = new Dictionary<string, TransformTypeAliasEntry>(
            baseConfiguration.TypeAliases.Length + overlayConfiguration.TypeAliases.Length,
            StringComparer.OrdinalIgnoreCase);
        foreach (var alias in baseConfiguration.TypeAliases)
        {
            typeAliases[BuildTypeAliasKey(alias)] = new TransformTypeAliasEntry(alias, baseSourceKind);
        }

        foreach (var alias in overlayConfiguration.TypeAliases)
        {
            var key = BuildTypeAliasKey(alias);
            if (typeAliases.TryGetValue(key, out var existing) &&
                existing.SourceKind == TransformConfigurationSourceKind.LegacyRuleFiles &&
                overlaySourceKind != TransformConfigurationSourceKind.LegacyRuleFiles)
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

            typeAliases[key] = new TransformTypeAliasEntry(alias, overlaySourceKind);
        }

        var propertyAliases = new Dictionary<string, TransformPropertyAliasEntry>(
            baseConfiguration.PropertyAliases.Length + overlayConfiguration.PropertyAliases.Length,
            StringComparer.OrdinalIgnoreCase);
        foreach (var alias in baseConfiguration.PropertyAliases)
        {
            propertyAliases[BuildPropertyAliasKey(alias)] = new TransformPropertyAliasEntry(alias, baseSourceKind);
        }

        foreach (var alias in overlayConfiguration.PropertyAliases)
        {
            var key = BuildPropertyAliasKey(alias);
            if (propertyAliases.TryGetValue(key, out var existing) &&
                existing.SourceKind == TransformConfigurationSourceKind.LegacyRuleFiles &&
                overlaySourceKind != TransformConfigurationSourceKind.LegacyRuleFiles)
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

            propertyAliases[key] = new TransformPropertyAliasEntry(alias, overlaySourceKind);
        }

        return new XamlTransformConfiguration(
            SortTypeAliases(typeAliases),
            SortPropertyAliases(propertyAliases));
    }

    private static string BuildTypeAliasKey(XamlTypeAliasRule alias)
    {
        return string.Concat(alias.XmlNamespace, ":", alias.XamlTypeName);
    }

    private static string BuildPropertyAliasKey(XamlPropertyAliasRule alias)
    {
        return string.Concat(alias.TargetTypeName, ":", alias.XamlPropertyName);
    }

    private static ImmutableArray<XamlTypeAliasRule> SortTypeAliases(
        Dictionary<string, TransformTypeAliasEntry> aliases)
    {
        if (aliases.Count == 0)
        {
            return ImmutableArray<XamlTypeAliasRule>.Empty;
        }

        var sorted = new KeyValuePair<string, TransformTypeAliasEntry>[aliases.Count];
        ((ICollection<KeyValuePair<string, TransformTypeAliasEntry>>)aliases).CopyTo(sorted, 0);
        Array.Sort(sorted, static (left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));

        var result = new XamlTypeAliasRule[sorted.Length];
        for (var index = 0; index < sorted.Length; index++)
        {
            result[index] = sorted[index].Value.Alias;
        }

        return ImmutableArray.Create(result);
    }

    private static ImmutableArray<XamlPropertyAliasRule> SortPropertyAliases(
        Dictionary<string, TransformPropertyAliasEntry> aliases)
    {
        if (aliases.Count == 0)
        {
            return ImmutableArray<XamlPropertyAliasRule>.Empty;
        }

        var sorted = new KeyValuePair<string, TransformPropertyAliasEntry>[aliases.Count];
        ((ICollection<KeyValuePair<string, TransformPropertyAliasEntry>>)aliases).CopyTo(sorted, 0);
        Array.Sort(sorted, static (left, right) => string.Compare(left.Key, right.Key, StringComparison.OrdinalIgnoreCase));

        var result = new XamlPropertyAliasRule[sorted.Length];
        for (var index = 0; index < sorted.Length; index++)
        {
            result[index] = sorted[index].Value.Alias;
        }

        return ImmutableArray.Create(result);
    }

    private static ImmutableArray<DiagnosticInfo> ApplyGlobalParityDiagnosticFilters(
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

        if (filtered is null)
        {
            return diagnostics;
        }

        return filtered.ToImmutable();
    }

    private static ImmutableArray<DiagnosticInfo> ApplyDefaultDiagnosticPolicy(
        ImmutableArray<DiagnosticInfo> diagnostics,
        GeneratorOptions options)
    {
        _ = options;
        return diagnostics;
    }

    private static ImmutableHashSet<string> BuildGlobalControlThemeKeySet(
        ImmutableArray<ParsedDocumentResult> allParsedDocuments)
    {
        var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var parsed in allParsedDocuments)
        {
            var document = parsed.Document;
            if (document is null || document.ControlThemes.IsDefaultOrEmpty)
            {
                continue;
            }

            foreach (var controlTheme in document.ControlThemes)
            {
                if (string.IsNullOrWhiteSpace(controlTheme.Key))
                {
                    continue;
                }

                keys.Add(NormalizeControlThemeKey(controlTheme.Key!));
            }
        }

        return keys.ToImmutable();
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
        var message = diagnostic.Message;
        var markerIndex = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var start = markerIndex + marker.Length;
        if (start >= message.Length)
        {
            return false;
        }

        var end = message.IndexOf('\'', start);
        if (end <= start)
        {
            return false;
        }

        var key = message.Substring(start, end - start).Trim();
        if (key.Length == 0)
        {
            return false;
        }

        return globalControlThemeKeys.Contains(NormalizeControlThemeKey(key));
    }

    private static string NormalizeControlThemeKey(string key)
    {
        return key.Trim();
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
        int typeResolutionFallbackCount,
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
                "XAML compile metrics for '{0}': total={1}, parse={2}, bind={3}, emit={4}, status={5}, generated={6}, fallback={7}, duplicateHint={8}, parseDiagnostics={9}, semanticDiagnostics={10}, typeResolutionFallbacks={11}.",
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
                semanticDiagnosticsCount,
                typeResolutionFallbackCount)
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
        if (filePath is null)
        {
            return Location.None;
        }

        var normalizedFilePath = filePath.Trim();
        if (normalizedFilePath.Length == 0)
        {
            return Location.None;
        }

        var lineIndex = Math.Max(0, line - 1);
        var columnIndex = Math.Max(0, column - 1);
        return Location.Create(
            normalizedFilePath,
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
        bool resilienceEnabled,
        GeneratorOptions options,
        string frameworkProfileId)
    {
        if (!resilienceEnabled)
        {
            return false;
        }

        if (!LastGoodGeneratedSources.TryGetValue(cacheKey, out var cached) &&
            !TryReadPersistedCachedSource(cacheKey, options, frameworkProfileId, out cached))
        {
            return false;
        }

        LastGoodGeneratedSources[cacheKey] = cached;
        _ = TryAddSource(sourceContext, cached.HintName, cached.Source);

        var location = CreateLocation(filePath, 1, 1);

        sourceContext.ReportDiagnostic(Diagnostic.Create(
            DiagnosticCatalog.HotReloadFallbackUsed,
            location,
            "Hot reload fallback is using the last successfully generated XAML source for this file until current XAML errors are fixed."));
        return true;
    }

    private static void PersistCachedSource(
        CachedGeneratedSource cachedSource,
        string cacheKey,
        GeneratorOptions options,
        string frameworkProfileId)
    {
        if (!TryGetPersistentCacheFilePath(cacheKey, options, frameworkProfileId, out var cacheFilePath))
        {
            return;
        }

        var cacheDirectoryPath = Path.GetDirectoryName(cacheFilePath);
        if (string.IsNullOrWhiteSpace(cacheDirectoryPath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(cacheDirectoryPath);
            var payload = SerializeCachedGeneratedSource(cachedSource);
            WriteAllTextAtomically(cacheFilePath, payload);
        }
        catch
        {
            // Best effort persistent fallback cache only.
        }
    }

    private static bool TryReadPersistedCachedSource(
        string cacheKey,
        GeneratorOptions options,
        string frameworkProfileId,
        out CachedGeneratedSource cached)
    {
        cached = default!;
        if (!TryGetPersistentCacheFilePath(cacheKey, options, frameworkProfileId, out var cacheFilePath) ||
            !File.Exists(cacheFilePath))
        {
            return false;
        }

        try
        {
            var payload = File.ReadAllText(cacheFilePath);
            return TryDeserializeCachedGeneratedSource(payload, out cached);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPersistentCacheFilePath(
        string cacheKey,
        GeneratorOptions options,
        string frameworkProfileId,
        out string cacheFilePath)
    {
        cacheFilePath = string.Empty;
        var cacheDirectory = ResolvePersistentCacheDirectory(options, frameworkProfileId);
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            return false;
        }

        cacheFilePath = Path.Combine(cacheDirectory, ComputeStableHashHex(cacheKey) + ".cache");
        return true;
    }

    private static string? ResolvePersistentCacheDirectory(GeneratorOptions options, string frameworkProfileId)
    {
        var intermediateOutputPath = ResolveIntermediateOutputPath(options);
        if (string.IsNullOrWhiteSpace(intermediateOutputPath))
        {
            return null;
        }

        return Path.Combine(
            intermediateOutputPath,
            "XamlToCSharpGenerator",
            "HotReloadFallback",
            NormalizeCachePathSegment(NormalizeFrameworkIdentifier(frameworkProfileId)),
            NormalizeCachePathSegment(options.AssemblyName ?? "UnknownAssembly"));
    }

    private static string? ResolveIntermediateOutputPath(GeneratorOptions options)
    {
        var configuredPath = options.IntermediateOutputPath;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            configuredPath = options.BaseIntermediateOutputPath;
        }

        if (configuredPath is null)
        {
            return null;
        }

        var trimmedPath = configuredPath.Trim();
        if (trimmedPath.Length == 0)
        {
            return null;
        }

        try
        {
            if (Path.IsPathRooted(trimmedPath))
            {
                return Path.GetFullPath(trimmedPath);
            }

            if (!string.IsNullOrWhiteSpace(options.ProjectDirectory))
            {
                return Path.GetFullPath(Path.Combine(options.ProjectDirectory!, trimmedPath));
            }

            return Path.GetFullPath(trimmedPath);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeCachePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        var invalidPathCharacters = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (Array.IndexOf(invalidPathCharacters, chars[index]) >= 0)
            {
                chars[index] = '_';
            }
        }

        var normalized = new string(chars).Trim();
        return normalized.Length == 0 ? "Unknown" : normalized;
    }

    private static string SerializeCachedGeneratedSource(CachedGeneratedSource cachedSource)
    {
        return PersistentCacheVersionHeader + "\n" +
               cachedSource.HintName + "\n" +
               cachedSource.Source;
    }

    private static bool TryDeserializeCachedGeneratedSource(
        string payload,
        out CachedGeneratedSource cachedSource)
    {
        cachedSource = default!;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        var header = PersistentCacheVersionHeader + "\n";
        if (!payload.StartsWith(header, StringComparison.Ordinal))
        {
            return false;
        }

        var hintStart = header.Length;
        var hintEnd = payload.IndexOf('\n', hintStart);
        if (hintEnd <= hintStart)
        {
            return false;
        }

        var hintName = payload.Substring(hintStart, hintEnd - hintStart).Trim();
        if (string.IsNullOrWhiteSpace(hintName))
        {
            return false;
        }

        var source = payload.Substring(hintEnd + 1);
        cachedSource = new CachedGeneratedSource(hintName, source);
        return true;
    }

    private static void WriteAllTextAtomically(string filePath, string contents)
    {
        var tempFilePath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempFilePath, contents);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            File.Move(tempFilePath, filePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Best effort temp cleanup only.
            }
        }
    }

    private static string BuildHotReloadCacheKey(
        string frameworkProfileId,
        string? assemblyName,
        string filePath,
        string targetPath,
        string? projectDirectory)
    {
        var normalizedAssemblyName = string.IsNullOrWhiteSpace(assemblyName)
            ? "UnknownAssembly"
            : assemblyName!;
        var normalizedProfileId = string.IsNullOrWhiteSpace(frameworkProfileId)
            ? "UnknownFramework"
            : frameworkProfileId.Trim();
        var normalizedFilePath = NormalizeCacheKeyFilePath(filePath, projectDirectory);
        var normalizedTargetPath = NormalizeCacheKeyTargetPath(targetPath);
        return normalizedProfileId + "|" + normalizedAssemblyName + "|" + normalizedFilePath + "|" + normalizedTargetPath;
    }

    private static string NormalizeCacheKeyFilePath(string filePath, string? projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        var normalized = filePath;
        try
        {
            if (Path.IsPathRooted(filePath))
            {
                normalized = Path.GetFullPath(filePath);
            }
            else if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                normalized = Path.GetFullPath(Path.Combine(projectDirectory!, filePath));
            }
        }
        catch
        {
            // Keep lexical normalization when physical normalization fails.
        }

        return NormalizePathSegments(normalized);
    }

    private static string NormalizeCacheKeyTargetPath(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return string.Empty;
        }

        return NormalizePathSegments(targetPath);
    }

    private static string ComputeStableHashHex(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var character in text)
            {
                hash ^= character;
                hash *= 16777619u;
            }

            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }

    private static string NormalizeFrameworkHintName(string frameworkProfileId, string hintName)
    {
        var normalizedHintName = hintName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHintName))
        {
            return NormalizeFrameworkIdentifier(frameworkProfileId) + ".Generated.XamlSourceGen.g.cs";
        }

        var normalizedProfileId = NormalizeFrameworkIdentifier(frameworkProfileId);
        if (normalizedHintName!.StartsWith(normalizedProfileId + ".", StringComparison.Ordinal))
        {
            return normalizedHintName;
        }

        return normalizedProfileId + "." + normalizedHintName;
    }

    private static string NormalizeFrameworkIdentifier(string frameworkProfileId)
    {
        if (string.IsNullOrWhiteSpace(frameworkProfileId))
        {
            return "Framework";
        }

        Span<char> buffer = stackalloc char[frameworkProfileId.Length];
        var length = 0;
        foreach (var ch in frameworkProfileId)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                buffer[length++] = ch;
            }
        }

        if (length == 0)
        {
            return "Framework";
        }

        var normalized = new string(buffer.Slice(0, length).ToArray());
        if (char.IsDigit(normalized[0]))
        {
            normalized = "_" + normalized;
        }

        return normalized;
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

    internal static (
        XamlDocumentModel? Document,
        ImmutableArray<DiagnosticInfo> Diagnostics) ApplyDocumentConventions(
        XamlFileInput input,
        XamlDocumentModel? document,
        ImmutableArray<DiagnosticInfo> diagnostics,
        Compilation compilation,
        GeneratorOptions options)
    {
        if (document is null)
        {
            return (null, diagnostics);
        }

        if (!options.InferClassFromPath || document.IsClassBacked)
        {
            return (document, diagnostics);
        }

        var inferredClassName = TryInferClassNameFromTargetPath(input.TargetPath, options);
        if (inferredClassName is null)
        {
            return (document, diagnostics);
        }

        var inferredClassNameValue = inferredClassName.Trim();
        if (inferredClassNameValue.Length == 0)
        {
            return (document, diagnostics);
        }

        if (compilation.GetTypeByMetadataName(inferredClassNameValue) is null)
        {
            return (document, diagnostics);
        }

        var adjustedDocument = document with
        {
            ClassFullName = inferredClassNameValue
        };

        var diagnosticsBuilder = diagnostics.ToBuilder();
        RemoveDiagnosticsById(diagnosticsBuilder, "AXSG0002");

        return (adjustedDocument, diagnosticsBuilder.ToImmutable());
    }

    private static void RemoveDiagnosticsById(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        string diagnosticId)
    {
        for (var index = diagnostics.Count - 1; index >= 0; index--)
        {
            if (string.Equals(diagnostics[index].Id, diagnosticId, StringComparison.Ordinal))
            {
                diagnostics.RemoveAt(index);
            }
        }
    }

    internal static string? TryInferClassNameFromTargetPath(string targetPath, GeneratorOptions options)
    {
        var rootNamespace = NormalizeRootNamespace(options.RootNamespace ?? options.AssemblyName);
        if (rootNamespace.Length == 0)
        {
            return null;
        }

        var effectiveTargetPath = targetPath;
        if (Path.IsPathRooted(effectiveTargetPath))
        {
            effectiveTargetPath = Path.GetFileName(effectiveTargetPath);
        }

        var effectiveTargetPathSpan = effectiveTargetPath.AsSpan();
        effectiveTargetPathSpan = TrimWhitespace(effectiveTargetPathSpan);
        if (effectiveTargetPathSpan.Length >= 2 &&
            effectiveTargetPathSpan[0] == '.' &&
            effectiveTargetPathSpan[1] == '/')
        {
            effectiveTargetPathSpan = effectiveTargetPathSpan.Slice(2);
        }

        if (effectiveTargetPathSpan.Length > 0 &&
            (effectiveTargetPathSpan[0] == '/' || effectiveTargetPathSpan[0] == '\\'))
        {
            effectiveTargetPathSpan = effectiveTargetPathSpan.Slice(1);
        }

        var lastSeparatorIndex = LastIndexOfPathSeparator(effectiveTargetPathSpan);
        var fileName = lastSeparatorIndex >= 0
            ? effectiveTargetPathSpan.Slice(lastSeparatorIndex + 1)
            : effectiveTargetPathSpan;
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex > 0)
        {
            fileName = fileName.Slice(0, extensionIndex);
        }

        var normalizedClassName = NormalizeIdentifier(fileName, "GeneratedView");
        if (string.IsNullOrWhiteSpace(normalizedClassName))
        {
            return null;
        }

        var normalizedClassNameValue = normalizedClassName!;
        var builder = new StringBuilder(rootNamespace.Length + effectiveTargetPathSpan.Length + normalizedClassNameValue.Length + 8);
        builder.Append(rootNamespace);
        if (lastSeparatorIndex > 0)
        {
            AppendNormalizedPathSegments(builder, effectiveTargetPathSpan.Slice(0, lastSeparatorIndex));
        }

        if (builder.Length > 0)
        {
            builder.Append('.');
        }

        builder.Append(normalizedClassNameValue);
        return builder.ToString();
    }

    internal static string NormalizeRootNamespace(string? rootNamespace)
    {
        if (rootNamespace is null)
        {
            return string.Empty;
        }

        var trimmedRootNamespace = TrimWhitespace(rootNamespace.AsSpan());
        if (trimmedRootNamespace.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(trimmedRootNamespace.Length);
        AppendNormalizedNamespaceSegments(builder, trimmedRootNamespace);
        return builder.ToString();
    }

    internal static ImmutableArray<string> NormalizeNamespaceSegments(string rawNamespace)
    {
        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return ImmutableArray<string>.Empty;
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        AppendNormalizedNamespaceSegments(segments, rawNamespace.AsSpan());

        return segments.Count == 0
            ? ImmutableArray<string>.Empty
            : segments.ToImmutable();
    }

    private static string? NormalizeIdentifier(string? token, string? fallback)
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

    private static string? NormalizeIdentifier(ReadOnlySpan<char> token, string? fallback)
    {
        var trimmedToken = TrimWhitespace(token);
        if (trimmedToken.Length == 0)
        {
            return fallback;
        }

        Span<char> buffer = stackalloc char[trimmedToken.Length + 1];
        var length = 0;
        for (var index = 0; index < trimmedToken.Length; index++)
        {
            var ch = trimmedToken[index];
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                buffer[length++] = ch;
            }
        }

        if (length == 0)
        {
            return fallback;
        }

        if (char.IsDigit(buffer[0]))
        {
            for (var index = length; index > 0; index--)
            {
                buffer[index] = buffer[index - 1];
            }

            buffer[0] = '_';
            length++;
        }

        return new string(buffer.Slice(0, length).ToArray());
    }

    private static void AppendNormalizedNamespaceSegments(
        ImmutableArray<string>.Builder segments,
        ReadOnlySpan<char> rawNamespace)
    {
        var segmentStart = 0;
        for (var index = 0; index <= rawNamespace.Length; index++)
        {
            if (index < rawNamespace.Length && rawNamespace[index] != '.')
            {
                continue;
            }

            var normalizedSegment = NormalizeIdentifier(rawNamespace.Slice(segmentStart, index - segmentStart), null);
            if (normalizedSegment is { Length: > 0 })
            {
                segments.Add(normalizedSegment);
            }

            segmentStart = index + 1;
        }
    }

    private static void AppendNormalizedNamespaceSegments(
        StringBuilder builder,
        ReadOnlySpan<char> rawNamespace)
    {
        var segmentStart = 0;
        for (var index = 0; index <= rawNamespace.Length; index++)
        {
            if (index < rawNamespace.Length && rawNamespace[index] != '.')
            {
                continue;
            }

            var normalizedSegment = NormalizeIdentifier(rawNamespace.Slice(segmentStart, index - segmentStart), null);
            if (!string.IsNullOrEmpty(normalizedSegment))
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(normalizedSegment);
            }

            segmentStart = index + 1;
        }
    }

    private static void AppendNormalizedPathSegments(
        StringBuilder builder,
        ReadOnlySpan<char> rawPath)
    {
        var segmentStart = 0;
        for (var index = 0; index <= rawPath.Length; index++)
        {
            if (index < rawPath.Length && !IsPathSeparator(rawPath[index]))
            {
                continue;
            }

            var normalizedSegment = NormalizeIdentifier(rawPath.Slice(segmentStart, index - segmentStart), null);
            if (!string.IsNullOrEmpty(normalizedSegment))
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                builder.Append(normalizedSegment);
            }

            segmentStart = index + 1;
        }
    }

    private static ReadOnlySpan<char> TrimWhitespace(ReadOnlySpan<char> value)
    {
        var start = 0;
        var end = value.Length - 1;
        while (start < value.Length && char.IsWhiteSpace(value[start]))
        {
            start++;
        }

        while (end >= start && char.IsWhiteSpace(value[end]))
        {
            end--;
        }

        return start > end
            ? ReadOnlySpan<char>.Empty
            : value.Slice(start, end - start + 1);
    }

    private static int LastIndexOfPathSeparator(ReadOnlySpan<char> value)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (IsPathSeparator(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsPathSeparator(char ch)
    {
        return ch == '/' || ch == '\\';
    }

    private static TimeSpan GetElapsedTimeSince(long startTimestamp)
    {
        var elapsedTimestamp = Stopwatch.GetTimestamp() - startTimestamp;
        return TimeSpan.FromSeconds((double)elapsedTimestamp / Stopwatch.Frequency);
    }

    internal static ImmutableArray<DiagnosticInfo> AnalyzeGlobalDocumentGraph(
        ImmutableArray<XamlDocumentModel> documents,
        GeneratorOptions options)
    {
        if (documents.IsDefaultOrEmpty)
        {
            return ImmutableArray<DiagnosticInfo>.Empty;
        }

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? "UnknownAssembly";
        var sortedDocuments = new XamlDocumentModel[documents.Length];
        documents.CopyTo(sortedDocuments, 0);
        Array.Sort(sortedDocuments, static (left, right) => string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase));

        var entriesByUri = new Dictionary<string, DocumentGraphEntry>(sortedDocuments.Length, StringComparer.OrdinalIgnoreCase);
        var entriesByTargetPath = new Dictionary<string, DocumentGraphEntry>(sortedDocuments.Length, StringComparer.OrdinalIgnoreCase);
        var orderedEntries = new List<DocumentGraphEntry>(sortedDocuments.Length);

        for (var index = 0; index < sortedDocuments.Length; index++)
        {
            var document = sortedDocuments[index];
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
            if (entriesByTargetPath.TryGetValue(normalizedTargetPath, out _))
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

            if (entriesByUri.TryGetValue(buildUri, out _))
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

        var edgesBySource = new Dictionary<string, List<IncludeGraphEdge>>(orderedEntries.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < orderedEntries.Count; index++)
        {
            var entry = orderedEntries[index];
            var includes = entry.Document.Includes;
            if (includes.IsDefaultOrEmpty)
            {
                continue;
            }

            for (var includeIndex = 0; includeIndex < includes.Length; includeIndex++)
            {
                var include = includes[includeIndex];
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
                    edges = new List<IncludeGraphEdge>(Math.Min(includes.Length, 4));
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

        var sortedEdgesBySource = new Dictionary<string, IncludeGraphEdge[]>(edgesBySource.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in edgesBySource)
        {
            var edges = pair.Value;
            var sortedEdges = edges.ToArray();
            Array.Sort(sortedEdges, static (left, right) => string.Compare(left.TargetUri, right.TargetUri, StringComparison.OrdinalIgnoreCase));
            sortedEdgesBySource[pair.Key] = sortedEdges;
        }

        var state = new Dictionary<string, byte>(sortedEdgesBySource.Count, StringComparer.OrdinalIgnoreCase);
        var cycleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sortedEdgesBySource.Count == 0)
        {
            return diagnostics.ToImmutable();
        }

        var sortedSourceUris = new string[sortedEdgesBySource.Count];
        sortedEdgesBySource.Keys.CopyTo(sortedSourceUris, 0);
        Array.Sort(sortedSourceUris, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sortedSourceUris.Length; index++)
        {
            DetectCycle(sortedSourceUris[index]);
        }

        return diagnostics.ToImmutable();

        void DetectCycle(string sourceUri)
        {
            if (state.TryGetValue(sourceUri, out var currentState))
            {
                if (currentState != 0)
                {
                    return;
                }
            }

            state[sourceUri] = 1;
            if (sortedEdgesBySource.TryGetValue(sourceUri, out var edges))
            {
                for (var edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    var edge = edges[edgeIndex];
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
                        DetectCycle(edge.TargetUri);
                    }
                }
            }

            state[sourceUri] = 2;
        }
    }

    internal static bool TryResolveIncludeUri(
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
        var trimmedSourceSpan = trimmedSource.AsSpan();
        if (trimmedSourceSpan.Length == 0)
        {
            return false;
        }

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

        if (TryResolveAvaresIncludeUri(trimmedSource, trimmedSourceSpan, assemblyName, out resolvedUri, out isProjectLocal))
        {
            return true;
        }

        if (LooksLikeAbsoluteUri(trimmedSourceSpan) &&
            Uri.TryCreate(trimmedSource, UriKind.Absolute, out var absoluteSource))
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

    private static bool TryResolveAvaresIncludeUri(
        string includeSourceText,
        ReadOnlySpan<char> includeSource,
        string assemblyName,
        out string resolvedUri,
        out bool isProjectLocal)
    {
        resolvedUri = string.Empty;
        isProjectLocal = false;

        if (!includeSource.StartsWith("avares://".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hostAndPath = includeSource.Slice("avares://".Length);
        var pathSeparatorIndex = hostAndPath.IndexOf('/');
        if (pathSeparatorIndex <= 0)
        {
            return false;
        }

        var host = hostAndPath.Slice(0, pathSeparatorIndex);
        var path = hostAndPath.Slice(pathSeparatorIndex + 1);
        if (!host.Equals(assemblyName.AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            resolvedUri = includeSourceText;
            return true;
        }

        var normalizedPath = NormalizeIncludePath(path);
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        resolvedUri = BuildUri(assemblyName, normalizedPath);
        isProjectLocal = true;
        return true;
    }

    private static bool LooksLikeAbsoluteUri(ReadOnlySpan<char> source)
    {
        if (source.Length < 3 || !char.IsLetter(source[0]))
        {
            return false;
        }

        for (var index = 1; index < source.Length; index++)
        {
            var ch = source[index];
            if (ch == ':')
            {
                return true;
            }

            if (IsDirectorySeparator(ch) || char.IsWhiteSpace(ch))
            {
                return false;
            }

            if (!char.IsLetterOrDigit(ch) &&
                ch != '+' &&
                ch != '-' &&
                ch != '.')
            {
                return false;
            }
        }

        return false;
    }

    internal static string NormalizeIncludeSource(string includeSource)
    {
        var trimmed = TrimWhitespace(includeSource.AsSpan());
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
        {
            return SliceToString(trimmed, includeSource);
        }

        var inner = TrimWhitespace(trimmed.Slice(1, trimmed.Length - 2));
        if (inner.Length == 0)
        {
            return SliceToString(trimmed, includeSource);
        }

        var separatorIndex = IndexOfWhitespaceOrComma(inner);
        var markupName = separatorIndex >= 0
            ? inner.Slice(0, separatorIndex)
            : inner;
        if (!markupName.Equals("x:Uri".AsSpan(), StringComparison.OrdinalIgnoreCase) &&
            !markupName.Equals("Uri".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return SliceToString(trimmed, includeSource);
        }

        var arguments = separatorIndex >= 0
            ? TrimWhitespace(inner.Slice(separatorIndex + 1))
            : ReadOnlySpan<char>.Empty;
        if (arguments.Length == 0)
        {
            return SliceToString(trimmed, includeSource);
        }

        var argumentSegment = arguments;
        var commaIndex = argumentSegment.IndexOf(',');
        if (commaIndex >= 0)
        {
            argumentSegment = TrimWhitespace(argumentSegment.Slice(0, commaIndex));
        }

        var equalsIndex = argumentSegment.IndexOf('=');
        if (equalsIndex > 0)
        {
            var key = TrimWhitespace(argumentSegment.Slice(0, equalsIndex));
            var value = TrimWhitespace(argumentSegment.Slice(equalsIndex + 1));
            if (key.Equals("Uri".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Value".AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return UnquoteIncludeSource(value);
            }

            return SliceToString(trimmed, includeSource);
        }

        return UnquoteIncludeSource(argumentSegment);
    }

    private static string UnquoteIncludeSource(ReadOnlySpan<char> value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\''))
            {
                return value.Slice(1, value.Length - 2).ToString();
            }
        }

        return value.ToString();
    }

    private static int IndexOfWhitespaceOrComma(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == ',' || char.IsWhiteSpace(value[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static string SliceToString(ReadOnlySpan<char> value, string original)
    {
        return value.Length == original.Length ? original : value.ToString();
    }

    private static string BuildUri(string assemblyName, string normalizedTargetPath)
    {
        return "avares://" + assemblyName + "/" + normalizedTargetPath;
    }

    private static string? BuildHotReloadAssemblyMetadataHandlerSource(
        string frameworkProfileId,
        bool hasXamlInputs,
        GeneratorOptions options)
    {
        if (!hasXamlInputs ||
            !options.IsEnabled ||
            !options.HotReloadEnabled ||
            !string.Equals(frameworkProfileId, FrameworkProfileIds.Avalonia, StringComparison.Ordinal))
        {
            return null;
        }

        var preserveIosDebugEntryPointsSource = options.IosHotReloadEnabled
            ? """
#if NET6_0_OR_GREATER && DEBUG && IOS
namespace XamlToCSharpGenerator.Generated
{
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    internal static class __SourceGenHotReloadLinkerHints
    {
        [global::System.Runtime.CompilerServices.ModuleInitializer]
        [global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.ClearCache), typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
        [global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.UpdateApplication), typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
        internal static void Initialize()
        {
        }
    }
}
#endif
"""
            : string.Empty;

        return """
#if NET6_0_OR_GREATER
[assembly: global::System.Reflection.Metadata.MetadataUpdateHandler(typeof(global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager))]
#endif
""" + preserveIosDebugEntryPointsSource;
    }

    private static ConfigurationSourcePrecedence ResolveConfigurationSourcePrecedence(
        AnalyzerConfigOptions options,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        var rawValue = GetNullableAnalyzerOption(options, "build_property.XamlSourceGenConfigurationPrecedence") ??
                       GetNullableAnalyzerOption(options, "build_property.AvaloniaSourceGenConfigurationPrecedence");
        return ResolveConfigurationSourcePrecedence(rawValue, issues);
    }

    internal static ConfigurationSourcePrecedence ResolveConfigurationSourcePrecedence(
        string? rawValue,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ConfigurationSourcePrecedence.Default;
        }

        var result = ConfigurationSourcePrecedence.Default;
        var precedenceText = rawValue.AsSpan();
        var segmentStart = -1;
        for (var index = 0; index <= precedenceText.Length; index++)
        {
            var isSeparator = index == precedenceText.Length ||
                              precedenceText[index] == ';' ||
                              precedenceText[index] == ',' ||
                              precedenceText[index] == '\r' ||
                              precedenceText[index] == '\n';
            if (!isSeparator)
            {
                if (segmentStart < 0)
                {
                    segmentStart = index;
                }

                continue;
            }

            if (segmentStart < 0)
            {
                continue;
            }

            var segment = TrimWhitespace(precedenceText.Slice(segmentStart, index - segmentStart));
            segmentStart = -1;
            if (segment.Length == 0)
            {
                continue;
            }

            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
            {
                AddConfigurationPrecedenceIssue(
                    issues,
                    "Invalid configuration precedence segment '" + segment.ToString() +
                    "'. Expected 'ProjectDefaultFile=90;File=100;MsBuild=200;Code=300'.");
                continue;
            }

            var key = TrimWhitespace(segment.Slice(0, separatorIndex));
            var valueText = TrimWhitespace(segment.Slice(separatorIndex + 1));
            if (!TryParseInvariantInt32(valueText, out var precedence))
            {
                AddConfigurationPrecedenceIssue(
                    issues,
                    "Invalid precedence value '" + valueText.ToString() + "' for key '" + key.ToString() +
                    "'. Expected an integer.");
                continue;
            }

            switch (NormalizeConfigurationPrecedenceKey(key))
            {
                case ConfigurationPrecedenceKey.ProjectDefaultFile:
                    result = result with { ProjectDefaultFile = precedence };
                    break;
                case ConfigurationPrecedenceKey.File:
                    result = result with { File = precedence };
                    break;
                case ConfigurationPrecedenceKey.MsBuild:
                    result = result with { MsBuild = precedence };
                    break;
                case ConfigurationPrecedenceKey.Code:
                    result = result with { Code = precedence };
                    break;
                default:
                    AddConfigurationPrecedenceIssue(
                        issues,
                        "Unknown configuration precedence key '" + key.ToString() +
                        "'. Supported keys: ProjectDefaultFile, File, MsBuild, Code.");
                    break;
            }
        }

        return result;
    }

    private static ConfigurationPrecedenceKey NormalizeConfigurationPrecedenceKey(ReadOnlySpan<char> key)
    {
        key = TrimWhitespace(key);
        if (key.Length == 0)
        {
            return ConfigurationPrecedenceKey.Unknown;
        }

        Span<char> stackBuffer = stackalloc char[32];
        char[]? rentedBuffer = null;
        var destination = key.Length <= stackBuffer.Length
            ? stackBuffer
            : (rentedBuffer = ArrayPool<char>.Shared.Rent(key.Length));

        try
        {
            var length = 0;
            for (var index = 0; index < key.Length; index++)
            {
                var value = key[index];
                if (value == '_' || value == '-' || char.IsWhiteSpace(value))
                {
                    continue;
                }

                destination[length++] = char.ToLowerInvariant(value);
            }

            var normalized = destination.Slice(0, length);
            if (normalized.SequenceEqual("projectdefaultfile".AsSpan()) ||
                normalized.SequenceEqual("projectdefault".AsSpan()) ||
                normalized.SequenceEqual("defaultfile".AsSpan()))
            {
                return ConfigurationPrecedenceKey.ProjectDefaultFile;
            }

            if (normalized.SequenceEqual("file".AsSpan()))
            {
                return ConfigurationPrecedenceKey.File;
            }

            if (normalized.SequenceEqual("msbuild".AsSpan()))
            {
                return ConfigurationPrecedenceKey.MsBuild;
            }

            if (normalized.SequenceEqual("code".AsSpan()))
            {
                return ConfigurationPrecedenceKey.Code;
            }

            return ConfigurationPrecedenceKey.Unknown;
        }
        finally
        {
            if (rentedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(rentedBuffer);
            }
        }
    }

    private static bool TryParseInvariantInt32(ReadOnlySpan<char> value, out int parsed)
    {
        parsed = 0;
        if (value.Length == 0)
        {
            return false;
        }

        var index = 0;
        var negative = false;
        if (value[index] == '+' || value[index] == '-')
        {
            negative = value[index] == '-';
            index++;
            if (index == value.Length)
            {
                return false;
            }
        }

        long accumulator = 0;
        for (; index < value.Length; index++)
        {
            var digit = value[index] - '0';
            if ((uint)digit > 9u)
            {
                return false;
            }

            accumulator = (accumulator * 10) + digit;
            if ((!negative && accumulator > int.MaxValue) ||
                (negative && accumulator > (long)int.MaxValue + 1))
            {
                return false;
            }
        }

        parsed = negative ? unchecked((int)-accumulator) : (int)accumulator;
        return true;
    }

    private static void AddConfigurationPrecedenceIssue(
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues,
        string message)
    {
        issues.Add(new XamlSourceGenConfigurationIssue(
            Code: "AXSG0933",
            Severity: XamlSourceGenConfigurationIssueSeverity.Warning,
            Message: message,
            SourceName: "MsBuild"));
    }

    private static string? GetNullableAnalyzerOption(AnalyzerConfigOptions options, string key)
    {
        if (!options.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value;
    }

    private static bool ShouldReplaceConfigurationFile(ConfigurationFileInput candidate, ConfigurationFileInput current)
    {
        return string.Compare(candidate.Path, current.Path, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static bool ShouldReplaceTransformRuleInput(
        XamlFrameworkTransformRuleInput candidate,
        XamlFrameworkTransformRuleInput current)
    {
        return string.Compare(candidate.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase) > 0;
    }

    private static bool ShouldReplaceXamlInput(XamlFileInput candidate, XamlFileInput current)
    {
        var targetPathPreference = CompareTargetPathPreference(candidate.TargetPath, current.TargetPath);
        if (targetPathPreference != 0)
        {
            return targetPathPreference < 0;
        }

        return string.Compare(candidate.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int CompareTargetPathPreference(string candidateTargetPath, string currentTargetPath)
    {
        var candidateRooted = Path.IsPathRooted(candidateTargetPath);
        var currentRooted = Path.IsPathRooted(currentTargetPath);
        if (candidateRooted != currentRooted)
        {
            return candidateRooted ? 1 : -1;
        }

        if (candidateTargetPath.Length != currentTargetPath.Length)
        {
            return candidateTargetPath.Length < currentTargetPath.Length ? -1 : 1;
        }

        return string.Compare(candidateTargetPath, currentTargetPath, StringComparison.OrdinalIgnoreCase);
    }

    private static ImmutableArray<ConfigurationFileInput> SortConfigurationFileInputs(
        Dictionary<string, ConfigurationFileInput>.ValueCollection values)
    {
        if (values.Count == 0)
        {
            return ImmutableArray<ConfigurationFileInput>.Empty;
        }

        var sorted = new ConfigurationFileInput[values.Count];
        values.CopyTo(sorted, 0);
        Array.Sort(sorted, static (left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
        return ImmutableArray.Create(sorted);
    }

    private static ImmutableArray<XamlFileInput> SortXamlFileInputs(
        Dictionary<string, XamlFileInput>.ValueCollection values)
    {
        if (values.Count == 0)
        {
            return ImmutableArray<XamlFileInput>.Empty;
        }

        var sorted = new XamlFileInput[values.Count];
        values.CopyTo(sorted, 0);
        Array.Sort(sorted, static (left, right) =>
        {
            var filePathComparison = string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase);
            if (filePathComparison != 0)
            {
                return filePathComparison;
            }

            return string.Compare(left.TargetPath, right.TargetPath, StringComparison.OrdinalIgnoreCase);
        });
        return ImmutableArray.Create(sorted);
    }

    private static ImmutableArray<XamlFrameworkTransformRuleInput> SortTransformRuleInputs(
        Dictionary<string, XamlFrameworkTransformRuleInput>.ValueCollection values)
    {
        if (values.Count == 0)
        {
            return ImmutableArray<XamlFrameworkTransformRuleInput>.Empty;
        }

        var sorted = new XamlFrameworkTransformRuleInput[values.Count];
        values.CopyTo(sorted, 0);
        Array.Sort(sorted, static (left, right) => string.Compare(left.FilePath, right.FilePath, StringComparison.OrdinalIgnoreCase));
        return ImmutableArray.Create(sorted);
    }

    internal static string NormalizeDedupePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = path;
        var hasUncPrefix = path.Length >= 2 && IsDirectorySeparator(path[0]) && IsDirectorySeparator(path[1]);

        try
        {
            if (!hasUncPrefix && Path.IsPathRooted(path))
            {
                normalized = Path.GetFullPath(path);
            }
        }
        catch
        {
            // Keep lexical normalization when physical normalization fails.
        }

        return NormalizePathSegments(normalized);
    }

    internal static string NormalizePathSegments(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var hasUncPrefix = path.Length >= 2 && IsDirectorySeparator(path[0]) && IsDirectorySeparator(path[1]);
        var hasUnixRoot = !hasUncPrefix && path.Length >= 1 && IsDirectorySeparator(path[0]);
        var isRooted = hasUncPrefix || hasUnixRoot || LooksLikeDriveRoot(path);

        var estimatedSegmentCount = CountSegments(path);
        if (estimatedSegmentCount == 0)
        {
            if (hasUncPrefix)
            {
                return "//";
            }

            if (hasUnixRoot)
            {
                return "/";
            }

            return string.Empty;
        }

        var rentedSegments = ArrayPool<PathSegment>.Shared.Rent(estimatedSegmentCount);
        var segmentCount = 0;

        try
        {
            var segmentStart = -1;
            for (var index = 0; index <= path.Length; index++)
            {
                var isSeparator = index == path.Length || IsDirectorySeparator(path[index]);
                if (!isSeparator)
                {
                    if (segmentStart < 0)
                    {
                        segmentStart = index;
                    }

                    continue;
                }

                if (segmentStart < 0)
                {
                    continue;
                }

                var segmentLength = index - segmentStart;
                ProcessPathSegment(path, segmentStart, segmentLength, isRooted, rentedSegments, ref segmentCount);
                segmentStart = -1;
            }

            if (segmentCount == 0)
            {
                if (hasUncPrefix)
                {
                    return "//";
                }

                if (hasUnixRoot)
                {
                    return "/";
                }

                return string.Empty;
            }

            var prefixLength = hasUncPrefix ? 2 : hasUnixRoot ? 1 : 0;
            var resultLength = prefixLength + Math.Max(segmentCount - 1, 0);
            for (var index = 0; index < segmentCount; index++)
            {
                resultLength += rentedSegments[index].Length;
            }

            var rentedChars = ArrayPool<char>.Shared.Rent(resultLength);
            try
            {
                var position = 0;
                if (hasUncPrefix)
                {
                    rentedChars[position++] = '/';
                    rentedChars[position++] = '/';
                }
                else if (hasUnixRoot)
                {
                    rentedChars[position++] = '/';
                }

                for (var index = 0; index < segmentCount; index++)
                {
                    if (index > 0)
                    {
                        rentedChars[position++] = '/';
                    }

                    var segment = rentedSegments[index];
                    path.CopyTo(segment.Start, rentedChars, position, segment.Length);
                    position += segment.Length;
                }

                return new string(rentedChars, 0, position);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }
        finally
        {
            Array.Clear(rentedSegments, 0, estimatedSegmentCount);
            ArrayPool<PathSegment>.Shared.Return(rentedSegments);
        }
    }

    private static bool IsDriveSegment(string value)
    {
        return value.Length == 2 &&
               value[1] == ':' &&
               char.IsLetter(value[0]);
    }

    private static int CountSegments(string path)
    {
        var count = 0;
        var inSegment = false;
        for (var index = 0; index < path.Length; index++)
        {
            if (IsDirectorySeparator(path[index]))
            {
                if (inSegment)
                {
                    count++;
                    inSegment = false;
                }

                continue;
            }

            inSegment = true;
        }

        if (inSegment)
        {
            count++;
        }

        return count;
    }

    private static int CountSegments(ReadOnlySpan<char> path)
    {
        var count = 0;
        var inSegment = false;
        for (var index = 0; index < path.Length; index++)
        {
            if (IsDirectorySeparator(path[index]))
            {
                if (inSegment)
                {
                    count++;
                    inSegment = false;
                }

                continue;
            }

            inSegment = true;
        }

        if (inSegment)
        {
            count++;
        }

        return count;
    }

    private static void ProcessPathSegment(
        string path,
        int start,
        int length,
        bool isRooted,
        PathSegment[] segments,
        ref int segmentCount)
    {
        if (length == 1 && path[start] == '.')
        {
            return;
        }

        if (length == 2 && path[start] == '.' && path[start + 1] == '.')
        {
            if (segmentCount > 0 &&
                !IsDotDotSegment(path, segments[segmentCount - 1]) &&
                !IsDriveSegment(path, segments[segmentCount - 1]))
            {
                segmentCount--;
                return;
            }

            if (!isRooted)
            {
                segments[segmentCount++] = new PathSegment(start, length);
            }

            return;
        }

        segments[segmentCount++] = new PathSegment(start, length);
    }

    private static bool IsDirectorySeparator(char value)
    {
        return value == '/' || value == '\\';
    }

    private static bool LooksLikeDriveRoot(string path)
    {
        return path.Length >= 2 &&
               path[1] == ':' &&
               char.IsLetter(path[0]);
    }

    private static bool IsDotDotSegment(string path, PathSegment segment)
    {
        return segment.Length == 2 &&
               path[segment.Start] == '.' &&
               path[segment.Start + 1] == '.';
    }

    private static bool IsDriveSegment(string path, PathSegment segment)
    {
        return segment.Length == 2 &&
               path[segment.Start + 1] == ':' &&
               char.IsLetter(path[segment.Start]);
    }

    private readonly struct PathSegment
    {
        public PathSegment(int start, int length)
        {
            Start = start;
            Length = length;
        }

        public int Start { get; }

        public int Length { get; }
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

    internal static string NormalizeIncludePath(string path)
    {
        return NormalizeIncludePath(path.AsSpan());
    }

    private static string NormalizeIncludePath(ReadOnlySpan<char> pathSpan)
    {
        if (pathSpan.IsEmpty)
        {
            return string.Empty;
        }

        var estimatedSegmentCount = CountSegments(pathSpan);
        if (estimatedSegmentCount == 0)
        {
            return string.Empty;
        }

        var rentedSegments = ArrayPool<PathSegment>.Shared.Rent(estimatedSegmentCount);
        var segmentCount = 0;

        try
        {
            var segmentStart = -1;
            for (var index = 0; index <= pathSpan.Length; index++)
            {
                var isSeparator = index == pathSpan.Length || IsDirectorySeparator(pathSpan[index]);
                if (!isSeparator)
                {
                    if (segmentStart < 0)
                    {
                        segmentStart = index;
                    }

                    continue;
                }

                if (segmentStart < 0)
                {
                    continue;
                }

                var segmentLength = index - segmentStart;
                if (segmentLength == 1 && pathSpan[segmentStart] == '.')
                {
                    segmentStart = -1;
                    continue;
                }

                if (segmentLength == 2 &&
                    pathSpan[segmentStart] == '.' &&
                    pathSpan[segmentStart + 1] == '.')
                {
                    if (segmentCount > 0)
                    {
                        segmentCount--;
                    }

                    segmentStart = -1;
                    continue;
                }

                rentedSegments[segmentCount++] = new PathSegment(segmentStart, segmentLength);
                segmentStart = -1;
            }

            if (segmentCount == 0)
            {
                return string.Empty;
            }

            var resultLength = Math.Max(segmentCount - 1, 0);
            for (var index = 0; index < segmentCount; index++)
            {
                resultLength += rentedSegments[index].Length;
            }

            var rentedChars = ArrayPool<char>.Shared.Rent(resultLength);
            try
            {
                var position = 0;
                for (var index = 0; index < segmentCount; index++)
                {
                    if (index > 0)
                    {
                        rentedChars[position++] = '/';
                    }

                    var segment = rentedSegments[index];
                    pathSpan.Slice(segment.Start, segment.Length).CopyTo(rentedChars.AsSpan(position));
                    position += segment.Length;
                }

                return new string(rentedChars, 0, position);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(rentedChars);
            }
        }
        finally
        {
            Array.Clear(rentedSegments, 0, estimatedSegmentCount);
            ArrayPool<PathSegment>.Shared.Return(rentedSegments);
        }
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

    internal enum ConfigurationPrecedenceKey
    {
        Unknown = 0,
        ProjectDefaultFile = 1,
        File = 2,
        MsBuild = 3,
        Code = 4
    }

    internal readonly record struct ConfigurationSourcePrecedence(
        int ProjectDefaultFile,
        int File,
        int MsBuild,
        int Code)
    {
        public static ConfigurationSourcePrecedence Default { get; } = new(90, 100, 200, 300);
    }

    internal enum TransformConfigurationSourceKind
    {
        LegacyRuleFiles = 0,
        UnifiedConfigurationRawDocuments = 1,
        UnifiedConfigurationTypedObject = 2
    }

    private sealed class TransformTypeAliasEntry
    {
        public TransformTypeAliasEntry(XamlTypeAliasRule alias, TransformConfigurationSourceKind sourceKind)
        {
            Alias = alias;
            SourceKind = sourceKind;
        }

        public XamlTypeAliasRule Alias { get; }

        public TransformConfigurationSourceKind SourceKind { get; }
    }

    private sealed class TransformPropertyAliasEntry
    {
        public TransformPropertyAliasEntry(XamlPropertyAliasRule alias, TransformConfigurationSourceKind sourceKind)
        {
            Alias = alias;
            SourceKind = sourceKind;
        }

        public XamlPropertyAliasRule Alias { get; }

        public TransformConfigurationSourceKind SourceKind { get; }
    }

    internal sealed class ConfigurationFileInput
    {
        public ConfigurationFileInput(string path, string text)
        {
            Path = path;
            Text = text;
        }

        public string Path { get; }

        public string Text { get; }
    }

    private sealed class GeneratorConfigurationSnapshot
    {
        public GeneratorConfigurationSnapshot(
            GeneratorOptions options,
            XamlSourceGenConfigurationBuildResult configurationResult)
        {
            Options = options;
            ConfigurationResult = configurationResult;
        }

        public GeneratorOptions Options { get; }

        public XamlSourceGenConfigurationBuildResult ConfigurationResult { get; }
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
