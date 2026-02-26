using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlMemberPathSemanticsTests
{
    [Fact]
    public void SplitPathSegments_Splits_Dot_Path()
    {
        var segments = XamlMemberPathSemantics.SplitPathSegments("Order.Customer.Name");

        Assert.Equal(new[] { "Order", "Customer", "Name" }, segments.ToArray());
    }

    [Fact]
    public void SplitPathSegments_Does_Not_Split_Inside_Attached_Property_Segment()
    {
        var segments = XamlMemberPathSemantics.SplitPathSegments("SelectedItem.(ScrollViewer.HorizontalScrollBarVisibility)");

        Assert.Equal(new[] { "SelectedItem", "(ScrollViewer.HorizontalScrollBarVisibility)" }, segments.ToArray());
    }

    [Theory]
    [InlineData("Items[0]", "Items")]
    [InlineData("(ScrollViewer.HorizontalScrollBarVisibility)", "HorizontalScrollBarVisibility")]
    [InlineData("  Name  ", "Name")]
    public void NormalizeSegmentForMemberLookup_Normalizes_Segment(string segment, string expected)
    {
        Assert.Equal(expected, XamlMemberPathSemantics.NormalizeSegmentForMemberLookup(segment));
    }
}
