using System;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class XamlLanguageServiceEngineTests
{
    [Fact]
    public async Task Completion_InElementContext_ReturnsKnownTypes()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TestView.axaml";
        const string xaml = "<Us";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(0, 3),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => item.Label.EndsWith("UserControl", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definition_ForElementName_ResolvesNamedElement()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TestView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(2, 45),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(1, definitions[0].Range.Start.Line);
    }

    [Fact]
    public async Task Definition_ForStaticResource_ResolvesResourceDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ResourceDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"AccentBrush\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <Border Background=\"{StaticResource AccentBrush}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(4, 40),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(2, definitions[0].Range.Start.Line);
    }

    [Fact]
    public async Task References_ForElementName_ReturnsDeclarationAndUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TestView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            new SourcePosition(2, 45),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 2);
        Assert.Contains(references, item => item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_DistinguishNamedElementAndResourceKey_WhenIdentifierIsShared()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SharedIdentifierView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"SharedId\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <Button x:Name=\"SharedId\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SharedId, Path=Content}\"/>\n" +
                            "  <Border Background=\"{StaticResource SharedId}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var elementReferences = await engine.GetReferencesAsync(
            uri,
            new SourcePosition(5, 44),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(elementReferences, item => item.IsDeclaration && item.Range.Start.Line == 4);
        Assert.Contains(elementReferences, item => !item.IsDeclaration && item.Range.Start.Line == 5);
        Assert.DoesNotContain(elementReferences, item => item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.DoesNotContain(elementReferences, item => !item.IsDeclaration && item.Range.Start.Line == 6);

        var resourceReferences = await engine.GetReferencesAsync(
            uri,
            new SourcePosition(6, 40),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(resourceReferences, item => item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(resourceReferences, item => !item.IsDeclaration && item.Range.Start.Line == 6);
        Assert.DoesNotContain(resourceReferences, item => item.IsDeclaration && item.Range.Start.Line == 4);
        Assert.DoesNotContain(resourceReferences, item => !item.IsDeclaration && item.Range.Start.Line == 5);
    }

    [Fact]
    public async Task Completion_ForStaticResourceValue_IncludesResourceKeys()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/CompletionResourceView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"AccentBrush\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <Border Background=\"{StaticResource AccentBrush}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(4, 42),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "AccentBrush", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DocumentSymbols_ReturnsObjectTree()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TestView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <StackPanel>\n" +
                            "    <Button x:Name=\"SubmitButton\"/>\n" +
                            "  </StackPanel>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var symbols = await engine.GetDocumentSymbolsAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Single(symbols);
        Assert.Contains("UserControl", symbols[0].Name);
        Assert.NotEmpty(symbols[0].Children);
    }

    [Fact]
    public async Task SemanticTokens_ProducesTokensForMarkup()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TestView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button Content=\"Hello\"/></UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, token => token.TokenType == "class");
        Assert.Contains(tokens, token => token.TokenType == "property");
    }

    [Fact]
    public async Task Diagnostics_ReportStableRangeForParseError()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BrokenView.axaml";
        const string xaml = "<UserControl>\n  <Button>\n</UserControl>";

        var diagnostics = await engine.OpenDocumentAsync(
            uri,
            xaml,
            version: 1,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, d => d.Range.Start.Line >= 0 && d.Range.Start.Character >= 0);
    }
}
