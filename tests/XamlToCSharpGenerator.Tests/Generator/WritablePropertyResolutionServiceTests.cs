using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class WritablePropertyResolutionServiceTests
{
    [Fact]
    public void Resolves_Writable_Property_From_Base_Type()
    {
        const string source = """
            namespace Demo
            {
                public abstract class BindingBase
                {
                    public string? Converter { get; set; }
                }

                public sealed class Binding : BindingBase
                {
                }
            }
            """;

        var compilation = CSharpCompilation.Create(
            "WritablePropertyResolutionServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);

        var bindingType = compilation.GetTypeByMetadataName("Demo.Binding");
        Assert.NotNull(bindingType);

        var service = new WritablePropertyResolutionService();

        var result = service.TryGetWritableProperty(bindingType!, "Converter", out var propertySymbol);

        Assert.True(result);
        Assert.NotNull(propertySymbol);
        Assert.Equal("Converter", propertySymbol!.Name);
        Assert.Equal(
            compilation.GetSpecialType(SpecialType.System_String),
            propertySymbol.Type,
            SymbolEqualityComparer.Default);
        Assert.Equal("BindingBase", propertySymbol.ContainingType.Name);
    }
}
