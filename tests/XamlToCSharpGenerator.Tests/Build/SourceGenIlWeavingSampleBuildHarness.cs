using System;
using System.IO;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class SourceGenIlWeavingSampleBuildHarness
{
    private static readonly Lazy<string> DebugAssemblyPath = new(BuildDebugAssemblyPath);

    public static string GetDebugAssemblyPath()
    {
        return DebugAssemblyPath.Value;
    }

    public static SourceGenIlWeavingSampleBuildArtifact BuildWithProperties(
        string scenario,
        params (string Name, string Value)[] properties)
    {
        var repositoryRoot = GetRepositoryRoot();
        var sampleDirectory = Path.Combine(
            repositoryRoot,
            "samples",
            "SourceGenIlWeavingSample");
        var workspaceDirectory = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, scenario);
        var copiedSampleDirectory = Path.Combine(workspaceDirectory, "SourceGenIlWeavingSample");
        var projectPath = Path.Combine(copiedSampleDirectory, "SourceGenIlWeavingSample.csproj");

        try
        {
            CopyDirectory(sampleDirectory, copiedSampleDirectory);
            RewriteCopiedSampleProject(projectPath, repositoryRoot);

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
                copiedSampleDirectory,
                "dotnet",
                arguments.ToString());
            Assert.True(buildResult.ExitCode == 0, buildResult.Output);

            var assemblyPath = Path.Combine(
                copiedSampleDirectory,
                "bin",
                "Debug",
                "net10.0",
                "SourceGenIlWeavingSample.dll");
            Assert.True(File.Exists(assemblyPath), buildResult.Output);
            return new SourceGenIlWeavingSampleBuildArtifact(workspaceDirectory, assemblyPath, buildResult.Output);
        }
        catch
        {
            BuildTestWorkspacePaths.TryDeleteDirectory(workspaceDirectory);
            throw;
        }
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            var directoryName = Path.GetFileName(directory);
            if (string.Equals(directoryName, "bin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(directoryName, "obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destinationDirectory, directoryName));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(filePath, Path.Combine(destinationDirectory, Path.GetFileName(filePath)), overwrite: true);
        }
    }

    private static void RewriteCopiedSampleProject(string projectPath, string repositoryRoot)
    {
        var projectText = File.ReadAllText(projectPath);
        var sourceRoot = NormalizeForMsBuild(Path.Combine(repositoryRoot, "src"));
        projectText = projectText.Replace("..\\..\\src\\", sourceRoot + "/", StringComparison.Ordinal);
        File.WriteAllText(projectPath, projectText);
    }

    private static string NormalizeForMsBuild(string path)
    {
        return path.Replace('\\', '/');
    }
}

internal sealed record SourceGenIlWeavingSampleBuildArtifact(
    string WorkspaceDirectory,
    string AssemblyPath,
    string BuildOutput);
