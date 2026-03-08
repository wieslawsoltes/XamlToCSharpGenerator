using System.Collections.Immutable;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostSnapshotNormalizationTests
{
    [Theory]
    [InlineData("Views\\Inner\\..\\Main.axaml", "Views/Main.axaml")]
    [InlineData("..\\Views\\.\\Main.axaml", "../Views/Main.axaml")]
    [InlineData("/tmp/../Views//Main.axaml", "/Views/Main.axaml")]
    [InlineData("//server/share/Views/../Main.axaml", "//server/share/Main.axaml")]
    public void NormalizeDedupePath_Collapses_Segments_Without_Changing_Portable_Root_Semantics(
        string path,
        string expected)
    {
        var normalized = XamlSourceGeneratorCompilerHost.NormalizeDedupePath(path);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ConfigurationFileSnapshot_Deduplicates_Normalized_Path_And_Prefers_Lexicographically_Later_Path()
    {
        var inputs = ImmutableArray.Create(
            new XamlSourceGeneratorCompilerHost.ConfigurationFileInput("./xaml-sourcegen.config.json", """{ "build": { "isEnabled": false } }"""),
            new XamlSourceGeneratorCompilerHost.ConfigurationFileInput("xaml-sourcegen.config.json", """{ "build": { "isEnabled": true } }"""));

        var snapshot = XamlSourceGeneratorCompilerHost.BuildConfigurationFileSnapshot(inputs);

        Assert.Single(snapshot);
        Assert.Equal("xaml-sourcegen.config.json", snapshot[0].Path);
        Assert.Contains("\"isEnabled\": true", snapshot[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UniqueXamlInputSnapshot_Prefers_Better_TargetPath_When_Duplicate_Normalized_FilePaths_Appear_Unsorted()
    {
        var inputs = ImmutableArray.Create(
            new XamlFileInput(
                "Views/MainView.axaml",
                "/tmp/build/AvaloniaResource/Views/MainView.axaml",
                "AvaloniaXaml",
                "<UserControl />"),
            new XamlFileInput(
                "./Views/MainView.axaml",
                "Views/MainView.axaml",
                "AvaloniaXaml",
                "<UserControl x:Class=\"Demo.Views.MainView\" xmlns=\"https://github.com/avaloniaui\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />"));

        var snapshot = XamlSourceGeneratorCompilerHost.BuildUniqueXamlInputSnapshot(inputs);

        Assert.Single(snapshot);
        Assert.Equal("./Views/MainView.axaml", snapshot[0].FilePath);
        Assert.Equal("Views/MainView.axaml", snapshot[0].TargetPath);
        Assert.Contains("Demo.Views.MainView", snapshot[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void UniqueXamlInputSnapshot_Uses_FilePath_TieBreak_When_TargetPath_Preference_Is_Equal()
    {
        var inputs = ImmutableArray.Create(
            new XamlFileInput(
                "Views/MainView.axaml",
                "Views/MainView.axaml",
                "AvaloniaXaml",
                "<UserControl />"),
            new XamlFileInput(
                "./Views/MainView.axaml",
                "Views/MainView.axaml",
                "AvaloniaXaml",
                "<UserControl xmlns=\"https://github.com/avaloniaui\" />"));

        var snapshot = XamlSourceGeneratorCompilerHost.BuildUniqueXamlInputSnapshot(inputs);

        Assert.Single(snapshot);
        Assert.Equal("./Views/MainView.axaml", snapshot[0].FilePath);
        Assert.Equal("Views/MainView.axaml", snapshot[0].TargetPath);
        Assert.Contains("https://github.com/avaloniaui", snapshot[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformRuleSnapshot_Deduplicates_Normalized_Path_And_Prefers_Lexicographically_Later_Path()
    {
        var inputs = ImmutableArray.Create(
            new XamlFrameworkTransformRuleInput("./transform-rules.json", """{ "typeAliases": [] }"""),
            new XamlFrameworkTransformRuleInput("transform-rules.json", """{ "propertyAliases": [] }"""));

        var snapshot = XamlSourceGeneratorCompilerHost.BuildUniqueTransformRuleInputSnapshot(inputs);

        Assert.Single(snapshot);
        Assert.Equal("transform-rules.json", snapshot[0].FilePath);
        Assert.Contains("\"propertyAliases\"", snapshot[0].Text, StringComparison.Ordinal);
    }
}
