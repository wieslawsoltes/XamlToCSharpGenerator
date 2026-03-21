using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.Tests.LanguageService;

internal static class LanguageServiceTestCompilationFactory
{
    public const string SymbolSourceFilePath = "/tmp/LanguageServiceTestTypes.cs";
    private static readonly Lazy<Compilation> CachedCompilation =
        new(CreateCompilationCore, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<MsBuildCompilationProvider> SharedMsBuildCompilationProvider =
        new(() => new MsBuildCompilationProvider(), LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly InMemoryCompilationProvider SharedInMemoryCompilationProvider =
        new(CachedCompilation.Value);
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> RepositoryTextCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static Compilation CreateCompilation()
    {
        return CachedCompilation.Value;
    }

    public static Compilation CreateNamespaceImportCompilation()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:Host.Controls", "Host.Controls")]
                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("using:Demo.Controls", "Demo.Controls")]
                              [assembly: Avalonia.Metadata.XmlnsPrefixAttribute("using:Demo.Controls", "local")]

                              namespace Avalonia.Metadata
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }

                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsPrefixAttribute : Attribute
                                  {
                                      public XmlnsPrefixAttribute(string xmlNamespace, string prefix) { }
                                  }
                              }

                              namespace Host.Controls
                              {
                                  public class UserControl { }

                                  public class ControlTheme
                                  {
                                      public object? TargetType { get; set; }
                                  }

                                  public class DataTemplate
                                  {
                                      public object? DataType { get; set; }
                                  }

                                  public class Style
                                  {
                                      public string Selector { get; set; } = string.Empty;
                                  }

                                  public class Setter
                                  {
                                      public string Property { get; set; } = string.Empty;
                                      public object? Value { get; set; }
                                  }
                              }

                              namespace Demo.Controls
                              {
                                  public class ThemeDoodad
                                  {
                                      public static object AccentProperty = new object();
                                      public string Title { get; set; } = string.Empty;
                                  }
                              }
                              """;

        return CreateAdHocCompilation(
            source,
            assemblyName: "NamespaceImportTests",
            filePath: "/tmp/NamespaceImportTests.cs");
    }

    public static ICompilationProvider CreateSharedMsBuildCompilationProvider()
    {
        return new NonDisposingCompilationProvider(SharedMsBuildCompilationProvider.Value);
    }

    public static ICompilationProvider CreateHarnessCompilationProvider(ICompilationProvider? provider = null)
    {
        return provider ?? SharedInMemoryCompilationProvider;
    }

    public static Task<string> ReadCachedTextAsync(string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var lazyText = RepositoryTextCache.GetOrAdd(
            normalizedPath,
            static cachedPath => new Lazy<Task<string>>(
                () => File.ReadAllTextAsync(cachedPath),
                LazyThreadSafetyMode.ExecutionAndPublication));
        return lazyText.Value;
    }

    private static Compilation CreateCompilationCore()
    {
        const string source = """
                              using System;

                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "TestApp.Controls")]
                              [assembly: Avalonia.Metadata.XmlnsDefinitionAttribute("https://github.com/avaloniaui", "XamlToCSharpGenerator.Runtime.Markup")]

                              namespace Avalonia.Metadata
                              {
                                  [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
                                  public sealed class XmlnsDefinitionAttribute : Attribute
                                  {
                                      public XmlnsDefinitionAttribute(string xmlNamespace, string clrNamespace) { }
                                  }
                              }

                              namespace Avalonia.Controls.Metadata
                              {
                                  [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                                  public sealed class PseudoClassesAttribute : Attribute
                                  {
                                      public PseudoClassesAttribute(params string[] pseudoClasses) { }
                                  }
                              }

                              namespace TestApp.Controls
                              {
                                  public class AdvancedClickEventArgs : EventArgs
                                  {
                                      public bool Handled { get; set; }
                                      public string Message { get; set; } = string.Empty;
                                  }

                                  public static class TestPseudoClasses
                                  {
                                      public const string ButtonPressed = ":pressed";
                                      public const string ExpanderExpanded = ":expanded";
                                  }

                                  public class UserControl { }
                                  public class StackPanel { }
                                  public class DataTemplate
                                  {
                                      public object? DataType { get; set; }
                                  }

                                  public class Border
                                  {
                                      public object Child { get; set; } = new object();
                                  }

                                  public class Control
                                  {
                                  }

                                  public class Visual
                                  {
                                      public double Opacity { get; set; }
                                  }

                                  public class Shape : Visual
                                  {
                                      public string Stroke { get; set; } = string.Empty;
                                  }

                                  public class Path : Shape
                                  {
                                      public string Data { get; set; } = string.Empty;
                                  }

                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(TestPseudoClasses.ButtonPressed, ":pointerover")]
                                  public class Button : Control
                                  {
                                      public string Content { get; set; } = string.Empty;
                                      public event EventHandler? Click;
                                      public event EventHandler<AdvancedClickEventArgs>? AdvancedClick;
                                  }

                                  public class TextBlock
                                  {
                                      public string Text { get; set; } = string.Empty;
                                  }

                                  [Avalonia.Controls.Metadata.PseudoClassesAttribute(TestPseudoClasses.ExpanderExpanded)]
                                  public class Expander : Control
                                  {
                                  }

                                  public class CustomerViewModel
                                  {
                                      public string DisplayName { get; set; } = string.Empty;
                                  }

                                  public static class UiHelpers
                                  {
                                      public static string Prefix { get; set; } = "prefix";
                                      public static CustomerViewModel SharedCustomer { get; } = new CustomerViewModel();
                                      public static CustomerViewModel BuildCustomer(int id) => SharedCustomer;
                                  }

                                  public class MainWindowViewModel
                                  {
                                      public string Name { get; set; } = string.Empty;
                                      public string FirstName { get; set; } = string.Empty;
                                      public string LastName { get; set; } = string.Empty;
                                      public string ProductName { get; set; } = string.Empty;
                                      public string LastAction { get; set; } = string.Empty;
                                      public int Count { get; set; }
                                      public int Quantity { get; set; }
                                      public int ClickCount { get; set; }
                                      public CustomerViewModel Customer { get; set; } = new CustomerViewModel();
                                      public bool IsLoading { get; set; }
                                      public bool HasAccount { get; set; }
                                      public bool AgreedToTerms { get; set; }
                                      public bool IsVip { get; set; }
                                      public decimal Price { get; set; }
                                      public CustomerViewModel GetCustomer() => Customer;
                                      public string FormatSummary(string firstName, string lastName, int count) => firstName + lastName + count;
                                      public void RecordSender(object? sender) { }
                                  }

                                  public partial class MainView : UserControl
                                  {
                                      public string Title { get; set; } = string.Empty;
                                      public string RootOnly { get; set; } = string.Empty;
                                      public string FormatTitle() => Title;
                                  }

                                  public class MyExtension
                                  {
                                  }
                              }

                              namespace XamlToCSharpGenerator.Runtime.Markup
                              {
                                  public class CSharp
                                  {
                                      public string Code { get; set; } = string.Empty;
                                  }

                                  public class CSharpExtension
                                  {
                                      public string Code { get; set; } = string.Empty;
                                  }
                              }

                              namespace XamlToCSharpGenerator.Runtime
                              {
                                  public class CSharp : Markup.CSharp
                                  {
                                  }

                                  public class CSharpExtension : Markup.CSharpExtension
                                  {
                                  }
                              }
                              """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: SymbolSourceFilePath);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };

        return CSharpCompilation.Create(
            "InMemoryLanguageServiceTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Compilation CreateAdHocCompilation(string source, string assemblyName, string filePath)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)
        };

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}

internal sealed class InMemoryCompilationProvider : ICompilationProvider
{
    private readonly Compilation _compilation;

    public InMemoryCompilationProvider(Compilation compilation)
    {
        _compilation = compilation;
    }

    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new CompilationSnapshot(
            ProjectPath: workspaceRoot,
            Project: null,
            Compilation: _compilation,
            Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
    }

    public void Invalidate(string filePath)
    {
    }

    public void Dispose()
    {
    }
}

internal sealed class NonDisposingCompilationProvider : ICompilationProvider
{
    private readonly ICompilationProvider _inner;

    public NonDisposingCompilationProvider(ICompilationProvider inner)
    {
        _inner = inner;
    }

    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        return _inner.GetCompilationAsync(filePath, workspaceRoot, cancellationToken);
    }

    public void Invalidate(string filePath)
    {
        _inner.Invalidate(filePath);
    }

    public void Dispose()
    {
    }
}
