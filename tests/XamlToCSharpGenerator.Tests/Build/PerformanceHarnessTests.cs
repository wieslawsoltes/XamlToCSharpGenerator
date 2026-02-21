using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PerformanceHarnessTests
{
    private const int ProcessTimeoutMilliseconds = 180_000;

    [PerfFact]
    public void SourceGen_Incremental_Build_Harness_Captures_Full_And_Edit_Rebuild_Timings()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var runtimeProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj");
        var coreProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core", "XamlToCSharpGenerator.Core.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var generatorProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");

        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "performance-harness");

        try
        {
            var projectPath = Path.Combine(tempDir, "PerfFixture.csproj");
            File.WriteAllText(projectPath, BuildProjectText(
                NormalizeForMsBuild(propsPath),
                NormalizeForMsBuild(targetsPath),
                NormalizeForMsBuild(runtimeProject),
                NormalizeForMsBuild(coreProject),
                NormalizeForMsBuild(avaloniaProject),
                NormalizeForMsBuild(generatorProject)));

            var appXamlPath = Path.Combine(tempDir, "App.axaml");
            var mainXamlPath = Path.Combine(tempDir, "MainWindow.axaml");
            var colorsXamlPath = Path.Combine(tempDir, "Colors.axaml");

            File.WriteAllText(appXamlPath, """
                <Application xmlns="https://github.com/avaloniaui"
                             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                             x:Class="PerfFixture.App">
                  <Application.Resources>
                    <ResourceDictionary>
                      <ResourceDictionary.MergedDictionaries>
                        <ResourceInclude Source="/Colors.axaml" />
                      </ResourceDictionary.MergedDictionaries>
                    </ResourceDictionary>
                  </Application.Resources>
                </Application>
                """);

            File.WriteAllText(Path.Combine(tempDir, "App.axaml.cs"), """
                using Avalonia;

                namespace PerfFixture;

                public partial class App : Application
                {
                    public override void Initialize()
                    {
                    }
                }
                """);

            File.WriteAllText(mainXamlPath, """
                <Window xmlns="https://github.com/avaloniaui"
                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                        x:Class="PerfFixture.MainWindow"
                        Title="Perf Harness">
                    <StackPanel>
                        <TextBlock Text="Hello Perf Harness" />
                    </StackPanel>
                </Window>
                """);

            File.WriteAllText(Path.Combine(tempDir, "MainWindow.axaml.cs"), """
                using Avalonia.Controls;

                namespace PerfFixture;

                public partial class MainWindow : Window
                {
                    public MainWindow()
                    {
                    }
                }
                """);

            File.WriteAllText(colorsXamlPath, """
                <ResourceDictionary xmlns="https://github.com/avaloniaui"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                  <Color x:Key="AccentColor">#FF3A7AFE</Color>
                </ResourceDictionary>
                """);

            var restore = RunProcess(tempDir, "dotnet", $"restore \"{projectPath}\" --nologo");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var clean = RunProcess(
                tempDir,
                "dotnet",
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(clean.ExitCode == 0, clean.Output);

            var fullBuild = TimedBuild(projectPath, tempDir);
            Assert.True(fullBuild.Result.ExitCode == 0, fullBuild.Result.Output);

            File.AppendAllText(mainXamlPath, Environment.NewLine + "<!-- incremental-edit-main -->");
            var singleFileBuild = TimedBuild(projectPath, tempDir);
            Assert.True(singleFileBuild.Result.ExitCode == 0, singleFileBuild.Result.Output);

            File.AppendAllText(colorsXamlPath, Environment.NewLine + "<!-- incremental-edit-include -->");
            var includeBuild = TimedBuild(projectPath, tempDir);
            Assert.True(includeBuild.Result.ExitCode == 0, includeBuild.Result.Output);

            Assert.True(singleFileBuild.Elapsed.TotalMilliseconds > 0);
            Assert.True(includeBuild.Elapsed.TotalMilliseconds > 0);
            Assert.True(fullBuild.Elapsed.TotalMilliseconds > 0);

            var fullBuildMaxMs = ReadThresholdMilliseconds("AXSG_PERF_MAX_FULL_BUILD_MS", 120_000d);
            var singleEditMaxMs = ReadThresholdMilliseconds("AXSG_PERF_MAX_SINGLE_EDIT_MS", 30_000d);
            var includeEditMaxMs = ReadThresholdMilliseconds("AXSG_PERF_MAX_INCLUDE_EDIT_MS", 45_000d);
            var incrementalRatioMax = ReadThresholdMilliseconds("AXSG_PERF_MAX_INCREMENTAL_TO_FULL_RATIO", 20d);

            Assert.True(
                fullBuild.Elapsed.TotalMilliseconds <= fullBuildMaxMs,
                $"Full build took {fullBuild.Elapsed.TotalMilliseconds:F2}ms, threshold={fullBuildMaxMs:F2}ms");
            Assert.True(
                singleFileBuild.Elapsed.TotalMilliseconds <= singleEditMaxMs,
                $"Single-edit incremental build took {singleFileBuild.Elapsed.TotalMilliseconds:F2}ms, threshold={singleEditMaxMs:F2}ms");
            Assert.True(
                includeBuild.Elapsed.TotalMilliseconds <= includeEditMaxMs,
                $"Include-edit incremental build took {includeBuild.Elapsed.TotalMilliseconds:F2}ms, threshold={includeEditMaxMs:F2}ms");

            // Guardrail: incremental rebuilds should not be pathological compared to clean full build.
            Assert.True(
                singleFileBuild.Elapsed.TotalMilliseconds < fullBuild.Elapsed.TotalMilliseconds * incrementalRatioMax,
                $"Single-edit incremental ratio exceeded threshold. single={singleFileBuild.Elapsed.TotalMilliseconds:F2}ms full={fullBuild.Elapsed.TotalMilliseconds:F2}ms ratioLimit={incrementalRatioMax:F2}");
            Assert.True(
                includeBuild.Elapsed.TotalMilliseconds < fullBuild.Elapsed.TotalMilliseconds * incrementalRatioMax,
                $"Include-edit incremental ratio exceeded threshold. include={includeBuild.Elapsed.TotalMilliseconds:F2}ms full={fullBuild.Elapsed.TotalMilliseconds:F2}ms ratioLimit={incrementalRatioMax:F2}");
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

    private static (TimeSpan Elapsed, (int ExitCode, string Output) Result) TimedBuild(string projectPath, string workingDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = RunProcess(workingDirectory, "dotnet", BuildArguments(projectPath));
        stopwatch.Stop();
        return (stopwatch.Elapsed, result);
    }

    private static string BuildArguments(string projectPath)
    {
        return
            $"build \"{projectPath}\" --nologo --no-restore -m:1 /nodeReuse:false --disable-build-servers " +
            "-p:AvaloniaXamlCompilerBackend=SourceGen";
    }

    private static double ReadThresholdMilliseconds(string environmentVariable, double fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallbackValue;
        }

        if (double.TryParse(raw, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return fallbackValue;
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
        if (!process.WaitForExit(ProcessTimeoutMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup for timed-out process.
            }

            return (-1, $"Timed out after {ProcessTimeoutMilliseconds}ms while running: {fileName} {arguments}");
        }

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
        string avaloniaProject,
        string generatorProject)
    {
        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <OutputType>Library</OutputType>
  </PropertyGroup>

  <Import Project="{propsPath}" />

  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.3.12" />
    <PackageReference Include="Avalonia.Desktop" Version="11.3.12" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="{runtimeProject}" />
    <ProjectReference Include="{coreProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{avaloniaProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{generatorProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>

  <Import Project="{targetsPath}" />
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
