using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public sealed class UriPathHelperTests
{
    [Theory]
    [InlineData(@"C:\Users\soltes\Cerebre\nest\NestStudioPro\Views\Diagram\MachineLearning\MainView.axaml")]
    [InlineData("C:/Users/soltes/Cerebre/nest/NestStudioPro/Views/Diagram/MachineLearning/MainView.axaml")]
    [InlineData("/C:/Users/soltes/Cerebre/nest/NestStudioPro/Views/Diagram/MachineLearning/MainView.axaml")]
    public void NormalizeFilePath_Normalizes_Windows_Drive_Rooted_Shapes(string input)
    {
        var normalized = UriPathHelper.NormalizeFilePath(input);

        Assert.Equal(
            @"C:\Users\soltes\Cerebre\nest\NestStudioPro\Views\Diagram\MachineLearning\MainView.axaml",
            normalized,
            ignoreCase: true,
            ignoreLineEndingDifferences: false,
            ignoreWhiteSpaceDifferences: false);
    }

    [Fact]
    public void ToFilePath_Normalizes_Windows_File_Uri()
    {
        var normalized = UriPathHelper.ToFilePath(
            "file:///C%3A/Users/soltes/Cerebre/nest/NestStudioPro/Views/Diagram/MachineLearning/MainView.axaml");

        Assert.Equal(
            @"C:\Users\soltes\Cerebre\nest\NestStudioPro\Views\Diagram\MachineLearning\MainView.axaml",
            normalized,
            ignoreCase: true,
            ignoreLineEndingDifferences: false,
            ignoreWhiteSpaceDifferences: false);
    }

    [Fact]
    public void ToDocumentUri_Converts_LeadingSlash_Windows_Path_To_File_Uri()
    {
        var uri = UriPathHelper.ToDocumentUri(
            "/C:/Users/soltes/Cerebre/nest/NestStudioPro/Views/Diagram/MachineLearning/MainView.axaml");

        Assert.Equal(
            "file:///C:/Users/soltes/Cerebre/nest/NestStudioPro/Views/Diagram/MachineLearning/MainView.axaml",
            uri,
            ignoreCase: true,
            ignoreLineEndingDifferences: false,
            ignoreWhiteSpaceDifferences: false);
    }

    [Fact]
    public void ToFilePath_Preserves_NonFile_Absolute_Uris()
    {
        const string uri = "axsg-metadata://symbol?kind=type&id=abc";

        Assert.Equal(uri, UriPathHelper.ToFilePath(uri));
    }
}
