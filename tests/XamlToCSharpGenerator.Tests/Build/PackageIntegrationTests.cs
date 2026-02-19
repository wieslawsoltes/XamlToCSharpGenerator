using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PackageIntegrationTests
{
    [Fact]
    public void TopLevel_Package_Packs_And_Contains_Expected_Assets()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var outputDir = Path.Combine(Path.GetTempPath(), "XamlToCSharpGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDir);

        try
        {
            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo --no-restore -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.props");
            Assert.Contains(archive.Entries, entry => entry.FullName == "buildTransitive/XamlToCSharpGenerator.targets");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Generator.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.dll");
        }
        finally
        {
            try
            {
                Directory.Delete(outputDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
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
        process.WaitForExit();
        System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);

        var outputBuilder = new StringBuilder();
        outputBuilder.Append(stdoutTask.Result);
        outputBuilder.Append(stderrTask.Result);
        return (process.ExitCode, outputBuilder.ToString());
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
