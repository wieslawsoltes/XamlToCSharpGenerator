using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class XBindSourceConfigurationServiceTests
{
    [Fact]
    public void Resolves_ElementName_Source_Configuration()
    {
        var compilation = CreateCompilation();
        var document = CreateDocument();
        var service = new XBindSourceConfigurationService(
            static (Compilation currentCompilation, XamlDocumentModel documentModel, string typeToken, string? fallbackClrNamespace) =>
            {
                _ = documentModel;
                _ = fallbackClrNamespace;
                return currentCompilation.GetTypeByMetadataName("Demo." + typeToken);
            },
            static (Compilation currentCompilation, XamlDocumentModel documentModel, XamlObjectNode currentNode, string elementName, out INamedTypeSymbol? typeSymbol) =>
            {
                _ = documentModel;
                _ = currentNode;
                typeSymbol = currentCompilation.GetTypeByMetadataName(elementName == "inputBox" ? "Demo.InputBox" : "Demo.RootViewModel");
                return typeSymbol is not null;
            },
            static (Compilation currentCompilation, XamlDocumentModel documentModel, RelativeSourceMarkup relativeSource, out string expression, out string errorMessage) =>
            {
                _ = currentCompilation;
                _ = documentModel;
                _ = relativeSource;
                expression = "new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.Self)";
                errorMessage = string.Empty;
                return true;
            },
            static (string? sourceValue, out string elementName) =>
            {
                _ = sourceValue;
                elementName = string.Empty;
                return false;
            },
            static (Compilation currentCompilation, XamlDocumentModel documentModel, string rawSource, out string expression, out string errorMessage) =>
            {
                _ = currentCompilation;
                _ = documentModel;
                expression = rawSource;
                errorMessage = string.Empty;
                return true;
            });

        var result = service.TryResolveSourceConfiguration(
            compilation,
            document,
            document.RootObject,
            new XBindMarkup("Text", null, null, "inputBox", null, null, null, null, null, null, null, null, null, null, null, null, false, null),
            compilation.GetTypeByMetadataName("Demo.RootViewModel")!,
            ambientDataContextType: null,
            compilation.GetTypeByMetadataName("Demo.RootView")!,
            targetType: compilation.GetTypeByMetadataName("Demo.Target"),
            new XBindPathReference(XBindSourceReferenceKind.Root, ".", null, null, null),
            out var sourceConfiguration,
            out var errorMessage);

        Assert.True(result, errorMessage);
        Assert.Equal("global::Demo.InputBox", sourceConfiguration.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        Assert.Equal(XBindSourceReferenceKind.ElementName, sourceConfiguration.SourceReference.Kind);
        Assert.Equal("inputBox", sourceConfiguration.SourceReference.ElementName);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class RootView { }
                                  public sealed class RootViewModel { }
                                  public sealed class InputBox { public string? Text { get; set; } }
                                  public sealed class Target { }
                              }
                              """;

        return CSharpCompilation.Create(
            "XBindSourceConfigurationServiceTests",
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
                    1)
            ],
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
