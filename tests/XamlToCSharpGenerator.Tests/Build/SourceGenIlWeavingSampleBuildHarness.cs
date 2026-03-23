using System;
using System.IO;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class SourceGenIlWeavingSampleBuildHarness
{
    public static string GetDebugAssemblyPath()
    {
        return BuildDebugAssemblyPath();
    }

    public static SourceGenIlWeavingSampleBuildArtifact BuildWithProperties(
        string scenario,
        params (string Name, string Value)[] properties)
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "SourceGenIlWeavingSample",
            "SourceGenIlWeavingSample.csproj");

        var arguments = new StringBuilder();
        arguments.Append("build \"")
            .Append(projectPath)
            .Append("\" -t:Rebuild -c Debug --nologo -m:1 /nodeReuse:false --disable-build-servers");

        for (var index = 0; index < properties.Length; index++)
        {
            arguments.Append(" -p:")
                .Append(properties[index].Name)
                .Append('=')
                .Append(properties[index].Value);
        }

        var buildResult = DifferentialBuildHarness.RunProcess(
            repositoryRoot,
            "dotnet",
            arguments.ToString());
        Assert.True(buildResult.ExitCode == 0, buildResult.Output);

        var assemblyPath = Path.Combine(
            repositoryRoot,
            "samples",
            "SourceGenIlWeavingSample",
            "bin",
            "Debug",
            "net10.0",
            "SourceGenIlWeavingSample.dll");
        Assert.True(File.Exists(assemblyPath), buildResult.Output);
        return new SourceGenIlWeavingSampleBuildArtifact(string.Empty, assemblyPath, buildResult.Output);
    }

    private static string BuildDebugAssemblyPath()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectPath = Path.Combine(
            repositoryRoot,
            "samples",
            "SourceGenIlWeavingSample",
            "SourceGenIlWeavingSample.csproj");

        var buildResult = DifferentialBuildHarness.RunProcess(
            repositoryRoot,
            "dotnet",
            $"build \"{projectPath}\" -t:Rebuild -c Debug --nologo -m:1 /nodeReuse:false --disable-build-servers");
        Assert.True(buildResult.ExitCode == 0, buildResult.Output);

        var assemblyPath = Path.Combine(
            repositoryRoot,
            "samples",
            "SourceGenIlWeavingSample",
            "bin",
            "Debug",
            "net10.0",
            "SourceGenIlWeavingSample.dll");
        Assert.True(File.Exists(assemblyPath), buildResult.Output);
        return assemblyPath;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}

internal sealed record SourceGenIlWeavingSampleBuildArtifact(
    string WorkspaceDirectory,
    string AssemblyPath,
    string BuildOutput);
