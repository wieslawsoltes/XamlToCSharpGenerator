using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class DifferentialBackendTests
{
    [Fact]
    public void Simple_Fixture_Builds_With_Both_XamlIl_And_SourceGen_Backends()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var runtimeProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj");
        var coreProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core", "XamlToCSharpGenerator.Core.csproj");
        var compilerProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Compiler", "XamlToCSharpGenerator.Compiler.csproj");
        var frameworkProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Framework.Abstractions", "XamlToCSharpGenerator.Framework.Abstractions.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var generatorProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");

        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "backend-diff");

        try
        {
            var projectPath = Path.Combine(tempDir, "DifferentialFixture.csproj");
            File.WriteAllText(projectPath, BuildProjectText(
                NormalizeForMsBuild(propsPath),
                NormalizeForMsBuild(targetsPath),
                NormalizeForMsBuild(runtimeProject),
                NormalizeForMsBuild(coreProject),
                NormalizeForMsBuild(compilerProject),
                NormalizeForMsBuild(frameworkProject),
                NormalizeForMsBuild(avaloniaProject),
                NormalizeForMsBuild(generatorProject)));

            File.WriteAllText(Path.Combine(tempDir, "App.axaml"), """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFixture.App">
                  <Application.Styles />
                </Application>
                """);

            File.WriteAllText(Path.Combine(tempDir, "App.axaml.cs"), """
                using Avalonia;

                namespace DifferentialFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                    }
                }
                """);

            File.WriteAllText(Path.Combine(tempDir, "MainView.axaml"), """
                <UserControl xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="DifferentialFixture.MainView">
                    <StackPanel>
                        <TextBlock Text="Hello from fixture" />
                    </StackPanel>
                </UserControl>
                """);

            File.WriteAllText(Path.Combine(tempDir, "MainView.axaml.cs"), """
                using Avalonia.Controls;

                namespace DifferentialFixture;

                public partial class MainView : UserControl
                {
                    public MainView()
                    {
                    }
                }
                """);

            var restore = RunProcess(tempDir, "dotnet", $"restore \"{projectPath}\" --nologo");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var sourceGen = BuildFixture(projectPath, tempDir, backend: "SourceGen");
            Assert.True(sourceGen.ExitCode == 0, sourceGen.Output);
            Assert.DoesNotContain("CS8785", sourceGen.Output, StringComparison.Ordinal);

            var sourceGenAssemblyPath = Path.Combine(tempDir, "bin", "Debug", "net10.0", "DifferentialFixture.dll");
            Assert.True(File.Exists(sourceGenAssemblyPath), sourceGen.Output);

            var sourceGenGeneratedDirectory = Path.Combine(tempDir, "obj", "generated");
            var sourceGenGeneratedFiles = Directory.Exists(sourceGenGeneratedDirectory)
                ? Directory.GetFiles(sourceGenGeneratedDirectory, "*.XamlSourceGen.g.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert.NotEmpty(sourceGenGeneratedFiles);

            var clean = RunProcess(
                tempDir,
                "dotnet",
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(clean.ExitCode == 0, clean.Output);

            var xamlIl = BuildFixture(projectPath, tempDir, backend: "XamlIl");
            Assert.True(xamlIl.ExitCode == 0, xamlIl.Output);
            Assert.DoesNotContain("CS8785", xamlIl.Output, StringComparison.Ordinal);

            var xamlIlAssemblyPath = Path.Combine(tempDir, "bin", "Debug", "net10.0", "DifferentialFixture.dll");
            Assert.True(File.Exists(xamlIlAssemblyPath), xamlIl.Output);
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(tempDir);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static (int ExitCode, string Output) BuildFixture(string projectPath, string workingDirectory, string backend)
    {
        var arguments =
            $"build \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers " +
            $"-p:AvaloniaXamlCompilerBackend={backend}";
        return RunProcess(workingDirectory, "dotnet", arguments);
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

    private static string BuildProjectText(
        string propsPath,
        string targetsPath,
        string runtimeProject,
        string coreProject,
        string compilerProject,
        string frameworkProject,
        string avaloniaProject,
        string generatorProject)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <Import Project="{propsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
  </ItemGroup>

  <ItemGroup Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'">
    <ProjectReference Include="{runtimeProject}" />
    <ProjectReference Include="{coreProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{compilerProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{frameworkProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{avaloniaProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{generatorProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <Import Project="{targetsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />
</Project>
""";
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }
}
