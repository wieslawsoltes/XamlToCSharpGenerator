using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Tests.Generator;

public class EventBindingPathSemanticsTests
{
    [Theory]
    [InlineData("Save", ".", "Save")]
    [InlineData("ViewModel.Save", "ViewModel", "Save")]
    [InlineData("Root.Panel.Save", "Root.Panel", "Save")]
    public void TrySplitMethodPath_Parses_Simple_Method_Paths(
        string methodPath,
        string expectedTargetPath,
        string expectedMethodName)
    {
        var success = EventBindingPathSemantics.TrySplitMethodPath(methodPath, out var targetPath, out var methodName);

        Assert.True(success);
        Assert.Equal(expectedTargetPath, targetPath);
        Assert.Equal(expectedMethodName, methodName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Save()")]
    [InlineData("Save-With-Dash")]
    public void TrySplitMethodPath_Rejects_NonSimple_Paths(string methodPath)
    {
        var success = EventBindingPathSemantics.TrySplitMethodPath(methodPath, out _, out _);
        Assert.False(success);
    }

    [Fact]
    public void BuildMethodArgumentSets_With_Parameter_Uses_Parameter_First()
    {
        var sets = EventBindingPathSemantics.BuildMethodArgumentSets(hasParameterToken: true, passEventArgs: false);

        Assert.Equal(3, sets.Length);
        Assert.Equal(
            [ResolvedEventBindingMethodArgumentKind.Parameter],
            sets[0].ToArray());
    }

    [Fact]
    public void BuildMethodArgumentSets_With_PassEventArgs_Adds_EventArgs_Combinations()
    {
        var sets = EventBindingPathSemantics.BuildMethodArgumentSets(hasParameterToken: false, passEventArgs: true);

        Assert.Equal(4, sets.Length);
        Assert.Equal(
            [ResolvedEventBindingMethodArgumentKind.Sender, ResolvedEventBindingMethodArgumentKind.EventArgs],
            sets[0].ToArray());
        Assert.Equal(
            ImmutableArray<ResolvedEventBindingMethodArgumentKind>.Empty,
            sets[3]);
    }

    [Theory]
    [InlineData("Alpha", true)]
    [InlineData("_Alpha2", true)]
    [InlineData("2Alpha", false)]
    [InlineData("Alpha-Beta", false)]
    public void IsSimpleIdentifier_Returns_Expected_Result(string value, bool expected)
    {
        var actual = EventBindingPathSemantics.IsSimpleIdentifier(value);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData("Root.Save", true)]
    [InlineData("Root..Save", true)]
    [InlineData("Root.Save()", false)]
    public void IsSimplePath_Returns_Expected_Result(string path, bool expected)
    {
        var actual = EventBindingPathSemantics.IsSimplePath(path);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Root.Save", "Save")]
    [InlineData("Save", "Save")]
    [InlineData(" ", "")]
    public void ExtractMethodName_Returns_Leaf_Name(string path, string expected)
    {
        var actual = EventBindingPathSemantics.ExtractMethodName(path);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildGeneratedMethodName_Sanitizes_Invalid_Characters()
    {
        var generated = EventBindingPathSemantics.BuildGeneratedMethodName("Pointer-Pressed", 12, 34);
        Assert.Equal("__AXSG_EventBinding_Pointer_Pressed_12_34", generated);
    }

    [Fact]
    public void BuildGeneratedMethodName_With_StableKey_Is_Deterministic()
    {
        var generated = EventBindingPathSemantics.BuildGeneratedMethodName(
            "Pointer-Pressed",
            "global::System.EventHandler|lambda|source.Count++");

        Assert.Equal("__AXSG_EventBinding_Pointer_Pressed_HA7695AE0", generated);
    }
}
