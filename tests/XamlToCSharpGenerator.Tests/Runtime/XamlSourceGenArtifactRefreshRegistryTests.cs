using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenArtifactRefreshRegistryTests : IDisposable
{
    public void Dispose()
    {
        GeneratedArtifactTestRestore.RestoreAllLoadedGeneratedArtifacts();
    }

    [Fact]
    public void TryRefresh_Invokes_Registered_Callback_For_Exact_Type()
    {
        XamlSourceGenArtifactRefreshRegistry.Clear();
        var invocationCount = 0;
        XamlSourceGenArtifactRefreshRegistry.Register(
            typeof(ExactRefreshTarget),
            () => invocationCount++);

        var refreshed = XamlSourceGenArtifactRefreshRegistry.TryRefresh(typeof(ExactRefreshTarget));

        Assert.True(refreshed);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void TryRefresh_Resolves_Declaring_Type_For_Nested_Metadata_Type()
    {
        XamlSourceGenArtifactRefreshRegistry.Clear();
        var invocationCount = 0;
        XamlSourceGenArtifactRefreshRegistry.Register(
            typeof(DeclaringRefreshTarget),
            () => invocationCount++);

        var refreshed = XamlSourceGenArtifactRefreshRegistry.TryRefresh(typeof(DeclaringRefreshTarget.NestedMetadataType));

        Assert.True(refreshed);
        Assert.Equal(1, invocationCount);
    }

    [Fact]
    public void TryRefresh_Returns_False_When_No_Callback_Is_Registered()
    {
        XamlSourceGenArtifactRefreshRegistry.Clear();

        var refreshed = XamlSourceGenArtifactRefreshRegistry.TryRefresh(typeof(UnregisteredRefreshTarget));

        Assert.False(refreshed);
    }

    private sealed class ExactRefreshTarget
    {
    }

    private sealed class DeclaringRefreshTarget
    {
        public sealed class NestedMetadataType
        {
        }
    }

    private sealed class UnregisteredRefreshTarget
    {
    }
}
