using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlTypeTokenSemanticsTests
{
    [Theory]
    [InlineData("global::Demo.MainView", "Demo.MainView")]
    [InlineData(" Demo.MainView ", "Demo.MainView")]
    [InlineData("global::", "")]
    public void TrimGlobalQualifier_Trims_Prefix_And_Whitespace(string token, string expected)
    {
        Assert.Equal(expected, XamlTypeTokenSemantics.TrimGlobalQualifier(token));
    }

    [Theory]
    [InlineData("x:String", "String")]
    [InlineData("X:Int32", "Int32")]
    [InlineData(" String ", "String")]
    public void TrimXamlDirectivePrefix_Trims_Xaml_Directive_Prefix(string token, string expected)
    {
        Assert.Equal(expected, XamlTypeTokenSemantics.TrimXamlDirectivePrefix(token));
    }
}
