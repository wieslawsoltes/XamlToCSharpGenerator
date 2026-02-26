using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlMarkupArgumentSemanticsTests
{
    [Fact]
    public void TryParseHead_Parses_Name_And_Arguments()
    {
        var success = XamlMarkupArgumentSemantics.TryParseHead(
            "Binding Path=Name, Mode=OneWay",
            out var name,
            out var argumentsText);

        Assert.True(success);
        Assert.Equal("Binding", name);
        Assert.Equal("Path=Name, Mode=OneWay", argumentsText);
    }

    [Fact]
    public void SplitArguments_Preserves_Top_Level_Comma_Semantics()
    {
        var arguments = XamlMarkupArgumentSemantics.SplitArguments(
            "Path=Name, Converter={StaticResource Demo, Key=Value}, Mode=OneWay");

        Assert.Equal(3, arguments.Length);
        Assert.Equal("Path=Name", arguments[0]);
        Assert.Equal("Converter={StaticResource Demo, Key=Value}", arguments[1]);
        Assert.Equal("Mode=OneWay", arguments[2]);
    }

    [Theory]
    [InlineData("Path=Name", XamlMarkupNamedArgumentParseStatus.Parsed, "Path", "Name")]
    [InlineData("=Name", XamlMarkupNamedArgumentParseStatus.LeadingEquals, "", "=Name")]
    [InlineData("  =Name", XamlMarkupNamedArgumentParseStatus.LeadingEquals, "", "=Name")]
    [InlineData("Name", XamlMarkupNamedArgumentParseStatus.None, "", "")]
    public void TryParseNamedArgument_Classifies_Tokens(
        string token,
        XamlMarkupNamedArgumentParseStatus expectedStatus,
        string expectedKey,
        string expectedValue)
    {
        var status = XamlMarkupArgumentSemantics.TryParseNamedArgument(token, out var key, out var value);

        Assert.Equal(expectedStatus, status);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedValue, value);
    }
}
