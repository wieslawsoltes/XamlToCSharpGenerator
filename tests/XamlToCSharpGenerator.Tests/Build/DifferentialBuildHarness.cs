using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class DifferentialBuildHarness
{
    private static readonly string[] RoslynTransientFailureMarkers =
    {
        "BoundStepThroughSequencePoint.<Span>k__BackingField",
        "ILOpCodeExtensions.StackPushCount",
        "SignatureData.ReturnParam"
    };

    public static string GetRestoreMsBuildProperties(string workingDirectory)
    {
        return $"-p:MSBuildProjectExtensionsPath=\"{EnsureTrailingSeparator(Path.Combine(workingDirectory, "obj", "restore"))}\"";
    }

    public static string GetBackendMsBuildProperties(string workingDirectory, string backend)
    {
        var normalizedBackend = NormalizeBackendName(backend);
        return string.Join(
            " ",
            GetRestoreMsBuildProperties(workingDirectory),
            $"-p:BaseIntermediateOutputPath=\"{EnsureTrailingSeparator(Path.Combine(workingDirectory, "obj", normalizedBackend))}\"",
            $"-p:BaseOutputPath=\"{EnsureTrailingSeparator(Path.Combine(workingDirectory, "bin", normalizedBackend))}\"");
    }

    public static string GetGeneratedDirectory(string workingDirectory, string backend)
    {
        return Path.Combine(workingDirectory, "obj", NormalizeBackendName(backend), "generated");
    }

    public static string GetAssemblyPath(string workingDirectory, string backend, string assemblyName, string targetFramework = "net10.0")
    {
        return Path.Combine(
            workingDirectory,
            "bin",
            NormalizeBackendName(backend),
            "Debug",
            targetFramework,
            assemblyName + ".dll");
    }

    public static (int ExitCode, string Output) RunProcess(string workingDirectory, string fileName, string arguments)
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

    private static string NormalizeBackendName(string backend)
    {
        return string.IsNullOrWhiteSpace(backend)
            ? "default"
            : backend.Trim().ToLowerInvariant();
    }

    private static string EnsureTrailingSeparator(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.EndsWith("/", StringComparison.Ordinal) ? normalized : normalized + "/";
    }
}
