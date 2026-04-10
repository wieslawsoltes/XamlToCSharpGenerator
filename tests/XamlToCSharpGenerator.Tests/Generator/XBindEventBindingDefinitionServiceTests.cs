using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XBindEventBindingDefinitionServiceTests
{
    [Fact]
    public void Returns_Error_When_Root_Type_Is_Missing()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var targetType = compilation.GetTypeByMetadataName("Demo.Target")!;
        var eventHandlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;

        var result = service.TryBuildDefinition(
            compilation,
            CreateDocument(),
            CreateObjectNode(),
            new XBindMarkup("HandleClick", null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, null),
            "Click",
            ambientDataContextType: null,
            rootType: null,
            targetType,
            eventHandlerType,
            isInsideDataTemplate: false,
            line: 1,
            column: 1,
            out var definition,
            out var errorMessage);

        Assert.False(result);
        Assert.Null(definition);
        Assert.Equal("x:Bind event 'Click' requires an x:Class-backed root type.", errorMessage);
    }

    [Fact]
    public void Returns_Error_When_Event_Path_Is_Empty()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;
        var targetType = compilation.GetTypeByMetadataName("Demo.Target")!;
        var eventHandlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;

        var result = service.TryBuildDefinition(
            compilation,
            CreateDocument(),
            CreateObjectNode(),
            new XBindMarkup(string.Empty, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, null),
            "Click",
            ambientDataContextType: null,
            rootType,
            targetType,
            eventHandlerType,
            isInsideDataTemplate: false,
            line: 1,
            column: 1,
            out var definition,
            out var errorMessage);

        Assert.False(result);
        Assert.Null(definition);
        Assert.Equal("x:Bind event 'Click' requires a method path.", errorMessage);
    }

    private static XBindEventBindingDefinitionService CreateService()
    {
        return new XBindEventBindingDefinitionService(
            static (Compilation compilation, XamlDocumentModel document, string? rawDataType, out INamedTypeSymbol? explicitSourceType, out string errorMessage) =>
            {
                _ = compilation;
                _ = document;
                _ = rawDataType;
                explicitSourceType = null;
                errorMessage = string.Empty;
                return true;
            },
            static (Compilation compilation, XamlDocumentModel document, XamlObjectNode currentNode, XBindMarkup xBindMarkup, INamedTypeSymbol baseSourceType, INamedTypeSymbol? ambientDataContextType, INamedTypeSymbol rootType, INamedTypeSymbol? targetType, XBindPathReference baseSourceReference, out ResolvedXBindSourceConfiguration sourceConfiguration, out string errorMessage) =>
            {
                _ = compilation;
                _ = document;
                _ = currentNode;
                _ = xBindMarkup;
                _ = baseSourceType;
                _ = ambientDataContextType;
                _ = rootType;
                _ = targetType;
                _ = baseSourceReference;
                sourceConfiguration = default;
                errorMessage = string.Empty;
                return true;
            },
            static (INamedTypeSymbol delegateType, out string delegateTypeName, out ImmutableArray<ResolvedEventBindingParameter> parameters) =>
            {
                delegateTypeName = delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                parameters = ImmutableArray<ResolvedEventBindingParameter>.Empty;
                return true;
            },
            static (XBindExpressionNode expression, XBindLoweringContext loweringContext, out XBindLoweredExpression loweredExpression, out string errorMessage) =>
            {
                _ = expression;
                _ = loweringContext;
                loweredExpression = default;
                errorMessage = "Not expected";
                return false;
            },
            static (XBindExpressionNode expression, string loweredTargetExpression, ImmutableArray<string> lambdaParameterNames) =>
            {
                _ = expression;
                _ = loweredTargetExpression;
                _ = lambdaParameterNames;
                return ImmutableArray<string>.Empty;
            },
            static (XBindPathReference sourceReference) =>
            {
                _ = sourceReference;
                return string.Empty;
            },
            static (string eventName, string stableKey) =>
            {
                _ = eventName;
                _ = stableKey;
                return string.Empty;
            },
            static (string normalizedExpression, INamedTypeSymbol delegateType, INamedTypeSymbol sourceType, INamedTypeSymbol rootType, INamedTypeSymbol? targetType, bool isLambdaExpression) =>
            {
                _ = normalizedExpression;
                _ = delegateType;
                _ = sourceType;
                _ = rootType;
                _ = targetType;
                _ = isLambdaExpression;
                return string.Empty;
            });
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class Root { }
                                  public sealed class Target { }
                                  public delegate void ClickHandler(object sender, object args);
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "XBindEventBindingDefinitionServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ]);
    }

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "MainView.xaml",
            TargetPath: "MainView.xaml",
            ClassFullName: "Demo.Root",
            ClassModifier: "public",
            Precompile: null,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: CreateObjectNode(),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }

    private static XamlObjectNode CreateObjectNode()
    {
        return new XamlObjectNode(
            XmlNamespace: "https://github.com/avaloniaui",
            XmlTypeName: "UserControl",
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
            ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
            PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
            Line: 1,
            Column: 1);
    }
}
