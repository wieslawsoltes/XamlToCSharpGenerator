using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Tests.Generator;

public class FrameworkRoutedEventResolutionServiceTests
{
    [Fact]
    public void ResolveTarget_Uses_Generic_EventHandler_For_Generic_RoutedEvent()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var controlType = compilation.GetTypeByMetadataName("Demo.Control")!;

        var resolution = service.ResolveTarget(controlType, "Activated", compilation);

        Assert.True(resolution.FoundStaticEventField);
        Assert.NotNull(resolution.OwnerType);
        Assert.NotNull(resolution.EventField);
        Assert.Equal("global::System.EventHandler<global::Demo.ActivatedEventArgs>", resolution.HandlerType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void ResolveTarget_Uses_Framework_RoutedEventHandler_For_NonGeneric_RoutedEvent()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var controlType = compilation.GetTypeByMetadataName("Demo.Control")!;

        var resolution = service.ResolveTarget(controlType, "Tapped", compilation);

        Assert.True(resolution.FoundStaticEventField);
        Assert.NotNull(resolution.HandlerType);
        Assert.Equal("global::Avalonia.Interactivity.RoutedEventHandler", resolution.HandlerType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    [Fact]
    public void ResolveTarget_Returns_NotFound_When_Static_Event_Field_Is_Missing()
    {
        var compilation = CreateCompilation();
        var service = CreateService();
        var controlType = compilation.GetTypeByMetadataName("Demo.Control")!;

        var resolution = service.ResolveTarget(controlType, "Missing", compilation);

        Assert.False(resolution.FoundStaticEventField);
        Assert.Null(resolution.OwnerType);
        Assert.Null(resolution.EventField);
        Assert.Null(resolution.HandlerType);
    }

    private static FrameworkRoutedEventResolutionService CreateService()
    {
        return new FrameworkRoutedEventResolutionService(
            ResolveContractType,
            TypeSymbolLookupSemanticsService.IsTypeAssignableTo,
            "Avalonia.Interactivity",
            "RoutedEvent");
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
                                  public sealed class ActivatedEventArgs : System.EventArgs
                                  {
                                  }

                                  public class Control
                                  {
                                      public static readonly Avalonia.Interactivity.RoutedEvent<ActivatedEventArgs> ActivatedEvent = new();
                                      public static readonly Avalonia.Interactivity.RoutedEvent TappedEvent = new();
                                  }
                              }
                              """;

        return CSharpCompilation.Create(
            assemblyName: "FrameworkRoutedEventResolutionServiceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(EventArgs).Assembly.Location)
            ]);
    }
}
