using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
            .Select(static (inputs, _) =>
            {
                if (inputs.IsDefaultOrEmpty)
                {
                    return ImmutableArray<ConfigurationFileInput>.Empty;
                }

                var byPath = new Dictionary<string, ConfigurationFileInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var input in inputs.OrderBy(static value => value.Path, StringComparer.OrdinalIgnoreCase))
                {
                    byPath[NormalizeDedupePath(input.Path)] = input;
                }

                return byPath.Values
                    .OrderBy(static value => value.Path, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();
            });

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
            .Select(static (inputs, _) =>
            {
                if (inputs.IsDefaultOrEmpty)
                {
                    return ImmutableArray<XamlFrameworkTransformRuleInput>.Empty;
                }

                var byPath = new Dictionary<string, XamlFrameworkTransformRuleInput>(StringComparer.OrdinalIgnoreCase);
                foreach (var input in inputs.OrderBy(static value => value.FilePath, StringComparer.OrdinalIgnoreCase))
                {
                    byPath[NormalizeDedupePath(input.FilePath)] = input;
                }

                return byPath.Values
                    .OrderBy(static value => value.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray();
            });

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
                    .Select(frameworkProfile.TransformProvider.ParseTransformRule)
                    .ToImmutableArray();
                return frameworkProfile.TransformProvider.MergeTransformRules(parsed);
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
                    frameworkProfile.TransformProvider);
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
                    IXamlFrameworkSemanticBinder binder = frameworkProfile.CreateSemanticBinder();
                    var (viewModel, semanticDiagnostics) = binder.Bind(parseResult, compilation, options, transformRules.Configuration);
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
                        IXamlFrameworkEmitter emitter = frameworkProfile.CreateEmitter();
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

    private static ImmutableArray<XamlFrameworkTransformRuleResult> ParseConfigurationTransformRuleInputs(
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
            var filePath = BuildConfigurationTransformRuleDocumentPath(pair.Key);
            var parsed = transformProvider.ParseTransformRule(new XamlFrameworkTransformRuleInput(filePath, pair.Value));
            results.Add(parsed);
        }

        return results.ToImmutable();
    }

    private static string BuildConfigurationTransformRuleDocumentPath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "xaml-sourcegen.config.json::transform.rawTransformDocuments[<empty>]";
        }

        return "xaml-sourcegen.config.json::transform.rawTransformDocuments[" + key.Trim() + "]";
    }

    private static XamlTransformConfiguration MergeTransformConfigurations(
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

        var typeAliases = new Dictionary<string, TransformTypeAliasEntry>(StringComparer.OrdinalIgnoreCase);
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

        var propertyAliases = new Dictionary<string, TransformPropertyAliasEntry>(StringComparer.OrdinalIgnoreCase);
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
            typeAliases
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.Value.Alias)
                .ToImmutableArray(),
            propertyAliases
                .OrderBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static entry => entry.Value.Alias)
                .ToImmutableArray());
    }

    private static string BuildTypeAliasKey(XamlTypeAliasRule alias)
    {
        return alias.XmlNamespace + ":" + alias.XamlTypeName;
    }

    private static string BuildPropertyAliasKey(XamlPropertyAliasRule alias)
    {
        return alias.TargetTypeName + ":" + alias.XamlPropertyName;
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

        if (filtered is null)
        {
            return ImmutableArray<DiagnosticInfo>.Empty;
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

        var normalized = filePath.Replace('\\', '/');
        try
        {
            if (Path.IsPathRooted(filePath))
            {
                normalized = Path.GetFullPath(filePath).Replace('\\', '/');
            }
            else if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                normalized = Path.GetFullPath(Path.Combine(projectDirectory!, filePath)).Replace('\\', '/');
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

        return NormalizePathSegments(targetPath.Replace('\\', '/'));
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

    private static (
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

    private static string? TryInferClassNameFromTargetPath(string targetPath, GeneratorOptions options)
    {
        var rootNamespace = NormalizeRootNamespace(options.RootNamespace ?? options.AssemblyName);
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
        var normalizedClassName = NormalizeIdentifier(fileName, "GeneratedView");
        if (string.IsNullOrWhiteSpace(normalizedClassName))
        {
            return null;
        }

        var namespaceSegments = NormalizeNamespaceSegments(rootNamespace).ToList();
        var directory = Path.GetDirectoryName(effectiveTargetPath)?
            .Replace('\\', '/');

        if (directory is not null)
        {
            var directoryValue = directory.Trim();
            if (directoryValue.Length > 0)
            {
                foreach (var rawSegment in directoryValue
                             .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var segment = rawSegment.Trim();
                    var normalizedSegment = NormalizeIdentifier(segment, null);
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

    private static string NormalizeRootNamespace(string? rootNamespace)
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

        var segments = NormalizeNamespaceSegments(trimmedRootNamespace);
        return segments.Length == 0
            ? string.Empty
            : string.Join(".", segments);
    }

    private static ImmutableArray<string> NormalizeNamespaceSegments(string rawNamespace)
    {
        if (string.IsNullOrWhiteSpace(rawNamespace))
        {
            return ImmutableArray<string>.Empty;
        }

        var segments = ImmutableArray.CreateBuilder<string>();
        foreach (var rawSegment in rawNamespace
                     .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Trim();
            var normalizedSegment = NormalizeIdentifier(segment, null);
            if (normalizedSegment is null || normalizedSegment.Length == 0)
            {
                continue;
            }

            segments.Add(normalizedSegment);
        }

        return segments.ToImmutable();
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

    private static TimeSpan GetElapsedTimeSince(long startTimestamp)
    {
        var elapsedTimestamp = Stopwatch.GetTimestamp() - startTimestamp;
        return TimeSpan.FromSeconds((double)elapsedTimestamp / Stopwatch.Frequency);
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
            if ((value[0] == '"' && value[value.Length - 1] == '"') ||
                (value[0] == '\'' && value[value.Length - 1] == '\''))
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
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return ConfigurationSourcePrecedence.Default;
        }

        var result = ConfigurationSourcePrecedence.Default;
        var precedenceText = rawValue!;
        var segments = precedenceText.Split(new[] { ';', ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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
                AddConfigurationPrecedenceIssue(
                    issues,
                    "Invalid configuration precedence segment '" + segment +
                    "'. Expected 'ProjectDefaultFile=90;File=100;MsBuild=200;Code=300'.");
                continue;
            }

            var key = segment.Substring(0, separatorIndex).Trim();
            var valueText = segment.Substring(separatorIndex + 1).Trim();
            if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var precedence))
            {
                AddConfigurationPrecedenceIssue(
                    issues,
                    "Invalid precedence value '" + valueText + "' for key '" + key +
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
                        "Unknown configuration precedence key '" + key +
                        "'. Supported keys: ProjectDefaultFile, File, MsBuild, Code.");
                    break;
            }
        }

        return result;
    }

    private static ConfigurationPrecedenceKey NormalizeConfigurationPrecedenceKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return ConfigurationPrecedenceKey.Unknown;
        }

        var normalized = key.Trim().Replace("_", string.Empty).Replace("-", string.Empty);
        if (normalized.Equals("projectdefaultfile", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("projectdefault", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("defaultfile", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationPrecedenceKey.ProjectDefaultFile;
        }

        if (normalized.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationPrecedenceKey.File;
        }

        if (normalized.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationPrecedenceKey.MsBuild;
        }

        if (normalized.Equals("code", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationPrecedenceKey.Code;
        }

        return ConfigurationPrecedenceKey.Unknown;
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
                    !string.Equals(stack[stack.Count - 1], "..", StringComparison.Ordinal) &&
                    !IsDriveSegment(stack[stack.Count - 1]))
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

    private enum ConfigurationPrecedenceKey
    {
        Unknown = 0,
        ProjectDefaultFile = 1,
        File = 2,
        MsBuild = 3,
        Code = 4
    }

    private readonly record struct ConfigurationSourcePrecedence(
        int ProjectDefaultFile,
        int File,
        int MsBuild,
        int Code)
    {
        public static ConfigurationSourcePrecedence Default { get; } = new(90, 100, 200, 300);
    }

    private enum TransformConfigurationSourceKind
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

    private sealed class ConfigurationFileInput
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
