using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text;
using System.Threading.Tasks;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PerformanceHarnessTests
{
    private static readonly int ProcessTimeoutMilliseconds = ReadTimeoutMilliseconds("AXSG_PERF_PROCESS_TIMEOUT_MS", 180_000);
    private static readonly int TotalTimeoutMilliseconds = ReadTimeoutMilliseconds("AXSG_PERF_TOTAL_TIMEOUT_MS", 600_000);
    private static readonly int StreamDrainTimeoutMilliseconds = ReadTimeoutMilliseconds("AXSG_PERF_STREAM_DRAIN_TIMEOUT_MS", 15_000);
    private static readonly int ProcessKillWaitMilliseconds = ReadTimeoutMilliseconds("AXSG_PERF_PROCESS_KILL_WAIT_MS", 5_000);

    [PerfFact]
    public async Task SourceGen_Incremental_Build_Harness_Captures_Full_And_Edit_Rebuild_Timings()
    {
        var totalStopwatch = Stopwatch.StartNew();
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var runtimeProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj");
        var coreProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core", "XamlToCSharpGenerator.Core.csproj");
        var compilerProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Compiler", "XamlToCSharpGenerator.Compiler.csproj");
        var frameworkProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Framework.Abstractions", "XamlToCSharpGenerator.Framework.Abstractions.csproj");
        var expressionSemanticsProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.ExpressionSemantics", "XamlToCSharpGenerator.ExpressionSemantics.csproj");
        var avaloniaProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Avalonia", "XamlToCSharpGenerator.Avalonia.csproj");
        var miniLanguageParsingProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.MiniLanguageParsing", "XamlToCSharpGenerator.MiniLanguageParsing.csproj");
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
                NormalizeForMsBuild(compilerProject),
                NormalizeForMsBuild(frameworkProject),
                NormalizeForMsBuild(expressionSemanticsProject),
                NormalizeForMsBuild(avaloniaProject),
                NormalizeForMsBuild(miniLanguageParsingProject),
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

            var restore = await RunProcessAsync(
                tempDir,
                "dotnet",
                $"restore \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);
            AssertNotTimedOut(totalStopwatch, "restore");

            var clean = await RunProcessAsync(
                tempDir,
                "dotnet",
                $"clean \"{projectPath}\" --nologo -m:1 /nodeReuse:false --disable-build-servers -p:BuildProjectReferences=false");
            Assert.True(clean.ExitCode == 0, clean.Output);
            AssertNotTimedOut(totalStopwatch, "clean");

            var fullBuild = await TimedBuildAsync(projectPath, tempDir);
            Assert.True(fullBuild.Result.ExitCode == 0, fullBuild.Result.Output);
            AssertNotTimedOut(totalStopwatch, "full-build");

            File.AppendAllText(mainXamlPath, Environment.NewLine + "<!-- incremental-edit-main -->");
            var singleFileBuild = await TimedBuildAsync(projectPath, tempDir);
            Assert.True(singleFileBuild.Result.ExitCode == 0, singleFileBuild.Result.Output);
            AssertNotTimedOut(totalStopwatch, "single-edit-build");

            File.AppendAllText(colorsXamlPath, Environment.NewLine + "<!-- incremental-edit-include -->");
            var includeBuild = await TimedBuildAsync(projectPath, tempDir);
            Assert.True(includeBuild.Result.ExitCode == 0, includeBuild.Result.Output);
            AssertNotTimedOut(totalStopwatch, "include-edit-build");

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

            TryWritePerfResult(repositoryRoot, fullBuild.Elapsed, singleFileBuild.Elapsed, includeBuild.Elapsed, totalStopwatch.Elapsed);
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

    private static async Task<(TimeSpan Elapsed, (int ExitCode, string Output) Result)> TimedBuildAsync(string projectPath, string workingDirectory)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await RunProcessAsync(workingDirectory, "dotnet", BuildArguments(projectPath));
        stopwatch.Stop();
        return (stopwatch.Elapsed, result);
    }

    private static void AssertNotTimedOut(Stopwatch totalStopwatch, string stage)
    {
        if (totalStopwatch.Elapsed.TotalMilliseconds > TotalTimeoutMilliseconds)
        {
            throw new TimeoutException(
                $"Performance harness exceeded total timeout after stage '{stage}'. " +
                $"elapsed={totalStopwatch.Elapsed.TotalMilliseconds:F2}ms timeout={TotalTimeoutMilliseconds}ms");
        }
    }

    private static string BuildArguments(string projectPath)
    {
        return
            $"build \"{projectPath}\" --nologo --no-restore -m:1 /nodeReuse:false --disable-build-servers " +
            "-p:AvaloniaXamlCompilerBackend=SourceGen " +
            "-p:UseSharedCompilation=false " +
            "-p:ProduceReferenceAssembly=false";
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

    private static int ReadTimeoutMilliseconds(string environmentVariable, int fallbackValue)
    {
        var raw = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallbackValue;
        }

        if (int.TryParse(raw, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return fallbackValue;
    }

    private static void TryWritePerfResult(
        string repositoryRoot,
        TimeSpan fullBuild,
        TimeSpan singleEditBuild,
        TimeSpan includeEditBuild,
        TimeSpan totalElapsed)
    {
        var configuredPath = Environment.GetEnvironmentVariable("AXSG_PERF_RESULTS_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        var outputPath = configuredPath!;
        if (!Path.IsPathRooted(outputPath))
        {
            outputPath = Path.Combine(repositoryRoot, outputPath);
        }

        var result = new Dictionary<string, object>
        {
            ["fullBuildMs"] = fullBuild.TotalMilliseconds,
            ["singleEditBuildMs"] = singleEditBuild.TotalMilliseconds,
            ["includeEditBuildMs"] = includeEditBuild.TotalMilliseconds,
            ["totalElapsedMs"] = totalElapsed.TotalMilliseconds,
            ["timestampUtc"] = DateTime.UtcNow.ToString("O")
        };

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(string workingDirectory, string fileName, string arguments)
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
        using var timeoutCancellation = new System.Threading.CancellationTokenSource(ProcessTimeoutMilliseconds);
        try
        {
            await process.WaitForExitAsync(timeoutCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup for timed-out process.
            }

            using var killWaitCancellation = new System.Threading.CancellationTokenSource(ProcessKillWaitMilliseconds);
            try
            {
                await process.WaitForExitAsync(killWaitCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Best-effort cleanup for timed-out process kill.
            }

            var drainedOutput = await TryDrainProcessStreamsAsync(stdoutTask, stderrTask);
            return (
                -1,
                $"Timed out after {ProcessTimeoutMilliseconds}ms while running: {fileName} {arguments}" +
                (drainedOutput.StreamDrainTimedOut ? $" (stream drain timed out after {StreamDrainTimeoutMilliseconds}ms)" : string.Empty) +
                (drainedOutput.Length == 0 ? string.Empty : Environment.NewLine + drainedOutput));
        }

        var output = await TryDrainProcessStreamsAsync(stdoutTask, stderrTask);
        if (output.StreamDrainTimedOut)
        {
            return (
                -1,
                $"Timed out after {StreamDrainTimeoutMilliseconds}ms while draining process output: {fileName} {arguments}" +
                (output.Length == 0 ? string.Empty : Environment.NewLine + output));
        }

        return (process.ExitCode, output);
    }

    private static async Task<ProcessStreamDrainResult> TryDrainProcessStreamsAsync(
        System.Threading.Tasks.Task<string> stdoutTask,
        System.Threading.Tasks.Task<string> stderrTask)
    {
        var combinedReadTask = Task.WhenAll(stdoutTask, stderrTask);
        var delayTask = Task.Delay(StreamDrainTimeoutMilliseconds);
        var completed = await Task.WhenAny(combinedReadTask, delayTask);
        var timedOut = completed != combinedReadTask;

        var outputBuilder = new StringBuilder();
        AppendCompletedTaskOutput(outputBuilder, stdoutTask);
        AppendCompletedTaskOutput(outputBuilder, stderrTask);
        return new ProcessStreamDrainResult(outputBuilder.ToString(), timedOut);
    }

    private static void AppendCompletedTaskOutput(
        StringBuilder outputBuilder,
        System.Threading.Tasks.Task<string> task)
    {
        if (task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion &&
            !string.IsNullOrEmpty(task.Result))
        {
            outputBuilder.Append(task.Result);
            return;
        }

        if (task.Status == System.Threading.Tasks.TaskStatus.Faulted &&
            task.Exception is not null)
        {
            outputBuilder.AppendLine(task.Exception.ToString());
        }
    }

    private readonly record struct ProcessStreamDrainResult(string Output, bool StreamDrainTimedOut)
    {
        public int Length => Output.Length;

        public static implicit operator string(ProcessStreamDrainResult value)
        {
            return value.Output;
        }
    }

    private static string BuildProjectText(
        string propsPath,
        string targetsPath,
        string runtimeProject,
        string coreProject,
        string compilerProject,
        string frameworkProject,
        string expressionSemanticsProject,
        string avaloniaProject,
        string miniLanguageParsingProject,
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
    <ProjectReference Include="{compilerProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{frameworkProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{expressionSemanticsProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{avaloniaProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{miniLanguageParsingProject}" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
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
