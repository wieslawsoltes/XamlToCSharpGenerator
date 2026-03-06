using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Avalonia.Framework;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Diagnostics;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService.Analysis;

public sealed class XamlCompilerAnalysisService
{
    private readonly ICompilationProvider _compilationProvider;
    private readonly IXamlFrameworkProfile _frameworkProfile;

    public XamlCompilerAnalysisService(ICompilationProvider compilationProvider)
        : this(compilationProvider, AvaloniaFrameworkProfile.Instance)
    {
    }

    public XamlCompilerAnalysisService(
        ICompilationProvider compilationProvider,
        IXamlFrameworkProfile frameworkProfile)
    {
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
        _frameworkProfile = frameworkProfile ?? throw new ArgumentNullException(nameof(frameworkProfile));
    }

    public async Task<XamlAnalysisResult> AnalyzeAsync(
        LanguageServiceDocument document,
        XamlLanguageServiceOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= XamlLanguageServiceOptions.Default;

        var snapshot = await _compilationProvider
            .GetCompilationAsync(document.FilePath, options.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);

        var diagnostics = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>();
        if (options.IncludeCompilationDiagnostics)
        {
            diagnostics.AddRange(snapshot.Diagnostics);
        }

        ResolvedViewModel? resolvedViewModel = null;

        var generatorOptions = GeneratorOptionsDefaults.Create(snapshot.Compilation, Path.GetDirectoryName(document.FilePath));
        var parser = CreateParser(snapshot.Compilation, generatorOptions);

        var (parsedDocument, parseDiagnostics) = parser.Parse(new XamlFileInput(
            FilePath: document.FilePath,
            TargetPath: Path.GetFileName(document.FilePath),
            SourceItemGroup: "AvaloniaXaml",
            Text: document.Text));

        diagnostics.AddRange(DiagnosticConversion.FromCoreDiagnostics(parseDiagnostics, source: "AXSG.Parse"));

        if (parsedDocument is not null && snapshot.Compilation is not null)
        {
            var binder = _frameworkProfile.CreateSemanticBinder();
            var (viewModel, semanticDiagnostics) = binder.Bind(
                parsedDocument,
                snapshot.Compilation,
                generatorOptions,
                XamlTransformConfiguration.Empty);
            resolvedViewModel = viewModel;

            if (options.IncludeSemanticDiagnostics)
            {
                diagnostics.AddRange(DiagnosticConversion.FromCoreDiagnostics(semanticDiagnostics, source: "AXSG.Semantic"));
            }
        }

        XDocument? xmlDocument;
        try
        {
            xmlDocument = XDocument.Parse(
                document.Text,
                LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        }
        catch
        {
            xmlDocument = null;
        }

        var typeIndex = snapshot.Compilation is null
            ? null
            : AvaloniaTypeIndex.Create(snapshot.Compilation);
        var prefixMap = XamlXmlNamespaceResolver.BuildPrefixMap(parsedDocument);

        return new XamlAnalysisResult(
            Document: document,
            ProjectPath: snapshot.ProjectPath,
            Compilation: snapshot.Compilation,
            ParsedDocument: parsedDocument,
            ViewModel: resolvedViewModel,
            XmlDocument: xmlDocument,
            PrefixMap: prefixMap,
            TypeIndex: typeIndex,
            Diagnostics: diagnostics.ToImmutable());
    }

    private SimpleXamlDocumentParser CreateParser(Compilation? compilation, GeneratorOptions options)
    {
        var settings = _frameworkProfile.BuildParserSettings(
            compilation ?? CSharpCompilationFactory.Empty,
            options);

        return new SimpleXamlDocumentParser(
            settings.GlobalXmlnsPrefixes,
            settings.AllowImplicitDefaultXmlns,
            settings.ImplicitDefaultXmlns,
            _frameworkProfile.CreateDocumentEnrichers());
    }

    private static class CSharpCompilationFactory
    {
        public static readonly Microsoft.CodeAnalysis.CSharp.CSharpCompilation Empty =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("AXSGLanguageServiceFallback");
    }
}
