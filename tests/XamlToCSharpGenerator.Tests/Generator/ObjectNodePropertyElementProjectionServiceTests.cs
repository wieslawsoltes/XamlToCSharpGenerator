using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ObjectNodePropertyElementProjectionServiceTests
{
    [Fact]
    public void Project_Uses_Aliased_Framework_Assignment_Before_Generic_Plan()
    {
        var compilation = CreateCompilation();
        var targetType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var document = CreateDocument();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var genericPlanCalled = false;
        var service = new ObjectNodePropertyElementProjectionService(
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(
                    new ResolvedPropertyElementAssignment(
                        PropertyName: "Title",
                        ClrPropertyOwnerTypeName: null,
                        ClrPropertyTypeName: null,
                        IsCollectionAdd: false,
                        IsDictionaryMerge: false,
                        ObjectValues: ImmutableArray.Create(CreateResolvedObjectNode()),
                        Line: 3,
                        Column: 4));
                return true;
            },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            },
            (
                INamedTypeSymbol _,
                string _,
                ImmutableArray<ResolvedObjectNode> _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                int _,
                ConditionalXamlExpression? _) =>
            {
                genericPlanCalled = true;
                return new ObjectNodePropertyElementAssignmentPlan(null, null, ObjectNodePropertyElementAssignmentIssueKind.None, ObjectNodePropertyElementSingleValueRequirementKind.None);
            },
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyElement _,
                Compilation _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                XamlDocumentModel _,
                GeneratorOptions _) => { },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                IPropertySymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            });

        service.Project(
            targetType,
            ImmutableArray.Create(CreateBoundPlan("Alias.Title")),
            compilation,
            diagnostics,
            document,
            CreateOptions(),
            bindingPriorityScope: 0,
            assignments);

        Assert.False(genericPlanCalled);
        Assert.Single(assignments);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Project_Reports_Missing_Property_When_Generic_Plan_Has_No_Target_Property()
    {
        var compilation = CreateCompilation();
        var targetType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var document = CreateDocument();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var service = new ObjectNodePropertyElementProjectionService(
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            },
            static (
                INamedTypeSymbol _,
                string _,
                ImmutableArray<ResolvedObjectNode> _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                int _,
                ConditionalXamlExpression? _) =>
                new ObjectNodePropertyElementAssignmentPlan(
                    null,
                    null,
                    ObjectNodePropertyElementAssignmentIssueKind.None,
                    ObjectNodePropertyElementSingleValueRequirementKind.None),
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyElement _,
                Compilation _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                XamlDocumentModel _,
                GeneratorOptions _) => { },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                IPropertySymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            });

        service.Project(
            targetType,
            ImmutableArray.Create(CreateBoundPlan("MissingProperty")),
            compilation,
            diagnostics,
            document,
            CreateOptions(),
            bindingPriorityScope: 0,
            assignments);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0101", diagnostic.Id);
        Assert.Contains("MissingProperty", diagnostic.Message);
        Assert.Empty(assignments);
    }

    [Fact]
    public void Project_Uses_Framework_Result_After_Generic_Property_Resolution()
    {
        var compilation = CreateCompilation();
        var targetType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var property = targetType.GetMembers("Title").OfType<IPropertySymbol>().Single();
        var document = CreateDocument();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var validated = false;
        var service = new ObjectNodePropertyElementProjectionService(
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(null);
                return false;
            },
            (
                INamedTypeSymbol _,
                string _,
                ImmutableArray<ResolvedObjectNode> _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                int _,
                ConditionalXamlExpression? _) =>
                new ObjectNodePropertyElementAssignmentPlan(
                    property,
                    null,
                    ObjectNodePropertyElementAssignmentIssueKind.None,
                    ObjectNodePropertyElementSingleValueRequirementKind.None),
            (
                INamedTypeSymbol _,
                IPropertySymbol resolvedProperty,
                XamlPropertyElement _,
                Compilation _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                XamlDocumentModel _,
                GeneratorOptions _) =>
            {
                Assert.Same(property, resolvedProperty);
                validated = true;
            },
            static (
                BoundObjectNodePropertyElementPlan _,
                INamedTypeSymbol _,
                IPropertySymbol _,
                Compilation _,
                XamlDocumentModel _,
                int _,
                out ObjectNodePropertyElementResolutionResult result) =>
            {
                result = new ObjectNodePropertyElementResolutionResult(
                    null,
                    "AXSG0103",
                    "Avalonia property element 'Title' requires exactly one object value.");
                return true;
            });

        service.Project(
            targetType,
            ImmutableArray.Create(CreateBoundPlan("Title")),
            compilation,
            diagnostics,
            document,
            CreateOptions(),
            bindingPriorityScope: 0,
            assignments);

        Assert.True(validated);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG0103", diagnostic.Id);
        Assert.Empty(assignments);
    }

    private static BoundObjectNodePropertyElementPlan CreateBoundPlan(string propertyName)
    {
        var propertyElement = new XamlPropertyElement(
            PropertyName: propertyName,
            XmlNamespace: "https://github.com/avaloniaui",
            ObjectValues: ImmutableArray.Create(CreateObjectValue()),
            Line: 3,
            Column: 4);
        return new BoundObjectNodePropertyElementPlan(
            propertyElement,
            new PropertyAliasResolution(propertyName),
            propertyName,
            ImmutableArray.Create(CreateResolvedObjectNode()));
    }

    private static ResolvedObjectNode CreateResolvedObjectNode()
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: "global::System.String",
            IsBindingObjectNode: false,
            FactoryExpression: "\"Value\"",
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: 1,
            Column: 1);
    }

    private static XamlObjectNode CreateObjectValue()
    {
        return new XamlObjectNode(
            XmlNamespace: "https://github.com/avaloniaui",
            XmlTypeName: "TextBlock",
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

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "/tests/Sample.axaml",
            TargetPath: "Sample.axaml",
            ClassFullName: "Demo.SampleView",
            ClassModifier: "public",
            Precompile: true,
            XmlNamespaces: ImmutableDictionary<string, string>.Empty,
            RootObject: CreateObjectValue(),
            NamedElements: ImmutableArray<XamlNamedElement>.Empty,
            Resources: ImmutableArray<XamlResourceDefinition>.Empty,
            Templates: ImmutableArray<XamlTemplateDefinition>.Empty,
            Styles: ImmutableArray<XamlStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<XamlControlThemeDefinition>.Empty,
            Includes: ImmutableArray<XamlIncludeDefinition>.Empty,
            IsValid: true);
    }

    private static GeneratorOptions CreateOptions()
    {
        return new GeneratorOptions(
            IsEnabled: true,
            UseCompiledBindingsByDefault: true,
            CSharpExpressionsEnabled: true,
            ImplicitCSharpExpressionsEnabled: true,
            CreateSourceInfo: false,
            StrictMode: true,
            HotReloadEnabled: false,
            HotReloadErrorResilienceEnabled: false,
            IdeHotReloadEnabled: false,
            HotDesignEnabled: false,
            IosHotReloadEnabled: false,
            IosHotReloadUseInterpreter: false,
            DotNetWatchBuild: false,
            BuildingInsideVisualStudio: false,
            BuildingByReSharper: false,
            TracePasses: false,
            MetricsEnabled: false,
            MetricsDetailed: false,
            MarkupParserLegacyInvalidNamedArgumentFallbackEnabled: false,
            TypeResolutionCompatibilityFallbackEnabled: false,
            AllowImplicitXmlnsDeclaration: false,
            ImplicitStandardXmlnsPrefixesEnabled: false,
            ImplicitDefaultXmlns: "https://github.com/avaloniaui",
            InferClassFromPath: false,
            ImplicitProjectNamespacesEnabled: false,
            GlobalXmlnsPrefixes: null,
            RootNamespace: "Demo",
            IntermediateOutputPath: null,
            BaseIntermediateOutputPath: null,
            ProjectDirectory: null,
            Backend: "SourceGen",
            AssemblyName: "Demo");
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public class Control
                                  {
                                      public string? Title { get; set; }
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "ObjectNodePropertyElementProjectionServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
