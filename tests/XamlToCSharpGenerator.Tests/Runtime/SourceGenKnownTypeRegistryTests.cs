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
}
