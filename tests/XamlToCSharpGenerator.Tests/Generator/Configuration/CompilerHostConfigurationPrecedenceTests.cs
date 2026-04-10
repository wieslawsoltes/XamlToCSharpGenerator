using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostConfigurationPrecedenceTests
{
    [Fact]
    public void ResolveConfigurationSourcePrecedence_Parses_Mixed_Delimiters_And_Key_Aliases()
    {
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        var result = XamlSourceGeneratorCompilerHost.ResolveConfigurationSourcePrecedence(
            " project-default = 80;\n File = 120,\r\n ms_build = 220; code=320 ",
            issues);

        Assert.Empty(issues);
        Assert.Equal(80, result.ProjectDefaultFile);
        Assert.Equal(120, result.File);
        Assert.Equal(220, result.MsBuild);
        Assert.Equal(320, result.Code);
    }

    [Fact]
    public void ResolveConfigurationSourcePrecedence_Invalid_Segments_Report_Warnings_And_Preserve_Valid_Values()
    {
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        var result = XamlSourceGeneratorCompilerHost.ResolveConfigurationSourcePrecedence(
            "File=NaN;Unknown=123;BrokenSegment;Code=450",
            issues);

        Assert.Equal(3, issues.Count);
        Assert.All(issues, static issue => Assert.Equal("AXSG0933", issue.Code));
        Assert.Equal(XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence.Default.ProjectDefaultFile, result.ProjectDefaultFile);
        Assert.Equal(XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence.Default.File, result.File);
        Assert.Equal(XamlSourceGeneratorCompilerHost.ConfigurationSourcePrecedence.Default.MsBuild, result.MsBuild);
        Assert.Equal(450, result.Code);
    }

    [Fact]
    public void ResolveConfigurationSourcePrecedence_Uses_Default_MsBuild_Key_When_Profile_Alias_Is_Omitted()
    {
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();
        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            [
                new KeyValuePair<string, string>(
                    "build_property.XamlSourceGenConfigurationPrecedence",
                    "ProjectDefaultFile=80;MsBuild=200;Code=300;File=400")
            ],
            []);
        var msBuildSettings = new XamlFrameworkMsBuildSettings(
            Array.Empty<KeyValuePair<XamlFrameworkMsBuildSettingKey, IEnumerable<string>>>());

        var result = XamlSourceGeneratorCompilerHost.ResolveConfigurationSourcePrecedence(
            optionsProvider.GlobalOptions,
            issues,
            msBuildSettings);

        Assert.Empty(issues);
        Assert.Equal(80, result.ProjectDefaultFile);
        Assert.Equal(400, result.File);
        Assert.Equal(200, result.MsBuild);
        Assert.Equal(300, result.Code);
    }
}
