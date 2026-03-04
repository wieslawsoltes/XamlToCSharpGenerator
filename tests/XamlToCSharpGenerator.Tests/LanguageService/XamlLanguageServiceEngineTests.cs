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
    public async Task Diagnostics_AfterLightweightRequest_UseDedicatedAnalysisProfile()
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

        // open/diagnostics analysis profiles are cached independently.
        Assert.Equal(2, countingProvider.GetCompilationCalls);
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

        static string CreateTokenSignature(ImmutableArray<XamlSemanticToken> tokens)
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

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var solutionPath = Path.Combine(directory, "XamlToCSharpGenerator.sln");
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
