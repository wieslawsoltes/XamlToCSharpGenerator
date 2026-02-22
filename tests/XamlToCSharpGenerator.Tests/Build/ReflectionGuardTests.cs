using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace XamlToCSharpGenerator.Tests.Build;

public class ReflectionGuardTests
{
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

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
