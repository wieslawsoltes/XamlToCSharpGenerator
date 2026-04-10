using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ObjectNodeAssemblyServiceTests
{
    [Fact]
    public void Assemble_Normalizes_Projects_And_Finalizes_Object_Node()
    {
        var compilation = CreateCompilation();
        var objectType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var existingAssignment = CreatePropertyAssignment("Existing");
        var normalizedAssignment = CreatePropertyAssignment("Normalized");
        var existingPropertyElementAssignment = CreatePropertyElementAssignment("ExistingElement");
        var projectedPropertyElementAssignment = CreatePropertyElementAssignment("ProjectedElement");
        var existingChild = CreateResolvedObjectNode("ExistingChild");
        var planChild = CreateResolvedObjectNode("PlanChild");
        var normalizedChild = CreateResolvedObjectNode("NormalizedChild");
        var finalizedChild = CreateResolvedObjectNode("FinalizedChild");
        var constructionCalled = false;
        var finalizationCalled = false;

        var service = new ObjectNodeAssemblyService(
            (
                XamlObjectNode _,
                INamedTypeSymbol? _,
                Compilation _,
                XamlDocumentModel _,
                GeneratorOptions _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                ImmutableArray<ResolvedPropertyAssignment> existingAssignments,
                ImmutableArray<ResolvedObjectNode> existingChildren,
                out ImmutableArray<ResolvedPropertyAssignment> normalizedAssignments,
                out ImmutableArray<ResolvedObjectNode> normalizedChildren) =>
            {
                Assert.Equal(["Existing"], existingAssignments.Select(static assignment => assignment.PropertyName));
                Assert.Equal(["ExistingChild", "PlanChild"], existingChildren.Select(static child => child.TypeName));

                normalizedAssignments = ImmutableArray.Create(normalizedAssignment);
                normalizedChildren = ImmutableArray.Create(normalizedChild);
                return true;
            },
            (
                INamedTypeSymbol projectedObjectType,
                ImmutableArray<BoundObjectNodePropertyElementPlan> propertyElementPlans,
                Compilation _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                XamlDocumentModel _,
                GeneratorOptions _,
                int bindingPriorityScope) =>
            {
                Assert.Same(objectType, projectedObjectType);
                Assert.Equal(7, bindingPriorityScope);
                Assert.Single(propertyElementPlans);
                Assert.Equal("ProjectedElement", propertyElementPlans[0].NormalizedPropertyName);
                return ImmutableArray.Create(projectedPropertyElementAssignment);
            },
            (
                XamlObjectNode _,
                INamedTypeSymbol? _,
                string _,
                string? _,
                Compilation _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                XamlDocumentModel _,
                GeneratorOptions _,
                ImmutableArray<ResolvedCompiledBindingDefinition>.Builder _,
                ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder _,
                bool _,
                INamedTypeSymbol? _,
                INamedTypeSymbol? _,
                int _,
                INamedTypeSymbol? _,
                ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
                ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
                ImmutableArray<ResolvedObjectNode> children) =>
            {
                constructionCalled = true;
                Assert.Equal(["Normalized"], propertyAssignments.Select(static assignment => assignment.PropertyName));
                Assert.Equal(
                    ["ExistingElement", "ProjectedElement"],
                    propertyElementAssignments.Select(static assignment => assignment.PropertyName));
                Assert.Equal(["NormalizedChild"], children.Select(static child => child.TypeName));
                return new ResolvedObjectNodeConstructionPlan(
                    "CreateNode()",
                    ResolvedValueRequirements.None,
                    propertyAssignments,
                    propertyElementAssignments,
                    children);
            },
            (
                INamedTypeSymbol? _,
                ResolvedChildAttachmentMode explicitAttachmentMode,
                string? explicitContentPropertyName,
                ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
                ImmutableArray<ResolvedObjectNode> children,
                Compilation _,
                XamlDocumentModel _,
                int _,
                int _,
                ConditionalXamlExpression? _) =>
            {
                finalizationCalled = true;
                Assert.Equal(ResolvedChildAttachmentMode.Content, explicitAttachmentMode);
                Assert.Equal("Content", explicitContentPropertyName);
                Assert.Equal(
                    ["ExistingElement", "ProjectedElement"],
                    propertyElementAssignments.Select(static assignment => assignment.PropertyName));
                Assert.Equal(["NormalizedChild"], children.Select(static child => child.TypeName));
                return new ResolvedObjectNodeAttachmentFinalizationPlan(
                    ResolvedChildAttachmentMode.Content,
                    "Content",
                    "global::System.Object",
                    propertyElementAssignments,
                    ImmutableArray.Create(finalizedChild),
                    ImmutableArray.Create(
                        new ResolvedObjectNodeAttachmentValidationIssue(
                            ResolvedObjectNodeAttachmentValidationIssueKind.DictionaryChildMissingKey,
                            11,
                            13)),
                    ImmutableArray<ResolvedCollectionAddInstruction>.Empty);
            },
            (
                ImmutableArray<ResolvedObjectNodeAttachmentValidationIssue> validationIssues,
                ImmutableArray<DiagnosticInfo>.Builder reportDiagnostics,
                string filePath,
                string typeName,
                int line,
                int column,
                bool strictMode) =>
            {
                Assert.Single(validationIssues);
                Assert.Equal("/tests/Sample.axaml", filePath);
                Assert.Equal("global::Demo.Control", typeName);
                Assert.Equal(7, line);
                Assert.Equal(9, column);
                Assert.True(strictMode);
                reportDiagnostics.Add(new DiagnosticInfo(
                    "AXSG9999",
                    "reported",
                    filePath,
                    validationIssues[0].Line,
                    validationIssues[0].Column,
                    strictMode));
            },
            static (XamlObjectNode _, INamedTypeSymbol? _, Compilation _) => "ResolvedName",
            static (string? rawKey, Compilation _, XamlDocumentModel _) => "Key(" + rawKey + ")",
            static (
                INamedTypeSymbol? _,
                Compilation _,
                XamlDocumentModel _,
                XamlObjectNode node,
                string? keyExpression,
                string? name,
                string typeName,
                bool isBindingObjectNode,
                string? factoryExpression,
                ResolvedValueRequirements factoryValueRequirements,
                ImmutableArray<ResolvedPropertyAssignment> propertyAssignments,
                ImmutableArray<ResolvedPropertyElementAssignment> propertyElementAssignments,
                ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
                ImmutableArray<ResolvedObjectNode> children,
                ResolvedChildAttachmentMode childAttachmentMode,
                string? contentPropertyName,
                string? contentPropertyTypeName) =>
                new ResolvedObjectNode(
                    keyExpression,
                    name,
                    typeName,
                    isBindingObjectNode,
                    factoryExpression,
                    factoryValueRequirements,
                    UseServiceProviderConstructor: false,
                    UseTopDownInitialization: false,
                    propertyAssignments,
                    propertyElementAssignments,
                    eventSubscriptions,
                    children,
                    childAttachmentMode,
                    contentPropertyName,
                    node.Line,
                    node.Column,
                    node.Condition,
                    ContentPropertyTypeName: contentPropertyTypeName),
            static (INamedTypeSymbol _, Compilation _) => false);

        var result = service.Assemble(new ObjectNodeAssemblyRequest(
            CreateObjectNode(),
            objectType,
            "global::Demo.Control",
            "Content",
            compilation,
            CreateDocument(),
            CreateOptions(),
            diagnostics,
            ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>(),
            ImmutableArray.CreateBuilder<ResolvedUnsafeAccessorDefinition>(),
            CompileBindingsEnabled: true,
            NodeDataType: objectType,
            CurrentSetterTargetType: objectType,
            CurrentBindingPriorityScope: 7,
            RootTypeSymbol: objectType,
            PropertyAssignments: ImmutableArray.Create(existingAssignment),
            PropertyElementAssignments: ImmutableArray.Create(existingPropertyElementAssignment),
            EventSubscriptions: ImmutableArray.Create(
                new ResolvedEventSubscription(
                    "Click",
                    "HandleClick",
                    ResolvedEventSubscriptionKind.ClrEvent,
                    null,
                    null,
                    null,
                    3,
                    4)),
            Children: ImmutableArray.Create(existingChild),
            PropertyElementBindingPlan: new BoundObjectNodePropertyElementSet(
                ImmutableArray.Create(CreateBoundPropertyElementPlan(planChild)),
                ImmutableArray.Create(planChild),
                ResolvedChildAttachmentMode.Content,
                "Content")));

        Assert.True(constructionCalled);
        Assert.True(finalizationCalled);
        Assert.Equal("Key(ResourceKey)", result.KeyExpression);
        Assert.Equal("ResolvedName", result.Name);
        Assert.Equal("CreateNode()", result.FactoryExpression);
        Assert.Equal(
            ["Normalized"],
            result.PropertyAssignments.Select(static assignment => assignment.PropertyName));
        Assert.Equal(
            ["ExistingElement", "ProjectedElement"],
            result.PropertyElementAssignments.Select(static assignment => assignment.PropertyName));
        Assert.Equal(["FinalizedChild"], result.Children.Select(static child => child.TypeName));
        Assert.Equal(ResolvedChildAttachmentMode.Content, result.ChildAttachmentMode);
        Assert.Equal("Content", result.ContentPropertyName);
        Assert.Equal("global::System.Object", result.ContentPropertyTypeName);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("AXSG9999", diagnostic.Id);
    }

    private static BoundObjectNodePropertyElementPlan CreateBoundPropertyElementPlan(ResolvedObjectNode child)
    {
        return new BoundObjectNodePropertyElementPlan(
            new XamlPropertyElement(
                "ProjectedElement",
                string.Empty,
                ImmutableArray.Create(CreateObjectNode("ProjectedValue")),
                5,
                6),
            new PropertyAliasResolution("ProjectedElement"),
            "ProjectedElement",
            ImmutableArray.Create(child));
    }

    private static ResolvedPropertyAssignment CreatePropertyAssignment(string propertyName)
    {
        return new ResolvedPropertyAssignment(
            propertyName,
            "\"" + propertyName + "\"",
            "global::Demo.Control",
            "global::System.String",
            1,
            2);
    }

    private static ResolvedPropertyElementAssignment CreatePropertyElementAssignment(string propertyName)
    {
        return new ResolvedPropertyElementAssignment(
            propertyName,
            "global::Demo.Control",
            "global::System.String",
            IsCollectionAdd: false,
            IsDictionaryMerge: false,
            ImmutableArray.Create(CreateResolvedObjectNode(propertyName + "Value")),
            2,
            3);
    }

    private static ResolvedObjectNode CreateResolvedObjectNode(string typeName)
    {
        return new ResolvedObjectNode(
            null,
            null,
            typeName,
            IsBindingObjectNode: false,
            FactoryExpression: "new " + typeName + "()",
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

    private static XamlObjectNode CreateObjectNode(string xmlTypeName = "Control")
    {
        return new XamlObjectNode(
            "clr-namespace:Demo",
            xmlTypeName,
            Key: "ResourceKey",
            Name: "NodeName",
            FieldModifier: null,
            DataType: null,
            CompileBindings: true,
            FactoryMethod: null,
            TypeArguments: ImmutableArray<string>.Empty,
            ArrayItemType: null,
            ConstructorArguments: ImmutableArray<XamlObjectNode>.Empty,
            TextContent: null,
            PropertyAssignments: ImmutableArray<XamlPropertyAssignment>.Empty,
            ChildObjects: ImmutableArray<XamlObjectNode>.Empty,
            PropertyElements: ImmutableArray<XamlPropertyElement>.Empty,
            Line: 7,
            Column: 9);
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

    private static XamlDocumentModel CreateDocument()
    {
        return new XamlDocumentModel(
            FilePath: "/tests/Sample.axaml",
            TargetPath: "Sample.axaml",
            ClassFullName: "Demo.SampleView",
            ClassModifier: "public partial",
            Precompile: true,
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

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              namespace Demo
                              {
                                  public class Control
                                  {
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "Demo",
            [CSharpSyntaxTree.ParseText(source)],
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location) is { } reference
                ? [reference]
                : [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
