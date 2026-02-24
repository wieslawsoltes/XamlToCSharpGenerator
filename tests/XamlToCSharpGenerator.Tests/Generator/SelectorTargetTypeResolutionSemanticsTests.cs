using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SelectorTargetTypeResolutionSemanticsTests
{
    [Fact]
    public void ResolveTargetType_Resolves_Common_Base_Type_For_Multiple_Branches()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class Control { }
            public class TextBlock : Control { }
            public class Button : Control { }
            """);
        var controlType = compilation.GetTypeByMetadataName("Demo.Control");
        Assert.NotNull(controlType);

        var branches = ImmutableArray.Create(
            new SelectorBranchInfo("TextBlock", 0),
            new SelectorBranchInfo("Button", 12));

        var result = SelectorTargetTypeResolutionSemantics.ResolveTargetType(
            branches,
            token => compilation.GetTypeByMetadataName("Demo." + token),
            IsTypeAssignableTo);

        Assert.NotNull(result.TargetType);
        Assert.Equal(
            controlType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            result.TargetType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Null(result.UnresolvedTypeToken);
        Assert.Equal(0, result.UnresolvedTypeOffset);
    }

    [Fact]
    public void ResolveTargetType_Reports_Unresolved_Branch_Type_And_Offset()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;

            public class TextBlock { }
            """);
        var branches = ImmutableArray.Create(
            new SelectorBranchInfo("TextBlock", 3),
            new SelectorBranchInfo("MissingControl", 27));

        var result = SelectorTargetTypeResolutionSemantics.ResolveTargetType(
            branches,
            token => compilation.GetTypeByMetadataName("Demo." + token),
            IsTypeAssignableTo);

        Assert.Null(result.TargetType);
        Assert.Equal("MissingControl", result.UnresolvedTypeToken);
        Assert.Equal(27, result.UnresolvedTypeOffset);
    }

    [Fact]
    public void ResolveTargetType_Returns_Default_When_Branch_Has_No_Type_Token()
    {
        var compilation = CreateCompilation(
            """
            namespace Demo;
            public class TextBlock { }
            """);
        var branches = ImmutableArray.Create(
            new SelectorBranchInfo("TextBlock", 0),
            new SelectorBranchInfo(null, 4));

        var result = SelectorTargetTypeResolutionSemantics.ResolveTargetType(
            branches,
            token => compilation.GetTypeByMetadataName("Demo." + token),
            IsTypeAssignableTo);

        Assert.Null(result.TargetType);
        Assert.Null(result.UnresolvedTypeToken);
        Assert.Equal(0, result.UnresolvedTypeOffset);
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

    private static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is not INamedTypeSymbol namedSource)
        {
            return false;
        }

        for (var current = namedSource.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, targetType))
            {
                return true;
            }
        }

        return false;
    }
}
