using System;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlDelimitedValueSemanticsTests
{
    [Fact]
    public void SplitEnumFlagTokens_Splits_Comma_And_Pipe_Separators()
    {
        var tokens = XamlDelimitedValueSemantics.SplitEnumFlagTokens("One, Two | Three");

        Assert.Equal(3, tokens.Length);
        Assert.Equal("One", tokens[0]);
        Assert.Equal("Two", tokens[1]);
        Assert.Equal("Three", tokens[2]);
    }

    [Fact]
    public void SplitEnumFlagTokens_Does_Not_Split_Inside_Markup_Envelope()
    {
        var tokens = XamlDelimitedValueSemantics.SplitEnumFlagTokens(
            "One, {x:Static local:Flags.Two|local:Flags.Three}");

        Assert.Equal(2, tokens.Length);
        Assert.Equal("One", tokens[0]);
        Assert.Equal("{x:Static local:Flags.Two|local:Flags.Three}", tokens[1]);
    }

    [Fact]
    public void SplitCollectionItems_Uses_Top_Level_Comma_Semantics()
    {
        var tokens = XamlDelimitedValueSemantics.SplitCollectionItems(
            "One, {x:Type vm:Pair(x:String, x:Int32)}, Three",
            [","],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(3, tokens.Length);
        Assert.Equal("One", tokens[0]);
        Assert.Equal("{x:Type vm:Pair(x:String, x:Int32)}", tokens[1]);
        Assert.Equal("Three", tokens[2]);
    }

    [Fact]
    public void SplitCollectionItems_Uses_Configured_StringSplit_For_NonComma_Separators()
    {
        var tokens = XamlDelimitedValueSemantics.SplitCollectionItems(
            "One; Two; ;Three",
            [";"],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(3, tokens.Length);
        Assert.Equal("One", tokens[0]);
        Assert.Equal("Two", tokens[1]);
        Assert.Equal("Three", tokens[2]);
    }

    [Fact]
    public void SplitCollectionItems_Honors_SplitOptions_When_Separators_Are_Empty()
    {
        var tokens = XamlDelimitedValueSemantics.SplitCollectionItems(
            " One, , Two ",
            [],
            StringSplitOptions.None);

        Assert.Equal(3, tokens.Length);
        Assert.Equal(" One", tokens[0]);
        Assert.Equal(" ", tokens[1]);
        Assert.Equal(" Two ", tokens[2]);
    }

    [Fact]
    public void SplitCollectionItems_Honors_SplitOptions_For_Comma_Separator()
    {
        var tokens = XamlDelimitedValueSemantics.SplitCollectionItems(
            " One, Two ",
            [","],
            StringSplitOptions.None);

        Assert.Equal(2, tokens.Length);
        Assert.Equal(" One", tokens[0]);
        Assert.Equal(" Two ", tokens[1]);
    }
}
