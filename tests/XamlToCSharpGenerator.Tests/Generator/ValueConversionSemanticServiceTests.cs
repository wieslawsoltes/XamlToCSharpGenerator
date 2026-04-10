using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ValueConversionSemanticServiceTests
{
    [Fact]
    public void TryConvert_Uses_Framework_Property_Reference_For_Framework_Property_Types()
    {
        var compilation = CreateCompilation();
        var document = CreateDocument();
        var frameworkPropertyType = compilation.GetTypeByMetadataName("Demo.FrameworkProperty")!;
        var service = CreateService(
            isFrameworkPropertyType: static type =>
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Demo.FrameworkProperty",
            tryResolveFrameworkPropertyReferenceExpression: static (
                string value,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                out string expression) =>
            {
                expression = "global::Demo.Control.ValueProperty";
                return value == "Control.Value";
            });

        var handled = service.TryConvert(
            "Control.Value",
            frameworkPropertyType,
            compilation,
            document,
            setterTargetType: null,
            bindingPriorityScope: 0,
            out var conversion);

        Assert.True(handled);
        Assert.Equal("global::Demo.Control.ValueProperty", conversion.Expression);
        Assert.Equal(ResolvedValueKind.Literal, conversion.ValueKind);
    }

    [Fact]
    public void TryConvertMarkupExtension_BindingMarkup_Returns_Binding_Conversion()
    {
        var compilation = CreateCompilation();
        var document = CreateDocument();
        var service = CreateService(
            tryParseMarkupExtension: static (string value, out MarkupExtensionInfo markup) =>
            {
                if (value == "{Binding Name}")
                {
                    markup = new MarkupExtensionInfo(
                        "Binding",
                        ImmutableArray.Create("Name"),
                        ImmutableDictionary<string, string>.Empty,
                        ImmutableArray<MarkupExtensionArgument>.Empty);
                    return true;
                }

                markup = default;
                return false;
            },
            tryParseBindingMarkup: static (string value, out BindingMarkup bindingMarkup) =>
            {
                if (value == "{Binding Name}")
                {
                    bindingMarkup = new BindingMarkup(
                        isCompiledBinding: false,
                        path: "Name",
                        mode: null,
                        elementName: null,
                        relativeSource: null,
                        source: null,
                        dataType: null,
                        converter: null,
                        converterCulture: null,
                        converterParameter: null,
                        stringFormat: null,
                        fallbackValue: null,
                        targetNullValue: null,
                        delay: null,
                        priority: null,
                        updateSourceTrigger: null,
                        hasSourceConflict: false,
                        sourceConflictMessage: null);
                    return true;
                }

                bindingMarkup = default;
                return false;
            },
            tryBuildBindingValueExpression: static (
                Compilation compilation,
                XamlDocumentModel document,
                BindingMarkup bindingMarkup,
                ITypeSymbol targetType,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = "Bind(" + bindingMarkup.Path + ")";
                return true;
            });

        var handled = service.TryConvertMarkupExtension(
            "{Binding Name}",
            compilation.ObjectType,
            compilation,
            document,
            setterTargetType: null,
            bindingPriorityScope: 0,
            out var conversion);

        Assert.True(handled);
        Assert.Equal("Bind(Name)", conversion.Expression);
        Assert.Equal(ResolvedValueKind.Binding, conversion.ValueKind);
        Assert.True(conversion.EffectiveRequirements.NeedsServiceProvider);
        Assert.True(conversion.EffectiveRequirements.NeedsParentStack);
    }

    [Fact]
    public void TryConvert_Strips_Quotes_For_Object_String_Fallback()
    {
        var compilation = CreateCompilation();
        var document = CreateDocument();
        var service = CreateService();

        var handled = service.TryConvert(
            "'marker'",
            compilation.ObjectType,
            compilation,
            document,
            setterTargetType: null,
            bindingPriorityScope: 0,
            out var conversion);

        Assert.True(handled);
        Assert.Equal("\"marker\"", conversion.Expression);
        Assert.Equal(ResolvedValueKind.Literal, conversion.ValueKind);
    }

    private static ValueConversionSemanticService CreateService(
        TryParseMarkupExtensionDelegate? tryParseMarkupExtension = null,
        ValueConversionSemanticService.TryParseBindingMarkupDelegate? tryParseBindingMarkup = null,
        ValueConversionSemanticService.TryBuildBindingValueExpressionDelegate? tryBuildBindingValueExpression = null,
        ValueConversionSemanticService.IsFrameworkPropertyTypeDelegate? isFrameworkPropertyType = null,
        ValueConversionSemanticService.TryResolveFrameworkPropertyReferenceExpressionDelegate? tryResolveFrameworkPropertyReferenceExpression = null)
    {
        var bindingInitializerPlanService = new BindingInitializerPlanService(
            static path => path,
            static (string modeToken, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (
                Compilation compilation,
                XamlDocumentModel document,
                RelativeSourceMarkup relativeSource,
                out string expression,
                out string errorMessage) =>
            {
                expression = string.Empty;
                errorMessage = string.Empty;
                return false;
            },
            static (INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol) =>
            {
                propertySymbol = null;
                return false;
            },
            static (
                string value,
                ITypeSymbol targetType,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static value => value,
            static _ => null);
        var bindingRuntimeProjectionService = new BindingRuntimeProjectionService(
            static (_, _) => null,
            bindingInitializerPlanService,
            new ObjectInitializerExpressionService(),
            static value => value);
        var frameworkBindingProjectionService = new FrameworkBindingProjectionService(
            static (_, _) => null,
            static (_, _) => false,
            bindingRuntimeProjectionService,
            bindingInitializerPlanService,
            new ObjectInitializerExpressionService(),
            new MarkupContextTokenSet("__SP__", "__ROOT__", "__INTERMEDIATE__", "__TARGET__", "__PROPERTY__", "__BASE__", "__STACK__"),
            static path => "new Binding(" + path + ")",
            static path => "new ReflectionBinding(" + path + ")",
            static path => "new TemplateBinding(" + path + ")",
            "TemplatedParent",
            static (string modeToken, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (INamedTypeSymbol typeSymbol, string propertyName, out IPropertySymbol? propertySymbol) =>
            {
                propertySymbol = null;
                return false;
            },
            static (
                string value,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            TypeContractId.AvaloniaBindingBase,
            TypeContractId.AvaloniaBindingInterface,
            TypeContractId.AvaloniaBindingInterface2,
            TypeContractId.AvaloniaBinding,
            TypeContractId.AvaloniaReflectionBindingExtension,
            TypeContractId.AvaloniaTemplateBinding,
            "AssignBindingAttribute");
        var resourceKeyResolutionService = new ResourceKeyResolutionService(
            static (string value, out MarkupExtensionInfo markupExtension) =>
            {
                markupExtension = default;
                return false;
            },
            static (Compilation compilation, XamlDocumentModel document, string token) => null,
            static (
                string token,
                Compilation compilation,
                XamlDocumentModel document,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static value => value);
        var markupRuntimeOperationResolutionService = new MarkupRuntimeOperationResolutionService(resourceKeyResolutionService);
        var markupRuntimeOperationEmissionService = new MarkupRuntimeOperationEmissionService(
            static _ => false,
            static key => "Static(" + key.Expression + ")",
            static key => "Dynamic(" + key.Expression + ")",
            static name => "Reference(" + name + ")",
            static (_, expression) => expression,
            static (_, expression) => expression);
        var commonMarkupExtensionConversionService = new CommonMarkupExtensionConversionService(
            static (_, _) => null,
            static (_, _, _) => null,
            static (
                string memberToken,
                Compilation compilation,
                XamlDocumentModel document,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (
                string? rawToken,
                ITypeSymbol targetType,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (string value, out RelativeSourceMarkup relativeSource) =>
            {
                relativeSource = default;
                return false;
            },
            static (_, _) => string.Empty);
        var typedLiteralValueConversionService = new TypedLiteralValueConversionService(
            static value => value,
            static value => value,
            static (Compilation compilation, XamlDocumentModel document, string? typeExpression, string? fallbackClrNamespace) => null,
            static (string value, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (ITypeSymbol type, string value, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (
                ITypeSymbol targetType,
                string value,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (INamedTypeSymbol enumType, string value, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (
                ITypeSymbol type,
                string value,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            static (
                ITypeSymbol type,
                string value,
                Compilation compilation,
                out string expression,
                out ResolvedValueRequirements requirements,
                ImmutableArray<AttributeData> converterAttributes) =>
            {
                expression = string.Empty;
                requirements = default;
                return false;
            },
            static (ITypeSymbol type, string value, out string expression) =>
            {
                expression = string.Empty;
                return false;
            });

        return new ValueConversionSemanticService(
            tryParseMarkupExtension ?? (static (string value, out MarkupExtensionInfo markupExtension) =>
            {
                markupExtension = default;
                return false;
            }),
            tryParseBindingMarkup ?? (static (string value, out BindingMarkup bindingMarkup) =>
            {
                bindingMarkup = default;
                return false;
            }),
            static (string value, out BindingMarkup bindingMarkup) =>
            {
                bindingMarkup = default;
                return false;
            },
            static (MarkupExtensionInfo markup, ITypeSymbol targetType, out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            tryBuildBindingValueExpression ?? (static (
                Compilation compilation,
                XamlDocumentModel document,
                BindingMarkup bindingMarkup,
                ITypeSymbol targetType,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            }),
            static (
                MarkupExtensionInfo markup,
                ITypeSymbol targetType,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                int bindingPriorityScope,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            isFrameworkPropertyType ?? (static _ => false),
            tryResolveFrameworkPropertyReferenceExpression ?? (static (
                string value,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            }),
            static _ => false,
            static (
                string selector,
                Compilation compilation,
                XamlDocumentModel document,
                INamedTypeSymbol? setterTargetType,
                INamedTypeSymbol? selectorNestingTypeHint,
                out string expression) =>
            {
                expression = string.Empty;
                return false;
            },
            markupRuntimeOperationResolutionService,
            markupRuntimeOperationEmissionService,
            commonMarkupExtensionConversionService,
            frameworkBindingProjectionService,
            typedLiteralValueConversionService);
    }

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "Test.xaml",
            TargetPath: "Test.xaml",
            ClassFullName: "Demo.Root",
            ClassModifier: "public",
            Precompile: true,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: new XamlObjectNode(
                XmlNamespace: "https://github.com/avaloniaui",
                XmlTypeName: "Control",
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
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public sealed class FrameworkProperty
                                  {
                                  }

                                  public sealed class Control
                                  {
                                      public static FrameworkProperty ValueProperty { get; } = new FrameworkProperty();
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "ValueConversionSemanticServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
