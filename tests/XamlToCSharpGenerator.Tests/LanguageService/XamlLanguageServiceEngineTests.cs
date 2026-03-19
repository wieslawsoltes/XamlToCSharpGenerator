using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Formatting;
using XamlToCSharpGenerator.LanguageService.InlayHints;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class XamlLanguageServiceEngineTests
{
    private static readonly Lazy<Compilation> CachedExternalControlsCompilation =
        new(CreateCompilationWithExternalControlsCore, LazyThreadSafetyMode.ExecutionAndPublication);

    [Fact]
    public async Task FormatDocumentAsync_ReturnsSingleFullDocumentEdit()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/FormattingView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><StackPanel><TextBlock Text=\"Hello\"/></StackPanel></UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var edits = await engine.FormatDocumentAsync(uri, new XamlFormattingOptions(2, InsertSpaces: true), CancellationToken.None);

        var edit = Assert.Single(edits);
        Assert.Equal(new SourceRange(new SourcePosition(0, 0), new SourcePosition(0, xaml.Length)), edit.Range);
        Assert.Equal(
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>",
            edit.NewText);
    }

    [Fact]
    public async Task FormatDocumentAsync_InvalidXaml_ReturnsNoEdits()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BrokenFormattingView.axaml";
        const string xaml = "<UserControl><StackPanel></UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var edits = await engine.FormatDocumentAsync(uri, XamlFormattingOptions.Default, CancellationToken.None);

        Assert.Empty(edits);
    }

    [Fact]
    public async Task GetFoldingRangesAsync_ReturnsElementAndCommentRanges()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/FoldingView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <!--\n" +
            "    comment\n" +
            "  -->\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var ranges = await engine.GetFoldingRangesAsync(uri, CancellationToken.None);

        Assert.Contains(new XamlFoldingRange(0, 7, "region"), ranges);
        Assert.Contains(new XamlFoldingRange(1, 3, "comment"), ranges);
        Assert.Contains(new XamlFoldingRange(4, 6, "region"), ranges);
    }

    [Fact]
    public async Task GetSelectionRangesAsync_ReturnsNestedAttributeAndElementChain()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectionView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock Text=\"Hello\" />\n" +
            "  </StackPanel>\n" +
            "</UserControl>";
        var position = GetPosition(xaml, xaml.IndexOf("Hello", StringComparison.Ordinal) + 2);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var ranges = await engine.GetSelectionRangesAsync(
            uri,
            [position],
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        var selection = Assert.Single(ranges);
        Assert.Equal("Hello", GetRangeText(xaml, selection.Range));
        Assert.NotNull(selection.Parent);
        Assert.Equal("Text=\"Hello\"", GetRangeText(xaml, selection.Parent!.Range));
        Assert.NotNull(selection.Parent.Parent);
        Assert.Equal("<TextBlock Text=\"Hello\" />", GetRangeText(xaml, selection.Parent.Parent!.Range));
        Assert.NotNull(selection.Parent.Parent.Parent);
        Assert.Equal(
            "<StackPanel>\n    <TextBlock Text=\"Hello\" />\n  </StackPanel>",
            GetRangeText(xaml, selection.Parent.Parent.Parent!.Range));
        Assert.NotNull(selection.Parent.Parent.Parent.Parent);
        Assert.Equal(xaml, GetRangeText(xaml, selection.Parent.Parent.Parent.Parent!.Range));
        Assert.Null(selection.Parent.Parent.Parent.Parent.Parent);
    }

    [Fact]
    public async Task GetLinkedEditingRangesAsync_ReturnsMatchingStartAndEndTagRanges()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/LinkedEditingView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel>\n" +
            "    <TextBlock>Hello</TextBlock>\n" +
            "  </StackPanel>\n" +
            "</UserControl>";
        var position = GetPosition(xaml, xaml.IndexOf("TextBlock", StringComparison.Ordinal) + 2);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var linkedEditingRanges = await engine.GetLinkedEditingRangesAsync(
            uri,
            position,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.NotNull(linkedEditingRanges);
        Assert.Equal(2, linkedEditingRanges!.Ranges.Length);
        Assert.Equal("TextBlock", GetRangeText(xaml, linkedEditingRanges.Ranges[0]));
        Assert.Equal("TextBlock", GetRangeText(xaml, linkedEditingRanges.Ranges[1]));
        Assert.Equal(@"[-.\w:]+", linkedEditingRanges.WordPattern);
    }

    [Fact]
    public async Task GetDocumentHighlightsAsync_ReturnsDeclarationAndUsageInCurrentDocument()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/DocumentHighlights.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <UserControl.Resources>\n" +
            "    <SolidColorBrush x:Key=\"AccentBrush\" />\n" +
            "  </UserControl.Resources>\n" +
            "  <Border Background=\"{StaticResource AccentBrush}\" />\n" +
            "</UserControl>";
        var position = GetPosition(xaml, xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var highlights = await engine.GetDocumentHighlightsAsync(
            uri,
            position,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.Equal(2, highlights.Length);
        Assert.Contains(highlights, highlight =>
            highlight.Kind == XamlDocumentHighlightKind.Write &&
            GetRangeText(xaml, highlight.Range) == "AccentBrush");
        Assert.Contains(highlights, highlight =>
            highlight.Kind == XamlDocumentHighlightKind.Read &&
            GetRangeText(xaml, highlight.Range) == "AccentBrush");
    }

    [Fact]
    public async Task GetDocumentLinksAsync_ReturnsResolvedIncludeTarget()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "axsg-doc-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var includedFilePath = Path.Combine(temporaryDirectory, "SharedStyles.axaml");
            await File.WriteAllTextAsync(
                includedFilePath,
                "<Styles xmlns=\"https://github.com/avaloniaui\" />",
                CancellationToken.None);
            var includedUri = new Uri(includedFilePath).AbsoluteUri;

            var hostFilePath = Path.Combine(temporaryDirectory, "Host.axaml");
            var hostUri = new Uri(hostFilePath).AbsoluteUri;
            var xaml =
                "<Styles xmlns=\"https://github.com/avaloniaui\">\n" +
                $"  <StyleInclude Source=\"{includedUri}\" />\n" +
                "</Styles>";

            await engine.OpenDocumentAsync(hostUri, xaml, version: 1, new XamlLanguageServiceOptions(temporaryDirectory), CancellationToken.None);
            var links = await engine.GetDocumentLinksAsync(
                hostUri,
                new XamlLanguageServiceOptions(temporaryDirectory),
                CancellationToken.None);

            var link = Assert.Single(links);
            Assert.Equal(includedUri, link.TargetUri);
            Assert.Equal(includedUri, GetRangeText(xaml, link.Range));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetWorkspaceSymbolsAsync_ReturnsMatchingSymbolsAcrossOpenDocuments()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string firstUri = "file:///tmp/WorkspaceSymbolOne.axaml";
        const string secondUri = "file:///tmp/WorkspaceSymbolTwo.axaml";
        const string firstXaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <Grid x:Name=\"LayoutRoot\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />\n" +
            "</UserControl>";
        const string secondXaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <StackPanel />\n" +
            "</UserControl>";

        await engine.OpenDocumentAsync(firstUri, firstXaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        await engine.OpenDocumentAsync(secondUri, secondXaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var symbols = await engine.GetWorkspaceSymbolsAsync(
            "LayoutRoot",
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        var symbol = Assert.Single(symbols);
        Assert.Equal(firstUri, symbol.Uri);
        Assert.Contains("LayoutRoot", symbol.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSignatureHelpAsync_ReturnsMarkupExtensionSignatureAndActiveParameter()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SignatureHelpView.axaml";
        const string xaml =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <TextBlock Text=\"{Binding Name, Mode=TwoWay}\" />\n" +
            "</UserControl>";
        var position = GetPosition(xaml, xaml.IndexOf("Mode", StringComparison.Ordinal) + 2);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var signatureHelp = await engine.GetSignatureHelpAsync(
            uri,
            position,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.NotNull(signatureHelp);
        Assert.Equal(0, signatureHelp!.ActiveSignature);
        Assert.Equal(1, signatureHelp.ActiveParameter);
        var signature = Assert.Single(signatureHelp.Signatures);
        Assert.Equal("Binding(path, Mode, Source, RelativeSource, ElementName, Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue)", signature.Label);
    }

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
    public async Task Completion_InOpenTag_ReturnsCurrentAndInheritedProperties()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/OpenTagCompletion.axaml";
        const string xaml = "<Path ";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(0, 6),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "Data", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "Stroke", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "Opacity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InQualifiedPropertyElementContext_ReturnsOwnerProperties()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.O\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var completions = await engine.GetCompletionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("<Path.O", StringComparison.Ordinal) + "<Path.O".Length),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "Opacity", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.InsertText, "Path.Opacity", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "Opacity", StringComparison.Ordinal) &&
                                             item.ReplaceRange is not null &&
                                             GetRangeText(xaml, item.ReplaceRange.Value) == "Path.O");
    }

    [Fact]
    public async Task Completion_InQualifiedPropertyElementContext_WithPrefixedOwner_ReturnsOwnerProperties()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/PrefixedQualifiedPropertyElementCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:controls=\"https://github.com/avaloniaui\">\n" +
                            "  <controls:Path.Op></controls:Path.Op>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var completions = await engine.GetCompletionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("<controls:Path.Op", StringComparison.Ordinal) + "<controls:Path.Op".Length),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "Opacity", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.InsertText, "controls:Path.Opacity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InBindingPathContext_ReturnsSourcePropertiesAndMethods()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding }\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{Binding }", StringComparison.Ordinal) + "{Binding ".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(bindingCaret.Line, bindingCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "FirstName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "GetCustomer", StringComparison.Ordinal));
        Assert.Contains(completions, item => item.Kind == XamlCompletionItemKind.Method && string.Equals(item.InsertText, "GetCustomer()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_ForMarkupExtension_ReplacesTypedOpeningBrace()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MarkupExtensionCompletion.axaml";
        const string xaml = "<Window xmlns=\"https://github.com/avaloniaui\" Title=\"{\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{", StringComparison.Ordinal) + 1);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(caret.Line, caret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item =>
            string.Equals(item.Label, "Binding", StringComparison.Ordinal) &&
            string.Equals(item.InsertText, "{Binding $0}", StringComparison.Ordinal) &&
            item.ReplaceRange is not null &&
            GetRangeText(xaml, item.ReplaceRange.Value) == "{");
    }

    [Fact]
    public async Task Completion_InBindingPathContext_OnNonStringTarget_ReturnsSourcePropertiesAndMethods()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingCompletionWidth.axaml";
        const string xaml = "<Window xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\" Width=\"{Binding }\">\n" +
                            "</Window>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{Binding }", StringComparison.Ordinal) + "{Binding ".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(bindingCaret.Line, bindingCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "FirstName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "GetCustomer", StringComparison.Ordinal));
        Assert.Contains(completions, item => item.Kind == XamlCompletionItemKind.Method && string.Equals(item.InsertText, "GetCustomer()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InNestedBindingPathContext_ReturnsNestedProperties()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NestedBindingCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Customer.Dis}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var bindingCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Dis", StringComparison.Ordinal) + 3);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(bindingCaret.Line, bindingCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "DisplayName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InExpressionBindingContext_ReturnsSourcePropertiesAndMethods()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExpressionCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= }\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{= }", StringComparison.Ordinal) + "{= ".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "FormatSummary", StringComparison.Ordinal));
        Assert.Contains(completions, item => item.Kind == XamlCompletionItemKind.Method);
    }

    [Fact]
    public async Task Completion_InImplicitExpressionContext_ReturnsSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ImplicitExpressionCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Prod}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Prod", StringComparison.Ordinal) + 4);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InBindingContextShorthand_ReturnsSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingContextShorthandCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{.}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{.}", StringComparison.Ordinal) + 2);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
        Assert.DoesNotContain(completions, item => string.Equals(item.Label, "RootOnly", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InRootShorthand_ReturnsRootMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/RootShorthandCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{this.}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("{this.}", StringComparison.Ordinal) + "{this.".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "RootOnly", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "FormatTitle", StringComparison.Ordinal));
        Assert.DoesNotContain(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InExplicitInlineCSharpCompactContext_ReturnsSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpCompactCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CSharp Code=source.}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("source.", StringComparison.Ordinal) + "source.".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InExplicitInlineCSharpObjectElementContext_ReturnsSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpElementCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock>\n" +
                            "    <TextBlock.Text>\n" +
                            "      <CSharp>source.</CSharp>\n" +
                            "    </TextBlock.Text>\n" +
                            "  </TextBlock>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("source.", StringComparison.Ordinal) + "source.".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InExplicitInlineCSharpCDataContext_ReturnsSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpCDataCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock>\n" +
                            "    <TextBlock.Text>\n" +
                            "      <CSharp><![CDATA[\n" +
                            "source.\n" +
                            "      ]]></CSharp>\n" +
                            "    </TextBlock.Text>\n" +
                            "  </TextBlock>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("source.", StringComparison.Ordinal) + "source.".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ProductName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InEventLambdaContext_ReturnsParametersAndSourceMembers()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/EventLambdaCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{(s, e) => Cli}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var lambdaBodyOffset = xaml.LastIndexOf("Cli", StringComparison.Ordinal);
        Assert.True(lambdaBodyOffset >= 0, "Expected lambda body token not found.");
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(lambdaBodyOffset + 3);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "ClickCount", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "s", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "e", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InExplicitInlineCSharpLambdaMemberAccess_ResolvesCustomParameterType()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpLambdaMemberCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" " +
                            "xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button AdvancedClick=\"{axsg:CSharp Code=(senderArg, argsArg) => argsArg.}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var caret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("argsArg.", StringComparison.Ordinal) + "argsArg.".Length);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(caret.Line, caret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "Handled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Completion_InNestedExpressionBindingContext_ReturnsNestedProperties()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NestedExpressionCompletion.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= Customer.Dis}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var expressionCaret = SourceText.From(xaml).Lines.GetLinePosition(xaml.IndexOf("Dis", StringComparison.Ordinal) + 3);
        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(expressionCaret.Line, expressionCaret.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "DisplayName", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hover_ForXDataTypeValue_ReturnsResolvedTypeDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverDataType.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("MainWindowViewModel", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Data Type", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("TestApp.Controls.MainWindowViewModel", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForMarkupExtensionToken_ReturnsMarkupExtensionDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverMarkupExtension.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock Text=\"{Binding Name}\" /></UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Binding", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Markup Extension", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("Binding", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForBindingArgumentName_ReturnsArgumentDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverBindingArgument.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Path=Name}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Path=", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Binding Argument", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("Path", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForBindingPathProperty_ReturnsPropertyDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverBindingProperty.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Customer.DisplayName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("DisplayName", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Property", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("CustomerViewModel.DisplayName", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForQualifiedPropertyElement_ReturnsPropertyDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverQualifiedPropertyElement.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Property", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("Path.Opacity", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForQualifiedPropertyElementOwner_ReturnsTypeDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverQualifiedPropertyElementOwner.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Path.Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Type", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("TestApp.Controls.Path", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForBindingPathMethod_ReturnsMethodDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverBindingMethod.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding GetCustomer().DisplayName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("GetCustomer", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Method", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("GetCustomer()", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForExpressionMethod_ReturnsMethodDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverExpressionMethod.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FormatSummary(FirstName, LastName, Count)}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("FormatSummary", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Method", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("FormatSummary", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForImplicitExpressionProperty_ReturnsPropertyDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverImplicitExpressionProperty.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{ProductName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ProductName", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Property", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("MainWindowViewModel.ProductName", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForExplicitInlineCSharpProperty_ReturnsPropertyDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverInlineCSharpProperty.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{axsg:CSharp Code=source.ProductName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ProductName", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Property", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("MainWindowViewModel.ProductName", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForSelectorPseudoClass_ReturnsPseudoClassDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverPseudoClass.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:controls=\"using:TestApp.Controls\">\n" +
                            "  <Style Selector=\"controls|Button:pressed\" />\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf(":pressed", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Pseudoclass", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("TestApp.Controls.Button", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hover_ForDynamicResourceKey_ReturnsResourceDetails()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/HoverResource.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <TextBlock x:Key=\"AccentBrush\" Text=\"Hello\" />\n" +
                            "  </UserControl.Resources>\n" +
                            "  <Border Tag=\"{DynamicResource AccentBrush}\" />\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hover = await engine.GetHoverAsync(
            uri,
            GetPosition(xaml, xaml.LastIndexOf("AccentBrush", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotNull(hover);
        Assert.Contains("Resource Key", hover!.Markdown, StringComparison.Ordinal);
        Assert.Contains("AccentBrush", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_AfterLightweightRequest_ReuseSharedAnalysisProfile()
    {
        var countingProvider = new CountingCompilationProvider(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        using var engine = new XamlLanguageServiceEngine(
            countingProvider);
        const string uri = "file:///tmp/DiagnosticsProfileCaching.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button/></UserControl>";

        await engine.OpenDocumentAsync(
            uri,
            xaml,
            version: 1,
            new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: false, IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        var completions = await engine.GetCompletionsAsync(
            uri,
            new SourcePosition(0, 2),
            new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: false, IncludeSemanticDiagnostics: false),
            CancellationToken.None);
        Assert.NotEmpty(completions); // ensures lightweight profile requested analysis once.

        await engine.GetDiagnosticsAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: true, IncludeSemanticDiagnostics: true),
            CancellationToken.None);

        await engine.GetDiagnosticsAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: true, IncludeSemanticDiagnostics: true),
            CancellationToken.None);

        Assert.Equal(1, countingProvider.GetCompilationCalls);
    }

    [Fact]
    public async Task Opening_And_Closing_Documents_DoesNotInvalidateCompilationSnapshot()
    {
        var countingProvider = new CountingCompilationProvider(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        using var engine = new XamlLanguageServiceEngine(countingProvider);
        var options = new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: false, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(
            "file:///tmp/FileA.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            version: 1,
            options,
            CancellationToken.None);

        await engine.OpenDocumentAsync(
            "file:///tmp/FileB.axaml",
            "<UserControl xmlns=\"https://github.com/avaloniaui\" />",
            version: 1,
            options,
            CancellationToken.None);

        engine.CloseDocument("file:///tmp/FileA.axaml");
        engine.CloseDocument("file:///tmp/FileB.axaml");

        Assert.Equal(0, countingProvider.InvalidateCalls);
    }

    [Fact]
    public async Task HandleWatchedFileChanges_InvalidatesOpenDocumentAnalyses()
    {
        var countingProvider = new CountingCompilationProvider(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        using var engine = new XamlLanguageServiceEngine(countingProvider);
        const string uri = "file:///tmp/MainView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" />";
        var options = new XamlLanguageServiceOptions("/tmp", IncludeCompilationDiagnostics: true, IncludeSemanticDiagnostics: true);

        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
        await engine.GetDiagnosticsAsync(uri, options, CancellationToken.None);
        await engine.GetDiagnosticsAsync(uri, options, CancellationToken.None);

        Assert.Equal(1, countingProvider.GetCompilationCalls);

        var invalidatedUris = engine.HandleWatchedFileChanges(new[]
        {
            "file:///tmp/Themes/Shared.axaml"
        });

        Assert.Equal(1, countingProvider.InvalidateCalls);
        Assert.Contains(uri, invalidatedUris);

        await engine.GetDiagnosticsAsync(uri, options, CancellationToken.None);

        Assert.Equal(2, countingProvider.GetCompilationCalls);
    }

    [Fact]
    public async Task SemanticTokens_AreStableForSameVersion_AndRefreshAfterUpdate()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SemanticTokenCache.axaml";
        const string xamlV1 = "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button/></UserControl>";
        const string xamlV2 = "<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock/></UserControl>";
        var options = new XamlLanguageServiceOptions("/tmp");

        await engine.OpenDocumentAsync(uri, xamlV1, version: 1, options, CancellationToken.None);

        var first = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);
        var second = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);
        Assert.Equal(CreateTokenSignature(first), CreateTokenSignature(second));

        await engine.UpdateDocumentAsync(uri, xamlV2, version: 2, options, CancellationToken.None);
        var third = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);
        Assert.NotEqual(CreateTokenSignature(first), CreateTokenSignature(third));
    }

    [Fact]
    public async Task Reopening_SameUri_SameVersion_DoesNotReuse_Stale_SemanticTokens()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ReopenSemanticTokenCache.axaml";
        const string firstXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><Button/></UserControl>";
        const string secondXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"><TextBlock/></UserControl>";
        var options = new XamlLanguageServiceOptions("/tmp");

        await engine.OpenDocumentAsync(uri, firstXaml, version: 1, options, CancellationToken.None);
        var firstTokens = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);

        engine.CloseDocument(uri);

        await engine.OpenDocumentAsync(uri, secondXaml, version: 1, options, CancellationToken.None);
        var secondTokens = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);

        Assert.NotEqual(CreateTokenSignature(firstTokens), CreateTokenSignature(secondTokens));
    }

    [Fact]
    public async Task Reopening_SameUri_SameVersion_DoesNotReuse_Stale_Definitions()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ReopenDefinitionCache.axaml";
        const string firstXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\"><TextBlock Text=\"{Binding FirstName}\"/></UserControl>";
        const string secondXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\"><TextBlock Text=\"{Binding Count}\"/></UserControl>";
        var options = new XamlLanguageServiceOptions("/tmp");

        await engine.OpenDocumentAsync(uri, firstXaml, version: 1, options, CancellationToken.None);
        var firstDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(firstXaml, firstXaml.IndexOf("FirstName", StringComparison.Ordinal) + 2),
            options,
            CancellationToken.None);

        engine.CloseDocument(uri);

        await engine.OpenDocumentAsync(uri, secondXaml, version: 1, options, CancellationToken.None);
        var secondDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(secondXaml, secondXaml.IndexOf("Count", StringComparison.Ordinal) + 2),
            options,
            CancellationToken.None);

        Assert.NotEmpty(firstDefinitions);
        Assert.NotEmpty(secondDefinitions);
        Assert.NotEqual(CreateDefinitionSignature(firstDefinitions), CreateDefinitionSignature(secondDefinitions));
    }

    [Fact]
    public async Task InlayHints_ForCompiledBinding_ReturnResolvedTypeHint()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlayHintsView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding Content}\"/>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp");
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(2, 14)),
            options,
            new XamlInlayHintOptions(),
            CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal(": string", hint.Label);
        Assert.Equal(2, hint.LabelParts.Length);
        Assert.Equal(": ", hint.LabelParts[0].Value);
        Assert.Equal("string", hint.LabelParts[1].Value);
        Assert.NotNull(hint.LabelParts[1].DefinitionLocation);
        Assert.Contains("Path: `Content`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Source type: `TestApp.Controls.Button`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Equal(1, hint.Position.Line);
    }

    [Fact]
    public async Task InlayHints_ForExpressionBinding_ReturnResolvedTypeHint()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExpressionInlayHintsView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FirstName + ' - ' + LastName}\"/>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp");
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(2, 14)),
            options,
            new XamlInlayHintOptions(),
            CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal(": string", hint.Label);
        Assert.Contains("Expression Binding", hint.Tooltip, StringComparison.Ordinal);
        Assert.Equal(1, hint.Position.Line);
    }

    [Fact]
    public async Task InlayHints_ForElementNameBinding_ReturnResolvedTypeHint()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ElementNameInlayHintsView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp");
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(3, 14)),
            options,
            new XamlInlayHintOptions(),
            CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal(": string", hint.Label);
        Assert.Contains("Path: `Content`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Source type: `TestApp.Controls.Button`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Equal(2, hint.Position.Line);
    }

    [Fact]
    public async Task InlayHints_ForAncestorTypeBinding_ReturnResolvedTypeHint()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/AncestorTypeInlayHintsView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:controls=\"using:TestApp.Controls\">\n" +
                            "  <controls:Border>\n" +
                            "    <TextBlock Text=\"{Binding RelativeSource={RelativeSource AncestorType=controls:Border}, Path=Child}\"/>\n" +
                            "  </controls:Border>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp");
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(4, 14)),
            options,
            new XamlInlayHintOptions(),
            CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal(": object", hint.Label);
        Assert.Contains("Path: `Child`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Source type: `TestApp.Controls.Border`", hint.Tooltip, StringComparison.Ordinal);
        Assert.Equal(2, hint.Position.Line);
    }

    [Fact]
    public async Task InlayHints_FilterToRequestedRange_ReturnOnlyHintsInsideRange()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlayRangeFilterView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding FirstName}\"/>\n" +
                            "  <TextBlock Text=\"{= Count + 1}\"/>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp");
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(2, 0), new SourcePosition(2, 40)),
            options,
            new XamlInlayHintOptions(),
            CancellationToken.None);

        var hint = Assert.Single(hints);
        Assert.Equal(2, hint.Position.Line);
        Assert.Contains("Expression Binding", hint.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InlayHints_ForExplicitInlineCSharpExpression_ReturnResultType()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpInlayHints.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{axsg:CSharp Code=source.ProductName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var hints = await engine.GetInlayHintsAsync(
            uri,
            new SourceRange(new SourcePosition(0, 0), new SourcePosition(1, 80)),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            new XamlInlayHintOptions(),
            CancellationToken.None);

        Assert.Contains(hints, hint => hint.LabelParts.Any(part => string.Equals(part.Value, "string", StringComparison.Ordinal)));
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
    public async Task Definition_ForElementType_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/TypeDefinitionView.axaml";
        const string xaml = "<Button />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForUsingNamespacePrefixedElement_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/UsingNamespaceTypeDefinitionView.axaml";
        const string xaml = "<pages:Path xmlns:pages=\"using:TestApp.Controls\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, 8),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForProperty_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/PropertyDefinitionView.axaml";
        const string xaml = "<Button Content=\"Save\"/>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, 10),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForQualifiedPropertyElement_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForQualifiedPropertyElementOwner_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementOwnerDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Path.Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForStyleIncludeSource_Resolves_Target_Xaml_File()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-include-def-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var themesDir = Path.Combine(projectDir, "Themes");
        Directory.CreateDirectory(themesDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var targetFilePath = Path.Combine(themesDir, "Fluent.xaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        const string xaml = """
<UserControl xmlns="https://github.com/avaloniaui">
  <UserControl.Styles>
    <StyleInclude Source="/Themes/Fluent.xaml" />
  </UserControl.Styles>
</UserControl>
""";

        await File.WriteAllTextAsync(
            projectPath,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <AvaloniaXaml Include="MainView.axaml" />
                <AvaloniaXaml Include="Themes/Fluent.xaml" />
              </ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(targetFilePath, "<Styles xmlns=\"https://github.com/avaloniaui\" />");

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, xaml, version: 1, options, CancellationToken.None);

            var definitions = await engine.GetDefinitionsAsync(
                openUri,
                new SourcePosition(2, xaml.Split('\n')[2].IndexOf("Fluent.xaml", StringComparison.Ordinal) + 2),
                options,
                CancellationToken.None);

            Assert.Single(definitions);
            Assert.Equal(new Uri(targetFilePath).AbsoluteUri, definitions[0].Uri);
            Assert.Equal(0, definitions[0].Range.Start.Line);
            Assert.Equal(0, definitions[0].Range.Start.Character);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Definition_ForStyleSelectorType_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleSelectorTypeDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.h2\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var selectorOffset = xaml.IndexOf("TextBlock", StringComparison.Ordinal);
        Assert.True(selectorOffset >= 0, "Expected selector type token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, selectorOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForStyleClassValue_ResolvesSelectorDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.warning\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Classes=\"warning\" Text=\"Inline\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var classOffset = xaml.IndexOf("warning", xaml.IndexOf("Classes=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(classOffset >= 0, "Expected style class token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, classOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(2, definitions[0].Range.Start.Line);
    }

    [Fact]
    public async Task Definition_ForComplexSelectorMiddleType_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ComplexSelectorMiddleTypeDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Border.local-card > StackPanel > TextBlock.subtitle\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <Border Classes=\"local-card\">\n" +
                            "    <StackPanel>\n" +
                            "      <TextBlock Classes=\"subtitle\"/>\n" +
                            "    </StackPanel>\n" +
                            "  </Border>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var stackPanelOffset = xaml.IndexOf("StackPanel", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(stackPanelOffset >= 0, "Expected StackPanel selector token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, stackPanelOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForComplexSelectorTrailingClass_ResolvesSelectorDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ComplexSelectorTrailingClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Border.local-card > StackPanel > TextBlock.subtitle\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <Border Classes=\"local-card\">\n" +
                            "    <StackPanel>\n" +
                            "      <TextBlock Classes=\"subtitle\"/>\n" +
                            "    </StackPanel>\n" +
                            "  </Border>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var subtitleOffset = xaml.IndexOf("subtitle", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(subtitleOffset >= 0, "Expected subtitle selector class token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, subtitleOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(2, definitions[0].Range.Start.Line);
    }

    [Fact]
    public async Task Definition_ForSelectorPseudoClass_ResolvesPseudoClassDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectorPseudoClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button:pressed\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var pseudoOffset = xaml.IndexOf("pressed", StringComparison.Ordinal);
        Assert.True(pseudoOffset >= 0, "Expected pseudoclass token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, pseudoOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForSelectorNamedElement_WithPseudoClass_ResolvesNamedElementDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectorNamedElementDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <ToggleButton x:Name=\"ThemeToggle\"/>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"ToggleButton#ThemeToggle:checked\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var nameOffset = xaml.IndexOf("ThemeToggle", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(nameOffset >= 0, "Expected selector named element token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, nameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(1, definitions[0].Range.Start.Line);
    }

    [Fact]
    public async Task Definition_ForQualifiedElementPrefix_ResolvesXmlnsDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedElementPrefixDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:pages=\"clr-namespace:TestApp.Controls\">\n" +
                            "  <pages:Button />\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var prefixOffset = xaml.IndexOf("pages:Button", StringComparison.Ordinal);
        Assert.True(prefixOffset >= 0, "Expected qualified element token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, prefixOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(0, definitions[0].Range.Start.Line);
        var startOffset = TextCoordinateHelper.GetOffset(xaml, definitions[0].Range.Start);
        var endOffset = TextCoordinateHelper.GetOffset(xaml, definitions[0].Range.End);
        Assert.Equal("pages", xaml.Substring(startOffset, endOffset - startOffset));
    }

    [Fact]
    public async Task References_ForSelectorNamedElement_WithPseudoClass_IncludeDeclarationAndSelectorUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectorNamedElementReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <ToggleButton x:Name=\"ThemeToggle\"/>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"ToggleButton#ThemeToggle:checked\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var nameOffset = xaml.IndexOf("ThemeToggle", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(nameOffset >= 0, "Expected selector named element token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, nameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 2);
        Assert.Contains(references, reference => reference.IsDeclaration && reference.Range.Start.Line == 1);
        Assert.Contains(references, reference => !reference.IsDeclaration && reference.Range.Start.Line == 3);
    }

    [Fact]
    public async Task References_FromNamedElementDeclaration_IncludeSelectorUsage_WithPseudoClass()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectorNamedElementDeclarationReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <ToggleButton x:Name=\"ThemeToggle\"/>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"ToggleButton#ThemeToggle:checked\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var declarationOffset = xaml.IndexOf("ThemeToggle", xaml.IndexOf("x:Name=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(declarationOffset >= 0, "Expected x:Name declaration token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, declarationOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 2);
        Assert.Contains(references, reference => reference.IsDeclaration && reference.Range.Start.Line == 1);
        Assert.Contains(references, reference => !reference.IsDeclaration && reference.Range.Start.Line == 3);
    }

    [Fact]
    public async Task Definition_ForNestedSelectorPseudoClass_UsesAncestorStyleTargetType()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NestedSelectorPseudoClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Expander\">\n" +
                            "      <Style Selector=\"^:expanded\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var pseudoOffset = xaml.IndexOf("expanded", StringComparison.Ordinal);
        Assert.True(pseudoOffset >= 0, "Expected nested pseudoclass token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, pseudoOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForStyleSetterPropertyValue_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleSetterPropertyDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock\">\n" +
                            "      <Setter Property=\"Text\" Value=\"Updated\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var propertyOffset = xaml.IndexOf("Property=\"Text\"", StringComparison.Ordinal);
        Assert.True(propertyOffset >= 0, "Expected Setter Property token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, propertyOffset + "Property=\"".Length + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForMarkupExtensionClass_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MarkupExtensionClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:controls=\"using:TestApp.Controls\">\n" +
                            "  <TextBlock Text=\"{controls:My Value=123}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var extensionOffset = xaml.IndexOf("controls:My", StringComparison.Ordinal);
        Assert.True(extensionOffset >= 0, "Expected markup extension class token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, extensionOffset + "controls:".Length + 1),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForXDataTypeAttributeValue_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/XDataTypeDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, xaml.IndexOf("vm:Button", StringComparison.Ordinal) + 5),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForXClassAttributeValue_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/XClassDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.Button\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, xaml.IndexOf("TestApp.Controls.Button", StringComparison.Ordinal) + 8),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForBindingPathProperty_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingPathPropertyDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("{Binding Name}", StringComparison.Ordinal) + "{Binding ".Length + 1),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForExpressionBindingProperty_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExpressionBindingPropertyDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FirstName + ' - ' + LastName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var firstNameOffset = xaml.IndexOf("FirstName", StringComparison.Ordinal);
        Assert.True(firstNameOffset >= 0, "Expected expression property token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, firstNameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForBindingContextShorthandProperty_ResolvesViewModelMember()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingContextShorthandDefinition.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{.Name}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var offset = xaml.IndexOf(".Name", StringComparison.Ordinal) + 2;
        var position = SourceText.From(xaml).Lines.GetLinePosition(offset);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(position.Line, position.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForRootShorthandProperty_ResolvesRootMember()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/RootShorthandDefinition.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:Class=\"TestApp.Controls.MainView\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{this.Title}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var offset = xaml.IndexOf("this.Title", StringComparison.Ordinal) + "this.".Length + 2;
        var position = SourceText.From(xaml).Lines.GetLinePosition(offset);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(position.Line, position.Character),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForEventLambdaProperty_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/EventLambdaPropertyDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{(s, e) => ClickCount++}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var offset = xaml.IndexOf("ClickCount", StringComparison.Ordinal);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, offset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForBindingPathProperty_WithElementNameSource_ResolvesPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingPathElementNameDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <Button x:Name=\"SubmitButton\" Content=\"Save\"/>\n" +
                            "  <TextBlock Text=\"{Binding ElementName=SubmitButton, Path=Content}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var contentOffset = xaml.IndexOf("Path=Content", StringComparison.Ordinal);
        Assert.True(contentOffset >= 0, "Expected binding Path token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, contentOffset + "Path=".Length + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForNestedBindingPathProperty_ResolvesNestedPropertySource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NestedBindingPathPropertyDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Customer.DisplayName}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var displayNameOffset = xaml.IndexOf("DisplayName", StringComparison.Ordinal);
        Assert.True(displayNameOffset >= 0, "Expected nested binding property token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, displayNameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Definition_ForBindingAncestorTypeToken_ResolvesTypeSource()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingAncestorTypeDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:controls=\"using:TestApp.Controls\">\n" +
                            "  <controls:Border>\n" +
                            "    <TextBlock Text=\"{Binding RelativeSource={RelativeSource AncestorType=controls:Border}, Path=Child}\"/>\n" +
                            "  </controls:Border>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var ancestorTokenOffset = xaml.IndexOf("controls:Border", xaml.IndexOf("AncestorType=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(ancestorTokenOffset >= 0, "Expected AncestorType token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, ancestorTokenOffset + "controls:".Length + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, definitions[0].Uri, StringComparison.OrdinalIgnoreCase);
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
        Assert.Equal("AccentBrush", GetRangeText(xaml, definitions[0].Range));
    }

    [Fact]
    public async Task Definition_ForDynamicResourceInSetterValue_ResolvesResourceDeclaration()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/DynamicResourceDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"AccentButtonBackgroundDisabled\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button\">\n" +
                            "      <Setter Property=\"Background\" Value=\"{DynamicResource AccentButtonBackgroundDisabled}\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var dynamicResourceOffset = xaml.IndexOf("AccentButtonBackgroundDisabled", xaml.IndexOf("DynamicResource", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(dynamicResourceOffset >= 0, "Expected DynamicResource key token not found.");

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, dynamicResourceOffset + 2),
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
    public async Task References_ForBindingPathProperty_ReturnDeclarationAndBindingUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingPathReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\"/>\n" +
                            "  <TextBlock Text=\"{Binding Path=Name}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("{Binding Name}", StringComparison.Ordinal) + "{Binding ".Length + 1),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 3);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_ForQualifiedPropertyElement_ReturnDeclarationAndUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 2);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_ForQualifiedPropertyElementOwner_ReturnTypeDeclarationAndUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementOwnerReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Path.Opacity", StringComparison.Ordinal) + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 2);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_ForExpressionBindingProperty_ReturnDeclarationAndExpressionUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExpressionBindingReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{= FirstName + ' - ' + LastName}\"/>\n" +
                            "  <TextBlock Text=\"{= FirstName + '!'}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var firstNameOffset = xaml.IndexOf("FirstName", StringComparison.Ordinal);
        Assert.True(firstNameOffset >= 0, "Expected expression property token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, firstNameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 3);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_ForImplicitExpressionProperty_ReturnDeclarationAndUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ImplicitExpressionReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{ProductName}\"/>\n" +
                            "  <TextBlock Text=\"{$'{Quantity}x {ProductName}'}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var productNameOffset = xaml.IndexOf("ProductName", StringComparison.Ordinal);
        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, productNameOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 3);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task Definitions_And_References_ForExplicitInlineCSharpProperty_ReturnDeclarationAndUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{axsg:CSharp Code=source.ProductName}\"/>\n" +
                            "  <TextBlock>\n" +
                            "    <TextBlock.Text>\n" +
                            "      <axsg:CSharp>source.ProductName + \"!\"</axsg:CSharp>\n" +
                            "    </TextBlock.Text>\n" +
                            "  </TextBlock>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var offset = xaml.IndexOf("ProductName", StringComparison.Ordinal);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, offset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, offset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.Contains(definitions, item => item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, references.Count(item => !item.IsDeclaration && string.Equals(item.Uri, uri, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Definitions_ForInlineCSharpCDataProperty_And_Method_ReturnClrDeclarations()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpCDataDefinitionView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button>\n" +
                            "    <Button.Click>\n" +
                            "      <CSharp><![CDATA[\n" +
                            "source.RecordSender(sender);\n" +
                            "source.ClickCount = 0;\n" +
                            "      ]]></CSharp>\n" +
                            "    </Button.Click>\n" +
                            "  </Button>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var methodOffset = xaml.IndexOf("RecordSender", StringComparison.Ordinal);
        var methodDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, methodOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        var propertyOffset = xaml.IndexOf("ClickCount", StringComparison.Ordinal);
        var propertyDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, propertyOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(methodDefinitions);
        Assert.Contains(methodDefinitions, item => item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(propertyDefinitions);
        Assert.Contains(propertyDefinitions, item => item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Definitions_And_References_ForInlineCSharpLocalVariable_ReturnLocalXamlRanges()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpLocalReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{axsg:CSharp Code=(s, e) => { var clickCount = source.ClickCount; source.ClickCount = clickCount; }}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var usageOffset = xaml.LastIndexOf("clickCount", StringComparison.Ordinal);
        var declarationOffset = xaml.IndexOf("clickCount", StringComparison.Ordinal);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, usageOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, usageOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Single(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(GetPosition(xaml, declarationOffset).Line, definitions[0].Range.Start.Line);
        Assert.Equal(2, references.Length);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri == uri);
        Assert.Contains(references, item => !item.IsDeclaration && item.Uri == uri);
    }

    [Fact]
    public async Task Definitions_And_References_ForInlineCSharpCDataLocalVariable_ReturnLocalXamlRanges()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpCDataLocalReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button>\n" +
                            "    <Button.Click>\n" +
                            "      <CSharp><![CDATA[(s, e) => { var clickCount = source.ClickCount; source.ClickCount = clickCount; }]]></CSharp>\n" +
                            "    </Button.Click>\n" +
                            "  </Button>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var usageOffset = xaml.LastIndexOf("clickCount", StringComparison.Ordinal);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xaml, usageOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, usageOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Single(definitions);
        Assert.Equal(uri, definitions[0].Uri);
        Assert.Equal(2, references.Length);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri == uri);
        Assert.Contains(references, item => !item.IsDeclaration && item.Uri == uri);
    }

    [Fact]
    public async Task References_ForBindingPathProperty_IncludeExpressionBindingUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingAndExpressionReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding FirstName}\"/>\n" +
                            "  <TextBlock Text=\"{= FirstName + '!'}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("{Binding FirstName}", StringComparison.Ordinal) + "{Binding ".Length + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.True(references.Length >= 3);
        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
    }

    [Fact]
    public async Task References_ForDynamicResourceInSetterValue_ReturnDeclarationAndDynamicUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/DynamicResourceReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <UserControl.Resources>\n" +
                            "    <SolidColorBrush x:Key=\"AccentButtonBackgroundDisabled\" Color=\"Red\"/>\n" +
                            "  </UserControl.Resources>\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button\">\n" +
                            "      <Setter Property=\"Background\" Value=\"{DynamicResource AccentButtonBackgroundDisabled}\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);

        var references = await engine.GetReferencesAsync(
            uri,
            new SourcePosition(2, 35),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 6);
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
    public async Task References_ForStyleSelectorType_IncludeSelectorAndElementUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleSelectorTypeReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.h2\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Text=\"Inline\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var selectorOffset = xaml.IndexOf("TextBlock.h2", StringComparison.Ordinal);
        Assert.True(selectorOffset >= 0, "Expected selector token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, selectorOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 4);
    }

    [Fact]
    public async Task References_ForStyleClass_IncludeSelectorDeclarations_And_ClassUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleClassReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.warning\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Classes=\"warning\"/>\n" +
                            "  <TextBlock Classes.warning=\"True\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var classOffset = xaml.IndexOf("warning", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(classOffset >= 0, "Expected selector class token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, classOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 4);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 5);
    }

    [Fact]
    public async Task References_ForComplexSelectorMiddleType_IncludeSelectorAndElementUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ComplexSelectorMiddleTypeReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Border.local-card > StackPanel > TextBlock.subtitle\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <Border Classes=\"local-card\">\n" +
                            "    <StackPanel>\n" +
                            "      <TextBlock Classes=\"subtitle\"/>\n" +
                            "    </StackPanel>\n" +
                            "  </Border>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var stackPanelOffset = xaml.IndexOf("StackPanel", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(stackPanelOffset >= 0, "Expected StackPanel selector token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, stackPanelOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 5);
    }

    [Fact]
    public async Task References_ForComplexSelectorTrailingClass_IncludeDeclarationAndUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ComplexSelectorTrailingClassReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Border.local-card > StackPanel > TextBlock.subtitle\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <Border Classes=\"local-card\">\n" +
                            "    <StackPanel>\n" +
                            "      <TextBlock Classes=\"subtitle\"/>\n" +
                            "    </StackPanel>\n" +
                            "  </Border>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var subtitleOffset = xaml.IndexOf("subtitle", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(subtitleOffset >= 0, "Expected subtitle selector class token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, subtitleOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 6);
    }

    [Fact]
    public async Task References_ForSelectorPseudoClass_IncludeDeclarationAndSelectorUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/SelectorPseudoClassReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button:pressed\"/>\n" +
                            "    <Style Selector=\":is(Button):pressed\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var pseudoOffset = xaml.IndexOf("pressed", StringComparison.Ordinal);
        Assert.True(pseudoOffset >= 0, "Expected pseudoclass token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, pseudoOffset + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration && item.Uri.Contains(LanguageServiceTestCompilationFactory.SymbolSourceFilePath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 3);
    }

    [Fact]
    public async Task References_ForStyleSetterPropertyValue_IncludeSetterUsage()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleSetterPropertyReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock\">\n" +
                            "      <Setter Property=\"Text\" Value=\"Updated\"/>\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Text=\"Inline\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var propertyOffset = xaml.IndexOf("Property=\"Text\"", StringComparison.Ordinal);
        Assert.True(propertyOffset >= 0, "Expected Setter Property token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, propertyOffset + "Property=\"".Length + 2),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 3);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 6);
    }

    [Fact]
    public async Task References_ForMarkupExtensionClass_IncludeAllMarkupUsages()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MarkupExtensionClassReferencesView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:controls=\"using:TestApp.Controls\">\n" +
                            "  <TextBlock Text=\"{controls:My Value=1}\"/>\n" +
                            "  <TextBlock Tag=\"{controls:My Value=2}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var extensionOffset = xaml.IndexOf("controls:My", StringComparison.Ordinal);
        Assert.True(extensionOffset >= 0, "Expected markup extension class token not found.");

        var references = await engine.GetReferencesAsync(
            uri,
            GetPosition(xaml, extensionOffset + "controls:".Length + 1),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.Contains(references, item => item.IsDeclaration);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 1);
        Assert.Contains(references, item => !item.IsDeclaration && item.Range.Start.Line == 2);
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
        Assert.Contains(tokens, token => token.TokenType == "xamlName");
        Assert.Contains(tokens, token => token.TokenType == "xamlAttribute");
        Assert.Contains(tokens, token => token.TokenType == "xamlAttributeValue");
    }

    [Fact]
    public async Task SemanticTokens_IncludeNamespaceTokens()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NamespaceTokens.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:DataType=\"vm:MainViewModel\"/>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, token => token.TokenType == "xamlNamespacePrefix");
    }

    [Fact]
    public async Task SemanticTokens_ClassifyMarkupExtensionSegments()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MarkupExtensionTokens.axaml";
        const string xaml = "<TextBlock xmlns=\"https://github.com/avaloniaui\" Text=\"{Binding Path=Title, Mode=TwoWay}\"/>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.Contains(tokens, token => token.TokenType == "xamlMarkupExtensionClass");
        Assert.Contains(tokens, token => token.TokenType == "xamlMarkupExtensionParameterName");
        Assert.Contains(tokens, token => token.TokenType == "xamlMarkupExtensionParameterValue");
    }

    [Fact]
    public async Task SemanticTokens_ClassifyImplicitExpressions_And_EventLambdas()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExpressionSemanticTokens.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{$'Total: ${Price * Quantity:F2}'}\"/>\n" +
                            "  <Button Click=\"{(s, e) => ClickCount++}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.Contains(tokens, token => token.TokenType == "property");
        Assert.Contains(tokens, token => token.TokenType == "parameter");
        Assert.Contains(tokens, token => token.TokenType == "operator");
    }

    [Fact]
    public async Task SemanticTokens_ClassifyExplicitInlineCSharpExpressions()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/ExplicitInlineCodeSemanticTokens.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" xmlns:axsg=\"using:XamlToCSharpGenerator.Runtime\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{axsg:CSharp Code=source.ProductName}\"/>\n" +
                            "  <Button Click=\"{axsg:CSharp Code=(sender, e) => source.ClickCount++}\"/>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);

        Assert.Contains(tokens, token => token.TokenType == "property");
        Assert.Contains(tokens, token => token.TokenType == "parameter");
        Assert.Contains(tokens, token => token.TokenType == "operator");
    }

    [Fact]
    public async Task SemanticTokens_ClassifyInlineCSharpLocals_And_CDataParameters()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InlineCSharpLocalSemanticTokens.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <Button Click=\"{CSharp Code=(s, e) => { var clickCount = source.ClickCount; source.ClickCount = clickCount; }}\"/>\n" +
                            "  <Button>\n" +
                            "    <Button.Click>\n" +
                            "      <CSharp><![CDATA[(senderArg, argsArg) => argsArg.Handled = true;]]></CSharp>\n" +
                            "    </Button.Click>\n" +
                            "  </Button>\n" +
                            "</UserControl>";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(
            uri,
            new XamlLanguageServiceOptions("/tmp"),
            CancellationToken.None);
        var cdataStart = GetPosition(xaml, xaml.IndexOf("<![CDATA[", StringComparison.Ordinal));

        Assert.Contains(tokens, token => token.TokenType == "variable");
        Assert.Contains(tokens, token => token.TokenType == "parameter");
        Assert.Contains(tokens, token => token.TokenType == "property");
        Assert.Contains(tokens, token => token.TokenType == "xamlDelimiter" && token.Line == cdataStart.Line && token.Character == cdataStart.Character && token.Length == 9);
        Assert.DoesNotContain(tokens, token => token.TokenType == "xamlName" && token.Line == cdataStart.Line && token.Character >= cdataStart.Character && token.Character < cdataStart.Character + 9);
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

    [Fact]
    public async Task References_ForElementType_IncludeLinkedXamlSources()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-refs-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var linkedDir = Path.Combine(tempRoot, "linked");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(linkedDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDir, "SharedView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var linkedUri = new Uri(linkedFilePath).AbsoluteUri;
        const string xaml = "<Path xmlns=\"https://github.com/avaloniaui\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"../linked/SharedView.axaml\" Link=\"SharedView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(linkedFilePath, xaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));

            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, xaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, 2),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, linkedUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForElementType_ResolveProjectFromWorkspaceDirectory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-refs-dirroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var projectPath = Path.Combine(tempRoot, "TestApp.csproj");
        var openFilePath = Path.Combine(tempRoot, "MainView.axaml");
        var secondaryFilePath = Path.Combine(tempRoot, "SecondaryView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var secondaryUri = new Uri(secondaryFilePath).AbsoluteUri;
        const string xaml = "<Path xmlns=\"https://github.com/avaloniaui\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"SecondaryView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(secondaryFilePath, xaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));

            var options = new XamlLanguageServiceOptions(tempRoot, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, xaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, 2),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, secondaryUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForUsingNamespacePrefixedElementType_IncludeLinkedXamlSources()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-refs-using-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var linkedDir = Path.Combine(tempRoot, "linked");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(linkedDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDir, "SharedView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var linkedUri = new Uri(linkedFilePath).AbsoluteUri;
        const string xaml = "<pages:Path xmlns:pages=\"using:TestApp.Controls\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"../linked/SharedView.axaml\" Link=\"SharedView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(linkedFilePath, xaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));

            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, xaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, 8),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, linkedUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForElementType_IncludeWildcardLinkedXamlSources()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-refs-wildcard-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var linkedDir = Path.Combine(tempRoot, "linked", "nested");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(linkedDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDir, "SharedView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var linkedUri = new Uri(linkedFilePath).AbsoluteUri;
        const string xaml = "<Path xmlns=\"https://github.com/avaloniaui\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"../linked/**/*.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, xaml);
        await File.WriteAllTextAsync(linkedFilePath, xaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));

            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, xaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, 2),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, linkedUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForProperty_IncludeLinkedXamlSources()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-prop-refs-" + Guid.NewGuid().ToString("N"));
        var projectDir = Path.Combine(tempRoot, "project");
        var linkedDir = Path.Combine(tempRoot, "linked");
        Directory.CreateDirectory(projectDir);
        Directory.CreateDirectory(linkedDir);

        var projectPath = Path.Combine(projectDir, "TestApp.csproj");
        var openFilePath = Path.Combine(projectDir, "MainView.axaml");
        var linkedFilePath = Path.Combine(linkedDir, "SharedView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var linkedUri = new Uri(linkedFilePath).AbsoluteUri;
        const string openXaml = "<Path xmlns=\"https://github.com/avaloniaui\" Data=\"Main\" />";
        const string linkedXaml = "<Path xmlns=\"https://github.com/avaloniaui\" Data=\"Linked\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"../linked/SharedView.axaml\" Link=\"SharedView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, openXaml);
        await File.WriteAllTextAsync(linkedFilePath, linkedXaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));

            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, openXaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, openXaml.IndexOf("Data", StringComparison.Ordinal) + 1),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, linkedUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForXDataTypeAttributeValue_IncludeDeclarationAndUsage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-xdatatype-refs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, "TestApp.csproj");
        var openFilePath = Path.Combine(tempRoot, "MainView.axaml");
        var linkedFilePath = Path.Combine(tempRoot, "SecondaryView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        var linkedUri = new Uri(linkedFilePath).AbsoluteUri;
        const string openXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\" />";
        const string linkedXaml = "<DataTemplate xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:Button\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"SecondaryView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, openXaml);
        await File.WriteAllTextAsync(linkedFilePath, linkedXaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, openXaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, openXaml.IndexOf("vm:Button", StringComparison.Ordinal) + 5),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, linkedUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task References_ForXClassAttributeValue_IncludeDeclarationAndUsage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-xclass-refs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, "TestApp.csproj");
        var openFilePath = Path.Combine(tempRoot, "MainView.axaml");
        var openUri = new Uri(openFilePath).AbsoluteUri;
        const string openXaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.Button\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(openFilePath, openXaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);
            await engine.OpenDocumentAsync(openUri, openXaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                openUri,
                new SourcePosition(0, openXaml.IndexOf("TestApp.Controls.Button", StringComparison.Ordinal) + 8),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.Contains(
                                                    LanguageServiceTestCompilationFactory.SymbolSourceFilePath,
                                                    StringComparison.OrdinalIgnoreCase));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, openUri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Definitions_And_References_Work_For_ControlCatalog_UsingPrefix_Type()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "pages:CompositionPage";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "pages:".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.NotEmpty(references);
    }

    [Fact]
    public async Task Completion_Works_For_Empty_ExpressionBinding_In_SourceGenCatalogSample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "ExpressionBindingsPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var originalText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string existingExpression = "<TextBlock Text=\"{= FirstName + ' ' + LastName}\" />";
        const string emptyExpression = "<TextBlock Text=\"{= }\" />";
        Assert.Contains(existingExpression, originalText, StringComparison.Ordinal);
        var xamlText = originalText.Replace(existingExpression, emptyExpression, StringComparison.Ordinal);

        var caretOffset = xamlText.IndexOf(emptyExpression, StringComparison.Ordinal) + "<TextBlock Text=\"{= ".Length;
        Assert.True(caretOffset >= 0, "Expected empty expression insertion point not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var completions = await engine.GetCompletionsAsync(
            uri,
            GetPosition(xamlText, caretOffset),
            options,
            CancellationToken.None);

        Assert.Contains(completions, item => string.Equals(item.Label, "FirstName", StringComparison.Ordinal));
        Assert.Contains(completions, item => string.Equals(item.Label, "FormatSummary", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definitions_Work_For_InlineCSharpCData_In_SourceGenCatalogSample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var methodOffset = xamlText.IndexOf("RecordSender", StringComparison.Ordinal);
        var propertyOffset = xamlText.IndexOf("ClickCount = 0", StringComparison.Ordinal);
        Assert.True(methodOffset >= 0, "Expected inline CDATA method usage not found.");
        Assert.True(propertyOffset >= 0, "Expected inline CDATA property usage not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var methodDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xamlText, methodOffset + 2),
            options,
            CancellationToken.None);
        var propertyDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xamlText, propertyOffset + 2),
            options,
            CancellationToken.None);

        Assert.NotEmpty(methodDefinitions);
        Assert.Contains(methodDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(propertyDefinitions);
        Assert.Contains(propertyDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Definitions_Work_For_InlineCSharpCompactMarkup_In_SourceGenCatalogSample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodePage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var propertyOffset = xamlText.IndexOf("{CSharp Code=source.ProductName}", StringComparison.Ordinal);
        var methodOffset = xamlText.IndexOf("source.RecordSender(sender)", StringComparison.Ordinal);
        Assert.True(propertyOffset >= 0, "Expected inline compact property usage not found.");
        Assert.True(methodOffset >= 0, "Expected inline compact method usage not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var propertyDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xamlText, propertyOffset + "{CSharp Code=source.".Length + 2),
            options,
            CancellationToken.None);
        var methodDefinitions = await engine.GetDefinitionsAsync(
            uri,
            GetPosition(xamlText, methodOffset + "source.".Length + 2),
            options,
            CancellationToken.None);

        Assert.NotEmpty(propertyDefinitions);
        Assert.Contains(propertyDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(methodDefinitions);
        Assert.Contains(methodDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Definitions_And_Completions_Work_For_InlineCSharpContextVariables_In_SourceGenCatalogSample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var cdataPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        var compactPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodePage.axaml");
        Assert.True(File.Exists(cdataPath), "Expected sample file not found: " + cdataPath);
        Assert.True(File.Exists(compactPath), "Expected sample file not found: " + compactPath);

        var cdataText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(cdataPath);
        var compactText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(compactPath);
        var cdataSourceOffset = cdataText.IndexOf("source.ClickCount++;", StringComparison.Ordinal);
        var compactSourceOffset = compactText.IndexOf("{CSharp Code=source.ProductName}", StringComparison.Ordinal);
        Assert.True(cdataSourceOffset >= 0, "Expected inline CDATA source usage not found.");
        Assert.True(compactSourceOffset >= 0, "Expected inline compact source usage not found.");

        var cdataUri = new Uri(cdataPath).AbsoluteUri;
        var compactUri = new Uri(compactPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(cdataUri, cdataText, version: 1, options, CancellationToken.None);
        await engine.OpenDocumentAsync(compactUri, compactText, version: 1, options, CancellationToken.None);

        var cdataDefinitions = await engine.GetDefinitionsAsync(
            cdataUri,
            GetPosition(cdataText, cdataSourceOffset + 2),
            options,
            CancellationToken.None);
        var compactDefinitions = await engine.GetDefinitionsAsync(
            compactUri,
            GetPosition(compactText, compactSourceOffset + "{CSharp Code=".Length + 2),
            options,
            CancellationToken.None);
        var cdataCompletions = await engine.GetCompletionsAsync(
            cdataUri,
            GetPosition(cdataText, cdataText.IndexOf("source.", StringComparison.Ordinal) + "source.".Length),
            options,
            CancellationToken.None);

        Assert.Contains(cdataDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(compactDefinitions, item => item.Uri.EndsWith("InlineCodePageViewModel.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(cdataCompletions, item => string.Equals(item.Label, "ClickCount", StringComparison.Ordinal));
        Assert.Contains(cdataCompletions, item => string.Equals(item.Label, "RecordSender", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SemanticTokens_Classify_InlineCSharpCData_In_SourceGenCatalogSample()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "SourceGenXamlCatalogSample", "Pages", "InlineCodeCDataPage.axaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);
        var tokens = await engine.GetSemanticTokensAsync(uri, options, CancellationToken.None);

        var sourceOffset = xamlText.IndexOf("source.ClickCount++;", StringComparison.Ordinal);
        var propertyOffset = xamlText.IndexOf("LastAction", sourceOffset, StringComparison.Ordinal);
        var methodOffset = xamlText.IndexOf("RecordSender", StringComparison.Ordinal);
        Assert.True(sourceOffset >= 0 && propertyOffset >= 0 && methodOffset >= 0, "Expected inline CDATA symbols not found.");

        var sourcePosition = GetPosition(xamlText, sourceOffset);
        var propertyPosition = GetPosition(xamlText, propertyOffset);
        var methodPosition = GetPosition(xamlText, methodOffset);

        Assert.Contains(tokens, token => token.TokenType == "parameter" && token.Line == sourcePosition.Line && token.Character == sourcePosition.Character);
        Assert.Contains(tokens, token => token.TokenType == "property" && token.Line == propertyPosition.Line && token.Character == propertyPosition.Character);
        Assert.Contains(tokens, token => token.TokenType == "method" && token.Line == methodPosition.Line && token.Character == methodPosition.Character);
    }

    [Fact]
    public async Task Definitions_And_References_Work_For_ControlCatalog_XDataType_Type()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "viewModels:MainWindowViewModel";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:DataType token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "viewModels:".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.NotEmpty(references);
        Assert.Contains(references, item => item.IsDeclaration);
    }

    [Fact]
    public async Task Definitions_And_References_Work_For_ControlCatalog_XClass_Type()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "ControlCatalog.MainView";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:Class token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "ControlCatalog.".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);

        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var definitions = await engine.GetDefinitionsAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);
        var references = await engine.GetReferencesAsync(
            uri,
            tokenPosition,
            options,
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.NotEmpty(references);
        Assert.Contains(references, item => item.IsDeclaration);
    }

    [Fact]
    public async Task CSharpReferences_ForExpressionBindingProperty_IncludeXamlUsages()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(fixture.RootPath, IncludeSemanticDiagnostics: false);

        var references = await engine.GetXamlReferencesForCSharpSymbolAsync(
            fixture.CodeUri,
            fixture.NamePropertyPosition,
            options,
            fixture.CodeText,
            CancellationToken.None);

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.EndsWith(".axaml", new Uri(reference.Uri).LocalPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => string.Equals(item.Uri, fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpReferences_ForExpressionBindingMethod_IncludeXamlUsages()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(fixture.RootPath, IncludeSemanticDiagnostics: false);

        var references = await engine.GetXamlReferencesForCSharpSymbolAsync(
            fixture.CodeUri,
            fixture.GetNameMethodPosition,
            options,
            fixture.CodeText,
            CancellationToken.None);

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.EndsWith(".axaml", new Uri(reference.Uri).LocalPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(references, item => string.Equals(item.Uri, fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpReferences_ForType_IncludeXamlTypeUsages()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(fixture.RootPath, IncludeSemanticDiagnostics: false);

        var references = await engine.GetXamlReferencesForCSharpSymbolAsync(
            fixture.CodeUri,
            fixture.ViewModelTypePosition,
            options,
            fixture.CodeText,
            CancellationToken.None);

        Assert.NotEmpty(references);
        Assert.All(references, reference => Assert.True(
            reference.Uri.EndsWith(".xaml", StringComparison.Ordinal) || reference.Uri.EndsWith(".axaml", StringComparison.Ordinal),
            "Expected XAML-only location but got: " + reference.Uri));
        Assert.Contains(references, item => string.Equals(item.Uri, fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CSharpDeclarations_ForRootViewType_ReturnXamlXClassLocation()
    {
        await using var fixture = await CreateCrossLanguageNavigationFixtureAsync();

        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(fixture.RootPath, IncludeSemanticDiagnostics: false);

        var declarations = await engine.GetXamlDeclarationsForCSharpSymbolAsync(
            fixture.CodeUri,
            fixture.MainViewTypePosition,
            options,
            fixture.CodeText,
            CancellationToken.None);

        Assert.NotEmpty(declarations);
        Assert.All(declarations, declaration => Assert.True(
            declaration.Uri.EndsWith(".xaml", StringComparison.Ordinal) || declaration.Uri.EndsWith(".axaml", StringComparison.Ordinal),
            "Expected XAML-only location but got: " + declaration.Uri));
        Assert.Contains(declarations, item => string.Equals(item.Uri, fixture.XamlUri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Definitions_And_References_Work_For_ControlCatalog_XDataType_AllTokenPositions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "viewModels:MainWindowViewModel";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:DataType token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        for (var index = 0; index < token.Length; index++)
        {
            var position = GetPosition(xamlText, tokenOffset + index);
            var definitions = await engine.GetDefinitionsAsync(uri, position, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(uri, position, options, CancellationToken.None);
            Assert.NotEmpty(definitions);
            Assert.NotEmpty(references);
        }
    }

    [Fact]
    public async Task Definitions_And_References_Work_For_ControlCatalog_XClass_AllTokenPositions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "ControlCatalog.MainView";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:Class token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        for (var index = 0; index < token.Length; index++)
        {
            var position = GetPosition(xamlText, tokenOffset + index);
            var definitions = await engine.GetDefinitionsAsync(uri, position, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(uri, position, options, CancellationToken.None);
            Assert.NotEmpty(definitions);
            Assert.NotEmpty(references);
        }
    }

    [Fact]
    public async Task Definition_And_References_ForAvaloniaPackageType_UseRichMetadataFallback_WhenSourceLinkUnavailable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        var tokenOffset = xamlText.IndexOf("<Grid", StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected Grid element not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var position = GetPosition(xamlText, tokenOffset + 1);
        var definitions = await engine.GetDefinitionsAsync(uri, position, options, CancellationToken.None);
        var references = await engine.GetReferencesAsync(uri, position, options, CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.StartsWith("axsg-metadata:///", definitions[0].Uri, StringComparison.Ordinal);
        var typeMetadataDocumentId = GetQueryParameter(definitions[0].Uri, "id");
        Assert.False(string.IsNullOrWhiteSpace(typeMetadataDocumentId));
        var typeMetadataDocument = engine.GetMetadataDocumentText(typeMetadataDocumentId!);
        Assert.NotNull(typeMetadataDocument);
        Assert.True(definitions[0].Range.Start.Line > 3);
        Assert.Contains("namespace Avalonia.Controls", typeMetadataDocument, StringComparison.Ordinal);
        Assert.Contains("public class Grid", typeMetadataDocument, StringComparison.Ordinal);
        Assert.True(typeMetadataDocument!.Split('\n').Length > 15);

        Assert.NotEmpty(references);
        var declarationReference = Assert.Single(references, item => item.IsDeclaration);
        Assert.StartsWith("axsg-metadata:///", declarationReference.Uri, StringComparison.Ordinal);
        var declarationDocumentId = GetQueryParameter(declarationReference.Uri, "id");
        Assert.False(string.IsNullOrWhiteSpace(declarationDocumentId));
        Assert.Equal(typeMetadataDocumentId, declarationDocumentId);
    }

    [Fact]
    public async Task Definition_And_References_ForAvaloniaPackageProperty_UseRichMetadataFallback_WhenSourceLinkUnavailable()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        var xamlText = await LanguageServiceTestCompilationFactory.ReadCachedTextAsync(xamlPath);
        const string token = "TextWrapping";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected TextWrapping property token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = CreateMsBuildEngine();
        var options = new XamlLanguageServiceOptions(repositoryRoot, IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xamlText, version: 1, options, CancellationToken.None);

        var position = GetPosition(xamlText, tokenOffset);
        var definitions = await engine.GetDefinitionsAsync(uri, position, options, CancellationToken.None);
        var references = await engine.GetReferencesAsync(uri, position, options, CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.StartsWith("axsg-metadata:///", definitions[0].Uri, StringComparison.Ordinal);
        var propertyMetadataDocumentId = GetQueryParameter(definitions[0].Uri, "id");
        Assert.False(string.IsNullOrWhiteSpace(propertyMetadataDocumentId));
        var propertyMetadataDocument = engine.GetMetadataDocumentText(propertyMetadataDocumentId!);
        Assert.NotNull(propertyMetadataDocument);
        Assert.True(definitions[0].Range.Start.Line > 3);
        Assert.Contains("namespace Avalonia.Controls", propertyMetadataDocument, StringComparison.Ordinal);
        Assert.Contains("public class TextBlock", propertyMetadataDocument, StringComparison.Ordinal);
        Assert.Contains("TextWrapping", propertyMetadataDocument, StringComparison.Ordinal);
        Assert.True(propertyMetadataDocument!.Split('\n').Length > 15);

        Assert.NotEmpty(references);
        var declarationReference = Assert.Single(references, item => item.IsDeclaration);
        Assert.StartsWith("axsg-metadata:///", declarationReference.Uri, StringComparison.Ordinal);
        var declarationDocumentId = GetQueryParameter(declarationReference.Uri, "id");
        Assert.False(string.IsNullOrWhiteSpace(declarationDocumentId));
        Assert.Equal(propertyMetadataDocumentId, declarationDocumentId);
    }

    [Fact]
    public async Task Definition_ForExternalAssemblyType_FallsBackToMetadataDocument()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(CreateCompilationWithExternalControls()));
        const string uri = "file:///tmp/ExternalTypeDefinitionView.axaml";
        const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

        await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
        var definitions = await engine.GetDefinitionsAsync(
            uri,
            new SourcePosition(0, 8),
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
            CancellationToken.None);

        Assert.NotEmpty(definitions);
        Assert.StartsWith("axsg-metadata:///", definitions[0].Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task References_ForExternalAssemblyType_IncludeMetadataDeclarationAndUsage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-external-refs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, "ExternalRefs.csproj");
        var xamlPath = Path.Combine(tempRoot, "ExternalTypeReferencesView.axaml");
        var uri = new Uri(xamlPath).AbsoluteUri;
        const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"ExternalTypeReferencesView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(xamlPath, xaml);

        try
        {
            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(CreateCompilationWithExternalControls()));
            var options = new XamlLanguageServiceOptions(projectPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);
            var references = await engine.GetReferencesAsync(
                uri,
                new SourcePosition(0, 8),
                options,
                CancellationToken.None);

            Assert.Contains(references, item => item.IsDeclaration &&
                                                item.Uri.StartsWith(
                                                    "axsg-metadata:///",
                                                    StringComparison.Ordinal));
            Assert.Contains(references, item => !item.IsDeclaration &&
                                                string.Equals(item.Uri, uri, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Definition_ForExternalAssemblyType_UsesSourceLink_WhenAvailable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-sourcelink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var externalDllPath = Path.Combine(tempRoot, "ExtLib.Controls.dll");
        var externalPdbPath = Path.Combine(tempRoot, "ExtLib.Controls.pdb");

        try
        {
            var externalSourcePath = "C:/src/ExtLib/ExternalButton.cs";
            const string externalSource = """
                                          namespace ExtLib.Controls
                                          {
                                              public class ExternalButton
                                              {
                                                  public string Content { get; set; } = string.Empty;

                                                  public void Click()
                                                  {
                                                      var value = 42;
                                                      _ = value;
                                                  }
                                              }
                                          }
                                          """;

            var externalSyntaxTree = CSharpSyntaxTree.ParseText(
                SourceText.From(externalSource, Encoding.UTF8),
                path: externalSourcePath);
            var coreReferences = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
            };

            var externalCompilation = CSharpCompilation.Create(
                assemblyName: "ExtLib.Controls",
                syntaxTrees: [externalSyntaxTree],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sourceLinkJson = """{"documents":{"C:/src/*":"https://raw.githubusercontent.com/example/repo/main/*"}}""";
            await using (var peStream = File.Create(externalDllPath))
            await using (var pdbStream = File.Create(externalPdbPath))
            await using (var sourceLinkStream = new MemoryStream(Encoding.UTF8.GetBytes(sourceLinkJson)))
            {
                var emitResult = externalCompilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                    sourceLinkStream: sourceLinkStream);
                Assert.True(
                    emitResult.Success,
                    "Failed to emit external source-link assembly: " +
                    string.Join(Environment.NewLine, emitResult.Diagnostics));
            }

            const string hostSource = """
                                      using System;

                                      namespace Avalonia.Metadata
                                      {
                                          [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                          public sealed class XmlnsDefinitionAttribute : Attribute
                                          {
                                              public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                          }
                                      }

                                      [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Host.Controls")]

                                      namespace Host.Controls
                                      {
                                          public class UserControl { }
                                      }
                                      """;

            var hostSyntaxTree = CSharpSyntaxTree.ParseText(hostSource, path: "/tmp/Host.Controls.cs");
            var hostCompilation = CSharpCompilation.Create(
                assemblyName: "Host.Controls",
                syntaxTrees: [hostSyntaxTree],
                references: [.. coreReferences, MetadataReference.CreateFromFile(externalDllPath)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(hostCompilation));
            const string uri = "file:///tmp/ExternalSourceLinkDefinitionView.axaml";
            const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

            await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
            var definitions = await engine.GetDefinitionsAsync(
                uri,
                new SourcePosition(0, 8),
                new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
                CancellationToken.None);

            Assert.NotEmpty(definitions);
            Assert.StartsWith("axsg-sourcelink:///", definitions[0].Uri, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Definition_ForExternalAssemblyType_UsesSourceLink_WhenCompilationReferencesRefAssembly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "axsg-ls-sourcelink-ref-lib-" + Guid.NewGuid().ToString("N"));
        var refDirectory = Path.Combine(tempRoot, "ref", "net8.0");
        var libDirectory = Path.Combine(tempRoot, "lib", "net8.0");
        Directory.CreateDirectory(refDirectory);
        Directory.CreateDirectory(libDirectory);

        var referenceAssemblyPath = Path.Combine(refDirectory, "ExtLib.Controls.dll");
        var implementationAssemblyPath = Path.Combine(libDirectory, "ExtLib.Controls.dll");
        var implementationPdbPath = Path.Combine(libDirectory, "ExtLib.Controls.pdb");

        try
        {
            var externalSourcePath = "C:/src/ExtLib/ExternalButton.cs";
            const string implementationSource = """
                                                namespace ExtLib.Controls
                                                {
                                                    public class ExternalButton
                                                    {
                                                        public string Content { get; set; } = string.Empty;
                                                    }
                                                }
                                                """;
            const string referenceSource = """
                                           namespace ExtLib.Controls
                                           {
                                               public class ExternalButton
                                               {
                                                   public string Content { get; set; }
                                               }
                                           }
                                           """;

            var coreReferences = new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
            };

            var implementationTree = CSharpSyntaxTree.ParseText(
                SourceText.From(implementationSource, Encoding.UTF8),
                path: externalSourcePath);
            var implementationCompilation = CSharpCompilation.Create(
                assemblyName: "ExtLib.Controls",
                syntaxTrees: [implementationTree],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var sourceLinkJson = """{"documents":{"C:/src/*":"https://raw.githubusercontent.com/example/repo/main/*"}}""";
            await using (var peStream = File.Create(implementationAssemblyPath))
            await using (var pdbStream = File.Create(implementationPdbPath))
            await using (var sourceLinkStream = new MemoryStream(Encoding.UTF8.GetBytes(sourceLinkJson)))
            {
                var emitResult = implementationCompilation.Emit(
                    peStream: peStream,
                    pdbStream: pdbStream,
                    options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                    sourceLinkStream: sourceLinkStream);
                Assert.True(
                    emitResult.Success,
                    "Failed to emit implementation source-link assembly: " +
                    string.Join(Environment.NewLine, emitResult.Diagnostics));
            }

            var referenceTree = CSharpSyntaxTree.ParseText(referenceSource, path: "/tmp/ExtLib.Controls.Ref.cs");
            var referenceCompilation = CSharpCompilation.Create(
                assemblyName: "ExtLib.Controls",
                syntaxTrees: [referenceTree],
                references: coreReferences,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            await using (var referenceStream = File.Create(referenceAssemblyPath))
            {
                var emitResult = referenceCompilation.Emit(referenceStream);
                Assert.True(
                    emitResult.Success,
                    "Failed to emit reference assembly: " +
                    string.Join(Environment.NewLine, emitResult.Diagnostics));
            }

            const string hostSource = """
                                      namespace Host.Controls
                                      {
                                          public class UserControl { }
                                      }
                                      """;
            var hostTree = CSharpSyntaxTree.ParseText(hostSource, path: "/tmp/Host.Controls.cs");
            var hostCompilation = CSharpCompilation.Create(
                assemblyName: "Host.Controls",
                syntaxTrees: [hostTree],
                references: [.. coreReferences, MetadataReference.CreateFromFile(referenceAssemblyPath)],
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var engine = new XamlLanguageServiceEngine(
                new InMemoryCompilationProvider(hostCompilation));
            const string uri = "file:///tmp/ExternalRefAssemblySourceLinkDefinitionView.axaml";
            const string xaml = "<ext:ExternalButton xmlns:ext=\"using:ExtLib.Controls\" />";

            await engine.OpenDocumentAsync(uri, xaml, version: 1, new XamlLanguageServiceOptions("/tmp"), CancellationToken.None);
            var definitions = await engine.GetDefinitionsAsync(
                uri,
                new SourcePosition(0, 8),
                new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false),
                CancellationToken.None);

            Assert.NotEmpty(definitions);
            Assert.StartsWith("axsg-sourcelink:///", definitions[0].Uri, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task<CrossLanguageNavigationFixture> CreateCrossLanguageNavigationFixtureAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-cross-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        const string codeText = """
                                using System;

                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]
                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:TestApp", "TestApp")]

                                namespace Avalonia.Metadata
                                {
                                    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                    public sealed class XmlnsDefinitionAttribute : Attribute
                                    {
                                        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                    }
                                }

                                namespace TestApp.Controls
                                {
                                    public class UserControl { }

                                    public class TextBlock
                                    {
                                        public string Text { get; set; } = string.Empty;
                                    }
                                }

                                namespace TestApp
                                {
                                    public partial class MainView : TestApp.Controls.UserControl { }

                                    public sealed class MainViewModel
                                    {
                                        public string Name { get; set; } = string.Empty;

                                        public string GetName() => Name;
                                    }
                                }
                                """;

        const string xamlText = """
                                <UserControl xmlns="https://github.com/avaloniaui"
                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                             xmlns:vm="using:TestApp"
                                             x:Class="TestApp.MainView"
                                             x:DataType="vm:MainViewModel">
                                  <TextBlock Text="{Binding Name}" />
                                  <TextBlock Text="{= GetName()}" />
                                </UserControl>
                                """;

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new CrossLanguageNavigationFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            GetPosition(codeText, codeText.IndexOf("Name { get;", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("GetName()", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainViewModel", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainView", StringComparison.Ordinal) + 2),
            new Uri(xamlPath).AbsoluteUri);
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var solutionPath = Path.Combine(directory, "XamlToCSharpGenerator.slnx");
            if (File.Exists(solutionPath))
            {
                return directory;
            }

            directory = Directory.GetParent(directory)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static SourcePosition GetPosition(string text, int offset)
    {
        var boundedOffset = Math.Max(0, Math.Min(offset, text.Length));
        var line = 0;
        var character = 0;
        for (var index = 0; index < boundedOffset; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                character = 0;
            }
            else
            {
                character++;
            }
        }

        return new SourcePosition(line, character);
    }

    private static string CreateTokenSignature(ImmutableArray<XamlSemanticToken> tokens)
    {
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            builder.Append(token.Line)
                .Append(':')
                .Append(token.Character)
                .Append(':')
                .Append(token.Length)
                .Append(':')
                .Append(token.TokenType)
                .Append('|');
        }

        return builder.ToString();
    }

    private static string CreateDefinitionSignature(ImmutableArray<XamlDefinitionLocation> definitions)
    {
        var builder = new StringBuilder();
        foreach (var definition in definitions)
        {
            builder.Append(definition.Uri)
                .Append('@')
                .Append(definition.Range.Start.Line)
                .Append(':')
                .Append(definition.Range.Start.Character)
                .Append('-')
                .Append(definition.Range.End.Line)
                .Append(':')
                .Append(definition.Range.End.Character)
                .Append('|');
        }

        return builder.ToString();
    }

    private static string? GetQueryParameter(string uri, string key)
    {
        var query = new Uri(uri).Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(segment.Substring(0, separatorIndex));
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            return Uri.UnescapeDataString(segment.Substring(separatorIndex + 1));
        }

        return null;
    }

    private sealed record CrossLanguageNavigationFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        SourcePosition NamePropertyPosition,
        SourcePosition GetNameMethodPosition,
        SourcePosition ViewModelTypePosition,
        SourcePosition MainViewTypePosition,
        string XamlUri) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }

    private static XamlLanguageServiceEngine CreateMsBuildEngine()
    {
        return new XamlLanguageServiceEngine(LanguageServiceTestCompilationFactory.CreateSharedMsBuildCompilationProvider());
    }

    private static Compilation CreateCompilationWithExternalControls()
    {
        return CachedExternalControlsCompilation.Value;
    }

    private static Compilation CreateCompilationWithExternalControlsCore()
    {
        const string metadataSource = """
                                      namespace ExtLib.Controls
                                      {
                                          public class ExternalButton
                                          {
                                              public string Content { get; set; } = string.Empty;
                                          }
                                      }
                                      """;

        var metadataSyntaxTree = CSharpSyntaxTree.ParseText(metadataSource, path: "/tmp/ExtLib.Controls.cs");
        var coreReferences = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };
        var metadataCompilation = CSharpCompilation.Create(
            assemblyName: "ExtLib.Controls",
            syntaxTrees: [metadataSyntaxTree],
            references: coreReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var metadataStream = new MemoryStream();
        var emitResult = metadataCompilation.Emit(metadataStream);
        if (!emitResult.Success)
        {
            throw new InvalidOperationException("Failed to emit metadata compilation for external-controls test.");
        }

        metadataStream.Position = 0;
        var metadataReference = MetadataReference.CreateFromImage(metadataStream.ToArray());

        const string hostSource = """
                                  using System;

                                  namespace Avalonia.Metadata
                                  {
                                      [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                      public sealed class XmlnsDefinitionAttribute : Attribute
                                      {
                                          public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                      }
                                  }

                                  [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "Host.Controls")]

                                  namespace Host.Controls
                                  {
                                      public class UserControl { }
                                  }
                                  """;

        var hostSyntaxTree = CSharpSyntaxTree.ParseText(hostSource, path: "/tmp/Host.Controls.cs");
        return CSharpCompilation.Create(
            assemblyName: "Host.Controls",
            syntaxTrees: [hostSyntaxTree],
            references: [.. coreReferences, metadataReference],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private sealed class CountingCompilationProvider : ICompilationProvider
    {
        private readonly ICompilationProvider _inner;
        private int _getCompilationCalls;
        private int _invalidateCalls;

        public CountingCompilationProvider(ICompilationProvider inner)
        {
            _inner = inner;
        }

        public int GetCompilationCalls => _getCompilationCalls;
        public int InvalidateCalls => _invalidateCalls;

        public async Task<CompilationSnapshot> GetCompilationAsync(
            string filePath,
            string? workspaceRoot,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _getCompilationCalls);
            return await _inner.GetCompilationAsync(filePath, workspaceRoot, cancellationToken).ConfigureAwait(false);
        }

        public void Invalidate(string filePath)
        {
            Interlocked.Increment(ref _invalidateCalls);
            _inner.Invalidate(filePath);
        }

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private static string GetRangeText(string text, SourceRange range)
    {
        var sourceText = SourceText.From(text);
        var start = sourceText.Lines.GetPosition(new LinePosition(range.Start.Line, range.Start.Character));
        var end = sourceText.Lines.GetPosition(new LinePosition(range.End.Line, range.End.Character));
        return text.Substring(start, end - start);
    }
}
