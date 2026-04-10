using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ObjectNodeStandardPropertyAssignmentBindingServiceTests
{
    [Fact]
    public void Bind_Uses_Clr_Property_Binder_For_Settable_Property()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Title", "42");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var eventBinderCalled = false;
        var frameworkBinderCalled = false;
        var service = new ObjectNodeStandardPropertyAssignmentBindingService(
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyAssignment _,
                Compilation _,
                out ResolvedPropertyElementAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Title",
                    ValueExpression: "BindClr()",
                    ClrPropertyOwnerTypeName: "global::Demo.Control",
                    ClrPropertyTypeName: "global::System.String",
                    Line: 3,
                    Column: 4);
                return true;
            },
            (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                eventBinderCalled = true;
                subscription = null;
                return false;
            },
            (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                frameworkBinderCalled = true;
                assignment = null;
                return false;
            });

        var result = service.Bind(request, diagnostics);

        Assert.NotNull(result.PropertyAssignment);
        Assert.Equal("BindClr()", result.PropertyAssignment!.ValueExpression);
        Assert.Null(result.PropertyElementAssignment);
        Assert.Null(result.EventSubscription);
        Assert.Null(result.Diagnostic);
        Assert.False(eventBinderCalled);
        Assert.True(frameworkBinderCalled);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Prefers_Framework_Property_Binder_Before_Clr_Property_Binder()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Title", "{Binding Name}");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var clrBinderCalled = false;
        var service = new ObjectNodeStandardPropertyAssignmentBindingService(
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyAssignment _,
                Compilation _,
                out ResolvedPropertyElementAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                clrBinderCalled = true;
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Title",
                    ValueExpression: "Clr()",
                    ClrPropertyOwnerTypeName: "global::Demo.Control",
                    ClrPropertyTypeName: "global::System.String",
                    Line: 3,
                    Column: 4);
                return true;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Title",
                    ValueExpression: "Framework()",
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: 3,
                    Column: 4);
                return true;
            });

        var result = service.Bind(request, diagnostics);

        Assert.NotNull(result.PropertyAssignment);
        Assert.Equal("Framework()", result.PropertyAssignment!.ValueExpression);
        Assert.False(clrBinderCalled);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Uses_Framework_Property_Binder_When_Clr_Property_Is_Missing()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Title", "{Binding Name}", property: null);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var clrBinderCalled = false;
        var service = new ObjectNodeStandardPropertyAssignmentBindingService(
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyAssignment _,
                Compilation _,
                out ResolvedPropertyElementAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                clrBinderCalled = true;
                assignment = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Title",
                    ValueExpression: "FrameworkOnly()",
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: 3,
                    Column: 4);
                return true;
            });

        var result = service.Bind(request, diagnostics);

        Assert.NotNull(result.PropertyAssignment);
        Assert.Equal("FrameworkOnly()", result.PropertyAssignment!.ValueExpression);
        Assert.False(clrBinderCalled);
        Assert.Null(result.Diagnostic);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Uses_Collection_Literal_For_Getter_Only_Property()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Items", "A, B");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = new ObjectNodeStandardPropertyAssignmentBindingService(
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyAssignment _,
                Compilation _,
                out ResolvedPropertyElementAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyElementAssignment(
                    PropertyName: "Items",
                    ClrPropertyOwnerTypeName: "global::Demo.Control",
                    ClrPropertyTypeName: "global::System.Collections.Generic.IList<string>",
                    IsCollectionAdd: true,
                    IsDictionaryMerge: false,
                    ObjectValues: ImmutableArray<ResolvedObjectNode>.Empty,
                    Line: 3,
                    Column: 4);
                return true;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            });

        var result = service.Bind(request, diagnostics);

        Assert.NotNull(result.PropertyElementAssignment);
        Assert.True(result.PropertyElementAssignment!.IsCollectionAdd);
        Assert.Null(result.PropertyAssignment);
        Assert.Null(result.EventSubscription);
        Assert.Null(result.Diagnostic);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Reports_Missing_Property_When_No_Handler_Claims_Assignment()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Missing", "42", property: null);
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = new ObjectNodeStandardPropertyAssignmentBindingService(
            static (
                INamedTypeSymbol _,
                IPropertySymbol _,
                XamlPropertyAssignment _,
                Compilation _,
                out ResolvedPropertyElementAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = null;
                return false;
            },
            static (
                ObjectNodeStandardPropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            });

        var result = service.Bind(request, diagnostics);

        Assert.Null(result.PropertyAssignment);
        Assert.Null(result.PropertyElementAssignment);
        Assert.Null(result.EventSubscription);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal("AXSG0101", result.Diagnostic!.Id);
    }

    private static ObjectNodeStandardPropertyAssignmentBindingRequest CreateRequest(
        CSharpCompilation compilation,
        string propertyName,
        string value,
        IPropertySymbol? property = null)
    {
        var ownerType = compilation.GetTypeByMetadataName("Demo.Control")!;
        property ??= ownerType.GetMembers(propertyName).OfType<IPropertySymbol>().SingleOrDefault();
        return new ObjectNodeStandardPropertyAssignmentBindingRequest(
            ObjectType: ownerType,
            ObjectTypeName: "global::Demo.Control",
            Assignment: new XamlPropertyAssignment(propertyName, string.Empty, value, IsAttached: false, Line: 3, Column: 4),
            NormalizedPropertyName: propertyName,
            Property: property,
            Compilation: compilation,
            Document: CreateDocument(),
            Options: CreateOptions(),
            CompiledBindings: ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>(),
            UnsafeAccessors: ImmutableArray.CreateBuilder<ResolvedUnsafeAccessorDefinition>(),
            CompileBindingsEnabled: true,
            AssignmentDataType: ownerType,
            CurrentSetterTargetType: ownerType,
            CurrentBindingPriorityScope: 0,
            RootTypeSymbol: ownerType,
            IsInsideDataTemplate: false,
            XBindDefaultMode: "OneTime",
            CurrentNode: CreateObjectNode(),
            InferredSetterValueType: null,
            SelectorNestingTypeHint: null);
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
            ClassModifier: "public",
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

    private static XamlObjectNode CreateObjectNode()
    {
        return new XamlObjectNode(
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
            Column: 1);
    }

    private static CSharpCompilation CreateCompilation()
    {
        const string source = """
                              using System.Collections.Generic;

                              namespace Demo
                              {
                                  public class Control
                                  {
                                      public string? Title { get; set; }
                                      public IList<string> Items { get; } = new List<string>();
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            "ObjectNodeStandardPropertyAssignmentBindingServiceTests",
            [CSharpSyntaxTree.ParseText(source)],
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location)
            ],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
