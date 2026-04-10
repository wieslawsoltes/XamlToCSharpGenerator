using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CompiledBindingEmissionServiceTests
{
    [Fact]
    public void RewriteSourceReceiver_Rewrites_Source_Placeholder()
    {
        var service = new CompiledBindingEmissionService();

        var rewritten = service.RewriteSourceReceiver("__source.WindowState == __source.PreviousState", "__source", "source");

        Assert.Equal("source.WindowState == source.PreviousState", rewritten);
    }
}
