using System.Reflection;
using Avalonia.Controls;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

public class SourceGenKnownTypeRegistryTests
{
    [Fact]
    public void Default_Avalonia_Namespace_Resolves_Inline_CSharp_To_Markup_Type()
    {
        var resolved = SourceGenKnownTypeRegistry.TryResolve("https://github.com/avaloniaui", "CSharp", out var type);

        Assert.True(resolved);
        Assert.NotNull(type);
        Assert.Equal("XamlToCSharpGenerator.Runtime.Markup.CSharp", type!.FullName);
    }

    [Fact]
    public void Default_Avalonia_Namespace_Candidates_Do_Not_Use_Global_Prefixes()
    {
        var candidatesField = typeof(SourceGenKnownTypeRegistry).GetField(
            "AvaloniaDefaultNamespaceCandidates",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(candidatesField);

        var candidates = Assert.IsType<string[]>(candidatesField!.GetValue(null));
        Assert.DoesNotContain(candidates, static candidate => candidate.StartsWith("global::", StringComparison.Ordinal));
    }

    [Fact]
    public void Default_Avalonia_Namespace_Resolves_Button_To_Framework_Type()
    {
        var resolved = SourceGenKnownTypeRegistry.TryResolve("https://github.com/avaloniaui", "Button", out var type);

        Assert.True(resolved);
        Assert.Same(typeof(Button), type);
    }
}
