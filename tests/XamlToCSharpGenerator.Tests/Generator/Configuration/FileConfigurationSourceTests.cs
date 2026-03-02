using System;
using System.IO;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Configuration.Sources;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class FileConfigurationSourceTests
{
    [Fact]
    public void Load_Parses_Configuration_Document()
    {
        const string json = """
            {
              "schemaVersion": 1,
              "build": {
                "isEnabled": true,
                "backend": "SourceGen",
                "strictMode": true
              },
              "parser": {
                "allowImplicitXmlnsDeclaration": true,
                "globalXmlnsPrefixes": {
                  "vm": "using:Demo.ViewModels"
                }
              },
              "binding": {
                "useCompiledBindingsByDefault": true
              },
              "diagnostics": {
                "severityOverrides": {
                  "AXSG0100": "Error"
                }
              }
            }
            """;

        var source = new FileConfigurationSource("xaml-sourcegen.config.json", json);
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        Assert.True(result.Patch.Build.IsEnabled.HasValue);
        Assert.True(result.Patch.Build.IsEnabled.Value);
        Assert.Equal("SourceGen", result.Patch.Build.Backend.Value);
        Assert.True(result.Patch.Build.StrictMode.Value);
        Assert.True(result.Patch.Parser.AllowImplicitXmlnsDeclaration.Value);
        Assert.Equal("using:Demo.ViewModels", result.Patch.Parser.GlobalXmlnsPrefixes["vm"]);
        Assert.True(result.Patch.Binding.UseCompiledBindingsByDefault.Value);
        Assert.Equal(
            XamlSourceGenConfigurationIssueSeverity.Error,
            result.Patch.Diagnostics.SeverityOverrides["AXSG0100"]);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Load_Reports_Invalid_Json()
    {
        var source = new FileConfigurationSource("xaml-sourcegen.config.json", "{ invalid json }");
        var result = source.Load(XamlSourceGenConfigurationSourceContext.Empty);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("AXSG0916", issue.Code);
        Assert.Equal(XamlSourceGenConfigurationIssueSeverity.Error, issue.Severity);
    }

    [Fact]
    public void Load_Project_Default_Configuration_File_When_Present()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "axsg-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var configPath = Path.Combine(tempDirectory, FileConfigurationSource.DefaultConfigurationFileName);
            File.WriteAllText(
                configPath,
                """
                {
                  "schemaVersion": 1,
                  "build": {
                    "backend": "ProjectDefaultBackend"
                  }
                }
                """);

            var source = FileConfigurationSource.CreateProjectDefault();
            var result = source.Load(new XamlSourceGenConfigurationSourceContext
            {
                ProjectDirectory = tempDirectory
            });

            Assert.Equal("ProjectDefaultBackend", result.Patch.Build.Backend.Value);
            Assert.Empty(result.Issues);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
