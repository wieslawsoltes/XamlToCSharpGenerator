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

        Assert.Contains("STATE|SourceGen|true|true|false|false|", output, StringComparison.Ordinal);
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

        Assert.Contains("STATE|XamlIl|false|false|", output, StringComparison.Ordinal);
        Assert.DoesNotContain("AF|AvaloniaXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGen_Backend_WatchMode_Keeps_Single_AvaloniaXaml_And_Leaves_Deduplicated_AdditionalFiles()
    {
        var output = RunEvaluation(sourceGenBackend: true, seedAdditionalFile: true, watchMode: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|1|1", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void SourceGen_Backend_Deduplicates_Duplicate_AvaloniaXaml_Items()
    {
        var output = RunEvaluation(sourceGenBackend: true, duplicateAvaloniaXaml: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|1|2", output, StringComparison.Ordinal);
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

    [Fact]
    public void Neutral_Backend_Property_Disables_Avalonia_Xaml_Compilation_And_Injects_AdditionalFiles()
    {
        var output = RunEvaluation(sourceGenBackend: true, useNeutralBackendProperty: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void Neutral_Enable_Flag_Enables_SourceGen_Without_Backend_Switch()
    {
        var output = RunEvaluation(sourceGenBackend: false, useNeutralEnableFlag: true);

        Assert.Contains("STATE|XamlIl|true|true|false|false|", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void Neutral_Input_Item_Group_Projects_Custom_Xaml_Items_Without_Duplicates()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            useNeutralBackendProperty: true,
            customInputItemGroup: "CustomXaml",
            duplicateAvaloniaXaml: true,
            watchMode: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|1|0", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void Neutral_AdditionalFiles_SourceItemGroup_Property_Overrides_Default_Group()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            useNeutralBackendProperty: true,
            customAdditionalFilesSourceItemGroup: "CustomFrameworkXaml");

        Assert.True(CountMatches(output, "AF|CustomFrameworkXaml|Views/MainView.axaml") == 1, output);
        Assert.DoesNotContain("AF|AvaloniaXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Neutral_TransformRule_Properties_Project_Transform_Files_Into_AdditionalFiles()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            useNeutralBackendProperty: true,
            includeTransformRule: true,
            useNeutralTransformRulesProperty: true,
            customTransformRuleItemGroup: "CustomTransformRule");

        Assert.True(CountMatches(output, "AF|CustomTransformRule|transform-rules.json") == 1, output);
    }

    [Fact]
    public void Legacy_Backend_Property_Works_With_Sdk_Style_Import_Order()
    {
        var output = RunEvaluation(sourceGenBackend: true, simulateSdkImportOrder: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public async System.Threading.Tasks.Task Local_Analyzer_Target_Restores_Analyzer_Project_Before_Build()
    {
        var repositoryRoot = GetRepositoryRoot();
        var repoTargetsPath = Path.Combine(repositoryRoot, "Directory.Build.targets");
        var analyzerProjectPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");
        var analyzerAssetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "obj", "project.assets.json");
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-local-analyzer-restore");

        byte[]? originalAssets = null;

        try
        {
            if (File.Exists(analyzerAssetsPath))
            {
                originalAssets = File.ReadAllBytes(analyzerAssetsPath);
                File.Delete(analyzerAssetsPath);
            }

            var projectFile = Path.Combine(tempDir, "AnalyzerRestoreProbe.csproj");
            File.WriteAllText(projectFile, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <XamlSourceGenLocalAnalyzerProject Include="{{NormalizeForMsBuild(analyzerProjectPath)}}" />
  </ItemGroup>
  <Import Project="{{NormalizeForMsBuild(repoTargetsPath)}}" />
  <Target Name="VerifyLocalAnalyzerRestore" DependsOnTargets="XamlToCSharpGenerator_BuildLocalAnalyzers">
    <Error Condition="!Exists('{{NormalizeForMsBuild(analyzerAssetsPath)}}')" Text="Expected analyzer assets file was not restored." />
  </Target>
</Project>
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:VerifyLocalAnalyzerRestore -m:1 /nodeReuse:false",
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
            await process.WaitForExitAsync();
            var output = await stdoutTask + await stderrTask;
            Assert.True(process.ExitCode == 0, output);
            Assert.True(File.Exists(analyzerAssetsPath), output);
        }
        finally
        {
            try
            {
                if (originalAssets is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(analyzerAssetsPath)!);
                    File.WriteAllBytes(analyzerAssetsPath, originalAssets);
                }
            }
            catch
            {
                // Best effort restore of test fixture state.
            }

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

    private static string RunEvaluation(
        bool sourceGenBackend,
        bool seedAdditionalFile = false,
        bool watchMode = false,
        bool duplicateAvaloniaXaml = false,
        bool runFromRepositoryRoot = false,
        bool setSourceGenKnobs = false,
        bool useNeutralBackendProperty = false,
        bool useNeutralEnableFlag = false,
        string? customInputItemGroup = null,
        string? customAdditionalFilesSourceItemGroup = null,
        bool includeTransformRule = false,
        bool useNeutralTransformRulesProperty = false,
        string? customTransformRuleItemGroup = null,
        bool simulateSdkImportOrder = false)
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
            if (includeTransformRule)
            {
                File.WriteAllText(Path.Combine(tempDir, "transform-rules.json"), "{ }");
            }

            File.WriteAllText(projectFile, BuildProjectText(
                sourceGenBackend,
                seedAdditionalFile,
                watchMode,
                duplicateAvaloniaXaml,
                setSourceGenKnobs,
                useNeutralBackendProperty,
                useNeutralEnableFlag,
                customInputItemGroup,
                customAdditionalFilesSourceItemGroup,
                includeTransformRule,
                useNeutralTransformRulesProperty,
                customTransformRuleItemGroup,
                simulateSdkImportOrder,
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
        bool useNeutralBackendProperty,
        bool useNeutralEnableFlag,
        string? customInputItemGroup,
        string? customAdditionalFilesSourceItemGroup,
        bool includeTransformRule,
        bool useNeutralTransformRulesProperty,
        string? customTransformRuleItemGroup,
        bool simulateSdkImportOrder,
        string propsPath,
        string targetsPath)
    {
        var backendProperty = sourceGenBackend
            ? useNeutralBackendProperty
                ? "\n    <XamlSourceGenBackend>SourceGen</XamlSourceGenBackend>"
                : "\n    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>"
            : string.Empty;
        var enableProperty = useNeutralEnableFlag
            ? "\n    <XamlSourceGenEnabled>true</XamlSourceGenEnabled>"
            : string.Empty;
        var inputItemGroupProperty = string.IsNullOrWhiteSpace(customInputItemGroup)
            ? string.Empty
            : $"\n    <XamlSourceGenInputItemGroup>{customInputItemGroup}</XamlSourceGenInputItemGroup>";
        var additionalFilesSourceItemGroupProperty = string.IsNullOrWhiteSpace(customAdditionalFilesSourceItemGroup)
            ? string.Empty
            : $"\n    <XamlSourceGenAdditionalFilesSourceItemGroup>{customAdditionalFilesSourceItemGroup}</XamlSourceGenAdditionalFilesSourceItemGroup>";
        var transformRulesProperty = includeTransformRule
            ? useNeutralTransformRulesProperty
                ? "\n    <XamlSourceGenTransformRules>transform-rules.json</XamlSourceGenTransformRules>"
                : "\n    <AvaloniaSourceGenTransformRules>transform-rules.json</AvaloniaSourceGenTransformRules>"
            : string.Empty;
        var transformRuleItemGroupProperty = string.IsNullOrWhiteSpace(customTransformRuleItemGroup)
            ? string.Empty
            : $"\n    <XamlSourceGenTransformRuleItemGroup>{customTransformRuleItemGroup}</XamlSourceGenTransformRuleItemGroup>";
        var watchProperty = watchMode
            ? "\n    <DotNetWatchBuild>true</DotNetWatchBuild>"
            : string.Empty;
        var seededAdditionalFiles = seedAdditionalFile
            ? "\n    <AdditionalFiles Include=\"MainView.axaml\" SourceItemGroup=\"AvaloniaXaml\" TargetPath=\"Views/MainView.axaml\" />"
            : string.Empty;
        var sourceItemGroupName = string.IsNullOrWhiteSpace(customInputItemGroup) ? "AvaloniaXaml" : customInputItemGroup;
        var sourceXamlItem = $"    <{sourceItemGroupName} Include=\"MainView.axaml\" Link=\"Views/MainView.axaml\" />";
        var duplicateAvaloniaXamlItem = duplicateAvaloniaXaml
            ? $"\n    <{sourceItemGroupName} Include=\"MainView.axaml\" Link=\"Views/MainView.axaml\" />"
            : string.Empty;
        var sourceGenKnobs = setSourceGenKnobs
            ? "\n    <AvaloniaSourceGenUseCompiledBindingsByDefault>true</AvaloniaSourceGenUseCompiledBindingsByDefault>\n    <AvaloniaSourceGenCreateSourceInfo>true</AvaloniaSourceGenCreateSourceInfo>\n    <AvaloniaSourceGenStrictMode>true</AvaloniaSourceGenStrictMode>"
            : string.Empty;
        var propertyGroup = $"""
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>{backendProperty}{enableProperty}{inputItemGroupProperty}{additionalFilesSourceItemGroupProperty}{transformRulesProperty}{transformRuleItemGroupProperty}{watchProperty}{sourceGenKnobs}
  </PropertyGroup>
""";
        var xamlItemGroup = $"""
  <ItemGroup>
{sourceXamlItem}
{duplicateAvaloniaXamlItem}
{seededAdditionalFiles}
  </ItemGroup>
""";
        var importsBeforeProjectContent = simulateSdkImportOrder
            ? $"""
  <Import Project="{propsPath}" />
"""
            : string.Empty;
        var importsAfterProjectContent = simulateSdkImportOrder
            ? $"""
  <Import Project="{targetsPath}" />
"""
            : $"""
  <Import Project="{propsPath}" />
  <Import Project="{targetsPath}" />
""";

        return $"""
<Project Sdk="Microsoft.NET.Sdk">
{importsBeforeProjectContent}
{propertyGroup}
{xamlItemGroup}
{importsAfterProjectContent}

  <Target Name="PrintState" DependsOnTargets="XamlToCSharpGenerator_InjectAdditionalFiles;XamlToCSharpGenerator_PrepareCoreCompileInputs;XamlToCSharpGenerator_CollectUpToDateCheckInputDesignTime">
    <Message Importance="high" Text="PROJDIR|$(MSBuildProjectDirectory)" />
    <Message Importance="high" Text="STATE|$(XamlSourceGenBackend)|$(XamlSourceGenEnabled)|$(AvaloniaSourceGenCompilerEnabled)|$(EnableAvaloniaXamlCompilation)|$(AvaloniaNameGeneratorIsEnabled)|@(AdditionalFiles->Count())|@(AvaloniaXaml->Count())" />
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
