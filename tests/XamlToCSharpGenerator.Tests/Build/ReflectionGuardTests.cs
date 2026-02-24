using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
                var scanContent = StripNonCodeText(content);
                for (var index = 0; index < bannedFragments.Length; index++)
                {
                    if (!scanContent.Contains(bannedFragments[index], StringComparison.Ordinal))
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
            var scanContent = StripNonCodeText(content);
            if (!reflectionPattern.IsMatch(scanContent))
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
            var scanContent = StripNonCodeText(content);
            if (reflectionPattern.IsMatch(scanContent))
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

    private static string StripNonCodeText(string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
        {
            return sourceText;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetRoot();
        var chars = sourceText.ToCharArray();

        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!literal.IsKind(SyntaxKind.StringLiteralExpression) &&
                !literal.IsKind(SyntaxKind.CharacterLiteralExpression))
            {
                continue;
            }

            BlankSpan(chars, literal.Span);
        }

        foreach (var interpolated in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            BlankSpan(chars, interpolated.Span);
        }

        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                BlankSpan(chars, trivia.Span);
            }
        }

        return new string(chars);
    }

    private static void BlankSpan(char[] chars, TextSpan span)
    {
        var start = Math.Clamp(span.Start, 0, chars.Length);
        var end = Math.Clamp(span.End, start, chars.Length);
        for (var index = start; index < end; index++)
        {
            if (chars[index] != '\r' && chars[index] != '\n')
            {
                chars[index] = ' ';
            }
        }
    }
}
