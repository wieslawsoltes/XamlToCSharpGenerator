using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlMarkupExtensionNameSemanticsTests
{
    [Theory]
    [InlineData("x:Type", XamlMarkupExtensionKind.Type)]
    [InlineData("TypeExtension", XamlMarkupExtensionKind.Type)]
    [InlineData("StaticResource", XamlMarkupExtensionKind.StaticResource)]
    [InlineData("DynamicResourceExtension", XamlMarkupExtensionKind.DynamicResource)]
    [InlineData("x:Null", XamlMarkupExtensionKind.Null)]
    [InlineData("x:Uri", XamlMarkupExtensionKind.Uri)]
    [InlineData("CompiledBinding", XamlMarkupExtensionKind.CompiledBinding)]
    [InlineData("x:EventBinding", XamlMarkupExtensionKind.EventBinding)]
    [InlineData("CSharp", XamlMarkupExtensionKind.CSharp)]
    [InlineData("axsg:CSharp", XamlMarkupExtensionKind.CSharp)]
    [InlineData("ResolveByNameExtension", XamlMarkupExtensionKind.ResolveByName)]
    [InlineData("UnknownThing", XamlMarkupExtensionKind.Unknown)]
    public void Classify_Maps_Xaml_And_Extension_Forms(string token, XamlMarkupExtensionKind expected)
    {
        var actual = XamlMarkupExtensionNameSemantics.Classify(token);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("TypeExtension", "x:Type")]
    [InlineData("x:Static", "StaticExtension")]
    [InlineData("Reference", "x:Reference")]
    [InlineData("DynamicResourceExtension", "DynamicResource")]
    public void Matches_Supports_Directive_And_Extension_Aliases(string token, string knownName)
    {
        Assert.True(XamlMarkupExtensionNameSemantics.Matches(token, knownName));
    }

    [Theory]
    [InlineData("x:Type", "TypeExtension")]
    [InlineData("TypeExtension", "TypeExtension")]
    [InlineData("local:Custom", "local:CustomExtension")]
    [InlineData(" local:CustomExtension ", "local:CustomExtension")]
    public void ToClrExtensionTypeToken_Preserves_Namespace_And_Adds_Suffix(string token, string expected)
    {
        var actual = XamlMarkupExtensionNameSemantics.ToClrExtensionTypeToken(token);

        Assert.Equal(expected, actual);
    }
}
