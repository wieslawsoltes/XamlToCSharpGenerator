using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class EventBindingDefinitionServiceTests
{
    private static readonly MarkupExpressionParser Parser =
        new(new MarkupExpressionParserOptions(AllowLegacyInvalidNamedArgumentFallback: true));

    [Fact]
    public void TryBuildParsedDefinition_Returns_Error_When_Root_Type_Is_Missing()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var eventHandlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;
        var nodeDataType = compilation.GetTypeByMetadataName("Demo.ViewModel")!;

        var success = service.TryBuildParsedDefinition(
            rawValue: "{EventBinding Command=SaveCommand}",
            eventName: "Click",
            compilation: compilation,
            eventHandlerType: eventHandlerType,
            nodeDataType: nodeDataType,
            rootTypeSymbol: null,
            line: 1,
            column: 1,
            out var result,
            out var errorMessage);

        Assert.False(success);
        Assert.Equal(default, result);
        Assert.Equal("EventBinding on 'Click' requires x:Class-backed root type.", errorMessage);
    }

    [Fact]
    public void TryBuildParsedDefinition_Parses_Command_Markup_And_Preserves_Line_Info()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var eventHandlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;
        var nodeDataType = compilation.GetTypeByMetadataName("Demo.ViewModel")!;
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;

        var success = service.TryBuildParsedDefinition(
            rawValue: "{EventBinding Command=SaveCommand, Parameter='42'}",
            eventName: "Click",
            compilation: compilation,
            eventHandlerType: eventHandlerType,
            nodeDataType: nodeDataType,
            rootTypeSymbol: rootType,
            line: 3,
            column: 4,
            out var result,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.Equal(ResolvedEventBindingTargetKind.Command, result.Definition.TargetKind);
        Assert.Equal("SaveCommand", result.Definition.TargetPath);
        Assert.True(result.Definition.HasParameterValueExpression);
        Assert.Equal("lit(42)", result.Definition.ParameterValueExpression);
        Assert.Equal(3, result.Definition.Line);
        Assert.Equal(4, result.Definition.Column);
        Assert.Contains("__AXSG_EventBinding_Click_", result.Definition.GeneratedMethodName, StringComparison.Ordinal);
        Assert.Empty(result.WarningMessages);
    }

    [Fact]
    public void TryBuildInlineCodeDefinition_Propagates_Line_And_Column()
    {
        var service = CreateService();
        var compilation = CreateCompilation();
        var eventHandlerType = compilation.GetTypeByMetadataName("Demo.ClickHandler")!;
        var nodeDataType = compilation.GetTypeByMetadataName("Demo.ViewModel")!;
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;
        var targetType = compilation.GetTypeByMetadataName("Demo.Target")!;

        var success = service.TryBuildInlineCodeDefinition(
            rawCode: "source.Count++;",
            isLambdaExpression: false,
            eventName: "Click",
            eventHandlerType: eventHandlerType,
            compilation: compilation,
            nodeDataType: nodeDataType,
            targetType: targetType,
            rootTypeSymbol: rootType,
            documentClassFullName: "Demo.Root",
            line: 10,
            column: 20,
            out var definition,
            out var errorMessage);

        Assert.True(success, errorMessage);
        Assert.NotNull(definition);
        Assert.Equal(10, definition!.Line);
        Assert.Equal(20, definition.Column);
        Assert.Contains("__AXSG_EventBinding_Click_", definition.GeneratedMethodName, StringComparison.Ordinal);
    }

    private static EventBindingDefinitionService CreateService()
    {
        var semanticService = new EventBindingSemanticBindingService(
            static (sourceType, targetType) =>
                SymbolEqualityComparer.Default.Equals(sourceType, targetType),
            static compilation => compilation.GetTypeByMetadataName("System.Windows.Input.ICommand"));

        return new EventBindingDefinitionService(
            semanticService,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression);
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return Parser.TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryConvertLiteralValueExpression(string literalValue, out string expression)
    {
        expression = "lit(" + literalValue + ")";
        return true;
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class Root { }
                                  public sealed class Target { }
                                  public sealed class ViewModel
                                  {
                                      public int Count { get; set; }
                                      public object? SaveCommand { get; set; }
                                  }

                                  public delegate void ClickHandler(object sender, object args);
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "EventBindingDefinitionServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ]);
    }
}
