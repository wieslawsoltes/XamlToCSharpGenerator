using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ClrPropertyAssignmentCreationServiceTests
{
    [Fact]
    public void Create_Populates_Clr_Assignment_Metadata()
    {
        var compilation = CreateCompilation();
        var property = compilation.GetTypeByMetadataName("Demo.Control")!
            .GetMembers("Title")
            .OfType<IPropertySymbol>()
            .Single();
        var service = new ClrPropertyAssignmentCreationService(
            static (_, requirements) => requirements.NeedsServiceProvider);

        var assignment = service.Create(
            property,
            "\"Hello\"",
            line: 3,
            column: 4,
            condition: null,
            valueKind: ResolvedValueKind.Binding,
            requiresStaticResourceResolver: true,
            valueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
            preserveBindingValue: true,
            clrSetterUnsafeAccessorMethodName: "__SetTitle");

        Assert.Equal("Title", assignment.PropertyName);
        Assert.Equal("\"Hello\"", assignment.ValueExpression);
        Assert.Equal("global::Demo.Control", assignment.ClrPropertyOwnerTypeName);
        Assert.Equal("string", assignment.ClrPropertyTypeName);
        Assert.Equal(ResolvedValueKind.Binding, assignment.ValueKind);
        Assert.True(assignment.RequiresStaticResourceResolver);
        Assert.True(assignment.PreserveBindingValue);
        Assert.True(assignment.RequiresObjectInitializer);
        Assert.Equal("__SetTitle", assignment.ClrSetterUnsafeAccessorMethodName);
        Assert.True(assignment.IsInitOnlyClrProperty);
        Assert.True(assignment.IsRequiredClrProperty);
    }

    [Fact]
    public void Create_From_Conversion_Uses_Effective_Conversion_Metadata()
    {
        var compilation = CreateCompilation();
        var property = compilation.GetTypeByMetadataName("Demo.Control")!
            .GetMembers("Title")
            .OfType<IPropertySymbol>()
            .Single();
        var service = new ClrPropertyAssignmentCreationService(
            static (_, requirements) => requirements.NeedsServiceProvider);
        var conversion = new ResolvedValueConversionResult(
            Expression: "Bind(Title)",
            ValueKind: ResolvedValueKind.Binding,
            RequiresRuntimeServiceProvider: true,
            RequiresParentStack: true,
            RequiresProvideValueTarget: true,
            RequiresRootObject: true,
            RequiresBaseUri: true,
            RequiresStaticResourceResolver: false);

        var assignment = service.Create(
            property,
            conversion,
            line: 8,
            column: 9,
            condition: null,
            preserveBindingValue: false,
            clrSetterUnsafeAccessorMethodName: null);

        Assert.Equal("Bind(Title)", assignment.ValueExpression);
        Assert.Equal(ResolvedValueKind.Binding, assignment.ValueKind);
        Assert.True(assignment.ValueRequirements.NeedsServiceProvider);
        Assert.True(assignment.ValueRequirements.NeedsParentStack);
        Assert.True(assignment.ValueRequirements.NeedsProvideValueTarget);
        Assert.True(assignment.ValueRequirements.NeedsRootObject);
        Assert.True(assignment.ValueRequirements.NeedsBaseUri);
        Assert.True(assignment.RequiresObjectInitializer);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public class Control
                                  {
                                      public required string Title { get; init; }
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "ClrPropertyAssignmentCreationServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RequiredMemberAttribute).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
