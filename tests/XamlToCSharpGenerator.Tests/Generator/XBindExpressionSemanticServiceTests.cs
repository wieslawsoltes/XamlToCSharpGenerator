using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XBindExpressionSemanticServiceTests
{
    [Fact]
    public void Lowers_Named_Element_References_Through_Runtime_Helper()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var document = CreateDocument();

        var result = service.TryLowerExpression(
            new XBindIdentifierExpression("inputBox"),
            new XBindLoweringContext(
                compilation,
                document,
                document.RootObject.ChildObjects[1],
                compilation.GetTypeByMetadataName("Demo.RootViewModel")!,
                compilation.GetTypeByMetadataName("Demo.RootView")!,
                compilation.GetTypeByMetadataName("Demo.Target")!,
                new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null)),
            out var loweredExpression,
            out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.Contains("ResolveNamedElement<global::Demo.InputBox>", loweredExpression.Expression, StringComparison.Ordinal);
        Assert.Contains("\"inputBox\"", loweredExpression.Expression, StringComparison.Ordinal);
    }

    [Fact]
    public void Builds_Runtime_Dependency_Array_For_Named_Element_Paths()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var document = CreateDocument();
        var loweringContext = new XBindLoweringContext(
            compilation,
            document,
            document.RootObject.ChildObjects[1],
            compilation.GetTypeByMetadataName("Demo.RootViewModel")!,
            compilation.GetTypeByMetadataName("Demo.RootView")!,
            compilation.GetTypeByMetadataName("Demo.Target")!,
            new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null));

        var dependencies = service.CollectDependencies(
            new XBindMemberAccessExpression(
                new XBindIdentifierExpression("inputBox"),
                "Text",
                IsConditional: false),
            loweringContext);

        var expression = service.BuildPathReferenceArrayLiteral(dependencies);

        Assert.Single(dependencies);
        Assert.Contains("SourceGenBindingDependency", expression, StringComparison.Ordinal);
        Assert.Contains("SourceGenBindingSourceKind.ElementName", expression, StringComparison.Ordinal);
        Assert.Contains("\"Text\"", expression, StringComparison.Ordinal);
        Assert.Contains("\"inputBox\"", expression, StringComparison.Ordinal);
    }

    private static XBindExpressionSemanticService CreateService()
    {
        return new XBindExpressionSemanticService(
            static (compilation, _, typeToken, _) => compilation.GetTypeByMetadataName("Demo." + typeToken.Trim()),
            static (compilation, _, xmlTypeName) => compilation.GetTypeByMetadataName("Demo." + xmlTypeName),
            static node => string.Equals(node.XmlTypeName, "DataTemplate", StringComparison.Ordinal),
            static value => value.Replace("\\", "\\\\").Replace("\"", "\\\""));
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class RootView { }
                                  public sealed class RootViewModel { public string? Title { get; set; } }
                                  public sealed class Target { }
                                  public sealed class InputBox { public string? Text { get; set; } }
                                  public sealed class Label { }
                              }
                              """;

        return CSharpCompilation.Create(
            "XBindExpressionSemanticServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
    }

    private static XamlDocumentModel CreateDocument()
    {
        var root = new XamlObjectNode(
            XmlNamespace: "https://github.com/avaloniaui",
            XmlTypeName: "RootView",
            Key: null,
            Name: null,
            FieldModifier: null,
            DataType: null,
            CompileBindings: null,
            FactoryMethod: null,
            TypeArguments: ImmutableArray<string>.Empty,
            ArrayItemType: null,
            ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
            TextContent: null,
            PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
            ChildObjects:
            [
                new XamlObjectNode(
                    "https://github.com/avaloniaui",
                    "InputBox",
                    null,
                    "inputBox",
                    null,
                    null,
                    null,
                    null,
                    ImmutableArray<string>.Empty,
                    null,
                    ImmutableArray<XamlObjectNode>.Empty,
                    null,
                    ImmutableArray<XamlPropertyAssignment>.Empty,
                    ImmutableArray<XamlObjectNode>.Empty,
                    ImmutableArray<XamlPropertyElement>.Empty,
                    2,
                    1),
                new XamlObjectNode(
                    "https://github.com/avaloniaui",
                    "Label",
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    ImmutableArray<string>.Empty,
                    null,
                    ImmutableArray<XamlObjectNode>.Empty,
                    null,
                    ImmutableArray<XamlPropertyAssignment>.Empty,
                    ImmutableArray<XamlObjectNode>.Empty,
                    ImmutableArray<XamlPropertyElement>.Empty,
                    3,
                    1)
            ],
            PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
            Line: 1,
            Column: 1);

        return new XamlDocumentModel(
            FilePath: "MainView.xaml",
            TargetPath: "MainView.xaml",
            ClassFullName: "Demo.RootView",
            ClassModifier: "public",
            Precompile: null,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: root,
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }
}
