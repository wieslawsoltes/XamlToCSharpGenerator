using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Tests.Generator;

public class EventSubscriptionBindingServiceTests
{
    [Fact]
    public void TryBindInlineCode_Binds_Routed_Event_Subscription()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var targetType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;
        var nodeDataType = compilation.GetTypeByMetadataName("Demo.ViewModel")!;

        var handled = service.TryBindInlineCode(
            targetType,
            propertyName: "Tapped",
            rawCode: "source.Count++;",
            line: 4,
            column: 5,
            condition: null,
            compilation,
            nodeDataType,
            rootType,
            diagnostics,
            CreateDocument(),
            CreateOptions(),
            out var subscription);

        Assert.True(handled);
        Assert.Empty(diagnostics);
        Assert.NotNull(subscription);
        Assert.Equal(ResolvedEventSubscriptionKind.RoutedEvent, subscription!.Kind);
        Assert.Equal("TappedEvent", subscription.RoutedEventFieldName);
        Assert.Equal("global::Avalonia.Interactivity.RoutedEventHandler", subscription.RoutedEventHandlerTypeName);
        Assert.NotNull(subscription.EventBindingDefinition);
    }

    [Fact]
    public void TryBindAssignment_Binds_Clr_Handler_Method_Name()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var targetType = compilation.GetTypeByMetadataName("Demo.Control")!;
        var rootType = compilation.GetTypeByMetadataName("Demo.Root")!;

        var handled = service.TryBindAssignment(
            targetType,
            new XamlPropertyAssignment("Click", "https://github.com/avaloniaui", "OnClick", false, 7, 9),
            compilation,
            nodeDataType: null,
            rootType,
            isInsideDataTemplate: false,
            diagnostics,
            CreateDocument(),
            CreateOptions(),
            currentNode: null,
            out var subscription);

        Assert.True(handled);
        Assert.Empty(diagnostics);
        Assert.NotNull(subscription);
        Assert.Equal(ResolvedEventSubscriptionKind.ClrEvent, subscription!.Kind);
        Assert.Equal("OnClick", subscription.HandlerMethodName);
        Assert.Null(subscription.RoutedEventFieldName);
    }

    private static EventSubscriptionBindingService CreateService()
    {
        var semanticBindingService = new EventBindingSemanticBindingService(
            TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
            static compilation => compilation.GetTypeByMetadataName("System.Windows.Input.ICommand"));
        var eventBindingDefinitionService = new EventBindingDefinitionService(
            semanticBindingService,
            TryParseMarkupExtension,
            TryConvertLiteralValueExpression);
        var eventHandlerBindingService = new EventHandlerBindingService(
            TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
            "__ROOT__");
        var routedEventResolutionService = new FrameworkRoutedEventResolutionService(
            ResolveContractType,
            TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
            "Avalonia.Interactivity",
            "RoutedEvent");

        return new EventSubscriptionBindingService(
            TypeSymbolLookupSemanticsService.FindEvent,
            static value => value,
            routedEventResolutionService,
            eventBindingDefinitionService,
            eventHandlerBindingService,
            static (string value, out string rawCode) =>
            {
                rawCode = string.Empty;
                return false;
            },
            static (string value, out XBindMarkup xBindMarkup) =>
            {
                xBindMarkup = default;
                return false;
            },
            TryParseMarkupExtension,
            static (
                Compilation compilation,
                XamlDocumentModel document,
                XamlObjectNode currentNode,
                XBindMarkup xBindMarkup,
                string eventName,
                INamedTypeSymbol? ambientDataContextType,
                INamedTypeSymbol? rootType,
                INamedTypeSymbol targetType,
                ITypeSymbol eventHandlerType,
                bool isInsideDataTemplate,
                int line,
                int column,
                out ResolvedEventBindingDefinition? eventBindingDefinition,
                out string errorMessage) =>
            {
                eventBindingDefinition = null;
                errorMessage = string.Empty;
                return false;
            },
            static (
                XamlPropertyAssignment assignment,
                string eventName,
                Compilation compilation,
                ITypeSymbol eventHandlerType,
                INamedTypeSymbol? nodeDataType,
                INamedTypeSymbol targetType,
                INamedTypeSymbol? rootTypeSymbol,
                ImmutableArray<DiagnosticInfo>.Builder diagnostics,
                XamlDocumentModel document,
                GeneratorOptions options,
                out ResolvedEventBindingDefinition? eventBindingDefinition,
                out bool handled) =>
            {
                eventBindingDefinition = null;
                handled = false;
                return false;
            });
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        markupExtension = default;
        return false;
    }

    private static bool TryConvertLiteralValueExpression(string literalValue, out string expression)
    {
        expression = "lit(" + literalValue + ")";
        return true;
    }

    private static INamedTypeSymbol? ResolveContractType(Compilation compilation, TypeContractId contractId)
    {
        return contractId switch
        {
            TypeContractId.SystemDelegate => compilation.GetTypeByMetadataName("System.Delegate"),
            TypeContractId.SystemEventHandlerOfT => compilation.GetTypeByMetadataName("System.EventHandler`1"),
            TypeContractId.SystemEventArgs => compilation.GetTypeByMetadataName("System.EventArgs"),
            TypeContractId.AvaloniaRoutedEvent => compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent"),
            TypeContractId.AvaloniaGenericRoutedEvent => compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent`1"),
            TypeContractId.AvaloniaRoutedEventHandler => compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEventHandler"),
            TypeContractId.AvaloniaRoutedEventArgs => compilation.GetTypeByMetadataName("System.EventArgs"),
            _ => null
        };
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
                              namespace Avalonia.Interactivity
                              {
                                  public class RoutedEvent
                                  {
                                  }

                                  public class RoutedEvent<TEventArgs> : RoutedEvent
                                  {
                                  }

                                  public delegate void RoutedEventHandler(object sender, System.EventArgs e);
                              }

                              namespace Demo
                              {
                                  public sealed class ViewModel
                                  {
                                      public int Count { get; set; }
                                  }

                                  public delegate void ClickHandler(object sender, object args);

                                  public sealed class Root
                                  {
                                      public void OnClick(object sender, object args)
                                      {
                                      }
                                  }

                                  public class Control
                                  {
                                      public event ClickHandler? Click;
                                      public static readonly Avalonia.Interactivity.RoutedEvent TappedEvent = new();
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "EventSubscriptionBindingServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EventArgs).Assembly.Location)
            ]);
    }
}
