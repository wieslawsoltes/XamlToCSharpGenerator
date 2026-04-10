using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Analysis;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class XamlInlineCSharpNavigationServiceTests
{
    [Fact]
    public async Task EnumerateContexts_ForMsBuildSample_CDataEventBlock_ResolvesSourceAndMethodSymbols()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var analysis = await AnalyzeMsBuildSampleAsync(repositoryRoot, xamlPath, xamlText);
        var inlineCodeElement = FindInlineCodeElement(analysis, "source.RecordSender(sender);");
        var ownerElement = inlineCodeElement.Parent?.Parent;

        Assert.Equal("Avalonia", analysis.Framework.Id);
        Assert.NotNull(analysis.TypeIndex);
        Assert.NotNull(ownerElement);
        var defaultButtonType = XamlSemanticSourceTypeResolver.ResolveTypeSymbolByFullTypeName(
            analysis.Compilation,
            "Avalonia.Controls.Button");
        var typeIndexHasButton = analysis.TypeIndex!.TryGetType(
            analysis.Framework.DefaultXmlNamespace,
            "Button",
            out var indexedButtonType);
        Assert.True(
            XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, ownerElement!, out var ownerType),
            $"Failed to resolve owner element '{ownerElement!.Name}' in project '{analysis.ProjectPath}'. " +
            $"CompilationHasButton={defaultButtonType is not null}; " +
            $"TypeIndexHasButton={typeIndexHasButton}; " +
            $"IndexedButtonType='{indexedButtonType?.FullTypeName ?? "<null>"}'; " +
            $"OwnerNamespace='{ownerElement.Name.NamespaceName}'.");
        Assert.Equal("Avalonia.Controls.Button", ownerType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        Assert.NotNull(XamlEventHandlerTypeResolver.ResolveHandlerType(analysis, ownerType, "Click"));

        var contexts = XamlInlineCSharpNavigationService.EnumerateContexts(analysis);
        var context = contexts.FirstOrDefault(candidate =>
            candidate.RawCode.Contains("source.RecordSender(sender);", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(context.RawCode));
        Assert.True(context.IsEventCode);
        Assert.NotNull(context.SourceType);
        Assert.Equal(
            "SourceGenXamlCatalogSample.ViewModels.InlineCodePageViewModel",
            context.SourceType!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        Assert.NotNull(context.EventHandlerType);
        Assert.Contains(context.SymbolOccurrences, occurrence => string.Equals(occurrence.Symbol.Name, "source", StringComparison.Ordinal));
        Assert.Contains(context.SymbolOccurrences, occurrence => string.Equals(occurrence.Symbol.Name, "RecordSender", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EnumerateContexts_ForMsBuildSample_CompactEventBlock_ResolvesSourceAndMethodSymbols()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodePage.axaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var analysis = await AnalyzeMsBuildSampleAsync(repositoryRoot, xamlPath, xamlText);
        var inlineCodeElement = FindInlineCodeElement(analysis, "source.RecordSender(sender);");
        var ownerElement = inlineCodeElement.Parent?.Parent;

        Assert.Equal("Avalonia", analysis.Framework.Id);
        Assert.NotNull(analysis.TypeIndex);
        Assert.NotNull(ownerElement);
        var defaultButtonType = XamlSemanticSourceTypeResolver.ResolveTypeSymbolByFullTypeName(
            analysis.Compilation,
            "Avalonia.Controls.Button");
        var typeIndexHasButton = analysis.TypeIndex!.TryGetType(
            analysis.Framework.DefaultXmlNamespace,
            "Button",
            out var indexedButtonType);
        Assert.True(
            XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, ownerElement!, out var ownerType),
            $"Failed to resolve owner element '{ownerElement!.Name}' in project '{analysis.ProjectPath}'. " +
            $"CompilationHasButton={defaultButtonType is not null}; " +
            $"TypeIndexHasButton={typeIndexHasButton}; " +
            $"IndexedButtonType='{indexedButtonType?.FullTypeName ?? "<null>"}'; " +
            $"OwnerNamespace='{ownerElement.Name.NamespaceName}'.");
        Assert.Equal("Avalonia.Controls.Button", ownerType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        Assert.NotNull(XamlEventHandlerTypeResolver.ResolveHandlerType(analysis, ownerType, "Click"));

        var contexts = XamlInlineCSharpNavigationService.EnumerateContexts(analysis);
        var context = contexts.FirstOrDefault(candidate =>
            candidate.RawCode.Contains("source.RecordSender(sender);", StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(context.RawCode));
        Assert.True(context.IsEventCode);
        Assert.NotNull(context.SourceType);
        Assert.Equal(
            "SourceGenXamlCatalogSample.ViewModels.InlineCodePageViewModel",
            context.SourceType!.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        Assert.NotNull(context.EventHandlerType);
        Assert.Contains(context.SymbolOccurrences, occurrence => string.Equals(occurrence.Symbol.Name, "source", StringComparison.Ordinal));
        Assert.Contains(context.SymbolOccurrences, occurrence => string.Equals(occurrence.Symbol.Name, "RecordSender", StringComparison.Ordinal));
    }

    private static async Task<XamlAnalysisResult> AnalyzeMsBuildSampleAsync(string repositoryRoot, string filePath, string xamlText)
    {
        var analysisService = new XamlCompilerAnalysisService(LanguageServiceTestCompilationFactory.CreateSharedMsBuildCompilationProvider());
        var document = new LanguageServiceDocument(
            UriPathHelper.ToDocumentUri(filePath),
            filePath,
            xamlText,
            Version: 1);

        return await analysisService.AnalyzeAsync(
            document,
            new XamlLanguageServiceOptions(
                WorkspaceRoot: repositoryRoot,
                IncludeCompilationDiagnostics: false,
                IncludeSemanticDiagnostics: false),
            CancellationToken.None);
    }

    private static XElement FindInlineCodeElement(XamlAnalysisResult analysis, string codeSnippet)
    {
        Assert.NotNull(analysis.XmlDocument?.Root);

        var inlineCodeElement = analysis.XmlDocument!.Root!
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "CSharp", StringComparison.Ordinal) &&
                element.ToString(SaveOptions.DisableFormatting).Contains(codeSnippet, StringComparison.Ordinal));

        Assert.NotNull(inlineCodeElement);
        return inlineCodeElement!;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "XamlToCSharpGenerator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
