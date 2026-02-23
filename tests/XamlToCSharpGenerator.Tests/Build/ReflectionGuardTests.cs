using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XamlToCSharpGenerator.Tests.Build;

public class ReflectionGuardTests
{
    private const string RuntimeAvaloniaReflectionPattern =
        @"\b(System\.Reflection\.(?!Metadata\b)|using\s+System\.Reflection(?:\.Emit)?\s*;|Type\.GetType\(|Activator\.CreateInstance\(|GetMethod\(|GetProperty\(|GetField\()";

    [Fact]
    public void DirectoryBuildProps_Enables_BannedApiAnalyzer_For_FrameworkAgnosticProjects()
    {
        var repositoryRoot = GetRepositoryRoot();
        var propsPath = Path.Combine(repositoryRoot, "Directory.Build.props");
        var props = File.ReadAllText(propsPath);

        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", props, StringComparison.Ordinal);
        Assert.Contains("BannedSymbols.Reflection.txt", props, StringComparison.Ordinal);
        Assert.Contains("XamlToCSharpGenerator.Core", props, StringComparison.Ordinal);
        Assert.Contains("XamlToCSharpGenerator.Compiler", props, StringComparison.Ordinal);
        Assert.Contains("XamlToCSharpGenerator.Generator", props, StringComparison.Ordinal);
        Assert.Contains("XamlToCSharpGenerator.Runtime.Core", props, StringComparison.Ordinal);
        Assert.Contains("RS0030", props, StringComparison.Ordinal);
    }

    [Fact]
    public void BannedSymbolsFile_Defines_ReflectionBlocks()
    {
        var repositoryRoot = GetRepositoryRoot();
        var bannedSymbolsPath = Path.Combine(repositoryRoot, "eng", "analyzers", "BannedSymbols.Reflection.txt");
        Assert.True(File.Exists(bannedSymbolsPath), "Banned symbols file is missing.");

        var text = File.ReadAllText(bannedSymbolsPath);
        Assert.Contains("N:System.Reflection", text, StringComparison.Ordinal);
        Assert.Contains("N:System.Reflection.Emit", text, StringComparison.Ordinal);
        Assert.Contains("M:System.Type.GetType(System.String)", text, StringComparison.Ordinal);
        Assert.Contains("M:System.Activator.CreateInstance(System.Type)", text, StringComparison.Ordinal);
        Assert.Contains("M:System.Reflection.MethodInfo.Invoke(System.Object,System.Object[])", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FrameworkAgnosticProjects_DoNotUseReflectionApis()
    {
        var repositoryRoot = GetRepositoryRoot();
        var projectDirectories = new[]
        {
            Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Core"),
            Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Compiler"),
            Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Generator"),
            Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime.Core")
        };

        var bannedFragments = new[]
        {
            "using System.Reflection;",
            "using System.Reflection.Emit;",
            "System.Reflection.",
            "Type.GetType(",
            "Activator.CreateInstance(",
            "AssemblyBuilder."
        };

        var violations = new List<string>();
        foreach (var projectDirectory in projectDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file);
                if (relativePath.Contains("/obj/", StringComparison.Ordinal) ||
                    relativePath.Contains("/bin/", StringComparison.Ordinal) ||
                    relativePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                    relativePath.Contains("\\bin\\", StringComparison.Ordinal))
                {
                    continue;
                }

                var content = File.ReadAllText(file);
                for (var index = 0; index < bannedFragments.Length; index++)
                {
                    if (!content.Contains(bannedFragments[index], StringComparison.Ordinal))
                    {
                        continue;
                    }

                    violations.Add(relativePath + " -> " + bannedFragments[index]);
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Framework-agnostic projects contain reflection API usage:\n" + string.Join('\n', violations.OrderBy(static x => x, StringComparer.Ordinal)));
    }

    [Fact]
    public void RuntimeAvalonia_ReflectionUsage_IsConfined_To_Tracked_AllowList()
    {
        var repositoryRoot = GetRepositoryRoot();
        var runtimeAvaloniaDirectory = Path.Combine(repositoryRoot, "src", "XamlToCSharpGenerator.Runtime.Avalonia");

        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var reflectionPattern = new Regex(RuntimeAvaloniaReflectionPattern, RegexOptions.CultureInvariant | RegexOptions.Multiline);

        var violatingFiles = new List<string>();
        foreach (var file in Directory.EnumerateFiles(runtimeAvaloniaDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, file);
            if (relativePath.Contains("/obj/", StringComparison.Ordinal) ||
                relativePath.Contains("/bin/", StringComparison.Ordinal) ||
                relativePath.Contains("\\obj\\", StringComparison.Ordinal) ||
                relativePath.Contains("\\bin\\", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (!reflectionPattern.IsMatch(content))
            {
                continue;
            }

            if (!allowedFiles.Contains(relativePath))
            {
                violatingFiles.Add(relativePath);
            }
        }

        Assert.True(
            violatingFiles.Count == 0,
            "Untracked reflection usage found in Runtime.Avalonia:\n" +
            string.Join('\n', violatingFiles.OrderBy(static x => x, StringComparer.Ordinal)));
    }

    [Fact]
    public void Wave7_Eliminated_Runtime_Files_Are_ReflectionFree()
    {
        var repositoryRoot = GetRepositoryRoot();
        var targetFiles = new[]
        {
            Path.Combine("src", "XamlToCSharpGenerator.Runtime.Avalonia", "SourceGenRuntimeXamlLoaderBridge.cs"),
            Path.Combine("src", "XamlToCSharpGenerator.Runtime.Avalonia", "XamlSourceGenHotReloadStateTracker.cs"),
            Path.Combine("src", "XamlToCSharpGenerator.Runtime.Avalonia", "XamlSourceGenHotReloadManager.cs"),
            Path.Combine("src", "XamlToCSharpGenerator.Runtime.Avalonia", "SourceGenEventBindingRuntime.cs")
        };

        var reflectionPattern = new Regex(RuntimeAvaloniaReflectionPattern, RegexOptions.CultureInvariant | RegexOptions.Multiline);
        var violations = new List<string>();
        for (var index = 0; index < targetFiles.Length; index++)
        {
            var relativePath = targetFiles[index];
            var fullPath = Path.Combine(repositoryRoot, relativePath);
            var content = File.ReadAllText(fullPath);
            if (reflectionPattern.IsMatch(content))
            {
                violations.Add(relativePath);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Wave 7 target files regressed to reflection usage:\n" +
            string.Join('\n', violations.OrderBy(static x => x, StringComparer.Ordinal)));
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
