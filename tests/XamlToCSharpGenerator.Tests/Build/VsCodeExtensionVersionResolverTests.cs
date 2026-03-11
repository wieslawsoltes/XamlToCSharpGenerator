using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

public class VsCodeExtensionVersionResolverTests
{
    [Theory]
    [InlineData("0.1.0", "0.2.0")]
    [InlineData("0.1.0-alpha.4", "0.3.200400")]
    [InlineData("0.1.0-beta.1", "0.3.300100")]
    [InlineData("0.1.0-rc.1", "0.3.400100")]
    public void Resolver_Maps_Common_Release_Channels(string version, string expected)
    {
        var result = RunResolver(version);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.Equal(expected, result.StandardOutput.Trim());
    }

    [Fact]
    public void Resolver_Assigns_Distinct_Versions_For_Different_Prerelease_Channels()
    {
        var alpha = RunResolver("0.1.0-alpha.1");
        var beta = RunResolver("0.1.0-beta.1");

        Assert.True(alpha.ExitCode == 0, alpha.CombinedOutput);
        Assert.True(beta.ExitCode == 0, beta.CombinedOutput);
        Assert.NotEqual(alpha.StandardOutput.Trim(), beta.StandardOutput.Trim());
    }

    [Fact]
    public void Resolver_Assigns_Distinct_Version_For_Ci_Suffixed_Prerelease()
    {
        var releaseCandidate = RunResolver("0.1.0-alpha.4");
        var ciBuild = RunResolver("0.1.0-alpha.4-ci.22949889894");

        Assert.True(releaseCandidate.ExitCode == 0, releaseCandidate.CombinedOutput);
        Assert.True(ciBuild.ExitCode == 0, ciBuild.CombinedOutput);
        Assert.NotEqual(releaseCandidate.StandardOutput.Trim(), ciBuild.StandardOutput.Trim());
        Assert.StartsWith("0.3.", ciBuild.StandardOutput.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_Rejects_Invalid_Semantic_Version()
    {
        var result = RunResolver("not-a-version");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unsupported semantic version: not-a-version", result.CombinedOutput, StringComparison.Ordinal);
    }

    private static ProcessResult RunResolver(string version)
    {
        var repositoryRoot = GetRepositoryRoot();
        var scriptPath = Path.Combine(repositoryRoot, "eng", "release", "resolve-vscode-extension-version.mjs");

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add(version);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        return new ProcessResult(
            process.ExitCode,
            stdoutTask.Result,
            stderrTask.Result);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput
        {
            get
            {
                var outputBuilder = new StringBuilder();
                outputBuilder.Append(StandardOutput);
                outputBuilder.Append(StandardError);
                return outputBuilder.ToString();
            }
        }
    }
}
