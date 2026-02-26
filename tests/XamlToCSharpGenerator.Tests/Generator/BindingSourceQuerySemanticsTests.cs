using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class BindingSourceQuerySemanticsTests
{
    [Fact]
    public void Parses_ElementName_Query()
    {
        var success = BindingSourceQuerySemantics.TryParseElementName("#SearchBox.Text", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.ElementName, query.Kind);
        Assert.Equal("SearchBox", query.ElementName);
        Assert.Equal("Text", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Self_Query_With_Implicit_Current_Path()
    {
        var success = BindingSourceQuerySemantics.TryParseSelf("$self", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Self, query.Kind);
        Assert.Equal(".", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Parent_Query_With_Type_And_Level()
    {
        var success = BindingSourceQuerySemantics.TryParseParent("$parent[Border,2].Bounds", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Parent, query.Kind);
        Assert.Equal("Border", query.AncestorTypeToken);
        Assert.Equal(2, query.AncestorLevel);
        Assert.Equal("Bounds", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Parent_Query_With_Semicolon_Descriptor_Separator()
    {
        var success = BindingSourceQuerySemantics.TryParseParent("$parent[Border;2].Bounds", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Parent, query.Kind);
        Assert.Equal("Border", query.AncestorTypeToken);
        Assert.Equal(2, query.AncestorLevel);
        Assert.Equal("Bounds", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Parent_Query_With_Level_Only()
    {
        var success = BindingSourceQuerySemantics.TryParseParent("$parent[3]", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Parent, query.Kind);
        Assert.Null(query.AncestorTypeToken);
        Assert.Equal(3, query.AncestorLevel);
        Assert.Equal(".", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Parent_Query_With_Type_Only()
    {
        var success = BindingSourceQuerySemantics.TryParseParent("$parent[Window]", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Parent, query.Kind);
        Assert.Equal("Window", query.AncestorTypeToken);
        Assert.Equal(1, query.AncestorLevel);
        Assert.Equal(".", query.NormalizedPath);
    }

    [Fact]
    public void Parses_Parent_Query_With_Whitespace_Descriptor_As_Default_Level()
    {
        var success = BindingSourceQuerySemantics.TryParseParent("$parent[ ]", out var query);

        Assert.True(success);
        Assert.Equal(BindingSourceQueryKind.Parent, query.Kind);
        Assert.Null(query.AncestorTypeToken);
        Assert.Equal(1, query.AncestorLevel);
        Assert.Equal(".", query.NormalizedPath);
    }

    [Fact]
    public void Rejects_Invalid_Parent_Query_Syntax()
    {
        Assert.False(BindingSourceQuerySemantics.TryParseParent("$parent[].Value", out _));
        Assert.False(BindingSourceQuerySemantics.TryParseParent("$parent[", out _));
        Assert.False(BindingSourceQuerySemantics.TryParseParent("$parent:Value", out _));
    }
}
