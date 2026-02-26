using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlIncludePathSemanticsTests
{
    [Theory]
    [InlineData("Accents/BaseResources.xaml", "Accents/BaseResources.xaml")]
    [InlineData("Accents/./BaseResources.xaml", "Accents/BaseResources.xaml")]
    [InlineData("Accents/Themes/../BaseResources.xaml", "Accents/BaseResources.xaml")]
    [InlineData("\\Accents\\BaseResources.xaml", "Accents/BaseResources.xaml")]
    public void NormalizePath_Normalizes_Relative_Segments(string input, string expected)
    {
        var result = XamlIncludePathSemantics.NormalizePath(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Accents/BaseResources.xaml", "Accents")]
    [InlineData("BaseResources.xaml", "")]
    [InlineData("", "")]
    public void GetDirectory_Returns_Path_Parent(string input, string expected)
    {
        var result = XamlIncludePathSemantics.GetDirectory(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Accents", "BaseResources.xaml", "Accents/BaseResources.xaml")]
    [InlineData("Accents/", "/BaseResources.xaml", "Accents/BaseResources.xaml")]
    [InlineData("", "BaseResources.xaml", "BaseResources.xaml")]
    public void CombinePath_Combines_Base_And_Relative(string baseDirectory, string relativePath, string expected)
    {
        var result = XamlIncludePathSemantics.CombinePath(baseDirectory, relativePath);

        Assert.Equal(expected, result);
    }
}
