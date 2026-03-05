using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XamlToCSharpGenerator.LanguageService;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Refactorings;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class XamlRefactoringTests
{
    [Fact]
    public async Task PrepareRename_ForBindingProperty_ReturnsExactPropertyRange()
    {
        const string uri = "file:///tmp/RenameView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" " +
                            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                            "xmlns:vm=\"using:TestApp.Controls\" x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\"/>\n" +
                            "</UserControl>";

        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var result = await engine.PrepareRenameAsync(
            uri,
            new SourcePosition(1, xaml.Split('\n')[1].IndexOf("Name", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Name", result!.Placeholder);
        Assert.Equal("Name", ReadRangeText(xaml, result.Range));
    }

    [Fact]
    public async Task Rename_FromXamlBindingProperty_ProducesCSharpAndXamlEdits()
    {
        var project = await CreateRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);
            var renameEdit = await engine.RenameAsync(
                project.XamlUri,
                project.BindingNamePosition,
                "DisplayName",
                options,
                documentTextOverride: null,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.XamlUri, renameEdit.Changes.Keys);
            Assert.Contains(project.CodeUri, renameEdit.Changes.Keys);

            var rewrittenXaml = ApplyEdits(project.XamlText, renameEdit.Changes[project.XamlUri]);
            var rewrittenCode = ApplyEdits(project.CodeText, renameEdit.Changes[project.CodeUri]);

            Assert.Contains("{Binding DisplayName}", rewrittenXaml, StringComparison.Ordinal);
            Assert.Contains("public string DisplayName", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("=> DisplayName;", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Rename_FromCSharpProperty_ProducesXamlAndCSharpEdits()
    {
        var project = await CreateRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            var renameEdit = await engine.RenameAsync(
                project.CodeUri,
                project.CodeNamePosition,
                "DisplayName",
                options,
                project.CodeText,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.XamlUri, renameEdit.Changes.Keys);
            Assert.Contains(project.CodeUri, renameEdit.Changes.Keys);

            var rewrittenXaml = ApplyEdits(project.XamlText, renameEdit.Changes[project.XamlUri]);
            var rewrittenCode = ApplyEdits(project.CodeText, renameEdit.Changes[project.CodeUri]);

            Assert.Contains("{Binding DisplayName}", rewrittenXaml, StringComparison.Ordinal);
            Assert.Contains("public string DisplayName", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Rename_FromXamlStyleClass_ProducesSelectorAndUsageEdits()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/StyleClassRenameView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"TextBlock.warning\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "  <TextBlock Classes=\"warning\"/>\n" +
                            "  <TextBlock Classes.warning=\"True\"/>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var classOffset = xaml.IndexOf("warning", xaml.IndexOf("Selector=", StringComparison.Ordinal), StringComparison.Ordinal);
        var renameEdit = await engine.RenameAsync(
            uri,
            GetPosition(xaml, classOffset + 2),
            "accent",
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(renameEdit.HasChanges);
        Assert.Contains(uri, renameEdit.Changes.Keys);

        var rewrittenXaml = ApplyEdits(xaml, renameEdit.Changes[uri]);
        Assert.Contains("TextBlock.accent", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"accent\"", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("Classes.accent=\"True\"", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_FromXamlPseudoClass_ProducesSelectorEdits()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/PseudoClassRenameView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"Button:pressed\"/>\n" +
                            "    <Style Selector=\":is(Button):pressed\"/>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var pseudoOffset = xaml.IndexOf("pressed", StringComparison.Ordinal);
        var renameEdit = await engine.RenameAsync(
            uri,
            GetPosition(xaml, pseudoOffset + 2),
            "down",
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(renameEdit.HasChanges);
        Assert.Contains(uri, renameEdit.Changes.Keys);

        var rewrittenXaml = ApplyEdits(xaml, renameEdit.Changes[uri]);
        Assert.Contains("Button:down", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains(":is(Button):down", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_FromXamlResourceKey_ProducesProjectWideXamlEdits()
    {
        var project = await CreateResourceRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.DefinitionUri, project.DefinitionXaml, version: 1, options, CancellationToken.None);
            await engine.OpenDocumentAsync(project.UsageUri, project.UsageXaml, version: 1, options, CancellationToken.None);

            var renameEdit = await engine.RenameAsync(
                project.DefinitionUri,
                project.ResourceKeyPosition,
                "AccentBrush2",
                options,
                documentTextOverride: null,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.DefinitionUri, renameEdit.Changes.Keys);
            Assert.Contains(project.UsageUri, renameEdit.Changes.Keys);

            var rewrittenDefinition = ApplyEdits(project.DefinitionXaml, renameEdit.Changes[project.DefinitionUri]);
            var rewrittenUsage = ApplyEdits(project.UsageXaml, renameEdit.Changes[project.UsageUri]);

            Assert.Contains("x:Key=\"AccentBrush2\"", rewrittenDefinition, StringComparison.Ordinal);
            Assert.Contains("{DynamicResource AccentBrush2}", rewrittenUsage, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task Rename_FromXamlNameDeclaration_ProducesDeclarationAndReferenceEdits()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/NameRenameView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <TextBlock Name=\"target\" />\n" +
                            "  <TextBlock Text=\"{Binding ElementName=target, Path=Text}\" />\n" +
                            "  <TextBlock Tag=\"{x:Reference target}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var renameEdit = await engine.RenameAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("target", xaml.IndexOf("Name=", StringComparison.Ordinal), StringComparison.Ordinal) + 2),
            "renamedTarget",
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(renameEdit.HasChanges);
        Assert.Contains(uri, renameEdit.Changes.Keys);

        var rewrittenXaml = ApplyEdits(xaml, renameEdit.Changes[uri]);
        Assert.Contains("Name=\"renamedTarget\"", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("ElementName=renamedTarget", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("x:Reference renamedTarget", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rename_FromXamlXNameDeclaration_ProducesDeclarationAndReferenceEdits()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/XNameRenameView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <TextBlock x:Name=\"target\" />\n" +
                            "  <TextBlock Text=\"{Binding ElementName=target, Path=Text}\" />\n" +
                            "  <TextBlock Tag=\"{x:Reference target}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var renameEdit = await engine.RenameAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("target", xaml.IndexOf("x:Name=", StringComparison.Ordinal), StringComparison.Ordinal) + 2),
            "renamedTarget",
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(renameEdit.HasChanges);
        Assert.Contains(uri, renameEdit.Changes.Keys);

        var rewrittenXaml = ApplyEdits(xaml, renameEdit.Changes[uri]);
        Assert.Contains("x:Name=\"renamedTarget\"", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("ElementName=renamedTarget", rewrittenXaml, StringComparison.Ordinal);
        Assert.Contains("x:Reference renamedTarget", rewrittenXaml, StringComparison.Ordinal);
    }

    private static async Task<RenameProjectFixture> CreateRenameProjectAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "TestApp.cs");
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
                                    public class MainViewModel
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
                                             x:DataType="vm:MainViewModel">
                                  <TextBlock Text="{Binding Name}" />
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

        return new RenameProjectFixture(
            rootPath,
            projectPath,
            codePath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            GetPosition(codeText, codeText.IndexOf("Name { get;", StringComparison.Ordinal) + 2),
            xamlPath,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("Name", StringComparison.Ordinal) + 2));
    }

    private static async Task<ResourceRenameProjectFixture> CreateResourceRenameProjectAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-resource-rename-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "TestApp.cs");
        var definitionPath = Path.Combine(rootPath, "ResourceDictionary.axaml");
        var usagePath = Path.Combine(rootPath, "MainView.axaml");

        const string codeText = """
                                using System;

                                [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]

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
                                        public object? Tag { get; set; }
                                    }
                                }
                                """;

        const string definitionXaml = """
                                     <UserControl xmlns="https://github.com/avaloniaui"
                                                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                       <UserControl.Resources>
                                         <TextBlock x:Key="AccentBrush" />
                                       </UserControl.Resources>
                                     </UserControl>
                                     """;

        const string usageXaml = """
                                <UserControl xmlns="https://github.com/avaloniaui"
                                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                                  <TextBlock Tag="{DynamicResource AccentBrush}" />
                                </UserControl>
                                """;

        await File.WriteAllTextAsync(projectPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"ResourceDictionary.axaml\" />\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>");
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(definitionPath, definitionXaml);
        await File.WriteAllTextAsync(usagePath, usageXaml);

        return new ResourceRenameProjectFixture(
            rootPath,
            new Uri(definitionPath).AbsoluteUri,
            definitionXaml,
            GetPosition(definitionXaml, definitionXaml.IndexOf("AccentBrush", StringComparison.Ordinal) + 2),
            new Uri(usagePath).AbsoluteUri,
            usageXaml);
    }

    private static SourcePosition GetPosition(string text, int offset)
    {
        var line = 0;
        var character = 0;
        for (var index = 0; index < offset && index < text.Length; index++)
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

    private static string ReadRangeText(string text, SourceRange range)
    {
        var startOffset = GetOffset(text, range.Start);
        var endOffset = GetOffset(text, range.End);
        return text.Substring(startOffset, endOffset - startOffset);
    }

    private static int GetOffset(string text, SourcePosition position)
    {
        var offset = 0;
        var line = 0;
        while (offset < text.Length && line < position.Line)
        {
            if (text[offset++] == '\n')
            {
                line++;
            }
        }

        return Math.Min(text.Length, offset + position.Character);
    }

    private static string ApplyEdits(string originalText, IReadOnlyList<XamlDocumentTextEdit> edits)
    {
        var text = originalText;
        foreach (var edit in edits.OrderByDescending(static item => item.Range.Start.Line)
                                  .ThenByDescending(static item => item.Range.Start.Character))
        {
            var startOffset = GetOffset(text, edit.Range.Start);
            var endOffset = GetOffset(text, edit.Range.End);
            text = text.Substring(0, startOffset) + edit.NewText + text.Substring(endOffset);
        }

        return text;
    }

    private sealed record RenameProjectFixture(
        string RootPath,
        string ProjectPath,
        string CodePath,
        string CodeUri,
        string CodeText,
        SourcePosition CodeNamePosition,
        string XamlPath,
        string XamlUri,
        string XamlText,
        SourcePosition BindingNamePosition);

    private sealed record ResourceRenameProjectFixture(
        string RootPath,
        string DefinitionUri,
        string DefinitionXaml,
        SourcePosition ResourceKeyPosition,
        string UsageUri,
        string UsageXaml);
}
