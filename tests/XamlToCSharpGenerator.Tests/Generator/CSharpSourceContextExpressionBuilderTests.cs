using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CSharpSourceContextExpressionBuilderTests
{
    [Fact]
    public void TryBuildAccessorExpression_Rewrites_Source_Members_And_Collects_Dependencies()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public sealed class PersonVm
                {
                    public string Name { get; set; } = string.Empty;
                    public string Surname;
                }
            }
            """);
        var sourceType = compilation.GetTypeByMetadataName("Demo.PersonVm");
        Assert.NotNull(sourceType);

        var success = CSharpSourceContextExpressionBuilder.TryBuildAccessorExpression(
            compilation,
            sourceType!,
            "Name + \" \" + Surname",
            "source",
            out var result,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Contains("source.Name", result.AccessorExpression, StringComparison.Ordinal);
        Assert.Contains("source.Surname", result.AccessorExpression, StringComparison.Ordinal);
        Assert.Equal(new[] { "Name", "Surname" }, result.DependencyNames.ToArray());
    }

    [Fact]
    public void TryBuildAccessorExpression_Does_Not_Treat_Lambda_Parameters_As_Dependencies()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public sealed class PersonVm
                {
                    public int Score { get; set; }
                }
            }
            """);
        var sourceType = compilation.GetTypeByMetadataName("Demo.PersonVm");
        Assert.NotNull(sourceType);

        var success = CSharpSourceContextExpressionBuilder.TryBuildAccessorExpression(
            compilation,
            sourceType!,
            "Score is var item ? item : Score",
            "source",
            out var result,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Contains("source.Score", result.AccessorExpression, StringComparison.Ordinal);
        Assert.Equal(new[] { "Score" }, result.DependencyNames.ToArray());
    }

    [Fact]
    public void TryBuildAccessorExpression_Returns_Parse_Error_For_Invalid_Expression()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public sealed class PersonVm
                {
                    public string Name { get; set; } = string.Empty;
                }
            }
            """);
        var sourceType = compilation.GetTypeByMetadataName("Demo.PersonVm");
        Assert.NotNull(sourceType);

        var success = CSharpSourceContextExpressionBuilder.TryBuildAccessorExpression(
            compilation,
            sourceType!,
            "Name +",
            "source",
            out _,
            out var errorMessage);

        Assert.False(success);
        Assert.False(string.IsNullOrWhiteSpace(errorMessage));
    }

    [Fact]
    public void TryBuildAccessorExpression_Returns_Validation_Error_For_Unbound_Identifier()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo
            {
                public sealed class PersonVm
                {
                    public int Score { get; set; }
                }
            }
            """);
        var sourceType = compilation.GetTypeByMetadataName("Demo.PersonVm");
        Assert.NotNull(sourceType);

        var success = CSharpSourceContextExpressionBuilder.TryBuildAccessorExpression(
            compilation,
            sourceType!,
            "MissingValue + Score",
            "source",
            out _,
            out var errorMessage);

        Assert.False(success);
        Assert.Contains("does not exist", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
        };

        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
