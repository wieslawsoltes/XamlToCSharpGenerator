using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class ObjectNodeAttachedPropertyAssignmentBindingServiceTests
{
    [Fact]
    public void Bind_Uses_Attached_Property_Binder_First()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Canvas.Left");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var staticSetterCalled = false;
        var classPropertyCalled = false;
        var eventCalled = false;
        var service = new ObjectNodeAttachedPropertyAssignmentBindingService(
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = new ResolvedPropertyAssignment(
                    PropertyName: "Left",
                    ValueExpression: "BindAttached()",
                    ClrPropertyOwnerTypeName: "global::Avalonia.Controls.Canvas",
                    ClrPropertyTypeName: null,
                    Line: 3,
                    Column: 4);
                return true;
            },
            (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                staticSetterCalled = true;
                assignment = null;
                return false;
            },
            (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                classPropertyCalled = true;
                assignment = null;
                return false;
            },
            (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                eventCalled = true;
                subscription = null;
                return false;
            });

        var result = service.Bind(request, diagnostics);

        Assert.NotNull(result.PropertyAssignment);
        Assert.Equal("BindAttached()", result.PropertyAssignment!.ValueExpression);
        Assert.Null(result.EventSubscription);
        Assert.Null(result.Diagnostic);
        Assert.False(staticSetterCalled);
        Assert.False(classPropertyCalled);
        Assert.False(eventCalled);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Uses_Attached_Event_Subscription_When_Previous_Binders_Do_Not_Handle()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Button.Click");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = new ObjectNodeAttachedPropertyAssignmentBindingService(
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = new ResolvedEventSubscription(
                    EventName: "Click",
                    HandlerMethodName: "OnClick",
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: "global::System.EventHandler",
                    Line: 3,
                    Column: 4);
                return true;
            });

        var result = service.Bind(request, diagnostics);

        Assert.Null(result.PropertyAssignment);
        Assert.NotNull(result.EventSubscription);
        Assert.Equal("OnClick", result.EventSubscription!.HandlerMethodName);
        Assert.Null(result.Diagnostic);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Bind_Reports_Missing_Attached_Property_When_No_Handler_Claims_Assignment()
    {
        var compilation = CreateCompilation();
        var request = CreateRequest(compilation, "Canvas.Top");
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var service = new ObjectNodeAttachedPropertyAssignmentBindingService(
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedPropertyAssignment? assignment) =>
            {
                assignment = null;
                return false;
            },
            static (
                AttachedObjectNodePropertyAssignmentBindingRequest _,
                ImmutableArray<DiagnosticInfo>.Builder _,
                out ResolvedEventSubscription? subscription) =>
            {
                subscription = null;
                return false;
            });

        var result = service.Bind(request, diagnostics);

        Assert.Null(result.PropertyAssignment);
        Assert.Null(result.EventSubscription);
        Assert.NotNull(result.Diagnostic);
        Assert.Equal("AXSG0101", result.Diagnostic!.Id);
    }

    private static AttachedObjectNodePropertyAssignmentBindingRequest CreateRequest(
        CSharpCompilation compilation,
        string propertyName)
    {
        var ownerType = compilation.GetTypeByMetadataName("Demo.Control")!;
        return new AttachedObjectNodePropertyAssignmentBindingRequest(
            TargetType: ownerType,
            TargetTypeName: "global::Demo.Control",
            Assignment: new XamlPropertyAssignment(propertyName, string.Empty, "42", IsAttached: true, Line: 3, Column: 4),
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
            ExplicitOwnerType: null,
            ExplicitPropertyName: null,
            ExplicitPropertyFieldName: null);
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
