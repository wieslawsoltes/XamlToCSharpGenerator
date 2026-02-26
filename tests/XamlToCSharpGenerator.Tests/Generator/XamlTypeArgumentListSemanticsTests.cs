using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlTypeArgumentListSemanticsTests
{
    [Fact]
    public void Parse_Splits_Simple_Type_Arguments()
    {
        var arguments = XamlTypeArgumentListSemantics.Parse("x:String, x:Int32");

        Assert.Equal(2, arguments.Length);
        Assert.Equal("x:String", arguments[0]);
        Assert.Equal("x:Int32", arguments[1]);
    }

    [Fact]
    public void Parse_Preserves_Nested_Generic_And_Markup_Expressions()
    {
        var arguments = XamlTypeArgumentListSemantics.Parse(
            "x:KeyValuePair<x:String, x:Int32>, {x:Type vm:Customer}");

        Assert.Equal(2, arguments.Length);
        Assert.Equal("x:KeyValuePair<x:String, x:Int32>", arguments[0]);
        Assert.Equal("{x:Type vm:Customer}", arguments[1]);
    }

    [Fact]
    public void Parse_Preserves_Commas_Inside_Quoted_Tokens()
    {
        var arguments = XamlTypeArgumentListSemantics.Parse(
            "x:String, \"value,with,commas\", x:Int32");

        Assert.Equal(3, arguments.Length);
        Assert.Equal("\"value,with,commas\"", arguments[1]);
    }
}
