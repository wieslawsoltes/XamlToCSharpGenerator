using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class BuildTestArtifactCache
{
    private static readonly string[] RoslynTransientFailureMarkers =
    {
        "BoundStepThroughSequencePoint.<Span>k__BackingField",
        "ILOpCodeExtensions.StackPushCount",
        "SignatureData.ReturnParam"
    };

    private static readonly Lazy<BuildTestSourceGenArtifacts> SourceGenArtifacts =
        new(CreateSourceGenArtifacts, LazyThreadSafetyMode.ExecutionAndPublication);

    public static BuildTestSourceGenArtifacts GetSourceGenArtifacts()
    {
        return SourceGenArtifacts.Value;
    }

    private static BuildTestSourceGenArtifacts CreateSourceGenArtifacts()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        BuildProject(repositoryRoot, Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator", "XamlToCSharpGenerator.Generator.csproj"));
        BuildProject(repositoryRoot, Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime", "XamlToCSharpGenerator.Runtime.csproj"));

        var runtimeReferences = new[]
        {
            CreateAssemblyReference(repositoryRoot, "XamlToCSharpGenerator.Runtime", "net10.0"),
            CreateAssemblyReference(repositoryRoot, "XamlToCSharpGenerator.Runtime.Avalonia", "net10.0"),
            CreateAssemblyReference(repositoryRoot, "XamlToCSharpGenerator.Runtime.Core", "net10.0")
        };

        var analyzerPaths = new[]
        {
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.Core", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.Compiler", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.Framework.Abstractions", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.ExpressionSemantics", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.Avalonia", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.MiniLanguageParsing", "netstandard2.0"),
            GetAssemblyPath(repositoryRoot, "XamlToCSharpGenerator.Generator", "netstandard2.0")
        };

        return new BuildTestSourceGenArtifacts(
            NormalizeForMsBuild(Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.props")),
            NormalizeForMsBuild(Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Build", "buildTransitive", "XamlToCSharpGenerator.Build.targets")),
            runtimeReferences,
            analyzerPaths);
    }

    private static BuildTestAssemblyReference CreateAssemblyReference(string repositoryRoot, string projectName, string targetFramework)
    {
        var assemblyPath = GetAssemblyPath(repositoryRoot, projectName, targetFramework);
        return new BuildTestAssemblyReference(projectName, assemblyPath);
    }

    private static string GetAssemblyPath(string repositoryRoot, string projectName, string targetFramework)
    {
        var projectDirectory = Path.Combine(repositoryRoot, "src", projectName);
        var assemblyPath = Path.Combine(projectDirectory, "bin", "Debug", targetFramework, projectName + ".dll");
        Assert.True(File.Exists(assemblyPath), $"Expected build artifact '{assemblyPath}' to exist.");
        return assemblyPath;
    }

    private static void BuildProject(string repositoryRoot, string projectPath)
    {
        var result = RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" -c Debug --nologo -m:1 /nodeReuse:false --disable-build-servers");
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
    {
        return RunProcess(workingDirectory, fileName, arguments, allowRetry: true);
    }

    private static (int ExitCode, string Output) RunProcess(
        string workingDirectory,
        string fileName,
        string arguments,
        bool allowRetry)
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

        var outputBuilder = new StringBuilder(stdoutTask.Result.Length + stderrTask.Result.Length);
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        var output = outputBuilder.ToString();

        if (allowRetry &&
            ShouldRetryAfterTransientRoslynFailure(fileName, arguments, process.ExitCode, output))
        {
            var retry = RunProcess(workingDirectory, fileName, arguments, allowRetry: false);
            var retryOutput = new StringBuilder(output.Length + retry.Output.Length + 128);
            retryOutput.AppendLine("[Transient Roslyn compiler failure detected; retrying once.]");
            retryOutput.AppendLine(output);
            retryOutput.AppendLine("[Retry result follows:]");
            retryOutput.Append(retry.Output);
            return (retry.ExitCode, retryOutput.ToString());
        }

        return (process.ExitCode, output);
    }

    private static bool ShouldRetryAfterTransientRoslynFailure(
        string fileName,
        string arguments,
        int exitCode,
        string output)
    {
        var hasRoslynMissingMemberFailure =
            output.Contains("MissingFieldException", StringComparison.Ordinal) ||
            output.Contains("MissingMethodException", StringComparison.Ordinal);
        var hasKnownMarker = RoslynTransientFailureMarkers.Any(marker => output.Contains(marker, StringComparison.Ordinal));

        return exitCode != 0 &&
               string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase) &&
               arguments.Contains("build", StringComparison.OrdinalIgnoreCase) &&
               hasRoslynMissingMemberFailure &&
               hasKnownMarker;
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }
}

internal sealed class BuildTestSourceGenArtifacts
{
    public BuildTestSourceGenArtifacts(
        string propsPath,
        string targetsPath,
        IReadOnlyList<BuildTestAssemblyReference> runtimeReferences,
        IReadOnlyList<string> analyzerPaths)
    {
        PropsPath = propsPath;
        TargetsPath = targetsPath;
        RuntimeReferences = runtimeReferences;
        AnalyzerPaths = analyzerPaths;
    }

    public string PropsPath { get; }

    public string TargetsPath { get; }

    public IReadOnlyList<BuildTestAssemblyReference> RuntimeReferences { get; }

    public IReadOnlyList<string> AnalyzerPaths { get; }

    public string CreateConditionalSourceGenItemGroup()
    {
        var builder = new StringBuilder();
        builder.AppendLine("  <ItemGroup Condition=\"'$(AvaloniaXamlCompilerBackend)' == 'SourceGen'\">");
        foreach (var runtimeReference in RuntimeReferences)
        {
            builder.Append("    <Reference Include=\"")
                .Append(runtimeReference.Include)
                .AppendLine("\">");
            builder.Append("      <HintPath>")
                .Append(NormalizeForMsBuild(runtimeReference.HintPath))
                .AppendLine("</HintPath>");
            builder.AppendLine("      <Private>true</Private>");
            builder.AppendLine("    </Reference>");
        }

        foreach (var analyzerPath in AnalyzerPaths)
        {
            builder.Append("    <Analyzer Include=\"")
                .Append(NormalizeForMsBuild(analyzerPath))
                .AppendLine("\" />");
        }

        builder.AppendLine("  </ItemGroup>");
        return builder.ToString();
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }
}

internal sealed record BuildTestAssemblyReference(string Include, string HintPath);
