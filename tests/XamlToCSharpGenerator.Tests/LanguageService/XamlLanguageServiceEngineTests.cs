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
using XamlToCSharpGenerator.LanguageService.InlayHints;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

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

        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "pages:CompositionPage";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "pages:".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        var originalText = await File.ReadAllTextAsync(xamlPath);
        const string existingExpression = "<TextBlock Text=\"{= FirstName + ' ' + LastName}\" />";
        const string emptyExpression = "<TextBlock Text=\"{= }\" />";
        Assert.Contains(existingExpression, originalText, StringComparison.Ordinal);
        var xamlText = originalText.Replace(existingExpression, emptyExpression, StringComparison.Ordinal);

        var caretOffset = xamlText.IndexOf(emptyExpression, StringComparison.Ordinal) + "<TextBlock Text=\"{= ".Length;
        Assert.True(caretOffset >= 0, "Expected empty expression insertion point not found.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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
    public async Task Definitions_And_References_Work_For_ControlCatalog_XDataType_Type()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(repositoryRoot, "samples", "ControlCatalog", "MainView.xaml");
        Assert.True(File.Exists(xamlPath), "Expected sample file not found: " + xamlPath);

        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "viewModels:MainWindowViewModel";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:DataType token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "viewModels:".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "ControlCatalog.MainView";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:Class token not found in sample XAML.");

        var tokenPosition = GetPosition(xamlText, tokenOffset + "ControlCatalog.".Length + 2);
        var uri = new Uri(xamlPath).AbsoluteUri;

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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
        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "viewModels:MainWindowViewModel";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:DataType token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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
        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "ControlCatalog.MainView";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected x:Class token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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
        var xamlText = await File.ReadAllTextAsync(xamlPath);
        var tokenOffset = xamlText.IndexOf("<Grid", StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected Grid element not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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
        var xamlText = await File.ReadAllTextAsync(xamlPath);
        const string token = "TextWrapping";
        var tokenOffset = xamlText.IndexOf(token, StringComparison.Ordinal);
        Assert.True(tokenOffset >= 0, "Expected TextWrapping property token not found in sample XAML.");

        var uri = new Uri(xamlPath).AbsoluteUri;
        using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
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

    private static Compilation CreateCompilationWithExternalControls()
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
}
