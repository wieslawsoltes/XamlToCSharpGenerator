using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class EventHandlerBindingServiceTests
{
    [Fact]
    public void TryParseHandlerName_Accepts_Identifier()
    {
        var service = CreateService();

        var success = service.TryParseHandlerName("OnButtonClick", out var handlerName);

        Assert.True(success);
        Assert.Equal("OnButtonClick", handlerName);
    }

    [Fact]
    public void HasCompatibleInstanceMethod_Matches_Delegate_Signature()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;
        var handlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;

        var success = service.HasCompatibleInstanceMethod(rootType, "OnClick", handlerType);

        Assert.True(success);
    }

    [Fact]
    public void TryBuildDelegateMethodGroupValueExpression_Builds_Root_Method_Group()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;
        var handlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;

        var success = service.TryBuildDelegateMethodGroupValueExpression(
            "OnClick",
            handlerType,
            rootType,
            out var expression);

        Assert.True(success);
        Assert.Equal(
            "new global::Demo.ClickHandler(((global::Demo.Root)__ROOT__).OnClick)",
            expression);
    }

    private static EventHandlerBindingService CreateService()
    {
        return new EventHandlerBindingService(
            static (sourceType, targetType) =>
                SymbolEqualityComparer.Default.Equals(sourceType, targetType),
            "__ROOT__");
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public delegate void ClickHandler(object sender, object args);

                                  public sealed class Root
                                  {
                                      public void OnClick(object sender, object args)
                                      {
                                      }
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "EventHandlerBindingServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ]);
    }
}
