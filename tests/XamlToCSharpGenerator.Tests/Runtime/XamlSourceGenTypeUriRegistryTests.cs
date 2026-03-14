using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenTypeUriRegistryTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

    [Fact]
    public void Register_And_TryGetUri_Normalize_Value()
    {
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Register(typeof(TargetType), "  avares://Demo/Main.axaml  ");

        var found = XamlSourceGenTypeUriRegistry.TryGetUri(typeof(TargetType), out var uri);

        Assert.True(found);
        Assert.Equal("avares://Demo/Main.axaml", uri);
    }

    [Fact]
    public void Register_Uses_Generic_Type_Definition_As_Key()
    {
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Register(typeof(GenericTarget<int>), "avares://Demo/Generic.axaml");

        var found = XamlSourceGenTypeUriRegistry.TryGetUri(typeof(GenericTarget<string>), out var uri);

        Assert.True(found);
        Assert.Equal("avares://Demo/Generic.axaml", uri);
    }

    [Fact]
    public void Clear_Removes_Registered_Entries()
    {
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Register(typeof(TargetType), "avares://Demo/Main.axaml");
        XamlSourceGenTypeUriRegistry.Clear();

        var found = XamlSourceGenTypeUriRegistry.TryGetUri(typeof(TargetType), out _);

        Assert.False(found);
    }

    [Fact]
    public void TryGetUri_Resolves_Nested_Type_To_Registered_Declaring_Type()
    {
        XamlSourceGenTypeUriRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Register(typeof(DeclaringType), "avares://Demo/Nested.axaml");

        var found = XamlSourceGenTypeUriRegistry.TryGetUri(typeof(DeclaringType.NestedType), out var uri);

        Assert.True(found);
        Assert.Equal("avares://Demo/Nested.axaml", uri);
    }

    private sealed class TargetType
    {
    }

    private sealed class GenericTarget<T>
    {
    }

    private sealed class DeclaringType
    {
        public sealed class NestedType
        {
        }
    }
}
