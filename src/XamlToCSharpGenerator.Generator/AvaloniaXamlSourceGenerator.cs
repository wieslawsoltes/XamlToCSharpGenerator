using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
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

        var parsedDocuments = uniqueXamlInputs.SelectMany(static (inputs, _) =>
        {
            if (inputs.IsDefaultOrEmpty)
            {
                return ImmutableArray<ParsedDocumentResult>.Empty;
            }

            IXamlDocumentParser parser = new SimpleXamlDocumentParser();
            var results = ImmutableArray.CreateBuilder<ParsedDocumentResult>(inputs.Length);
            foreach (var input in inputs)
            {
                var (document, diagnostics) = parser.Parse(input);
                results.Add(new ParsedDocumentResult(input, document, diagnostics));
            }

            return results.ToImmutable();
        });

        var globalGraphDiagnostics = parsedDocuments
            .Select(static (result, _) => result.Document)
            .Where(static document => document is not null)
            .Select(static (document, _) => document!)
            .Collect()
            .Combine(optionsProvider)
            .Select(static (pair, _) => new GlobalDiagnosticsResult(
                AnalyzeGlobalDocumentGraph(pair.Left, pair.Right),
                IsHotReloadErrorResilienceEnabled(pair.Right)));

        context.RegisterSourceOutput(globalGraphDiagnostics,
            static (sourceContext, diagnosticsResult) =>
            {
                ReportDiagnostics(
                    sourceContext,
                    diagnosticsResult.Diagnostics,
                    diagnosticsResult.DemoteErrorsToWarnings);
            });

        context.RegisterSourceOutput(parsedDocuments.Combine(context.CompilationProvider.Combine(optionsProvider)),
            static (sourceContext, payload) =>
            {
                var (parsedDocument, (compilation, options)) = payload;
                var parseResult = parsedDocument.Document;
                var parseDiagnostics = parsedDocument.Diagnostics;
                var resilienceEnabled = IsHotReloadErrorResilienceEnabled(options);
                var cacheKey = BuildHotReloadCacheKey(
                    options.AssemblyName,
                    parsedDocument.Input.FilePath,
                    parsedDocument.Input.TargetPath);

                ReportDiagnostics(sourceContext, parseDiagnostics, resilienceEnabled);
                if (parseResult is null)
                {
                    TryUseCachedSource(sourceContext, cacheKey, parsedDocument.Input.FilePath, resilienceEnabled);
                    return;
                }

                if (parseResult.Precompile == false)
                {
                    return;
                }

                IXamlSemanticBinder binder = new AvaloniaSemanticBinder();
                var (viewModel, semanticDiagnostics) = binder.Bind(parseResult, compilation, options);
                ReportDiagnostics(sourceContext, semanticDiagnostics, resilienceEnabled);
                if (viewModel is null)
                {
                    TryUseCachedSource(sourceContext, cacheKey, parseResult.FilePath, resilienceEnabled);
                    return;
                }

                try
                {
                    IXamlCodeEmitter emitter = new AvaloniaCodeEmitter();
                    var (hintName, source) = emitter.Emit(viewModel);
                    if (TryAddSource(sourceContext, hintName, source))
                    {
                        if (resilienceEnabled)
                        {
                            LastGoodGeneratedSources[cacheKey] = new CachedGeneratedSource(hintName, source);
                        }
                    }
                    else
                    {
                        sourceContext.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticCatalog.DuplicateGeneratedHintName,
                            Location.None,
                            hintName,
                            parseResult.FilePath));
                    }
                }
                catch (Exception ex)
                {
                    if (!TryUseCachedSource(sourceContext, cacheKey, parseResult.FilePath, resilienceEnabled))
                    {
                        sourceContext.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticCatalog.EmissionFailed,
                            Location.None,
                            parseResult.ClassFullName ?? parseResult.TargetPath,
                            ex.Message));
                    }
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

            var location = string.IsNullOrWhiteSpace(diagnostic.FilePath)
                ? Location.None
                : Location.Create(
                    diagnostic.FilePath,
                    TextSpan.FromBounds(0, 0),
                    new LinePositionSpan(
                        new LinePosition(Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1)),
                        new LinePosition(Math.Max(0, diagnostic.Line - 1), Math.Max(0, diagnostic.Column - 1))));

            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, diagnostic.Message));
        }
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

        var location = string.IsNullOrWhiteSpace(filePath)
            ? Location.None
            : Location.Create(
                filePath,
                TextSpan.FromBounds(0, 0),
                new LinePositionSpan(
                    new LinePosition(0, 0),
                    new LinePosition(0, 0)));

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

    private sealed class ParsedDocumentResult
    {
        public ParsedDocumentResult(
            XamlFileInput input,
            XamlDocumentModel? document,
            ImmutableArray<DiagnosticInfo> diagnostics)
        {
            Input = input;
            Document = document;
            Diagnostics = diagnostics;
        }

        public XamlFileInput Input { get; }

        public XamlDocumentModel? Document { get; }

        public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
    }

    private sealed class GlobalDiagnosticsResult
    {
        public GlobalDiagnosticsResult(
            ImmutableArray<DiagnosticInfo> diagnostics,
            bool demoteErrorsToWarnings)
        {
            Diagnostics = diagnostics;
            DemoteErrorsToWarnings = demoteErrorsToWarnings;
        }

        public ImmutableArray<DiagnosticInfo> Diagnostics { get; }

        public bool DemoteErrorsToWarnings { get; }
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
