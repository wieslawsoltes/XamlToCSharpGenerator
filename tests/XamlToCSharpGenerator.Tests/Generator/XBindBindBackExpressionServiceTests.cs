using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XBindBindBackExpressionServiceTests
{
    [Fact]
    public void Builds_Assignment_Based_BindBack_Lambda()
    {
        var compilation = CreateCompilation();
        var document = CreateDocument();
        var semanticService = new XBindExpressionSemanticService(
            static (currentCompilation, _, typeToken, _) => currentCompilation.GetTypeByMetadataName(typeToken),
            static (currentCompilation, _, xmlTypeName) => currentCompilation.GetTypeByMetadataName(xmlTypeName),
            static _ => false,
            static value => value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        var service = new XBindBindBackExpressionService(
            semanticService,
            static (currentCompilation, contractId) => contractId == TypeContractId.SystemActionOfT1T2
                ? currentCompilation.GetTypeByMetadataName("System.Action`2")
                : null);

        var result = service.TryBuildBindBackExpression(
            compilation,
            document,
            new XBindIdentifierExpression("Text"),
            rawBindBack: null,
            new XBindLoweringContext(
                compilation,
                document,
                document.RootObject,
                compilation.GetTypeByMetadataName("Demo.RootViewModel")!,
                compilation.GetTypeByMetadataName("Demo.RootView")!,
                compilation.GetTypeByMetadataName("Demo.Target")!,
                new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null)),
            compilation.GetTypeByMetadataName("Demo.RootViewModel")!,
            compilation.GetTypeByMetadataName("Demo.RootView")!,
            compilation.GetTypeByMetadataName("Demo.Target"),
            bindingValueType: compilation.GetSpecialType(SpecialType.System_String),
            resultTypeSymbol: compilation.GetSpecialType(SpecialType.System_String),
            out var bindBackExpression,
            out var bindBackValueTypeExpression,
            out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.Contains("source.Text =", bindBackExpression, StringComparison.Ordinal);
        Assert.Contains("typeof(string)", bindBackValueTypeExpression, StringComparison.OrdinalIgnoreCase);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class RootView { }
                                  public sealed class Target { }
                                  public sealed class RootViewModel
                                  {
                                      public string? Text { get; set; }
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "XBindBindBackExpressionServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]);
    }

    private static XamlDocumentModel CreateDocument()
    {
        var root = new XamlObjectNode(
            "https://github.com/avaloniaui",
            "RootView",
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
            1,
            1);

        return new XamlDocumentModel(
            "MainView.xaml",
            "MainView.xaml",
            "Demo.RootView",
            "public",
            null,
            ImmutableDictionary<string, string>.Empty,
            root,
            ImmutableArray<XamlNamedElement>.Empty,
            ImmutableArray<XamlResourceDefinition>.Empty,
            ImmutableArray<XamlTemplateDefinition>.Empty,
            ImmutableArray<XamlStyleDefinition>.Empty,
            ImmutableArray<XamlControlThemeDefinition>.Empty,
            ImmutableArray<XamlIncludeDefinition>.Empty,
            true);
    }
}
