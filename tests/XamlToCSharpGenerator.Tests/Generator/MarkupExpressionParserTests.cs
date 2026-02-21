using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public sealed class MarkupExpressionParserTests
{
    [Fact]
    public void Parses_Positional_And_Named_Arguments()
    {
        var parser = new MarkupExpressionParser();

        var parsed = parser.TryParseMarkupExtension(
            "{Binding Path=Name, Mode=OneWay, Converter={StaticResource NameConverter}}",
            out var markup);

        Assert.True(parsed);
        Assert.Equal("Binding", markup.Name);
        Assert.Empty(markup.PositionalArguments);
        Assert.Equal("Name", markup.NamedArguments["Path"]);
        Assert.Equal("OneWay", markup.NamedArguments["Mode"]);
        Assert.Equal("{StaticResource NameConverter}", markup.NamedArguments["Converter"]);
        Assert.Equal(3, markup.Arguments.Length);
        Assert.All(markup.Arguments, argument => Assert.True(argument.IsNamed));
    }

    [Fact]
    public void Parses_Nested_Arguments_Without_Splitting_Inner_Commas()
    {
        var parser = new MarkupExpressionParser();

        var parsed = parser.TryParseMarkupExtension(
            "{OnPlatform Default='A,B', Linux={StaticResource LinuxValue}, Windows='X,Y'}",
            out var markup);

        Assert.True(parsed);
        Assert.Equal("OnPlatform", markup.Name);
        Assert.Equal("'A,B'", markup.NamedArguments["Default"]);
        Assert.Equal("{StaticResource LinuxValue}", markup.NamedArguments["Linux"]);
        Assert.Equal("'X,Y'", markup.NamedArguments["Windows"]);
    }

    [Fact]
    public void Legacy_Invalid_Named_Fallback_Can_Be_Disabled()
    {
        var parser = new MarkupExpressionParser(
            new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: false));

        var parsed = parser.TryParseMarkupExtension("{Binding =Foo}", out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Legacy_Invalid_Named_Fallback_Defaults_To_Positional()
    {
        var parser = new MarkupExpressionParser();

        var parsed = parser.TryParseMarkupExtension("{Binding =Foo}", out var markup);

        Assert.True(parsed);
        Assert.Single(markup.PositionalArguments);
        Assert.Equal("=Foo", markup.PositionalArguments[0]);
    }
}
