using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Mono.Cecil;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PackageIntegrationTests
{
    [Fact]
    public void TopLevel_Package_Packs_And_Contains_Expected_Assets()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var outputDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "package-integration");

        try
        {
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.props");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.targets");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.Build.Tasks.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/Mono.Cecil.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Generator.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Compiler.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Framework.Abstractions.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.ExpressionSemantics.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.dll");
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(outputDir);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void RuntimeAvalonia_Package_Packs_And_Declares_Runtime_Dependencies()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime.Avalonia", "XamlToCSharpGenerator.Runtime.Avalonia.csproj");
        var outputDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "runtime-avalonia-package-integration");

        try
        {
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.Runtime.Avalonia.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var nuspecStream = nuspecEntry.Open();
            var nuspec = XDocument.Load(nuspecStream);
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            var dependencyIds = nuspec
                .Descendants(ns + "dependency")
                .Select(static element => (string?)element.Attribute("id"))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            Assert.Contains("XamlToCSharpGenerator.MiniLanguageParsing", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.RemoteProtocol", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.Runtime.Core", dependencyIds);
            Assert.Contains("Avalonia", dependencyIds);
            Assert.Contains("Avalonia.Markup.Xaml.Loader", dependencyIds);
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(outputDir);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Rewrites_Avalonia_Loader_Calls()
    {
        using var artifact = BuildTopLevelPackageConsumer("package-il-weaving-consumer");
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.App"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedInitializerParameterTypes: ["PackageIlWeavingConsumer.MainWindow"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsGeneratedInitializer(
            assembly,
            "PackageIlWeavingConsumer.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedInitializerParameterTypes:
            [
                "System.IServiceProvider",
                "PackageIlWeavingConsumer.ServiceProviderPanel"
            ]);

        AvaloniaLoaderCallAssertions.AssertNoAvaloniaLoaderCallsRemain(assembly, "PackageIlWeavingConsumer");
        Assert.Contains("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevel_Package_Consumer_Build_Can_Disable_Il_Weaving_Via_MsBuild_Flag()
    {
        using var artifact = BuildTopLevelPackageConsumer(
            "package-il-weaving-consumer-disabled",
            ("XamlSourceGenIlWeavingEnabled", "false"));
        using var assembly = AssemblyDefinition.ReadAssembly(artifact.AssemblyPath);

        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.App",
            "Initialize",
            explicitParameterCount: 0,
            expectedLoaderParameterTypes: ["System.Object"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.MainWindow",
            ".ctor",
            explicitParameterCount: 0,
            expectedLoaderParameterTypes: ["System.Object"]);
        AvaloniaLoaderCallAssertions.AssertMethodCallsAvaloniaLoader(
            assembly,
            "PackageIlWeavingConsumer.ServiceProviderPanel",
            ".ctor",
            explicitParameterCount: 1,
            expectedLoaderParameterTypes:
            [
                "System.IServiceProvider",
                "System.Object"
            ]);

        Assert.DoesNotContain("[AXSG.Build] IL weaving inspected", artifact.BuildOutput, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        return (process.ExitCode, outputBuilder.ToString());
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private static PackageConsumerArtifact BuildTopLevelPackageConsumer(
        string scenario,
        params (string Name, string Value)[] msbuildProperties)
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, scenario);

        try
        {
            var packageOutputDirectory = Path.Combine(workspaceDirectory, "packages");
            var consumerDirectory = Path.Combine(workspaceDirectory, "consumer");
            Directory.CreateDirectory(packageOutputDirectory);
            Directory.CreateDirectory(consumerDirectory);

            var packageVersion = CreateUniquePackageVersion();

            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var pack = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{packageOutputDirectory}\" -p:PackageVersion={packageVersion}");
            Assert.True(pack.ExitCode == 0, pack.Output);

            WritePackageConsumerFiles(consumerDirectory, packageOutputDirectory, packageVersion, msbuildProperties);

            var projectPath = Path.Combine(consumerDirectory, "PackageIlWeavingConsumer.csproj");
            var build = RunProcess(
                consumerDirectory,
                "dotnet",
                $"build \"{projectPath}\" -t:Rebuild -c Debug --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(build.ExitCode == 0, build.Output);

            var assemblyPath = Path.Combine(consumerDirectory, "bin", "Debug", "net10.0", "PackageIlWeavingConsumer.dll");
            Assert.True(File.Exists(assemblyPath), build.Output);

            return new PackageConsumerArtifact(workspaceDirectory, assemblyPath, build.Output);
        }
        catch
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
            throw;
        }
    }

    private static void WritePackageConsumerFiles(
        string consumerDirectory,
        string packageOutputDirectory,
        string packageVersion,
        IReadOnlyList<(string Name, string Value)> msbuildProperties)
    {
        var projectPath = Path.Combine(consumerDirectory, "PackageIlWeavingConsumer.csproj");
        var nuGetConfigPath = Path.Combine(consumerDirectory, "NuGet.Config");
        var propertyOverrides = string.Concat(msbuildProperties.Select(static property =>
            $"\n    <{property.Name}>{property.Value}</{property.Name}>"));

        File.WriteAllText(projectPath, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>PackageIlWeavingConsumer</AssemblyName>
    <RootNamespace>PackageIlWeavingConsumer</RootNamespace>
    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
    <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>{{propertyOverrides}}
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="XamlToCSharpGenerator" Version="{{packageVersion}}" />
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.3.12" />
    <PackageReference Include="Avalonia.Fonts.Inter" Version="11.3.12" />
  </ItemGroup>

  <ItemGroup>
    <AvaloniaXaml Update="App.axaml;MainWindow.axaml;ServiceProviderPanel.axaml"
                  SubType="Designer"
                  Visible="true" />
  </ItemGroup>
</Project>
""");

        var nuGetConfig = new XDocument(
            new XElement(
                "configuration",
                new XElement(
                    "packageSources",
                    new XElement("clear"),
                    new XElement(
                        "add",
                        new XAttribute("key", "local"),
                        new XAttribute("value", packageOutputDirectory)),
                    new XElement(
                        "add",
                        new XAttribute("key", "nuget.org"),
                        new XAttribute("value", "https://api.nuget.org/v3/index.json")))));
        nuGetConfig.Save(nuGetConfigPath);

        File.WriteAllText(Path.Combine(consumerDirectory, "Program.cs"), """
using System;
using Avalonia;
using XamlToCSharpGenerator.Runtime;

namespace PackageIlWeavingConsumer;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseAvaloniaSourceGeneratedXaml();
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "App.axaml"), """
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:themes="clr-namespace:Avalonia.Themes.Fluent;assembly=Avalonia.Themes.Fluent"
             x:Class="PackageIlWeavingConsumer.App">
  <Application.Styles>
    <themes:FluentTheme />
  </Application.Styles>
</Application>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "App.axaml.cs"), """
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "MainWindow.axaml"), """
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="PackageIlWeavingConsumer.MainWindow"
        Width="480"
        Height="320"
        Title="IL Weaving Package Consumer">
  <TextBlock HorizontalAlignment="Center"
             VerticalAlignment="Center"
             Text="Package consumer IL weaving integration test" />
</Window>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "MainWindow.axaml.cs"), """
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "ServiceProviderPanel.axaml"), """
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="PackageIlWeavingConsumer.ServiceProviderPanel">
  <TextBlock Text="Service provider panel" />
</UserControl>
""");

        File.WriteAllText(Path.Combine(consumerDirectory, "ServiceProviderPanel.axaml.cs"), """
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PackageIlWeavingConsumer;

public partial class ServiceProviderPanel : UserControl
{
    public ServiceProviderPanel(IServiceProvider serviceProvider)
    {
        AvaloniaXamlLoader.Load(serviceProvider, this);
    }
}
""");
    }

    private static string CreateUniquePackageVersion()
    {
        return $"1.0.0-ilweaving-test-{Guid.NewGuid():N}";
    }

    private sealed class PackageConsumerArtifact : IDisposable
    {
        public PackageConsumerArtifact(string workspaceDirectory, string assemblyPath, string buildOutput)
        {
            WorkspaceDirectory = workspaceDirectory;
            AssemblyPath = assemblyPath;
            BuildOutput = buildOutput;
        }

        public string WorkspaceDirectory { get; }

        public string AssemblyPath { get; }

        public string BuildOutput { get; }

        public void Dispose()
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(WorkspaceDirectory);
        }
    }
}
