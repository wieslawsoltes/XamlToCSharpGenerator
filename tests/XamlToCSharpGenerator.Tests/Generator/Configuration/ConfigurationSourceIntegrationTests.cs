using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Configuration.Sources;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Tests.Infrastructure;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class ConfigurationSourceIntegrationTests
{
    [Fact]
    public void Build_MsBuild_Only_Mode_Produces_Expected_Configuration()
    {
        var msBuildOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.XamlSourceGenEnabled"] = "true",
            ["build_property.XamlSourceGenBackend"] = "SourceGen",
            ["build_property.AvaloniaSourceGenUseCompiledBindingsByDefault"] = "true"
        });

        var result = new XamlSourceGenConfigurationBuilder()
            .AddSource(new MsBuildConfigurationSource(msBuildOptions))
            .Build();

        Assert.True(result.Configuration.Build.IsEnabled);
        Assert.Equal("SourceGen", result.Configuration.Build.Backend);
        Assert.True(result.Configuration.Binding.UseCompiledBindingsByDefault);
    }

    [Fact]
    public void Build_File_Only_Mode_Produces_Expected_Configuration()
    {
        const string fileJson = """
            {
              "schemaVersion": 1,
              "build": {
                "isEnabled": true,
                "backend": "SourceGen"
              },
              "parser": {
                "allowImplicitXmlnsDeclaration": true
              }
            }
            """;

        var result = new XamlSourceGenConfigurationBuilder()
            .AddSource(new FileConfigurationSource("xaml-sourcegen.config.json", fileJson))
            .Build();

        Assert.True(result.Configuration.Build.IsEnabled);
        Assert.Equal("SourceGen", result.Configuration.Build.Backend);
        Assert.True(result.Configuration.Parser.AllowImplicitXmlnsDeclaration);
    }

    [Fact]
    public void Build_Code_Only_Mode_Produces_Expected_Configuration()
    {
        var compilation = CreateCompilation(
            """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.IsEnabled", "true")]
            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "SourceGen")]
            [assembly: AssemblyMetadata("XamlSourceGen.Parser.AllowImplicitXmlnsDeclaration", "true")]
            namespace Demo { public static class Marker { } }
            """);

        var result = new XamlSourceGenConfigurationBuilder()
            .AddSource(new CodeConfigurationSource(compilation))
            .Build();

        Assert.True(result.Configuration.Build.IsEnabled);
        Assert.Equal("SourceGen", result.Configuration.Build.Backend);
        Assert.True(result.Configuration.Parser.AllowImplicitXmlnsDeclaration);
    }

    [Fact]
    public void Build_Combines_File_MsBuild_And_Code_Sources_With_Deterministic_Precedence()
    {
        const string fileJson = """
            {
              "schemaVersion": 1,
              "build": {
                "backend": "FileBackend",
                "strictMode": true
              },
              "binding": {
                "useCompiledBindingsByDefault": true
              }
            }
            """;

        var msBuildOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.XamlSourceGenBackend"] = "MsBuildBackend",
            ["build_property.XamlSourceGenEnabled"] = "true",
            ["build_property.AvaloniaSourceGenStrictMode"] = "false",
            ["build_property.RootNamespace"] = "Demo.Root"
        });

        var compilation = CreateCompilation(
            """
            using System.Reflection;

            [assembly: AssemblyMetadata("XamlSourceGen.Build.Backend", "CodeBackend")]
            namespace Demo { public static class Marker { } }
            """);

        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new FileConfigurationSource("xaml-sourcegen.config.json", fileJson))
            .AddSource(new MsBuildConfigurationSource(msBuildOptions))
            .AddSource(new CodeConfigurationSource(compilation));

        var buildResult = builder.Build(new XamlSourceGenConfigurationSourceContext
        {
            ProjectDirectory = "/tmp/demo",
            AssemblyName = compilation.AssemblyName
        });

        var generatorOptions = GeneratorOptions.FromConfiguration(
            buildResult.Configuration,
            msBuildOptions,
            compilation.AssemblyName);

        Assert.Equal("CodeBackend", buildResult.Configuration.Build.Backend);
        Assert.True(buildResult.Configuration.Build.StrictMode);
        Assert.True(buildResult.Configuration.Binding.UseCompiledBindingsByDefault);
        Assert.Equal("CodeBackend", generatorOptions.Backend);
        Assert.True(generatorOptions.IsEnabled);
        Assert.Equal("Demo.Root", generatorOptions.RootNamespace);
    }

    [Fact]
    public void Build_File_Config_Is_Not_Overridden_By_MsBuild_Default_Values()
    {
        const string fileJson = """
            {
              "schemaVersion": 1,
              "build": {
                "isEnabled": true,
                "backend": "SourceGen"
              }
            }
            """;

        var msBuildOptions = new TestAnalyzerConfigOptions(new Dictionary<string, string>
        {
            ["build_property.XamlSourceGenBackend"] = "XamlIl",
            ["build_property.XamlSourceGenEnabled"] = "false"
        });

        var compilation = CreateCompilation("namespace Demo { public static class Marker { } }");

        var buildResult = new XamlSourceGenConfigurationBuilder()
            .AddSource(new FileConfigurationSource("xaml-sourcegen.config.json", fileJson))
            .AddSource(new MsBuildConfigurationSource(msBuildOptions))
            .Build(new XamlSourceGenConfigurationSourceContext
            {
                AssemblyName = compilation.AssemblyName
            });

        var generatorOptions = GeneratorOptions.FromConfiguration(
            buildResult.Configuration,
            msBuildOptions,
            compilation.AssemblyName);

        Assert.Equal("SourceGen", buildResult.Configuration.Build.Backend);
        Assert.True(buildResult.Configuration.Build.IsEnabled);
        Assert.Equal("SourceGen", generatorOptions.Backend);
        Assert.True(generatorOptions.IsEnabled);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            assemblyName: "ConfigurationSourceIntegrationTests",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Reflection.AssemblyMetadataAttribute).Assembly.Location)
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
