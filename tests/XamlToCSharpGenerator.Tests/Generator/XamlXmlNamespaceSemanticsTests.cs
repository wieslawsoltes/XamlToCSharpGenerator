using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlXmlNamespaceSemanticsTests
{
    [Theory]
    [InlineData("clr-namespace:Demo.App", true, "Demo.App")]
    [InlineData("clr-namespace:Demo.App;assembly=Demo.Assembly", true, "Demo.App")]
    [InlineData("using:Demo.App", true, "Demo.App")]
    [InlineData("using:Demo.App;assembly=Demo.Assembly", true, "Demo.App")]
    [InlineData("https://github.com/avaloniaui", false, "")]
    public void TryExtractClrNamespace_Parses_Namespace_Uri(
        string xmlNamespace,
        bool expectedResult,
        string expectedClrNamespace)
    {
        var result = XamlXmlNamespaceSemantics.TryExtractClrNamespace(xmlNamespace, out var clrNamespace);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedClrNamespace, clrNamespace);
    }

    [Theory]
    [InlineData("clr-namespace:Demo.App", "MainView", null, true, "Demo.App.MainView")]
    [InlineData("clr-namespace:Demo.App;assembly=Demo.Assembly", "GenericView", 2, true, "Demo.App.GenericView`2")]
    [InlineData("using:Demo.App", "Item", 1, true, "Demo.App.Item`1")]
    [InlineData("https://github.com/avaloniaui", "Button", null, false, "")]
    public void TryBuildClrNamespaceMetadataName_Builds_Metadata_Name(
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity,
        bool expectedResult,
        string expectedMetadataName)
    {
        var result = XamlXmlNamespaceSemantics.TryBuildClrNamespaceMetadataName(
            xmlNamespace,
            xmlTypeName,
            genericArity,
            out var metadataName);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedMetadataName, metadataName);
    }
}
