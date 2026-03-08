using System.Collections.Immutable;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.Tests.LanguageService;

public class XamlReferenceServiceTests
{
    [Fact]
    public void SortReferencesDeterministically_Orders_By_Uri_Then_Full_Range_Then_Declaration()
    {
        var builder = ImmutableArray.CreateBuilder<XamlReferenceLocation>();
        builder.Add(CreateReference("file:///b.axaml", 10, 2, 10, 8, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 5, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 4, false));
        builder.Add(CreateReference("file:///a.axaml", 4, 3, 4, 4, true));
        builder.Add(CreateReference("file:///a.axaml", 3, 9, 3, 12, false));

        var sorted = XamlReferenceService.SortReferencesDeterministically(builder);

        Assert.Collection(
            sorted,
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(3, item.Range.Start.Line);
                Assert.Equal(12, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.True(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(4, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(4, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///a.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
                Assert.Equal(4, item.Range.Start.Line);
                Assert.Equal(5, item.Range.End.Character);
            },
            item =>
            {
                Assert.Equal("file:///b.axaml", item.Uri);
                Assert.False(item.IsDeclaration);
            });
    }

    private static XamlReferenceLocation CreateReference(
        string uri,
        int startLine,
        int startCharacter,
        int endLine,
        int endCharacter,
        bool isDeclaration)
    {
        return new XamlReferenceLocation(
            uri,
            new SourceRange(
                new SourcePosition(startLine, startCharacter),
                new SourcePosition(endLine, endCharacter)),
            isDeclaration);
    }
}
