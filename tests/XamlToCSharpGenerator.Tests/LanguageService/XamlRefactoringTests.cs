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
    public async Task CSharpRenamePropagation_FromProperty_ProducesOnlyXamlEdits()
    {
        var project = await CreateRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            var renameEdit = await engine.GetCSharpRenamePropagationEditsAsync(
                project.CodeUri,
                project.CodeNamePosition,
                "DisplayName",
                options,
                project.CodeText,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.XamlUri, renameEdit.Changes.Keys);
            Assert.DoesNotContain(project.CodeUri, renameEdit.Changes.Keys);

            var rewrittenXaml = ApplyEdits(project.XamlText, renameEdit.Changes[project.XamlUri]);
            Assert.Contains("{Binding DisplayName}", rewrittenXaml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CSharpRenamePropagation_FromMethod_ProducesExpressionBindingEdits()
    {
        var project = await CreateRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            var renameEdit = await engine.GetCSharpRenamePropagationEditsAsync(
                project.CodeUri,
                project.CodeMethodPosition,
                "BuildName",
                options,
                project.CodeText,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.XamlUri, renameEdit.Changes.Keys);
            Assert.DoesNotContain(project.CodeUri, renameEdit.Changes.Keys);

            var rewrittenXaml = ApplyEdits(project.XamlText, renameEdit.Changes[project.XamlUri]);
            Assert.Contains("{= BuildName()}", rewrittenXaml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CSharpRenamePropagation_FromType_ProducesXDataTypeEdits()
    {
        var project = await CreateRenameProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            var renameEdit = await engine.GetCSharpRenamePropagationEditsAsync(
                project.CodeUri,
                project.CodeTypePosition,
                "ShellViewModel",
                options,
                project.CodeText,
                CancellationToken.None);

            Assert.True(renameEdit.HasChanges);
            Assert.Contains(project.XamlUri, renameEdit.Changes.Keys);
            Assert.DoesNotContain(project.CodeUri, renameEdit.Changes.Keys);

            var rewrittenXaml = ApplyEdits(project.XamlText, renameEdit.Changes[project.XamlUri]);
            Assert.Contains("x:DataType=\"vm:ShellViewModel\"", rewrittenXaml, StringComparison.Ordinal);
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
    public async Task PrepareRename_ForQualifiedPropertyElementOwner_ReturnsExactTypeRange()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/QualifiedPropertyElementOwnerRename.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Path>\n" +
                            "    <Path.Opacity>0.5</Path.Opacity>\n" +
                            "  </Path>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var result = await engine.PrepareRenameAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Path.Opacity", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Path", result!.Placeholder);
        Assert.Equal("Path", ReadRangeText(xaml, result.Range));
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

    [Fact]
    public async Task CodeActions_ForBindingWithAmbientDataType_IncludeConvertToCompiledBinding()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                            "             xmlns:vm=\"using:TestApp.Controls\"\n" +
                            "             x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Binding", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Convert to CompiledBinding"));
        Assert.Equal("refactor.rewrite", action.Kind);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("{CompiledBinding Name}", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeActions_ForCompiledBinding_IncludeConvertToBinding()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/CompiledBindingConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                            "             xmlns:vm=\"using:TestApp.Controls\"\n" +
                            "             x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding Name}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("CompiledBinding", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Convert to Binding"));
        Assert.Equal("refactor.rewrite", action.Kind);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("{Binding Name}", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeActions_ForCompiledBindingWithoutDataType_IncludeQuickFixToBinding()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/CompiledBindingWithoutDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding Name}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("CompiledBinding", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Fix missing x:DataType by converting to Binding"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("{Binding Name}", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeActions_ForCompiledBindingWithInvalidPath_IncludeQuickFixToBinding()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/CompiledBindingInvalidPathView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
                            "             xmlns:vm=\"using:TestApp.Controls\"\n" +
                            "             x:DataType=\"vm:MainWindowViewModel\">\n" +
                            "  <TextBlock Text=\"{CompiledBinding MissingName}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("CompiledBinding", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Fix invalid compiled binding path by converting to Binding"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("{Binding MissingName}", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenDocument_ForXClassWithoutPartial_ReturnsDiagnostic()
    {
        var project = await CreateXClassPartialProjectAsync(isPartial: false);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: true);

            var diagnostics = await engine.OpenDocumentAsync(
                project.XamlUri,
                project.XamlText,
                version: 1,
                options,
                CancellationToken.None);

            Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "AXSG0109");
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForXClassWithoutPartial_IncludeQuickFixToAddPartial()
    {
        var project = await CreateXClassPartialProjectAsync(isPartial: false);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.XamlClassPosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Fix x:Class companion type by adding partial"));
            Assert.Equal("quickfix", action.Kind);
            Assert.True(action.IsPreferred);
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.CodeUri, out var edits));

            var rewrittenCode = ApplyEdits(project.CodeText, edits);
            Assert.Contains("public partial class MainView", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForXClassWithPartial_DoNotIncludeQuickFix()
    {
        var project = await CreateXClassPartialProjectAsync(isPartial: true);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.XamlClassPosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            Assert.DoesNotContain(actions, static item => item.Title == "AXSG: Fix x:Class companion type by adding partial");
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task OpenDocument_ForInvalidXClassModifier_ReturnsDiagnostic()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InvalidXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"friend\" />";

        var diagnostics = await engine.OpenDocumentAsync(
            uri,
            xaml,
            version: 1,
            new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: true),
            CancellationToken.None);

        Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == "AXSG0104");
    }

    [Fact]
    public async Task CodeActions_ForInvalidXClassModifier_IncludeQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/InvalidXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"friend\" />";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            new SourcePosition(0, 0),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Set x:ClassModifier to public"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("x:ClassModifier=\"public\"", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeActions_ForMismatchedXClassModifier_IncludeQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MismatchedXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"internal\" />";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            new SourcePosition(0, 0),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Set x:ClassModifier to public"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        Assert.Contains("x:ClassModifier=\"public\"", rewrittenXaml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CodeActions_ForMatchingXClassModifier_DoNotIncludeQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/MatchingXClassModifier.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:Class=\"TestApp.Controls.MainView\" x:ClassModifier=\"public\" />";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            new SourcePosition(0, 0),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.DoesNotContain(actions, static item => item.Title.StartsWith("AXSG: Set x:ClassModifier to ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CodeActions_ForBindingWithoutAmbientDataType_DoNotIncludeConvertToCompiledBinding()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/BindingWithoutDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <TextBlock Text=\"{Binding Name}\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Binding", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.DoesNotContain(actions, static item => item.Title == "AXSG: Convert to CompiledBinding");
    }

    [Fact]
    public async Task CodeActions_ForPropertyAttribute_IncludeConvertToPropertyElement()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/PropertyElementConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <TextBlock Text=\"Hello\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Text=", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Convert attribute to property element"));
        Assert.Equal("refactor.rewrite", action.Kind);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                                "  <TextBlock>\n" +
                                "    <TextBlock.Text>Hello</TextBlock.Text>\n" +
                                "  </TextBlock>\n" +
                                "</UserControl>";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForDirectiveAttribute_DoNotIncludeConvertToPropertyElement()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/DirectiveAttributeConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
                            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
                            "  <TextBlock x:Name=\"Title\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("x:Name", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.DoesNotContain(actions, static item => item.Title == "AXSG: Convert attribute to property element");
    }

    [Fact]
    public async Task CodeActions_ForEventAttribute_DoNotIncludeConvertToPropertyElement()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateCompilation()));
        const string uri = "file:///tmp/EventAttributeConvertView.axaml";
        const string xaml = "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
                            "  <Button Click=\"OnClick\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("Click=", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.DoesNotContain(actions, static item => item.Title == "AXSG: Convert attribute to property element");
    }

    [Fact]
    public async Task CodeActions_ForUnresolvedElementType_IncludeNamespaceImportQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        const string uri = "file:///tmp/NamespaceImportView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <ThemeDoodad />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(
            actions.Any(),
            "Expected at least one code action, but none were returned. Titles: " +
            string.Join(", ", actions.Select(static item => item.Title)));
        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Import namespace for local:ThemeDoodad"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<UserControl xmlns=\"using:Host.Controls\" xmlns:local=\"using:Demo.Controls\">\n" +
                                "  <local:ThemeDoodad />\n" +
                                "</UserControl>";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForUnresolvedAttachedPropertyOwner_IncludeNamespaceImportQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        const string uri = "file:///tmp/NamespaceImportAttachedPropertyView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <UserControl ThemeDoodad.Accent=\"True\" />\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ThemeDoodad.Accent", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(
            actions.Any(),
            "Expected at least one code action, but none were returned. Titles: " +
            string.Join(", ", actions.Select(static item => item.Title)));
        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Import namespace for local:ThemeDoodad"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<UserControl xmlns=\"using:Host.Controls\" xmlns:local=\"using:Demo.Controls\">\n" +
                                "  <UserControl local:ThemeDoodad.Accent=\"True\" />\n" +
                                "</UserControl>";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForUnresolvedSetterPropertyOwner_IncludeNamespaceImportQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        const string uri = "file:///tmp/NamespaceImportSetterPropertyView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\">\n" +
                            "  <UserControl.Styles>\n" +
                            "    <Style Selector=\"UserControl\">\n" +
                            "      <Setter Property=\"ThemeDoodad.Accent\" Value=\"True\" />\n" +
                            "    </Style>\n" +
                            "  </UserControl.Styles>\n" +
                            "</UserControl>";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ThemeDoodad.Accent", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(
            actions.Any(),
            "Expected at least one code action, but none were returned. Titles: " +
            string.Join(", ", actions.Select(static item => item.Title)));
        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Import namespace for local:ThemeDoodad"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<UserControl xmlns=\"using:Host.Controls\" xmlns:local=\"using:Demo.Controls\">\n" +
                                "  <UserControl.Styles>\n" +
                                "    <Style Selector=\"UserControl\">\n" +
                                "      <Setter Property=\"local:ThemeDoodad.Accent\" Value=\"True\" />\n" +
                                "    </Style>\n" +
                                "  </UserControl.Styles>\n" +
                                "</UserControl>";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForUnresolvedXDataTypeValue_IncludeNamespaceImportQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        const string uri = "file:///tmp/NamespaceImportXDataTypeView.axaml";
        const string xaml = "<UserControl xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:DataType=\"ThemeDoodad\" />";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(
            actions.Any(),
            "Expected at least one code action, but none were returned. Titles: " +
            string.Join(", ", actions.Select(static item => item.Title)));
        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Import namespace for local:ThemeDoodad"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<UserControl xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" x:DataType=\"local:ThemeDoodad\" xmlns:local=\"using:Demo.Controls\" />";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForUnresolvedControlThemeTargetType_IncludeNamespaceImportQuickFix()
    {
        using var engine = new XamlLanguageServiceEngine(
            new InMemoryCompilationProvider(LanguageServiceTestCompilationFactory.CreateNamespaceImportCompilation()));
        const string uri = "file:///tmp/NamespaceImportControlThemeTargetType.axaml";
        const string xaml = "<ControlTheme xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ThemeDoodad}\" />";

        var options = new XamlLanguageServiceOptions("/tmp", IncludeSemanticDiagnostics: false);
        await engine.OpenDocumentAsync(uri, xaml, version: 1, options, CancellationToken.None);

        var actions = await engine.GetCodeActionsAsync(
            uri,
            GetPosition(xaml, xaml.IndexOf("ThemeDoodad", StringComparison.Ordinal) + 2),
            options,
            documentTextOverride: null,
            CancellationToken.None);

        Assert.True(
            actions.Any(),
            "Expected at least one code action, but none were returned. Titles: " +
            string.Join(", ", actions.Select(static item => item.Title)));
        var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Import namespace for local:ThemeDoodad"));
        Assert.Equal("quickfix", action.Kind);
        Assert.True(action.IsPreferred);
        Assert.NotNull(action.Edit);
        Assert.True(action.Edit!.Changes.TryGetValue(uri, out var edits));

        var rewrittenXaml = ApplyEdits(xaml, edits);
        const string expected = "<ControlTheme xmlns=\"using:Host.Controls\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type local:ThemeDoodad}\" xmlns:local=\"using:Demo.Controls\" />";
        Assert.Equal(expected, rewrittenXaml);
    }

    [Fact]
    public async Task CodeActions_ForMissingEventHandler_IncludeQuickFixToAddHandlerStub()
    {
        var project = await CreateEventHandlerProjectAsync(withIncompatibleHandler: false);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.EventHandlerPosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Add event handler 'OnClick'"));
            Assert.Equal("quickfix", action.Kind);
            Assert.True(action.IsPreferred);
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.CodeUri, out var edits));

            var rewrittenCode = ApplyEdits(project.CodeText, edits);
            Assert.Contains("private void OnClick(", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("global::System.EventArgs e", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForIncompatibleEventHandler_IncludeQuickFixToAddCompatibleOverload()
    {
        var project = await CreateEventHandlerProjectAsync(withIncompatibleHandler: true);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.EventHandlerPosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Add compatible event handler overload 'OnClick'"));
            Assert.Equal("quickfix", action.Kind);
            Assert.True(action.IsPreferred);
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.CodeUri, out var edits));

            var rewrittenCode = ApplyEdits(project.CodeText, edits);
            Assert.Contains("private void OnClick()", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("private void OnClick(", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("global::System.EventArgs e", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForEventHandlerWithOutParameter_IncludeThrowingStub()
    {
        var project = await CreateEventHandlerProjectAsync(withIncompatibleHandler: false, useOutParameterEvent: true);
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.EventHandlerPosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Add event handler 'OnClick'"));
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.CodeUri, out var edits));

            var rewrittenCode = ApplyEdits(project.CodeText, edits);
            Assert.Contains("private void OnClick(", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("out int size", rewrittenCode, StringComparison.Ordinal);
            Assert.Contains("throw new global::System.NotImplementedException();", rewrittenCode, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForMissingIncludeTargetFromProject_IncludeQuickFixToAddAvaloniaXamlItem()
    {
        var project = await CreateMissingIncludeProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.IncludePosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Add included XAML file to project"));
            Assert.Equal("quickfix", action.Kind);
            Assert.True(action.IsPreferred);
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.ProjectUri, out var edits));

            var rewrittenProject = ApplyEdits(project.ProjectText, edits);
            Assert.Contains("<AvaloniaXaml Include=\"Themes/Shared.axaml\" />", rewrittenProject, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
    }

    [Fact]
    public async Task CodeActions_ForInvalidInclude_IncludeQuickFixToRemoveElement()
    {
        var project = await CreateInvalidIncludeProjectAsync();
        try
        {
            using var engine = new XamlLanguageServiceEngine(new MsBuildCompilationProvider());
            var options = new XamlLanguageServiceOptions(project.RootPath, IncludeSemanticDiagnostics: false);

            await engine.OpenDocumentAsync(project.XamlUri, project.XamlText, version: 1, options, CancellationToken.None);

            var actions = await engine.GetCodeActionsAsync(
                project.XamlUri,
                project.IncludePosition,
                options,
                documentTextOverride: null,
                CancellationToken.None);

            var action = Assert.Single(actions.Where(static item => item.Title == "AXSG: Remove invalid include"));
            Assert.Equal("quickfix", action.Kind);
            Assert.NotNull(action.Edit);
            Assert.True(action.Edit!.Changes.TryGetValue(project.XamlUri, out var edits));

            var rewrittenXaml = ApplyEdits(project.XamlText, edits);
            Assert.DoesNotContain("<StyleInclude", rewrittenXaml, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(project.RootPath, recursive: true);
        }
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

        return new RenameProjectFixture(
            rootPath,
            projectPath,
            codePath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            GetPosition(codeText, codeText.IndexOf("Name { get;", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("GetName()", StringComparison.Ordinal) + 2),
            GetPosition(codeText, codeText.IndexOf("MainViewModel", StringComparison.Ordinal) + 2),
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

    private static async Task<XClassPartialProjectFixture> CreateXClassPartialProjectAsync(bool isPartial)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-xclass-partial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        string classDeclaration = isPartial
            ? "    public partial class MainView : TestApp.Controls.UserControl { }\n"
            : "    public class MainView : TestApp.Controls.UserControl { }\n";

        string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"using:TestApp\", \"TestApp\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "}\n\n" +
            "namespace TestApp\n" +
            "{\n" +
            classDeclaration +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             x:Class=\"TestApp.MainView\" />\n";

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

        return new XClassPartialProjectFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("MainView", StringComparison.Ordinal) + 2));
    }

    private static async Task<EventHandlerProjectFixture> CreateEventHandlerProjectAsync(
        bool withIncompatibleHandler,
        bool useOutParameterEvent = false)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-event-handler-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "MainView.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        string handlerDeclaration = withIncompatibleHandler
            ? "\n    private void OnClick() { }\n"
            : "\n";

        string delegateDeclaration = useOutParameterEvent
            ? "    public delegate void ClickHandler(object? sender, out int size);\n"
            : string.Empty;

        string eventDeclaration = useOutParameterEvent
            ? "        public event ClickHandler? Click;\n"
            : "        public event EventHandler? Click;\n";

        string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"using:TestApp\", \"TestApp\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n\n" +
            "    public class Button\n" +
            "    {\n" +
            delegateDeclaration +
            eventDeclaration +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp\n" +
            "{\n" +
            "    public partial class MainView : TestApp.Controls.UserControl\n" +
            "    {" +
            handlerDeclaration +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"\n" +
            "             x:Class=\"TestApp.MainView\">\n" +
            "  <Button Click=\"OnClick\" />\n" +
            "</UserControl>\n";

        const string projectText =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>";

        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new EventHandlerProjectFixture(
            rootPath,
            new Uri(codePath).AbsoluteUri,
            codeText,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("Click", StringComparison.Ordinal) + 2));
    }

    private static async Task<MissingIncludeProjectFixture> CreateMissingIncludeProjectAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-missing-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(Path.Combine(rootPath, "Themes"));

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "Types.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");
        var includedPath = Path.Combine(rootPath, "Themes", "Shared.axaml");

        const string projectText =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>";

        const string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "    public class Styles { }\n" +
            "    public class StyleInclude\n" +
            "    {\n" +
            "        public string Source { get; set; } = string.Empty;\n" +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <UserControl.Styles>\n" +
            "    <StyleInclude Source=\"/Themes/Shared.axaml\" />\n" +
            "  </UserControl.Styles>\n" +
            "</UserControl>\n";

        const string includedText =
            "<Styles xmlns=\"https://github.com/avaloniaui\" />\n";

        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);
        await File.WriteAllTextAsync(includedPath, includedText);

        return new MissingIncludeProjectFixture(
            rootPath,
            new Uri(projectPath).AbsoluteUri,
            projectText,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("StyleInclude", StringComparison.Ordinal) + 2));
    }

    private static async Task<InvalidIncludeProjectFixture> CreateInvalidIncludeProjectAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "axsg-ls-invalid-include-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var projectPath = Path.Combine(rootPath, "TestApp.csproj");
        var codePath = Path.Combine(rootPath, "Types.cs");
        var xamlPath = Path.Combine(rootPath, "MainView.axaml");

        const string projectText =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
            "  <PropertyGroup>\n" +
            "    <TargetFramework>net10.0</TargetFramework>\n" +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            "    <AvaloniaXaml Include=\"MainView.axaml\" />\n" +
            "  </ItemGroup>\n" +
            "</Project>";

        const string codeText =
            "using System;\n\n" +
            "[assembly: Avalonia.Metadata.XmlnsDefinitionAttribute(\"https://github.com/avaloniaui\", \"TestApp.Controls\")]\n\n" +
            "namespace Avalonia.Metadata\n" +
            "{\n" +
            "    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]\n" +
            "    public sealed class XmlnsDefinitionAttribute : Attribute\n" +
            "    {\n" +
            "        public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }\n" +
            "    }\n" +
            "}\n\n" +
            "namespace TestApp.Controls\n" +
            "{\n" +
            "    public class UserControl { }\n" +
            "    public class Styles { }\n" +
            "    public class StyleInclude\n" +
            "    {\n" +
            "        public string Source { get; set; } = string.Empty;\n" +
            "    }\n" +
            "}\n";

        const string xamlText =
            "<UserControl xmlns=\"https://github.com/avaloniaui\">\n" +
            "  <UserControl.Styles>\n" +
            "    <StyleInclude />\n" +
            "  </UserControl.Styles>\n" +
            "</UserControl>\n";

        await File.WriteAllTextAsync(projectPath, projectText);
        await File.WriteAllTextAsync(codePath, codeText);
        await File.WriteAllTextAsync(xamlPath, xamlText);

        return new InvalidIncludeProjectFixture(
            rootPath,
            new Uri(xamlPath).AbsoluteUri,
            xamlText,
            GetPosition(xamlText, xamlText.IndexOf("StyleInclude", StringComparison.Ordinal) + 2));
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
        SourcePosition CodeMethodPosition,
        SourcePosition CodeTypePosition,
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

    private sealed record XClassPartialProjectFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        string XamlUri,
        string XamlText,
        SourcePosition XamlClassPosition);

    private sealed record EventHandlerProjectFixture(
        string RootPath,
        string CodeUri,
        string CodeText,
        string XamlUri,
        string XamlText,
        SourcePosition EventHandlerPosition);

    private sealed record MissingIncludeProjectFixture(
        string RootPath,
        string ProjectUri,
        string ProjectText,
        string XamlUri,
        string XamlText,
        SourcePosition IncludePosition);

    private sealed record InvalidIncludeProjectFixture(
        string RootPath,
        string XamlUri,
        string XamlText,
        SourcePosition IncludePosition);
}
