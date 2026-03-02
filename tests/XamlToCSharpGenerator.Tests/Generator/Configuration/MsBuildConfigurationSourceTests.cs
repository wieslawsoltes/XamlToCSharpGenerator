using System.Collections.Generic;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Configuration.Sources;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class MsBuildConfigurationSourceTests
{
    [Fact]
    public void Load_Maps_Known_Build_And_Parser_Keys()
    {
        var options = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.XamlSourceGenBackend"] = "SourceGen",
            ["build_property.XamlSourceGenEnabled"] = "false",
            ["build_property.AvaloniaSourceGenStrictMode"] = "true",
            ["build_property.AvaloniaSourceGenCSharpExpressionsEnabled"] = "false",
            ["build_property.AvaloniaSourceGenGlobalXmlnsPrefixes"] = "vm=using:Demo.ViewModels;conv=using:Demo.Converters"
        });

        var source = new MsBuildConfigurationSource(options);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        Assert.True(result.Patch.Build.IsEnabled.HasValue);
        Assert.True(result.Patch.Build.IsEnabled.Value);
        Assert.True(result.Patch.Build.StrictMode.HasValue);
        Assert.True(result.Patch.Build.StrictMode.Value);
        Assert.Equal("SourceGen", result.Patch.Build.Backend.Value);
        Assert.True(result.Patch.Binding.CSharpExpressionsEnabled.HasValue);
        Assert.False(result.Patch.Binding.CSharpExpressionsEnabled.Value);
        Assert.Equal("using:Demo.ViewModels", result.Patch.Parser.GlobalXmlnsPrefixes["vm"]);
        Assert.Equal("using:Demo.Converters", result.Patch.Parser.GlobalXmlnsPrefixes["conv"]);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Load_Reports_Invalid_Boolean_Values()
    {
        var options = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.AvaloniaSourceGenStrictMode"] = "invalid"
        });

        var source = new MsBuildConfigurationSource(options);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("AXSG0911", issue.Code);
        Assert.Equal(XamlSourceGenConfigurationIssueSeverity.Warning, issue.Severity);
    }

    [Fact]
    public void Load_Ignores_Empty_Boolean_Values()
    {
        var options = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.DotNetWatchBuild"] = "",
            ["build_property.BuildingInsideVisualStudio"] = "   ",
            ["build_property.BuildingByReSharper"] = ""
        });

        var source = new MsBuildConfigurationSource(options);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        Assert.False(result.Patch.Build.DotNetWatchBuild.HasValue);
        Assert.False(result.Patch.Build.BuildingInsideVisualStudio.HasValue);
        Assert.False(result.Patch.Build.BuildingByReSharper.HasValue);
        Assert.Empty(result.Issues);
    }
}
