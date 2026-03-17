using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Previewer.DesignerHost;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class SourceGeneratedPreviewXamlPreprocessorTests
{
    private static readonly Assembly TestAssembly = typeof(PreviewTestRoot).Assembly;
    private static readonly MarkupExpressionParser MarkupParser = new();

    [Fact]
    public void Rewrite_Rewrites_Implicit_Expression_Markup_To_Preview_CSharp()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock Text="{Name}" />
            </UserControl>
            """);

        var textValue = document.Descendants().Single(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")?.Value;

        Assert.NotNull(textValue);
        var (code, dependencyNames) = DecodeMarkupValue(textValue!);
        Assert.Equal("source.Name", code);
        Assert.Equal(["Name"], dependencyNames);
    }

    [Fact]
    public void Rewrite_Rewrites_Root_Shorthand_Using_XClass_Context()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock Text="{this.Title}" />
            </UserControl>
            """);

        var textValue = document.Descendants().Single(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")?.Value;

        Assert.NotNull(textValue);
        var (code, dependencyNames) = DecodeMarkupValue(textValue!);
        Assert.Equal("root.Title", code);
        Assert.Empty(dependencyNames);
    }

    [Fact]
    public void Rewrite_Normalizes_Compact_Inline_CSharp_With_Dependencies()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         xmlns:axsg="using:XamlToCSharpGenerator.Runtime"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock Text="{axsg:CSharp Code=source.ProductName}" />
            </UserControl>
            """);

        var textValue = document.Descendants().Single(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")?.Value;

        Assert.NotNull(textValue);
        var (code, dependencyNames) = DecodeMarkupValue(textValue!);
        Assert.Equal("source.ProductName", code);
        Assert.Equal(["ProductName"], dependencyNames);
    }

    [Fact]
    public void Rewrite_Normalizes_ObjectElement_Inline_CSharp_Block()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         xmlns:axsg="using:XamlToCSharpGenerator.Runtime"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock>
                <TextBlock.Text>
                  <axsg:CSharp>source.ProductName</axsg:CSharp>
                </TextBlock.Text>
              </TextBlock>
            </UserControl>
            """);

        var csharpElement = document.Descendants().Single(element => element.Name.LocalName == "CSharp");
        var encodedCode = csharpElement.Attribute("CodeBase64Url")?.Value;
        var encodedDependencies = csharpElement.Attribute("DependencyNamesBase64Url")?.Value;

        Assert.NotNull(encodedCode);
        Assert.Equal("source.ProductName", PreviewMarkupValueCodec.DecodeBase64Url(encodedCode!));
        Assert.NotNull(encodedDependencies);
        Assert.Equal("ProductName", PreviewMarkupValueCodec.DecodeBase64Url(encodedDependencies!));
        Assert.True(string.IsNullOrWhiteSpace(csharpElement.Value));
        Assert.Null(csharpElement.Attribute("Code"));
    }

    [Fact]
    public void Rewrite_Rewrites_Implicit_Event_Lambda_To_Preview_CSharp()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <Button Click="{(sender, e) => ClickCount++}" />
            </UserControl>
            """);

        var clickValue = document.Descendants().Single(element => element.Name.LocalName == "Button")
            .Attribute("Click")?.Value;

        Assert.NotNull(clickValue);
        var (code, dependencyNames) = DecodeMarkupValue(clickValue!);
        Assert.Contains("source.ClickCount", code, StringComparison.Ordinal);
        Assert.Equal(["ClickCount"], dependencyNames);
    }

    [Fact]
    public void Rewrite_Normalizes_Event_Statement_Block_To_Preview_CSharp()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         xmlns:axsg="using:XamlToCSharpGenerator.Runtime"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <Button>
                <Button.Click>
                  <axsg:CSharp>ClickCount++;</axsg:CSharp>
                </Button.Click>
              </Button>
            </UserControl>
            """);

        var csharpElement = document.Descendants().Single(element => element.Name.LocalName == "CSharp");
        var encodedCode = csharpElement.Attribute("CodeBase64Url")?.Value;
        var encodedDependencies = csharpElement.Attribute("DependencyNamesBase64Url")?.Value;

        Assert.NotNull(encodedCode);
        Assert.Equal("source.ClickCount++;", PreviewMarkupValueCodec.DecodeBase64Url(encodedCode!));
        Assert.NotNull(encodedDependencies);
        Assert.Equal("ClickCount", PreviewMarkupValueCodec.DecodeBase64Url(encodedDependencies!));
    }

    [Fact]
    public void Rewrite_Preserves_Local_Function_Scope_In_Event_Code()
    {
        var document = Rewrite("""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         xmlns:axsg="using:XamlToCSharpGenerator.Runtime"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <Button>
                <Button.Click>
                  <axsg:CSharp>
                    void Reset(string Name)
                    {
                        _ = Name;
                    }

                    Reset(source.Name);
                    ClickCount++;
                  </axsg:CSharp>
                </Button.Click>
              </Button>
            </UserControl>
            """);

        var csharpElement = document.Descendants().Single(element => element.Name.LocalName == "CSharp");
        var encodedCode = csharpElement.Attribute("CodeBase64Url")?.Value;

        Assert.NotNull(encodedCode);
        var rewrittenCode = PreviewMarkupValueCodec.DecodeBase64Url(encodedCode!);
        Assert.True(rewrittenCode.Contains("void Reset(string Name)", StringComparison.Ordinal), rewrittenCode);
        Assert.True(rewrittenCode.Contains("_ = Name;", StringComparison.Ordinal), rewrittenCode);
        Assert.True(rewrittenCode.Contains("Reset(source.Name);", StringComparison.Ordinal), rewrittenCode);
        Assert.True(rewrittenCode.Contains("source.ClickCount++;", StringComparison.Ordinal), rewrittenCode);
        Assert.DoesNotContain("source.Reset(", rewrittenCode, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = source.Name;", rewrittenCode, StringComparison.Ordinal);
    }

    [Fact]
    public void Rewrite_Leaves_Regular_Binding_Markup_Unchanged()
    {
        const string xaml = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock Text="{Binding Name}" />
            </UserControl>
            """;

        var document = Rewrite(xaml);
        var textValue = document.Descendants().Single(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")?.Value;

        Assert.Equal("{Binding Name}", textValue);
    }

    [Fact]
    public void Rewrite_Leaves_Custom_Markup_Extension_Unchanged_When_Assembly_Is_Loaded_On_Demand()
    {
        var assemblyName = "AxsgPreviewExternalMarkup_" + Guid.NewGuid().ToString("N");
        var assemblyImage = CompileExternalMarkupExtensionAssembly(assemblyName);
        using var resolver = new TestAssemblyResolveScope(assemblyName, assemblyImage);

        var document = Rewrite($$"""
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:vm="using:XamlToCSharpGenerator.Tests.PreviewerHost"
                         xmlns:ext="clr-namespace:ExternalPreviewMarkup;assembly={{assemblyName}}"
                         x:Class="XamlToCSharpGenerator.Tests.PreviewerHost.PreviewTestRoot"
                         x:DataType="vm:PreviewTestViewModel">
              <TextBlock Text="{ext:Echo Value=Name}" />
            </UserControl>
            """);

        var textValue = document.Descendants().Single(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")?.Value;

        Assert.True(resolver.WasResolved);
        Assert.Equal("{ext:Echo Value=Name}", textValue);
    }

    private static XDocument Rewrite(string xaml)
    {
        RegisterPreviewTestTypes();
        var rewritten = SourceGeneratedPreviewXamlPreprocessor.Rewrite(xaml, TestAssembly);
        return XDocument.Parse(rewritten, LoadOptions.PreserveWhitespace);
    }

    private static void RegisterPreviewTestTypes()
    {
        SourceGenKnownTypeRegistry.RegisterTypes(typeof(PreviewTestRoot), typeof(PreviewTestViewModel));
        SourceGenKnownTypeRegistry.RegisterXmlnsDefinition(
            "using:XamlToCSharpGenerator.Tests.PreviewerHost",
            "XamlToCSharpGenerator.Tests.PreviewerHost");
    }

    private static (string Code, IReadOnlyList<string> DependencyNames) DecodeMarkupValue(string rawValue)
    {
        Assert.True(MarkupParser.TryParseMarkupExtension(rawValue, out var markupExtension));
        Assert.Equal(XamlMarkupExtensionKind.CSharp, XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name));
        Assert.True(markupExtension.NamedArguments.TryGetValue("CodeBase64Url", out var encodedCode));

        var dependencyNames = markupExtension.NamedArguments.TryGetValue("DependencyNamesBase64Url", out var encodedDependencies)
            ? PreviewMarkupValueCodec.DecodeBase64Url(encodedDependencies)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();

        return (PreviewMarkupValueCodec.DecodeBase64Url(encodedCode), dependencyNames);
    }

    private static byte[] CompileExternalMarkupExtensionAssembly(string assemblyName)
    {
        var source = """
            using System;
            using Avalonia.Markup.Xaml;

            namespace ExternalPreviewMarkup;

            public sealed class EchoExtension : MarkupExtension
            {
                public string? Value { get; set; }

                public override object? ProvideValue(IServiceProvider serviceProvider)
                    => Value;
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source, path: assemblyName + ".cs");
        var references = CreateLoadedAssemblyMetadataReferences();
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(
            emitResult.Success,
            "Failed to emit preview markup-extension test assembly: " +
            string.Join(Environment.NewLine, emitResult.Diagnostics));
        return stream.ToArray();
    }

    private static IReadOnlyList<MetadataReference> CreateLoadedAssemblyMetadataReferences()
    {
        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var assembly = loadedAssemblies[index];
            if (assembly.IsDynamic ||
                string.IsNullOrWhiteSpace(assembly.Location) ||
                !seenPaths.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        return references;
    }

    private sealed class TestAssemblyResolveScope : IDisposable
    {
        private readonly string _assemblyName;
        private readonly byte[] _assemblyImage;

        public TestAssemblyResolveScope(string assemblyName, byte[] assemblyImage)
        {
            _assemblyName = assemblyName;
            _assemblyImage = assemblyImage;
            AssemblyLoadContext.Default.Resolving += OnResolving;
        }

        public bool WasResolved { get; private set; }

        public void Dispose()
        {
            AssemblyLoadContext.Default.Resolving -= OnResolving;
        }

        private Assembly? OnResolving(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (!string.Equals(assemblyName.Name, _assemblyName, StringComparison.Ordinal))
            {
                return null;
            }

            WasResolved = true;
            return context.LoadFromStream(new MemoryStream(_assemblyImage, writable: false));
        }
    }
}

internal sealed class PreviewTestRoot : UserControl
{
    public string Title { get; set; } = string.Empty;
}

internal sealed class PreviewTestViewModel
{
    public string Name { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int ClickCount { get; set; }

    public void Reset()
    {
    }
}
