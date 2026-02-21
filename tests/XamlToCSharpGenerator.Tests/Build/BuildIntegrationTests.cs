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
        var afMatches = CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml");
        var watchMatches = CountMatches(output, "WATCH|");
        var caciMatches = CountMatches(output, "CACI|");
        var utdiMatches = CountMatches(output, "UTDI|");
        Assert.True(afMatches == 1, output);
        Assert.True(watchMatches == 1, output);
        Assert.True(caciMatches == 1, output);
        Assert.True(utdiMatches == 1, output);
    }

    [Fact]
    public void SourceGen_Backend_Rewrites_AvaloniaXaml_AdditionalFiles_Without_Duplicates()
    {
        var output = RunEvaluation(sourceGenBackend: true, seedAdditionalFile: true);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
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

        Assert.Contains("STATE|true|false|false|1|1", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void SourceGen_Backend_Deduplicates_Duplicate_AvaloniaXaml_Items()
    {
        var output = RunEvaluation(sourceGenBackend: true, duplicateAvaloniaXaml: true);

        Assert.Contains("STATE|true|false|false|1|2", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void SourceGen_Backend_Watch_Uses_Project_Rooted_Path_When_Working_Directory_Differs()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            watchMode: true,
            runFromRepositoryRoot: true);

        var projectDirectory = GetSinglePrefixedValue(output, "PROJDIR|");
        var watchPath = GetSinglePrefixedValue(output, "WATCH|");

        var normalizedProjectDirectory = NormalizePathForAssert(projectDirectory).TrimEnd('/');
        var normalizedWatchPath = NormalizePathForAssert(watchPath);
        Assert.StartsWith(
            normalizedProjectDirectory + "/",
            normalizedWatchPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceGen_Backend_Honors_CompiledBindings_And_SourceInfo_Knobs()
    {
        var output = RunEvaluation(sourceGenBackend: true, setSourceGenKnobs: true);

        Assert.Contains("CFG|true|true|true|false", output, StringComparison.Ordinal);
    }

    private static string RunEvaluation(
        bool sourceGenBackend,
        bool seedAdditionalFile = false,
        bool watchMode = false,
        bool duplicateAvaloniaXaml = false,
        bool runFromRepositoryRoot = false,
        bool setSourceGenKnobs = false)
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");

        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-integration");

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
                setSourceGenKnobs,
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
                WorkingDirectory = runFromRepositoryRoot ? repositoryRoot : tempDir
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
                BuildTestWorkspacePaths.TryDeleteDirectory(tempDir);
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
        bool setSourceGenKnobs,
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
        var sourceGenKnobs = setSourceGenKnobs
            ? "\n    <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>\n    <AvaloniaSourceGenCreateSourceInfo>true</AvaloniaSourceGenCreateSourceInfo>\n    <AvaloniaSourceGenStrictMode>true</AvaloniaSourceGenStrictMode>"
            : string.Empty;

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>{backendProperty}{watchProperty}{sourceGenKnobs}
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaXaml Include="MainView.axaml" Link="Views/MainView.axaml" />
{duplicateAvaloniaXamlItem}
{seededAdditionalFiles}
  </ItemGroup>

  <Import Project="{propsPath}" />
  <Import Project="{targetsPath}" />

  <Target Name="PrintState" DependsOnTargets="XamlToCSharpGenerator_InjectAdditionalFiles;XamlToCSharpGenerator_PrepareCoreCompileInputs;XamlToCSharpGenerator_CollectUpToDateCheckInputDesignTime">
    <Message Importance="high" Text="PROJDIR|$(MSBuildProjectDirectory)" />
    <Message Importance="high" Text="STATE|$(AvaloniaSourceGenCompilerEnabled)|$(EnableAvaloniaXamlCompilation)|$(AvaloniaNameGeneratorIsEnabled)|@(AdditionalFiles->Count())|@(AvaloniaXaml->Count())" />
    <Message Importance="high" Text="CFG|$(AvaloniaSourceGenUseCompiledBindingsByDefault)|$(AvaloniaSourceGenCreateSourceInfo)|$(AvaloniaSourceGenStrictMode)|$(EnableAvaloniaXamlCompilation)" />
    <Message Importance="high" Condition="'@(AdditionalFiles)' != ''" Text="AF|%(AdditionalFiles.SourceItemGroup)|%(AdditionalFiles.TargetPath)" />
    <Message Importance="high" Condition="'@(Watch)' != '' and '%(Watch.XamlToCSharpGenerator)' == 'true'" Text="WATCH|%(Watch.Identity)" />
    <Message Importance="high" Condition="'@(CustomAdditionalCompileInputs)' != '' and '%(CustomAdditionalCompileInputs.XamlToCSharpGenerator)' == 'true'" Text="CACI|%(CustomAdditionalCompileInputs.Identity)" />
    <Message Importance="high" Condition="'@(UpToDateCheckInput)' != '' and '%(UpToDateCheckInput.XamlToCSharpGenerator)' == 'true'" Text="UTDI|%(UpToDateCheckInput.Identity)" />
  </Target>
</Project>
""";
    }

    private static int CountMatches(string output, string value)
    {
        return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.Contains(value, StringComparison.Ordinal));
    }

    private static string GetSinglePrefixedValue(string output, string prefix)
    {
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var matches = lines
            .Select(static line => line.TrimStart())
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        var match = Assert.Single(matches);
        return match.Substring(prefix.Length);
    }

    private static string NormalizePathForAssert(string value)
    {
        return value.Replace('\\', '/');
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
