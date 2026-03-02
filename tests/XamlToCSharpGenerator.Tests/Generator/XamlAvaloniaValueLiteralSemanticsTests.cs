using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XamlAvaloniaValueLiteralSemanticsTests
{
    [Fact]
    public void Parses_Thickness_Single_Value()
    {
        var ok = XamlAvaloniaValueLiteralSemantics.TryParseThickness(
            "4",
            out var count,
            out var left,
            out var top,
            out var right,
            out var bottom);

        Assert.True(ok);
        Assert.Equal(1, count);
        Assert.Equal(4d, left);
        Assert.Equal(4d, top);
        Assert.Equal(4d, right);
        Assert.Equal(4d, bottom);
    }

    [Fact]
    public void Parses_Thickness_Two_Values()
    {
        var ok = XamlAvaloniaValueLiteralSemantics.TryParseThickness(
            "4 8",
            out var count,
            out var left,
            out var top,
            out var right,
            out var bottom);

        Assert.True(ok);
        Assert.Equal(2, count);
        Assert.Equal(4d, left);
        Assert.Equal(8d, top);
        Assert.Equal(4d, right);
        Assert.Equal(8d, bottom);
    }

    [Fact]
    public void Rejects_Thickness_Three_Values()
    {
        var ok = XamlAvaloniaValueLiteralSemantics.TryParseThickness(
            "1,2,3",
            out _,
            out _,
            out _,
            out _,
            out _);

        Assert.False(ok);
    }

    [Fact]
    public void Parses_Point_And_Vector()
    {
        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParsePoint("1,2", out var px, out var py));
        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParseVector("3 4", out var vx, out var vy));

        Assert.Equal(1d, px);
        Assert.Equal(2d, py);
        Assert.Equal(3d, vx);
        Assert.Equal(4d, vy);
    }

    [Fact]
    public void Parses_Matrix_6_And_9_Component_Forms()
    {
        var ok6 = XamlAvaloniaValueLiteralSemantics.TryParseMatrix(
            "1,2,3,4,5,6",
            out var count6,
            out var m11,
            out var m12,
            out var m21,
            out var m22,
            out var m31,
            out var m32,
            out _,
            out _,
            out _);
        var ok9 = XamlAvaloniaValueLiteralSemantics.TryParseMatrix(
            "1,2,7,3,4,8,5,6,9",
            out var count9,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out var m13,
            out var m23,
            out var m33);

        Assert.True(ok6);
        Assert.Equal(6, count6);
        Assert.Equal(1d, m11);
        Assert.Equal(2d, m12);
        Assert.Equal(3d, m21);
        Assert.Equal(4d, m22);
        Assert.Equal(5d, m31);
        Assert.Equal(6d, m32);

        Assert.True(ok9);
        Assert.Equal(9, count9);
        Assert.Equal(5d, m13);
        Assert.Equal(6d, m23);
        Assert.Equal(9d, m33);
    }

    [Theory]
    [InlineData("Auto", AvaloniaGridLengthLiteralUnit.Auto, 1d)]
    [InlineData("*", AvaloniaGridLengthLiteralUnit.Star, 1d)]
    [InlineData("2*", AvaloniaGridLengthLiteralUnit.Star, 2d)]
    [InlineData("42", AvaloniaGridLengthLiteralUnit.Pixel, 42d)]
    public void Parses_GridLength(string token, AvaloniaGridLengthLiteralUnit expectedUnit, double expectedValue)
    {
        var ok = XamlAvaloniaValueLiteralSemantics.TryParseGridLength(token, out var unit, out var value);

        Assert.True(ok);
        Assert.Equal(expectedUnit, unit);
        Assert.Equal(expectedValue, value);
    }

    [Fact]
    public void Parses_RelativePoint_And_Rejects_Mixed_Units()
    {
        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParseRelativePoint(
            "10%, 20%",
            out var rx,
            out var ry,
            out var relativeUnit));
        Assert.Equal(AvaloniaRelativeUnitLiteral.Relative, relativeUnit);
        Assert.Equal(0.10d, rx, 3);
        Assert.Equal(0.20d, ry, 3);

        Assert.False(XamlAvaloniaValueLiteralSemantics.TryParseRelativePoint(
            "10%, 20",
            out _,
            out _,
            out _));
    }

    [Fact]
    public void Parses_RelativeScalar_And_RelativeRect()
    {
        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParseRelativeScalar(
            "50%",
            out var scalar,
            out var scalarUnit));
        Assert.Equal(AvaloniaRelativeUnitLiteral.Relative, scalarUnit);
        Assert.Equal(0.5d, scalar, 3);

        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParseRelativeRect(
            "10%,20%,30%,40%",
            out var x,
            out var y,
            out var width,
            out var height,
            out var rectUnit));
        Assert.Equal(AvaloniaRelativeUnitLiteral.Relative, rectUnit);
        Assert.Equal(0.10d, x, 3);
        Assert.Equal(0.20d, y, 3);
        Assert.Equal(0.30d, width, 3);
        Assert.Equal(0.40d, height, 3);

        Assert.False(XamlAvaloniaValueLiteralSemantics.TryParseRelativeRect(
            "10%,20,30%,40%",
            out _,
            out _,
            out _,
            out _,
            out _));
    }

    [Theory]
    [InlineData("#fff", 0xFFFFFFFFu)]
    [InlineData("#ffff", 0xFFFFFFFFu)]
    [InlineData("#112233", 0xFF112233u)]
    [InlineData("#aa112233", 0xAA112233u)]
    public void Parses_Hex_Colors(string token, uint expectedArgb)
    {
        var ok = XamlAvaloniaValueLiteralSemantics.TryParseHexColor(token, out var parsed);

        Assert.True(ok);
        Assert.Equal(expectedArgb, parsed);
    }

    [Fact]
    public void Parses_Pixel_And_Rect_Shapes()
    {
        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParsePixelPoint("1,2", out var px, out var py));
        Assert.Equal(1, px);
        Assert.Equal(2, py);

        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParsePixelRect("1,2,3,4", out var rx, out var ry, out var rw, out var rh));
        Assert.Equal(1, rx);
        Assert.Equal(2, ry);
        Assert.Equal(3, rw);
        Assert.Equal(4, rh);

        Assert.True(XamlAvaloniaValueLiteralSemantics.TryParseRect("1,2,3,4", out var x, out var y, out var w, out var h));
        Assert.Equal(1d, x);
        Assert.Equal(2d, y);
        Assert.Equal(3d, w);
        Assert.Equal(4d, h);
    }

    [Fact]
    public void Parses_TransformOperations_None_As_Identity()
    {
        var ok = XamlAvaloniaTransformLiteralSemantics.TryParse("none", out var isIdentity, out var operations);

        Assert.True(ok);
        Assert.True(isIdentity);
        Assert.Empty(operations);
    }

    [Fact]
    public void Parses_TransformOperations_Function_List()
    {
        var ok = XamlAvaloniaTransformLiteralSemantics.TryParse(
            "translate(10px,20px) rotate(180deg) scale(2) skewY(90deg) matrix(1,0,0,1,5,6)",
            out var isIdentity,
            out var operations);

        Assert.True(ok);
        Assert.False(isIdentity);
        Assert.Equal(5, operations.Length);

        Assert.Equal(AvaloniaTransformOperationLiteralKind.Translate, operations[0].Kind);
        Assert.Equal(10d, operations[0].Value1);
        Assert.Equal(20d, operations[0].Value2);

        Assert.Equal(AvaloniaTransformOperationLiteralKind.Rotate, operations[1].Kind);
        Assert.Equal(System.Math.PI, operations[1].Value1, 6);

        Assert.Equal(AvaloniaTransformOperationLiteralKind.Scale, operations[2].Kind);
        Assert.Equal(2d, operations[2].Value1);
        Assert.Equal(2d, operations[2].Value2);

        Assert.Equal(AvaloniaTransformOperationLiteralKind.Skew, operations[3].Kind);
        Assert.Equal(0d, operations[3].Value1);
        Assert.Equal(System.Math.PI / 2d, operations[3].Value2, 6);

        Assert.Equal(AvaloniaTransformOperationLiteralKind.Matrix, operations[4].Kind);
        Assert.Equal(1d, operations[4].Value1);
        Assert.Equal(0d, operations[4].Value2);
        Assert.Equal(0d, operations[4].Value3);
        Assert.Equal(1d, operations[4].Value4);
        Assert.Equal(5d, operations[4].Value5);
        Assert.Equal(6d, operations[4].Value6);
    }

    [Fact]
    public void Parses_TransformOperations_ScaleX_And_ScaleY_Default_Component()
    {
        var scaleXOk = XamlAvaloniaTransformLiteralSemantics.TryParse(
            "scaleX(2)",
            out var isScaleXIdentity,
            out var scaleXOperations);
        var scaleYOk = XamlAvaloniaTransformLiteralSemantics.TryParse(
            "scaleY(3)",
            out var isScaleYIdentity,
            out var scaleYOperations);

        Assert.True(scaleXOk);
        Assert.False(isScaleXIdentity);
        Assert.Single(scaleXOperations);
        Assert.Equal(AvaloniaTransformOperationLiteralKind.Scale, scaleXOperations[0].Kind);
        Assert.Equal(2d, scaleXOperations[0].Value1);
        Assert.Equal(1d, scaleXOperations[0].Value2);

        Assert.True(scaleYOk);
        Assert.False(isScaleYIdentity);
        Assert.Single(scaleYOperations);
        Assert.Equal(AvaloniaTransformOperationLiteralKind.Scale, scaleYOperations[0].Kind);
        Assert.Equal(1d, scaleYOperations[0].Value1);
        Assert.Equal(3d, scaleYOperations[0].Value2);
    }

    [Theory]
    [InlineData("translate(10deg)", false)]
    [InlineData("scaleX(1,2)", false)]
    [InlineData("unknown(1)", false)]
    [InlineData("translate(1 2)", false)]
    public void Rejects_Invalid_TransformOperations_Shapes(string literal, bool expected)
    {
        var ok = XamlAvaloniaTransformLiteralSemantics.TryParse(literal, out _, out _);

        Assert.Equal(expected, ok);
    }
}
