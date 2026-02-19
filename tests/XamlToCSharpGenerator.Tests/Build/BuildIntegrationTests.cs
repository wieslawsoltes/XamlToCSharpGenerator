using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class BuildIntegrationTests
{
    [Fact]
    public void SourceGen_Backend_Disables_Avalonia_Xaml_Compilation_And_Injects_AdditionalFiles()
    {
        var output = RunEvaluation(sourceGenBackend: true);

        Assert.Contains("STATE|true|false|false|", output, StringComparison.Ordinal);
        Assert.Contains("AF|AvaloniaXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGen_Backend_Rewrites_AvaloniaXaml_AdditionalFiles_Without_Duplicates()
    {
        var output = RunEvaluation(sourceGenBackend: true, seedAdditionalFile: true);
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = lines.Count(line =>
            line.Contains("AF|AvaloniaXaml|Views/MainView.axaml", StringComparison.Ordinal));

        Assert.Equal(1, matches);
    }

    [Fact]
    public void Default_Backend_Does_Not_Inject_SourceGen_AdditionalFiles()
    {
        var output = RunEvaluation(sourceGenBackend: false);

        Assert.Contains("STATE|false|", output, StringComparison.Ordinal);
        Assert.DoesNotContain("AF|AvaloniaXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGen_Backend_WatchMode_Keeps_Single_AvaloniaXaml_And_Leaves_Deduplicated_AdditionalFiles()
    {
        var output = RunEvaluation(sourceGenBackend: true, seedAdditionalFile: true, watchMode: true);

        Assert.Contains("STATE|true|false|false|1|1|1", output, StringComparison.Ordinal);
        Assert.Contains("AF|AvaloniaXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGen_Backend_Deduplicates_Duplicate_AvaloniaXaml_Items()
    {
        var output = RunEvaluation(sourceGenBackend: true, duplicateAvaloniaXaml: true);
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = lines.Count(line =>
            line.Contains("AF|AvaloniaXaml|Views/MainView.axaml", StringComparison.Ordinal));

        Assert.Contains("STATE|true|false|false|1|1|1", output, StringComparison.Ordinal);
        Assert.Equal(1, matches);
    }

    private static string RunEvaluation(
        bool sourceGenBackend,
        bool seedAdditionalFile = false,
        bool watchMode = false,
        bool duplicateAvaloniaXaml = false)
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");

        var tempDir = Path.Combine(Path.GetTempPath(), "XamlToCSharpGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var projectFile = Path.Combine(tempDir, "BuildIntegration.csproj");
            var xamlFile = Path.Combine(tempDir, "MainView.axaml");
            File.WriteAllText(xamlFile, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            File.WriteAllText(projectFile, BuildProjectText(
                sourceGenBackend,
                seedAdditionalFile,
                watchMode,
                duplicateAvaloniaXaml,
                NormalizeForMsBuild(propsPath),
                NormalizeForMsBuild(targetsPath)));

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:PrintState",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempDir
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

            var output = outputBuilder.ToString();
            Assert.True(process.ExitCode == 0, output);
            return output;
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup in tests.
            }
        }
    }

    private static string BuildProjectText(
        bool sourceGenBackend,
        bool seedAdditionalFile,
        bool watchMode,
        bool duplicateAvaloniaXaml,
        string propsPath,
        string targetsPath)
    {
        var backendProperty = sourceGenBackend
            ? "\n    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>"
            : string.Empty;
        var watchProperty = watchMode
            ? "\n    <DotNetWatchBuild>true</DotNetWatchBuild>"
            : string.Empty;
        var seededAdditionalFiles = seedAdditionalFile
            ? "\n    <AdditionalFiles Include=\"MainView.axaml\" SourceItemGroup=\"AvaloniaXaml\" TargetPath=\"Views/MainView.axaml\" />"
            : string.Empty;
        var duplicateAvaloniaXamlItem = duplicateAvaloniaXaml
            ? "\n    <AvaloniaXaml Include=\"MainView.axaml\" Link=\"Views/MainView.axaml\" />"
            : string.Empty;

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>{backendProperty}{watchProperty}
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaXaml Include="MainView.axaml" Link="Views/MainView.axaml" />
{duplicateAvaloniaXamlItem}
{seededAdditionalFiles}
  </ItemGroup>

  <Import Project="{propsPath}" />
  <Import Project="{targetsPath}" />

  <Target Name="PrintState" DependsOnTargets="XamlToCSharpGenerator_InjectAdditionalFiles">
    <Message Importance="high" Text="STATE|$(AvaloniaSourceGenCompilerEnabled)|$(EnableAvaloniaXamlCompilation)|$(AvaloniaNameGeneratorIsEnabled)|@(AdditionalFiles->Count())|@(AvaloniaXaml->Count())|@(Watch->Count())" />
    <Message Importance="high" Condition="'@(AdditionalFiles)' != ''" Text="AF|%(AdditionalFiles.SourceItemGroup)|%(AdditionalFiles.TargetPath)" />
  </Target>
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
