using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CompiledBindingPathSegmentSemanticsTests
{
    [Fact]
    public void TryParseAttachedPropertySegment_Parses_Valid_Attached_Property()
    {
        var status = CompiledBindingPathSegmentSemantics.TryParseAttachedPropertySegment(
            "(controls:Grid.Row).Next",
            0,
            out var ownerTypeToken,
            out var memberName,
            out var nextIndex);

        Assert.Equal(CompiledBindingAttachedPropertyParseStatus.Parsed, status);
        Assert.Equal("controls:Grid", ownerTypeToken);
        Assert.Equal("Row", memberName);
        Assert.Equal('.', "(controls:Grid.Row).Next"[nextIndex]);
    }

    [Fact]
    public void TryParseAttachedPropertySegment_Reports_Invalid_Segment()
    {
        var status = CompiledBindingPathSegmentSemantics.TryParseAttachedPropertySegment(
            "(controls:Grid.).Next",
            0,
            out _,
            out _,
            out _);

        Assert.Equal(CompiledBindingAttachedPropertyParseStatus.Invalid, status);
    }

    [Fact]
    public void TryParseAttachedPropertySegment_Does_Not_Treat_Cast_As_Attached_Property()
    {
        var status = CompiledBindingPathSegmentSemantics.TryParseAttachedPropertySegment(
            "(vm:Customer).Orders",
            0,
            out _,
            out _,
            out _);

        Assert.Equal(CompiledBindingAttachedPropertyParseStatus.NotAttached, status);
    }

    [Fact]
    public void TryParseCastTypeToken_Parses_Simple_Cast()
    {
        var path = "(vm:Customer)Orders";
        var index = 0;

        var ok = CompiledBindingPathSegmentSemantics.TryParseCastTypeToken(
            path,
            ref index,
            out var castTypeToken,
            out var requiresSegmentClosure,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("vm:Customer", castTypeToken);
        Assert.False(requiresSegmentClosure);
        Assert.Equal('O', path[index]);
    }

    [Fact]
    public void TryParseCastTypeToken_Parses_Double_Paren_Cast()
    {
        var path = "((vm:Customer)Orders)";
        var index = 0;

        var ok = CompiledBindingPathSegmentSemantics.TryParseCastTypeToken(
            path,
            ref index,
            out var castTypeToken,
            out var requiresSegmentClosure,
            out var errorMessage);

        Assert.True(ok, errorMessage);
        Assert.Equal("vm:Customer", castTypeToken);
        Assert.True(requiresSegmentClosure);
        Assert.Equal('O', path[index]);
    }
}
