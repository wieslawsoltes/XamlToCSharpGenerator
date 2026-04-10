using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class CSharpExpressionClassificationServiceTests
{
    [Fact]
    public void TryParseCSharpExpressionMarkup_Recognizes_Implicit_NonShorthand_Expression()
    {
        var service = CreateService();

        var handled = service.TryParseCSharpExpressionMarkup(
            "{FirstName + '!'}",
            CreateCompilation(),
            CreateDocument(),
            csharpExpressionsEnabled: false,
            implicitCSharpExpressionsEnabled: true,
            out var code,
            out var isExplicitExpression);

        Assert.True(handled);
        Assert.Equal("FirstName + '!'", code);
        Assert.False(isExplicitExpression);
    }

    [Fact]
    public void TryParseCSharpExpressionMarkup_Does_Not_Classify_Known_Markup_Extension_As_CSharp()
    {
        var service = CreateService();

        var handled = service.TryParseCSharpExpressionMarkup(
            "{Binding FirstName}",
            CreateCompilation(),
            CreateDocument(),
            csharpExpressionsEnabled: false,
            implicitCSharpExpressionsEnabled: true,
            out _,
            out _);

        Assert.False(handled);
    }

    private static CSharpExpressionClassificationService CreateService()
    {
        return new CSharpExpressionClassificationService(
            static (string value, out MarkupExtensionInfo markup) =>
            {
                if (value == "{Binding FirstName}")
                {
                    markup = new MarkupExtensionInfo(
                        "Binding",
                        ImmutableArray.Create("FirstName"),
                        ImmutableDictionary<string, string>.Empty,
                        ImmutableArray<MarkupExtensionArgument>.Empty);
                    return true;
                }

                markup = default;
                return false;
            },
            ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "Binding"),
            static (Compilation _, XamlDocumentModel _, string _, out INamedTypeSymbol? extensionType) =>
            {
                extensionType = null;
                return false;
            });
    }

    private static CSharpCompilation CreateCompilation()
    {
        return CSharpCompilation.Create(
            "Tests",
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            ]);
    }

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "/tmp/MainView.axaml",
            TargetPath: "/tmp/MainView.axaml",
            ClassFullName: "Demo.MainView",
            ClassModifier: "public",
            Precompile: null,
            RootObject: new XamlObjectNode(
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
                Column: 1),
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }
}
