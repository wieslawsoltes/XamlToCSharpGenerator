using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlEventHandlerNameSemanticsTests
{
    [Fact]
    public void TryParseHandlerName_Accepts_Identifier()
    {
        var success = XamlEventHandlerNameSemantics.TryParseHandlerName("OnButtonClick", out var handlerName);

        Assert.True(success);
        Assert.Equal("OnButtonClick", handlerName);
    }

    [Fact]
    public void TryParseHandlerName_Rejects_Markup_Expression()
    {
        var success = XamlEventHandlerNameSemantics.TryParseHandlerName("{Binding Command}", out _);

        Assert.False(success);
    }

    [Fact]
    public void TryParseHandlerName_Rejects_Member_Path_And_Method_Call_Tokens()
    {
        Assert.False(XamlEventHandlerNameSemantics.TryParseHandlerName("ViewModel.OnButtonClick", out _));
        Assert.False(XamlEventHandlerNameSemantics.TryParseHandlerName("OnButtonClick()", out _));
    }

    [Fact]
    public void TryParseHandlerName_Rejects_Invalid_Identifier_Start()
    {
        var success = XamlEventHandlerNameSemantics.TryParseHandlerName("9OnButtonClick", out _);

        Assert.False(success);
    }
}
