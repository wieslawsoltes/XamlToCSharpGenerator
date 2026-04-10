using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class TypeSymbolLookupSemanticsServiceTests
{
    [Fact]
    public void IsTypeAssignableTo_Returns_True_For_Base_And_Interface_Types()
    {
        var compilation = CreateCompilation();
        var derivedType = compilation.GetTypeByMetadataName("Demo.DerivedControl")!;
        var baseType = compilation.GetTypeByMetadataName("Demo.BaseControl")!;
        var interfaceType = compilation.GetTypeByMetadataName("Demo.IInteractive")!;

        Assert.True(TypeSymbolLookupSemanticsService.IsTypeAssignableTo(derivedType, baseType));
        Assert.True(TypeSymbolLookupSemanticsService.IsTypeAssignableTo(derivedType, interfaceType));
    }

    [Fact]
    public void FindProperty_Finds_Property_On_Base_Type()
    {
        var compilation = CreateCompilation();
        var derivedType = compilation.GetTypeByMetadataName("Demo.DerivedControl")!;

        var property = TypeSymbolLookupSemanticsService.FindProperty(derivedType, "Content");

        Assert.NotNull(property);
        Assert.Equal("Content", property!.Name);
    }

    [Fact]
    public void FindEvent_Finds_Event_On_Implemented_Interface()
    {
        var compilation = CreateCompilation();
        var interfaceType = compilation.GetTypeByMetadataName("Demo.IInteractive")!;

        var eventSymbol = TypeSymbolLookupSemanticsService.FindEvent(interfaceType, "Activated");

        Assert.NotNull(eventSymbol);
        Assert.Equal("Activated", eventSymbol!.Name);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public interface IInteractive
                                  {
                                      event System.EventHandler Activated;
                                  }

                                  public class BaseControl
                                  {
                                      public string Content { get; set; } = string.Empty;
                                  }

                                  public sealed class DerivedControl : BaseControl, IInteractive
                                  {
                                      public event System.EventHandler? Activated;
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "TypeSymbolLookupSemanticsServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ]);
    }
}
