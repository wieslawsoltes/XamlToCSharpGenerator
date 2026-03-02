using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Configuration.Sources;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CodeConfigurationSourceTests
{
    [Fact]
    public void Load_Reads_AssemblyMetadata_Configuration()
    {
        var compilation = CreateCompilation(
            """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "CodeBackend")]
            [assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]
            [assembly: AssemblyMetadata("XamlSourceGen.Binding.UseCompiledBindingsByDefault", "true")]
            [assembly: AssemblyMetadata("XamlSourceGen.Parser.GlobalXmlnsPrefixes.vm", "using:Demo.ViewModels")]
            [assembly: AssemblyMetadata("XamlSourceGen.Diagnostics.SeverityOverrides.AXSG0100", "Error")]

            namespace Demo
            {
                public static class Marker
                {
                }
            }
            """);

        var source = new CodeConfigurationSource(compilation);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        Assert.Equal("CodeBackend", result.Patch.Build.Backend.Value);
        Assert.True(result.Patch.Build.IsEnabled.Value);
        Assert.True(result.Patch.Binding.UseCompiledBindingsByDefault.Value);
        Assert.Equal("using:Demo.ViewModels", result.Patch.Parser.GlobalXmlnsPrefixes["vm"]);
        Assert.Equal(
            XamlSourceGenConfigurationIssueSeverity.Error,
            result.Patch.Diagnostics.SeverityOverrides["AXSG0100"]);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Load_Reports_Invalid_Boolean_AssemblyMetadata()
    {
        var compilation = CreateCompilation(
            """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.StrictMode", "nope")]
            namespace Demo { public static class Marker { } }
            """);

        var source = new CodeConfigurationSource(compilation);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("AXSG0931", issue.Code);
        Assert.Equal(XamlSourceGenConfigurationIssueSeverity.Warning, issue.Severity);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: "CodeConfigurationSourceTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Reflection.AssemblyMetadataAttribute).Assembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
