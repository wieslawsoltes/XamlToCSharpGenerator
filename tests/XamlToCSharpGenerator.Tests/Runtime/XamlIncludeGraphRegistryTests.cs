using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlIncludeGraphRegistryTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

    [Fact]
    public void GetTransitive_Returns_Deterministic_Depth_First_Include_Order()
    {
        XamlIncludeGraphRegistry.Clear();
        XamlIncludeGraphRegistry.Register("avares://Demo/Main.axaml", "avares://Demo/Styles.axaml", "Styles");
        XamlIncludeGraphRegistry.Register("avares://Demo/Main.axaml", "avares://Demo/Colors.axaml", "MergedDictionaries");
        XamlIncludeGraphRegistry.Register("avares://Demo/Styles.axaml", "avares://Demo/BaseStyles.axaml", "Styles");

        var styles = XamlIncludeGraphRegistry.GetTransitive("avares://Demo/Main.axaml", "Styles");
        var merged = XamlIncludeGraphRegistry.GetTransitive("avares://Demo/Main.axaml", "MergedDictionaries");

        Assert.Equal(2, styles.Count);
        Assert.Equal("avares://Demo/Styles.axaml", styles[0].IncludedUri);
        Assert.Equal("avares://Demo/BaseStyles.axaml", styles[1].IncludedUri);
        Assert.Single(merged);
        Assert.Equal("avares://Demo/Colors.axaml", merged[0].IncludedUri);
    }

    [Fact]
    public void GetTransitive_Does_Not_Revisit_Already_Visited_Nodes()
    {
        XamlIncludeGraphRegistry.Clear();
        XamlIncludeGraphRegistry.Register("avares://Demo/A.axaml", "avares://Demo/B.axaml", "Styles");
        XamlIncludeGraphRegistry.Register("avares://Demo/B.axaml", "avares://Demo/A.axaml", "Styles");

        var includes = XamlIncludeGraphRegistry.GetTransitive("avares://Demo/A.axaml", "Styles");

        Assert.Single(includes);
        Assert.Equal("avares://Demo/B.axaml", includes[0].IncludedUri);
    }

    [Fact]
    public void GetDirect_Preserves_Registration_Order()
    {
        XamlIncludeGraphRegistry.Clear();
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Z.axaml",
            "MergedDictionaries");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/A.axaml",
            "MergedDictionaries");

        var includes = XamlIncludeGraphRegistry.GetDirect("avares://Demo/Main.axaml", "MergedDictionaries");

        Assert.Equal(2, includes.Count);
        Assert.Equal("avares://Demo/Z.axaml", includes[0].IncludedUri);
        Assert.Equal("avares://Demo/A.axaml", includes[1].IncludedUri);
        Assert.True(includes[0].Order < includes[1].Order);
    }

    [Fact]
    public void GetIncoming_Returns_Include_Owners_In_Registration_Order()
    {
        XamlIncludeGraphRegistry.Clear();
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Main.axaml",
            "avares://Demo/Shared.axaml",
            "Styles");
        XamlIncludeGraphRegistry.Register(
            "avares://Demo/Theme.axaml",
            "avares://Demo/Shared.axaml",
            "MergedDictionaries");

        var incoming = XamlIncludeGraphRegistry.GetIncoming("avares://Demo/Shared.axaml");

        Assert.Equal(2, incoming.Count);
        Assert.Equal("avares://Demo/Main.axaml", incoming[0].SourceUri);
        Assert.Equal("avares://Demo/Theme.axaml", incoming[1].SourceUri);
    }
}
