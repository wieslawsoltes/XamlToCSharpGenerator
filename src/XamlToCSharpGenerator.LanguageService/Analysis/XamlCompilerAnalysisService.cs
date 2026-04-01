using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Diagnostics;
using XamlToCSharpGenerator.LanguageService.Framework;
using XamlToCSharpGenerator.LanguageService.Framework.All;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService.Analysis;

public sealed class XamlCompilerAnalysisService
{
    private readonly ICompilationProvider _compilationProvider;
    private readonly XamlLanguageFrameworkRegistry _frameworkRegistry;
    private readonly XamlLanguageFrameworkResolver _frameworkResolver;

    public XamlCompilerAnalysisService(ICompilationProvider compilationProvider)
        : this(compilationProvider, XamlBuiltInLanguageFrameworkRegistry.Instance)
    {
    }

    public XamlCompilerAnalysisService(
        ICompilationProvider compilationProvider,
        XamlLanguageFrameworkRegistry frameworkRegistry)
    {
        _compilationProvider = compilationProvider ?? throw new ArgumentNullException(nameof(compilationProvider));
        _frameworkRegistry = frameworkRegistry ?? throw new ArgumentNullException(nameof(frameworkRegistry));
        _frameworkResolver = new XamlLanguageFrameworkResolver(_frameworkRegistry);
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

        var framework = _frameworkResolver.Resolve(
            options.FrameworkId,
            snapshot.ProjectPath,
            snapshot.Compilation,
            document.FilePath,
            document.Text);
        ResolvedViewModel? resolvedViewModel = null;

        var generatorOptions = GeneratorOptionsDefaults.Create(
            snapshot.Compilation,
            Path.GetDirectoryName(document.FilePath),
            framework);
        var parser = CreateParser(framework, snapshot.Compilation, generatorOptions);

        var (parsedDocument, parseDiagnostics) = parser.Parse(new XamlFileInput(
            FilePath: document.FilePath,
            TargetPath: Path.GetFileName(document.FilePath),
            SourceItemGroup: framework.Profile.BuildContract.XamlSourceItemGroup,
            Text: document.Text));

        diagnostics.AddRange(DiagnosticConversion.FromCoreDiagnostics(parseDiagnostics, source: "AXSG.Parse"));

        if (parsedDocument is not null && snapshot.Compilation is not null)
        {
            var binder = framework.Profile.CreateSemanticBinder();
            var (viewModel, semanticDiagnostics) = binder.Bind(
                parsedDocument,
                snapshot.Compilation,
                generatorOptions,
                XamlTransformConfiguration.Empty);
            resolvedViewModel = viewModel;

            if (options.IncludeSemanticDiagnostics)
            {
                diagnostics.AddRange(DiagnosticConversion.FromCoreDiagnostics(semanticDiagnostics, source: "AXSG.Semantic"));

                if (TryCreateNonPartialClassDiagnostic(parsedDocument, snapshot.Compilation, out var partialClassDiagnostic))
                {
                    diagnostics.AddRange(DiagnosticConversion.FromCoreDiagnostics(
                        ImmutableArray.Create(partialClassDiagnostic),
                        source: "AXSG.Semantic"));
                }
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
            : AvaloniaTypeIndex.Create(snapshot.Compilation, framework);
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
            FrameworkRegistry: _frameworkRegistry,
            Framework: framework,
            Diagnostics: diagnostics.ToImmutable());
    }

    private static SimpleXamlDocumentParser CreateParser(
        XamlLanguageFrameworkInfo framework,
        Compilation? compilation,
        GeneratorOptions options)
    {
        var settings = framework.Profile.BuildParserSettings(
            compilation ?? CSharpCompilationFactory.Empty,
            options);

        return new SimpleXamlDocumentParser(
            settings.GlobalXmlnsPrefixes,
            settings.AllowImplicitDefaultXmlns,
            settings.ImplicitDefaultXmlns,
            framework.Profile.CreateDocumentEnrichers());
    }

    private static bool TryCreateNonPartialClassDiagnostic(
        XamlDocumentModel document,
        Compilation compilation,
        out DiagnosticInfo diagnostic)
    {
        diagnostic = null!;
        if (!document.IsClassBacked ||
            string.IsNullOrWhiteSpace(document.ClassFullName))
        {
            return false;
        }

        if (ResolveTypeSymbol(compilation, document.ClassFullName!) is not INamedTypeSymbol classSymbol ||
            HasOnlyPartialSourceDeclarations(classSymbol))
        {
            return false;
        }

        diagnostic = new DiagnosticInfo(
            "AXSG0109",
            $"Type '{document.ClassFullName}' must be declared partial to use the SourceGen XAML backend.",
            document.FilePath,
            document.RootObject.Line,
            document.RootObject.Column,
            IsError: false);
        return true;
    }

    private static ISymbol? ResolveTypeSymbol(Compilation compilation, string fullTypeName)
    {
        return compilation.GetTypeByMetadataName(fullTypeName)
               ?? compilation.GetTypeByMetadataName(fullTypeName.Replace('.', '+'));
    }

    private static bool HasOnlyPartialSourceDeclarations(INamedTypeSymbol typeSymbol)
    {
        var sawSourceDeclaration = false;
        foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
            {
                continue;
            }

            sawSourceDeclaration = true;
            if (!typeDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            {
                return false;
            }
        }

        return sawSourceDeclaration;
    }

    private static class CSharpCompilationFactory
    {
        public static readonly Microsoft.CodeAnalysis.CSharp.CSharpCompilation Empty =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("AXSGLanguageServiceFallback");
    }
}
