using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class SetterPropertyBindingPlanServiceTests
{
    [Fact]
    public void BuildPlan_Prefers_Framework_Property_On_Target_Type_Before_Clr_Property()
    {
        var compilation = CreateCompilation(
            """
            namespace Avalonia
            {
                public class AvaloniaProperty<T> { }
            }

            namespace Demo
            {
                public class Button
                {
                    public static readonly global::Avalonia.AvaloniaProperty<string> ContentProperty = new();
                    public string Content { get; set; } = string.Empty;
                }
            }
            """);

        var targetType = compilation.GetTypeByMetadataName("Demo.Button");
        Assert.NotNull(targetType);

        var service = new SetterPropertyBindingPlanService(
            frameworkId: FrameworkProfileIds.Avalonia,
            resolvePropertyAlias: static (_, propertyToken) => new PropertyAliasResolution(propertyToken),
            trySplitOwnerQualifiedPropertyToken: static (string propertyToken, out string ownerToken, out string propertyName) =>
            {
                ownerToken = string.Empty;
                propertyName = string.Empty;
                return false;
            },
            resolveTypeToken: static (_, _, _, _) => null,
            findProperty: static (ownerType, propertyName) => ownerType.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault(),
            tryFindFrameworkPropertyField: static (
                INamedTypeSymbol ownerType,
                string propertyName,
                string? explicitFieldName,
                out INamedTypeSymbol resolvedOwnerType,
                out IFieldSymbol propertyField) =>
            {
                var candidateFieldName = string.IsNullOrWhiteSpace(explicitFieldName)
                    ? propertyName + "Property"
                    : explicitFieldName!;
                propertyField = ownerType.GetMembers(candidateFieldName).OfType<IFieldSymbol>().First();
                resolvedOwnerType = ownerType;
                return true;
            },
            tryGetFrameworkPropertyValueType: static propertyFieldType =>
                (propertyFieldType as INamedTypeSymbol)?.TypeArguments.LastOrDefault(),
            setterIdentityPlanningService: new SetterIdentityPlanningService(),
            createFrameworkPropertyOperation: static (ownerTypeName, propertyFieldName) =>
                new ResolvedFrameworkPropertyOperation(FrameworkProfileIds.Avalonia, ownerTypeName, propertyFieldName));

        var plan = service.BuildPlan(
            propertyName: "Content",
            targetType: targetType,
            compilation: compilation,
            document: new XamlDocumentModel(
                FilePath: "MainView.axaml",
                TargetPath: "MainView.axaml",
                ClassFullName: "Demo.MainView",
                ClassModifier: null,
                Precompile: null,
                XmlNamespaces: ImmutableDictionary<string, string>.Empty,
                RootObject: new XamlObjectNode(
                    XmlNamespace: "clr-namespace:Demo",
                    XmlTypeName: "Button",
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
                IsValid: true));

        Assert.Equal("Content", plan.ResolvedPropertyName);
        Assert.Null(plan.TargetProperty);
        Assert.NotNull(plan.FrameworkPropertyOperation);
        Assert.Equal("global::Demo.Button", plan.FrameworkPropertyOperation!.PropertyOwnerTypeName);
        Assert.Equal("ContentProperty", plan.FrameworkPropertyOperation.PropertyFieldName);
        Assert.Equal("string", plan.SetterValueType?.ToDisplayString());
    }

    private static CSharpCompilation CreateCompilation(string code)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        return CSharpCompilation.Create(
            assemblyName: "Demo.Assembly",
            syntaxTrees: [syntaxTree],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
