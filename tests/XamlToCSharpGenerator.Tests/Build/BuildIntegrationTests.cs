using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
        Assert.True(CountMatches(output, "WATCH|") == 0, output);
        Assert.True(CountMatches(output, "CACI|") == 0, output);
        Assert.True(CountMatches(output, "UTDI|") == 0, output);
    }

    [Fact]
    public void SourceGen_Backend_WatchMode_Can_Opt_Back_Into_DotNetWatch_Xaml_Build_Triggers()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            seedAdditionalFile: true,
            watchMode: true,
            enableDotNetWatchXamlBuildTriggers: true);

        Assert.Contains("STATE|SourceGen|true|true|false|false|1|1", output, StringComparison.Ordinal);
        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.True(CountMatches(output, "WATCH|") == 1, output);
        Assert.True(CountMatches(output, "CACI|") == 1, output);
        Assert.True(CountMatches(output, "UTDI|") == 1, output);
    }

    [Fact]
    public void SourceGen_Backend_DotNetWatchSdk_Target_Removes_Xaml_From_Final_Watch_List()
    {
        var output = RunDotNetWatchEvaluation(sourceGenBackend: true);

        Assert.DoesNotContain("WATCHXAML|", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CACIXAML|", output, StringComparison.Ordinal);
        Assert.DoesNotContain("UTDIXAML|", output, StringComparison.Ordinal);
        Assert.Contains("WATCHPROJ|", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceGen_Backend_DotNetWatchSdk_Target_Can_Opt_Back_Into_Xaml_Watch_List()
    {
        var output = RunDotNetWatchEvaluation(
            sourceGenBackend: true,
            enableDotNetWatchXamlBuildTriggers: true);

        Assert.Contains("WATCHXAML|", output, StringComparison.Ordinal);
        Assert.Contains("CACIXAML|", output, StringComparison.Ordinal);
        Assert.Contains("UTDIXAML|", output, StringComparison.Ordinal);
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
            enableDotNetWatchXamlBuildTriggers: true,
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
        Assert.True(CountMatches(output, "WATCH|") == 0, output);
        Assert.True(CountMatches(output, "CACI|") == 0, output);
        Assert.True(CountMatches(output, "UTDI|") == 0, output);
    }

    [Fact]
    public void Neutral_AdditionalFiles_SourceItemGroup_Property_Is_Coerced_Back_To_AvaloniaXaml_With_Warning()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            useNeutralBackendProperty: true,
            customAdditionalFilesSourceItemGroup: "CustomFrameworkXaml");

        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.DoesNotContain("AF|CustomFrameworkXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
        Assert.Contains("[AXSG.Build] XamlSourceGenAdditionalFilesSourceItemGroup='CustomFrameworkXaml' is not supported for Avalonia consumers.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Neutral_AdditionalFiles_SourceItemGroup_Guard_Removes_Stale_Custom_Projection()
    {
        var output = RunEvaluation(
            sourceGenBackend: true,
            useNeutralBackendProperty: true,
            seedAdditionalFile: true,
            customAdditionalFilesSourceItemGroup: "CustomFrameworkXaml");

        Assert.True(CountMatches(output, "AF|AvaloniaXaml|Views/MainView.axaml") == 1, output);
        Assert.DoesNotContain("AF|CustomFrameworkXaml|Views/MainView.axaml", output, StringComparison.Ordinal);
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

    [Fact]
    public async System.Threading.Tasks.Task Local_Analyzer_Target_Builds_Analyzer_Project_Using_Its_Resolved_TargetFramework()
    {
        var repositoryRoot = GetRepositoryRoot();
        var repoTargetsPath = Path.Combine(repositoryRoot, "Directory.Build.targets");
        var analyzerProjectPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj");
        var analyzerAssemblyPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "bin", "Debug", "netstandard2.0", "XamlToCSharpGenerator.Generator.dll");
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-local-analyzer-targetframework");

        byte[]? originalAssembly = null;

        try
        {
            if (File.Exists(analyzerAssemblyPath))
            {
                originalAssembly = File.ReadAllBytes(analyzerAssemblyPath);
                File.Delete(analyzerAssemblyPath);
            }

            var projectFile = Path.Combine(tempDir, "AnalyzerBuildProbe.csproj");
            File.WriteAllText(projectFile, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <XamlSourceGenLocalAnalyzerProject Include="{{NormalizeForMsBuild(analyzerProjectPath)}}" />
  </ItemGroup>
  <Import Project="{{NormalizeForMsBuild(repoTargetsPath)}}" />
  <Target Name="VerifyLocalAnalyzerBuild" DependsOnTargets="XamlToCSharpGenerator_BuildLocalAnalyzers">
    <Error Condition="!Exists('{{NormalizeForMsBuild(analyzerAssemblyPath)}}')" Text="Expected analyzer assembly was not built for its resolved target framework." />
  </Target>
</Project>
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:VerifyLocalAnalyzerBuild -m:1 /nodeReuse:false",
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
            Assert.True(File.Exists(analyzerAssemblyPath), output);
        }
        finally
        {
            try
            {
                if (originalAssembly is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(analyzerAssemblyPath)!);
                    File.WriteAllBytes(analyzerAssemblyPath, originalAssembly);
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

    [Fact]
    public async System.Threading.Tasks.Task Local_IlWeaver_Target_Builds_Task_Project_Using_Its_Own_Restore_And_Output_Paths()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var ilWeaverAssetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build.Tasks", "obj", "project.assets.json");
        var ilWeaverAssemblyPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build.Tasks", "bin", "Debug", "netstandard2.0", "XamlToCSharpGenerator.Build.Tasks.dll");
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-local-ilweaver-targetframework");

        byte[]? originalAssets = null;
        byte[]? originalAssembly = null;

        try
        {
            if (File.Exists(ilWeaverAssetsPath))
            {
                originalAssets = File.ReadAllBytes(ilWeaverAssetsPath);
                File.Delete(ilWeaverAssetsPath);
            }

            if (File.Exists(ilWeaverAssemblyPath))
            {
                originalAssembly = File.ReadAllBytes(ilWeaverAssemblyPath);
                File.Delete(ilWeaverAssemblyPath);
            }

            var projectFile = Path.Combine(tempDir, "IlWeaverBuildProbe.csproj");
            File.WriteAllText(projectFile, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="{{NormalizeForMsBuild(propsPath)}}" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
    <XamlSourceGenIlWeavingEnabled>true</XamlSourceGenIlWeavingEnabled>
  </PropertyGroup>

  <Import Project="{{NormalizeForMsBuild(targetsPath)}}" />

  <Target Name="VerifyLocalIlWeaverBuild" DependsOnTargets="XamlToCSharpGenerator_BuildLocalIlWeaverTask">
    <Error Condition="!Exists('{{NormalizeForMsBuild(ilWeaverAssetsPath)}}')" Text="Expected IL weaver assets file was restored using the child project path." />
    <Error Condition="!Exists('{{NormalizeForMsBuild(ilWeaverAssemblyPath)}}')" Text="Expected IL weaver assembly was built using the child project path." />
  </Target>
</Project>
""");

            var restorePath = EnsureTrailingSeparator(Path.Combine(tempDir, "obj", "restore"));
            var intermediatePath = EnsureTrailingSeparator(Path.Combine(tempDir, "obj", "sourcegen"));
            var outputPath = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "sourcegen"));
            var projectOutputPath = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "project-output"));
            var projectOutDir = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "project-outdir"));
            var runtimeIdentifier = GetCurrentTestRuntimeIdentifier();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:VerifyLocalIlWeaverBuild -m:1 /nodeReuse:false -p:MSBuildProjectExtensionsPath=\"{restorePath}\" -p:BaseIntermediateOutputPath=\"{intermediatePath}\" -p:BaseOutputPath=\"{outputPath}\" -p:OutputPath=\"{projectOutputPath}\" -p:OutDir=\"{projectOutDir}\" -p:RuntimeIdentifier={runtimeIdentifier}",
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
            Assert.True(File.Exists(ilWeaverAssetsPath), output);
            Assert.True(File.Exists(ilWeaverAssemblyPath), output);
        }
        finally
        {
            try
            {
                if (originalAssets is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ilWeaverAssetsPath)!);
                    File.WriteAllBytes(ilWeaverAssetsPath, originalAssets);
                }
            }
            catch
            {
                // Best effort restore of test fixture state.
            }

            try
            {
                if (originalAssembly is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ilWeaverAssemblyPath)!);
                    File.WriteAllBytes(ilWeaverAssemblyPath, originalAssembly);
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

    [Fact]
    public async System.Threading.Tasks.Task Explicit_IlWeaver_Task_Path_Skips_Local_IlWeaver_Project_Build()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var artifacts = BuildTestArtifactCache.GetSourceGenArtifacts();
        var explicitIlWeaverPath = artifacts.IlWeaverTaskAssembly.HintPath;
        var ilWeaverAssetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build.Tasks", "obj", "project.assets.json");
        var ilWeaverAssemblyPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build.Tasks", "bin", "Debug", "netstandard2.0", "XamlToCSharpGenerator.Build.Tasks.dll");
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-local-ilweaver-explicit-path");

        byte[]? originalAssets = null;
        byte[]? originalAssembly = null;

        try
        {
            if (File.Exists(ilWeaverAssetsPath))
            {
                originalAssets = File.ReadAllBytes(ilWeaverAssetsPath);
                File.Delete(ilWeaverAssetsPath);
            }

            if (File.Exists(ilWeaverAssemblyPath))
            {
                originalAssembly = File.ReadAllBytes(ilWeaverAssemblyPath);
                File.Delete(ilWeaverAssemblyPath);
            }

            var projectFile = Path.Combine(tempDir, "IlWeaverExplicitPathProbe.csproj");
            File.WriteAllText(projectFile, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="{{NormalizeForMsBuild(propsPath)}}" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>
    <XamlSourceGenIlWeavingEnabled>true</XamlSourceGenIlWeavingEnabled>
    <XamlSourceGenIlWeaverTaskAssemblyPath>{{NormalizeForMsBuild(explicitIlWeaverPath)}}</XamlSourceGenIlWeaverTaskAssemblyPath>
  </PropertyGroup>

  <Import Project="{{NormalizeForMsBuild(targetsPath)}}" />

  <Target Name="VerifyExplicitIlWeaverPathSkipsLocalBuild" DependsOnTargets="XamlToCSharpGenerator_BuildLocalIlWeaverTask">
    <Error Condition="Exists('{{NormalizeForMsBuild(ilWeaverAssetsPath)}}')" Text="Local IL weaver restore should be skipped when an explicit task assembly path exists." />
    <Error Condition="Exists('{{NormalizeForMsBuild(ilWeaverAssemblyPath)}}')" Text="Local IL weaver build should be skipped when an explicit task assembly path exists." />
  </Target>
</Project>
""");

            var restorePath = EnsureTrailingSeparator(Path.Combine(tempDir, "obj", "restore"));
            var intermediatePath = EnsureTrailingSeparator(Path.Combine(tempDir, "obj", "sourcegen"));
            var outputPath = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "sourcegen"));
            var projectOutputPath = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "project-output"));
            var projectOutDir = EnsureTrailingSeparator(Path.Combine(tempDir, "bin", "project-outdir"));
            var runtimeIdentifier = GetCurrentTestRuntimeIdentifier();
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:VerifyExplicitIlWeaverPathSkipsLocalBuild -m:1 /nodeReuse:false -p:MSBuildProjectExtensionsPath=\"{restorePath}\" -p:BaseIntermediateOutputPath=\"{intermediatePath}\" -p:BaseOutputPath=\"{outputPath}\" -p:OutputPath=\"{projectOutputPath}\" -p:OutDir=\"{projectOutDir}\" -p:RuntimeIdentifier={runtimeIdentifier}",
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
            Assert.False(File.Exists(ilWeaverAssetsPath), output);
            Assert.False(File.Exists(ilWeaverAssemblyPath), output);
        }
        finally
        {
            try
            {
                if (originalAssets is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ilWeaverAssetsPath)!);
                    File.WriteAllBytes(ilWeaverAssetsPath, originalAssets);
                }
            }
            catch
            {
                // Best effort restore of test fixture state.
            }

            try
            {
                if (originalAssembly is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ilWeaverAssemblyPath)!);
                    File.WriteAllBytes(ilWeaverAssemblyPath, originalAssembly);
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

    [Fact]
    public async System.Threading.Tasks.Task Project_File_Avalonia_IlWeaving_Aliases_Override_Canonical_Defaults()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-ilweaving-alias-evaluation");

        try
        {
            var projectFile = Path.Combine(tempDir, "IlWeavingAliasEvaluation.csproj");
            File.WriteAllText(projectFile, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="{{NormalizeForMsBuild(propsPath)}}" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AvaloniaSourceGenIlWeavingEnabled>false</AvaloniaSourceGenIlWeavingEnabled>
    <AvaloniaSourceGenIlWeavingStrict>false</AvaloniaSourceGenIlWeavingStrict>
    <AvaloniaSourceGenIlWeavingVerbose>true</AvaloniaSourceGenIlWeavingVerbose>
    <AvaloniaSourceGenIlWeavingBackend>Cecil</AvaloniaSourceGenIlWeavingBackend>
  </PropertyGroup>

  <Import Project="{{NormalizeForMsBuild(targetsPath)}}" />

  <Target Name="PrintWeavingState">
    <Message Importance="high"
             Text="WEAVE|$(XamlSourceGenIlWeavingEnabled)|$(XamlSourceGenIlWeavingStrict)|$(XamlSourceGenIlWeavingVerbose)|$(XamlSourceGenIlWeavingBackend)|$(AvaloniaSourceGenIlWeavingEnabled)|$(AvaloniaSourceGenIlWeavingStrict)|$(AvaloniaSourceGenIlWeavingVerbose)|$(AvaloniaSourceGenIlWeavingBackend)" />
  </Target>
</Project>
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:PrintWeavingState -m:1 /nodeReuse:false",
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
            Assert.Contains("WEAVE|false|false|true|Cecil|false|false|true|Cecil", output, StringComparison.Ordinal);
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

    private static string RunEvaluation(
        bool sourceGenBackend,
        bool seedAdditionalFile = false,
        bool watchMode = false,
        bool duplicateAvaloniaXaml = false,
        bool runFromRepositoryRoot = false,
        bool setSourceGenKnobs = false,
        bool useNeutralBackendProperty = false,
        bool useNeutralEnableFlag = false,
        bool enableDotNetWatchXamlBuildTriggers = false,
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
                enableDotNetWatchXamlBuildTriggers,
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

    private static string RunDotNetWatchEvaluation(
        bool sourceGenBackend,
        bool enableDotNetWatchXamlBuildTriggers = false)
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props");
        var targetsPath = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets");
        var dotNetWatchTargetsPath = FindDotNetWatchTargetsPath();
        var tempDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "build-dotnet-watch");

        try
        {
            var projectFile = Path.Combine(tempDir, "BuildIntegration.csproj");
            var xamlFile = Path.Combine(tempDir, "MainView.axaml");
            File.WriteAllText(xamlFile, "<UserControl xmlns=\"https://github.com/avaloniaui\" />");

            var backendProperty = sourceGenBackend
                ? "\n    <AvaloniaXamlCompilerBackend>SourceGen</AvaloniaXamlCompilerBackend>"
                : string.Empty;
            var dotNetWatchBuildTriggersProperty = enableDotNetWatchXamlBuildTriggers
                ? "\n    <AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled>true</AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled>"
                : string.Empty;

            File.WriteAllText(projectFile, $"""
<Project Sdk="Microsoft.NET.Sdk">
  <Import Project="{NormalizeForMsBuild(propsPath)}" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>{backendProperty}
    <DotNetWatchBuild>true</DotNetWatchBuild>{dotNetWatchBuildTriggersProperty}
  </PropertyGroup>

  <ItemGroup>
    <AvaloniaXaml Include="MainView.axaml" Link="Views/MainView.axaml" />
  </ItemGroup>

  <Import Project="{NormalizeForMsBuild(targetsPath)}" />
  <Import Project="{NormalizeForMsBuild(dotNetWatchTargetsPath)}" />

  <Target Name="PrintDotNetWatchState" DependsOnTargets="XamlToCSharpGenerator_PrepareCoreCompileInputs;XamlToCSharpGenerator_CollectUpToDateCheckInputDesignTime;_CollectWatchItems">
    <Message Importance="high" Condition="'@(Watch)' != '' and $([System.String]::Copy('%(Watch.Identity)').EndsWith('.axaml'))" Text="WATCHXAML|%(Watch.Identity)" />
    <Message Importance="high" Condition="'@(Watch)' != '' and $([System.String]::Copy('%(Watch.Identity)').EndsWith('.csproj'))" Text="WATCHPROJ|%(Watch.Identity)" />
    <Message Importance="high" Condition="'@(CustomAdditionalCompileInputs)' != '' and $([System.String]::Copy('%(CustomAdditionalCompileInputs.Identity)').EndsWith('.axaml'))" Text="CACIXAML|%(CustomAdditionalCompileInputs.Identity)" />
    <Message Importance="high" Condition="'@(UpToDateCheckInput)' != '' and $([System.String]::Copy('%(UpToDateCheckInput.Identity)').EndsWith('.axaml'))" Text="UTDIXAML|%(UpToDateCheckInput.Identity)" />
  </Target>
</Project>
""");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"msbuild \"{projectFile}\" -nologo -v:minimal -t:PrintDotNetWatchState",
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
        bool enableDotNetWatchXamlBuildTriggers,
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
        var dotNetWatchBuildTriggersProperty = enableDotNetWatchXamlBuildTriggers
            ? "\n    <AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled>true</AvaloniaSourceGenDotNetWatchXamlBuildTriggersEnabled>"
            : string.Empty;
        var seededAdditionalFilesSourceItemGroup = string.IsNullOrWhiteSpace(customAdditionalFilesSourceItemGroup)
            ? "AvaloniaXaml"
            : customAdditionalFilesSourceItemGroup;
        var seededAdditionalFiles = seedAdditionalFile
            ? $"\n    <AdditionalFiles Include=\"MainView.axaml\" SourceItemGroup=\"{seededAdditionalFilesSourceItemGroup}\" TargetPath=\"Views/MainView.axaml\" />"
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
    <TargetFramework>net10.0</TargetFramework>{backendProperty}{enableProperty}{inputItemGroupProperty}{additionalFilesSourceItemGroupProperty}{transformRulesProperty}{transformRuleItemGroupProperty}{watchProperty}{dotNetWatchBuildTriggersProperty}{sourceGenKnobs}
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

    private static string EnsureTrailingSeparator(string path)
    {
        var normalized = NormalizeForMsBuild(path);
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }

    private static string FindDotNetWatchTargetsPath()
    {
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(hostPath))
            {
                dotnetRoot = Path.GetDirectoryName(hostPath);
            }
        }

        if (string.IsNullOrWhiteSpace(dotnetRoot))
        {
            var fallbackRoots = OperatingSystem.IsWindows()
                ? new[] { @"C:\Program Files\dotnet", @"C:\Program Files (x86)\dotnet" }
                : new[] { "/usr/local/share/dotnet", "/usr/share/dotnet" };
            dotnetRoot = fallbackRoots.FirstOrDefault(Directory.Exists);
        }

        Assert.False(string.IsNullOrWhiteSpace(dotnetRoot), "Unable to locate DOTNET_ROOT for dotnet-watch target discovery.");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var version = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && !string.IsNullOrWhiteSpace(version), "Unable to resolve active dotnet SDK version for dotnet-watch target discovery.");

        var watchRoot = Path.Combine(dotnetRoot!, "sdk", version, "DotnetTools", "dotnet-watch");
        Assert.True(Directory.Exists(watchRoot), $"Unable to locate dotnet-watch tool directory '{watchRoot}'.");

        var targetFiles = Directory.GetFiles(watchRoot, "DotNetWatch.targets", SearchOption.AllDirectories);
        return Assert.Single(targetFiles);
    }

    private static string GetCurrentTestRuntimeIdentifier()
    {
        if (OperatingSystem.IsWindows())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }

        if (OperatingSystem.IsMacOS())
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }

        return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
    }
}
