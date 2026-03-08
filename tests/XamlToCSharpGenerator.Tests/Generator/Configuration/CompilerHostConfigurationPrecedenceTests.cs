using System.Collections.Immutable;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Configuration;

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
}
