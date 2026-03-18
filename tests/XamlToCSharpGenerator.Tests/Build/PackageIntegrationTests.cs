using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.Tests.Build;

[Collection("BuildSerial")]
public class PackageIntegrationTests
{
    [Fact]
    public void TopLevel_Package_Packs_And_Contains_Expected_Assets()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator", "XamlToCSharpGenerator.csproj");
        var outputDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "package-integration");

        try
        {
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

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
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Compiler.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Framework.Abstractions.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "analyzers/dotnet/cs/XamlToCSharpGenerator.ExpressionSemantics.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net6.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net8.0/XamlToCSharpGenerator.Runtime.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.MiniLanguageParsing.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Core.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.RemoteProtocol.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.Avalonia.dll");
            Assert.Contains(archive.Entries, entry => entry.FullName == "lib/net10.0/XamlToCSharpGenerator.Runtime.dll");
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(outputDir);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    [Fact]
    public void RuntimeAvalonia_Package_Packs_And_Declares_Runtime_Dependencies()
    {
        var repositoryRoot = GetRepositoryRoot();
        var packageProject = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime.Avalonia", "XamlToCSharpGenerator.Runtime.Avalonia.csproj");
        var outputDir = BuildTestWorkspacePaths.CreateTemporaryDirectory(repositoryRoot, "runtime-avalonia-package-integration");

        try
        {
            var restore = RunProcess(
                repositoryRoot,
                "dotnet",
                $"restore \"{packageProject}\" --nologo -m:1 /nodeReuse:false --disable-build-servers");
            Assert.True(restore.ExitCode == 0, restore.Output);

            var result = RunProcess(
                repositoryRoot,
                "dotnet",
                $"pack \"{packageProject}\" --nologo -c Debug -m:1 /nodeReuse:false --disable-build-servers -o \"{outputDir}\"");

            Assert.True(result.ExitCode == 0, result.Output);

            var packagePath = Directory.GetFiles(outputDir, "XamlToCSharpGenerator.Runtime.Avalonia.*.nupkg")
                .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            Assert.False(string.IsNullOrWhiteSpace(packagePath), result.Output);

            using var stream = File.OpenRead(packagePath!);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var nuspecEntry = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var nuspecStream = nuspecEntry.Open();
            var nuspec = XDocument.Load(nuspecStream);
            XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
            var dependencyIds = nuspec
                .Descendants(ns + "dependency")
                .Select(static element => (string?)element.Attribute("id"))
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .ToArray();

            Assert.Contains("XamlToCSharpGenerator.MiniLanguageParsing", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.RemoteProtocol", dependencyIds);
            Assert.Contains("XamlToCSharpGenerator.Runtime.Core", dependencyIds);
            Assert.Contains("Avalonia", dependencyIds);
            Assert.Contains("Avalonia.Markup.Xaml.Loader", dependencyIds);
        }
        finally
        {
            try
            {
                BuildTestWorkspacePaths.TryDeleteDirectory(outputDir);
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
