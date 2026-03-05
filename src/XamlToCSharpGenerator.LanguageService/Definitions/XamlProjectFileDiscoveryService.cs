using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlProjectFileDiscoveryService
{
    private static readonly TimeSpan ProjectDiscoveryCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, CachedProjectFileList> ProjectFileListCache =
        new(PathComparer);

    public static ImmutableArray<string> DiscoverProjectXamlFilePaths(
        string? projectPath,
        string? currentFilePath)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathComparer);

        if (!string.IsNullOrWhiteSpace(currentFilePath))
        {
            AddCandidatePath(builder, seen, currentFilePath);
        }

        var resolvedProjectPath = ResolveProjectPath(projectPath, currentFilePath);
        if (resolvedProjectPath is null)
        {
            return builder.ToImmutable();
        }

        foreach (var includePath in GetCachedProjectXamlFileList(resolvedProjectPath))
        {
            AddCandidatePath(builder, seen, includePath);
        }

        return builder.ToImmutable();
    }

    public static string? ResolveProjectPath(string? projectPath, string? currentFilePath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var normalizedProjectPath = NormalizePath(projectPath);
            if (File.Exists(normalizedProjectPath))
            {
                return normalizedProjectPath;
            }

            if (Directory.Exists(normalizedProjectPath))
            {
                try
                {
                    var directoryProject = Directory
                        .EnumerateFiles(normalizedProjectPath, "*.csproj", SearchOption.TopDirectoryOnly)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (directoryProject is not null)
                    {
                        return NormalizePath(directoryProject);
                    }
                }
                catch
                {
                    // Ignore inaccessible workspace roots.
                }
            }
        }

        if (string.IsNullOrWhiteSpace(currentFilePath))
        {
            return null;
        }

        var currentDirectory = Path.GetDirectoryName(NormalizePath(currentFilePath));
        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            try
            {
                var projectFile = Directory
                    .EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (projectFile is not null)
                {
                    return NormalizePath(projectFile);
                }
            }
            catch
            {
                // Ignore inaccessible parent directories and continue searching upward.
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return null;
    }

    private static ImmutableArray<string> GetCachedProjectXamlFileList(string projectFilePath)
    {
        var normalizedProjectPath = NormalizePath(projectFilePath);
        var now = DateTimeOffset.UtcNow;
        if (ProjectFileListCache.TryGetValue(normalizedProjectPath, out var cached) &&
            now - cached.CachedAtUtc <= ProjectDiscoveryCacheTtl)
        {
            return cached.Paths;
        }

        var paths = BuildProjectXamlFileList(normalizedProjectPath);
        ProjectFileListCache[normalizedProjectPath] = new CachedProjectFileList(now, paths);
        return paths;
    }

    private static ImmutableArray<string> BuildProjectXamlFileList(string projectFilePath)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathComparer);
        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return builder.ToImmutable();
        }

        foreach (var filePath in EnumerateXamlFilesUnder(projectDirectory))
        {
            AddCandidatePath(builder, seen, filePath);
        }

        foreach (var includePath in EnumerateExplicitXamlIncludes(projectFilePath, projectDirectory))
        {
            AddCandidatePath(builder, seen, includePath);
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<string> EnumerateXamlFilesUnder(string rootDirectory)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(rootDirectory, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                yield break;
            }

            if (!IsXamlFile(filePath) || IsUnderBuildOutputDirectory(filePath))
            {
                continue;
            }

            yield return filePath;
        }
    }

    private static IEnumerable<string> EnumerateExplicitXamlIncludes(string projectFilePath, string projectDirectory)
    {
        XDocument projectDocument;
        try
        {
            projectDocument = XDocument.Load(projectFilePath, LoadOptions.PreserveWhitespace);
        }
        catch
        {
            yield break;
        }

        foreach (var itemElement in projectDocument.Descendants())
        {
            if (!IsXamlItemElement(itemElement.Name.LocalName))
            {
                continue;
            }

            var includeValue = itemElement.Attribute("Include")?.Value
                ?? itemElement.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(includeValue))
            {
                continue;
            }

            foreach (var includePath in ExpandProjectIncludePattern(projectDirectory, includeValue))
            {
                yield return includePath;
            }
        }
    }

    private static IEnumerable<string> ExpandProjectIncludePattern(string projectDirectory, string includeValue)
    {
        var normalizedPattern = includeValue.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalizedPattern))
        {
            yield break;
        }

        var hasWildcard = normalizedPattern.IndexOfAny(['*', '?']) >= 0;
        if (!hasWildcard)
        {
            var candidatePath = Path.GetFullPath(Path.Combine(projectDirectory, normalizedPattern));
            if (IsXamlFile(candidatePath) && File.Exists(candidatePath))
            {
                yield return candidatePath;
            }

            yield break;
        }

        var searchRoot = ResolveSearchRoot(projectDirectory, normalizedPattern);
        if (searchRoot is null || !Directory.Exists(searchRoot))
        {
            yield break;
        }

        var patternRegex = BuildGlobRegex(normalizedPattern);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(searchRoot, "*.*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        using var enumerator = files.GetEnumerator();
        while (true)
        {
            string filePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    yield break;
                }

                filePath = enumerator.Current;
            }
            catch
            {
                yield break;
            }

            if (!IsXamlFile(filePath))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/');
            if (patternRegex.IsMatch(relativePath))
            {
                yield return Path.GetFullPath(filePath);
            }
        }
    }

    private static string? ResolveSearchRoot(string projectDirectory, string includePattern)
    {
        var wildcardIndex = includePattern.IndexOfAny(['*', '?']);
        var basePrefix = wildcardIndex <= 0 ? string.Empty : includePattern.Substring(0, wildcardIndex);
        if (string.IsNullOrWhiteSpace(basePrefix))
        {
            return projectDirectory;
        }

        var normalizedBase = basePrefix.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalizedBase))
        {
            return Path.GetDirectoryName(normalizedBase);
        }

        var combined = Path.GetFullPath(Path.Combine(projectDirectory, normalizedBase));
        if (Directory.Exists(combined))
        {
            return combined;
        }

        return Path.GetDirectoryName(combined);
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*", ".*", StringComparison.Ordinal)
            .Replace(@"\*", @"[^/]*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal);

        return new Regex("^" + escaped + "$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static bool IsXamlItemElement(string localName)
    {
        return string.Equals(localName, "AvaloniaXaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Page", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "ApplicationDefinition", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "EmbeddedResource", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "None", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(localName, "Content", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCandidatePath(
        ImmutableArray<string>.Builder builder,
        HashSet<string> seen,
        string? candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return;
        }

        var normalizedPath = NormalizePath(candidatePath);
        if (!File.Exists(normalizedPath) || !seen.Add(normalizedPath))
        {
            return;
        }

        builder.Add(normalizedPath);
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path);
    }

    private static bool IsXamlFile(string path)
    {
        var extension = Path.GetExtension(path);
        return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderBuildOutputDirectory(string path)
    {
        var normalized = NormalizePath(path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .ToLowerInvariant();

        var separator = Path.DirectorySeparatorChar;
        return normalized.Contains(separator + "obj" + separator, StringComparison.Ordinal) ||
               normalized.Contains(separator + "bin" + separator, StringComparison.Ordinal);
    }

    private readonly record struct CachedProjectFileList(
        DateTimeOffset CachedAtUtc,
        ImmutableArray<string> Paths);
}
