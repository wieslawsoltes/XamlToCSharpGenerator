using System;
using System.IO;
using System.Linq;

namespace XamlToCSharpGenerator.Tests.Build;

public class DifferentialBackendTests
{
    [Fact]
    public void Simple_Fixture_Builds_With_Both_XamlIl_And_SourceGen_Backends()
    {
        var repositoryRoot = GetRepositoryRoot();
        var artifacts = BuildTestArtifactCache.GetSourceGenArtifacts();

        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "backend-diff");

        try
        {
            var projectPath = Path.Combine(tempDir, "DifferentialFixture.csproj");
            File.WriteAllText(projectPath, BuildProjectText(
                artifacts.PropsPath,
                artifacts.TargetsPath,
                artifacts.CreateConditionalSourceGenItemGroup()));

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

            var restore = RunProcess(
                tempDir,
                "dotnet",
                $"restore \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers {DifferentialBuildHarness.GetRestoreMsBuildProperties(tempDir)}");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var sourceGen = BuildFixture(projectPath, tempDir, backend: "SourceGen");
            Assert.True(sourceGen.ExitCode == 0, sourceGen.Output);
            Assert.DoesNotContain("CS8785", sourceGen.Output, StringComparison.Ordinal);

            var sourceGenAssemblyPath = DifferentialBuildHarness.GetAssemblyPath(tempDir, "SourceGen", "DifferentialFixture");
            Assert.True(File.Exists(sourceGenAssemblyPath), sourceGen.Output);

            var sourceGenGeneratedDirectory = DifferentialBuildHarness.GetGeneratedDirectory(tempDir, "SourceGen");
            var sourceGenGeneratedFiles = Directory.Exists(sourceGenGeneratedDirectory)
                ? Directory.GetFiles(sourceGenGeneratedDirectory, "*.XamlSourceGen.g.cs", SearchOption.AllDirectories)
                : Array.Empty<string>();
            Assert.NotEmpty(sourceGenGeneratedFiles);

            var xamlIl = BuildFixture(projectPath, tempDir, backend: "XamlIl");
            Assert.True(xamlIl.ExitCode == 0, xamlIl.Output);
            Assert.DoesNotContain("CS8785", xamlIl.Output, StringComparison.Ordinal);

            var xamlIlAssemblyPath = DifferentialBuildHarness.GetAssemblyPath(tempDir, "XamlIl", "DifferentialFixture");
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
            $"build \"{projectPath}\" --nologo -t:Rebuild -m:1 /nodeReuse:false --disable-build-servers --no-restore " +
            $"-p:AvaloniaXamlCompilerBackend={backend} " +
            $"{DifferentialBuildHarness.GetBackendMsBuildProperties(workingDirectory, backend)} " +
            "-p:UseSharedCompilation=false " +
            "-p:ProduceReferenceAssembly=false";
        return RunProcess(workingDirectory, "dotnet", arguments);
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
    {
        return DifferentialBuildHarness.RunProcess(workingDirectory, fileName, arguments);
    }

    private static string BuildProjectText(
        string propsPath,
        string targetsPath,
        string sourceGenItemGroup)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)generated</CompilerGeneratedFilesOutputPath>
    <DefaultItemExcludes>$(DefaultItemExcludes);obj/**;bin/**</DefaultItemExcludes>
  </PropertyGroup>

  <Import Project="{propsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
  </ItemGroup>

{sourceGenItemGroup}

  <Import Project="{targetsPath}" Condition="'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'" />
</Project>
""";
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

}
