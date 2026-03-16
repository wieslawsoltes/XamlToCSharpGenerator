using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlProjectFileDiscoveryService
{
    private static readonly TimeSpan ProjectDiscoveryCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly ConcurrentDictionary<string, CachedProjectFileList> ProjectFileListCache =
        new(PathComparer);
    private static readonly ConcurrentDictionary<string, CachedWorkspaceProjectList> WorkspaceProjectListCache =
        new(PathComparer);

    internal readonly record struct ProjectXamlFileEntry(string FilePath, string TargetPath);

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

    public static bool TryResolveProjectXamlFileByTargetPath(
        string? projectPath,
        string? currentFilePath,
        string? targetPath,
        out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var resolvedProjectPath = ResolveProjectPath(projectPath, currentFilePath);
        if (resolvedProjectPath is null)
        {
            return false;
        }

        var normalizedTargetPath = NormalizeTargetPath(targetPath);
        if (normalizedTargetPath.Length == 0)
        {
            return false;
        }

        foreach (var entry in GetCachedProjectXamlFileEntries(resolvedProjectPath))
        {
            if (!PathComparer.Equals(entry.TargetPath, normalizedTargetPath))
            {
                continue;
            }

            filePath = entry.FilePath;
            return true;
        }

        return false;
    }

    public static bool TryResolveProjectXamlEntryByFilePath(
        string? projectPath,
        string? currentFilePath,
        string filePath,
        out ProjectXamlFileEntry entry)
    {
        entry = default;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var resolvedProjectPath = ResolveProjectPath(projectPath, currentFilePath);
        if (resolvedProjectPath is null)
        {
            return false;
        }

        var normalizedFilePath = NormalizePath(filePath);
        foreach (var candidate in GetCachedProjectXamlFileEntries(resolvedProjectPath))
        {
            if (!PathComparer.Equals(candidate.FilePath, normalizedFilePath))
            {
                continue;
            }

            entry = candidate;
            return true;
        }

        return false;
    }

    public static bool TryResolveOwningProjectXamlEntry(
        string filePath,
        string? workspaceRoot,
        out string projectPath,
        out ProjectXamlFileEntry entry)
    {
        projectPath = string.Empty;
        entry = default;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var normalizedFilePath = NormalizePath(filePath);
        var ancestorProjectPath = ResolveProjectPath(projectPath: null, currentFilePath: normalizedFilePath);
        if (!string.IsNullOrWhiteSpace(ancestorProjectPath) &&
            TryResolveProjectXamlEntryByFilePath(
                ancestorProjectPath,
                normalizedFilePath,
                normalizedFilePath,
                out entry))
        {
            projectPath = ancestorProjectPath;
            return true;
        }

        foreach (var candidateProjectPath in GetCachedWorkspaceProjectPaths(workspaceRoot))
        {
            if (!string.IsNullOrWhiteSpace(ancestorProjectPath) &&
                PathComparer.Equals(candidateProjectPath, ancestorProjectPath))
            {
                continue;
            }

            if (!TryResolveProjectXamlEntryByFilePath(
                    candidateProjectPath,
                    normalizedFilePath,
                    normalizedFilePath,
                    out entry))
            {
                continue;
            }

            projectPath = candidateProjectPath;
            return true;
        }

        return false;
    }

    public static bool TryResolveOwningProjectPath(
        string filePath,
        string? workspaceRoot,
        out string projectPath)
    {
        projectPath = string.Empty;
        if (!TryResolveOwningProjectXamlEntry(filePath, workspaceRoot, out var resolvedProjectPath, out _))
        {
            return false;
        }

        projectPath = resolvedProjectPath;
        return true;
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
        var entries = GetCachedProjectXamlFileEntries(projectFilePath);
        if (entries.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(entries.Length);
        foreach (var entry in entries)
        {
            builder.Add(entry.FilePath);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<ProjectXamlFileEntry> GetCachedProjectXamlFileEntries(string projectFilePath)
    {
        var normalizedProjectPath = NormalizePath(projectFilePath);
        var now = DateTimeOffset.UtcNow;
        if (ProjectFileListCache.TryGetValue(normalizedProjectPath, out var cached) &&
            now - cached.CachedAtUtc <= ProjectDiscoveryCacheTtl)
        {
            return cached.Entries;
        }

        var entries = BuildProjectXamlFileList(normalizedProjectPath);
        ProjectFileListCache[normalizedProjectPath] = new CachedProjectFileList(now, entries);
        return entries;
    }

    private static ImmutableArray<string> GetCachedWorkspaceProjectPaths(string? workspaceRoot)
    {
        var normalizedWorkspaceRoot = NormalizeWorkspaceRoot(workspaceRoot);
        if (string.IsNullOrWhiteSpace(normalizedWorkspaceRoot))
        {
            return ImmutableArray<string>.Empty;
        }

        var now = DateTimeOffset.UtcNow;
        if (WorkspaceProjectListCache.TryGetValue(normalizedWorkspaceRoot, out var cached) &&
            now - cached.CachedAtUtc <= ProjectDiscoveryCacheTtl)
        {
            return cached.ProjectPaths;
        }

        var projectPaths = BuildWorkspaceProjectPathList(normalizedWorkspaceRoot);
        WorkspaceProjectListCache[normalizedWorkspaceRoot] = new CachedWorkspaceProjectList(now, projectPaths);
        return projectPaths;
    }

    private static ImmutableArray<ProjectXamlFileEntry> BuildProjectXamlFileList(string projectFilePath)
    {
        var entriesByFilePath = new Dictionary<string, ProjectXamlFileEntry>(PathComparer);
        var projectDirectory = Path.GetDirectoryName(projectFilePath);
        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return ImmutableArray<ProjectXamlFileEntry>.Empty;
        }

        foreach (var filePath in EnumerateXamlFilesUnder(projectDirectory))
        {
            AddOrUpdateEntry(entriesByFilePath, new ProjectXamlFileEntry(
                NormalizePath(filePath),
                NormalizeTargetPath(Path.GetRelativePath(projectDirectory, filePath))));
        }

        foreach (var includeEntry in EnumerateExplicitXamlIncludes(projectFilePath, projectDirectory))
        {
            AddOrUpdateEntry(entriesByFilePath, includeEntry);
        }

        if (entriesByFilePath.Count == 0)
        {
            return ImmutableArray<ProjectXamlFileEntry>.Empty;
        }

        var ordered = entriesByFilePath.Values
            .OrderBy(static value => value.FilePath, PathComparer)
            .ToImmutableArray();
        return ordered;
    }

    private static ImmutableArray<string> BuildWorkspaceProjectPathList(string workspaceRoot)
    {
        if (File.Exists(workspaceRoot))
        {
            return string.Equals(Path.GetExtension(workspaceRoot), ".csproj", StringComparison.OrdinalIgnoreCase)
                ? ImmutableArray.Create(NormalizePath(workspaceRoot))
                : ImmutableArray<string>.Empty;
        }

        if (!Directory.Exists(workspaceRoot))
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(PathComparer);
        IEnumerable<string> projectFiles;
        try
        {
            projectFiles = Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories);
        }
        catch
        {
            return ImmutableArray<string>.Empty;
        }

        using var enumerator = projectFiles.GetEnumerator();
        while (true)
        {
            string projectFilePath;
            try
            {
                if (!enumerator.MoveNext())
                {
                    break;
                }

                projectFilePath = enumerator.Current;
            }
            catch
            {
                break;
            }

            if (IsUnderBuildOutputDirectory(projectFilePath))
            {
                continue;
            }

            var normalizedProjectPath = NormalizePath(projectFilePath);
            if (seen.Add(normalizedProjectPath))
            {
                builder.Add(normalizedProjectPath);
            }
        }

        return builder
            .OrderBy(static value => value, PathComparer)
            .ToImmutableArray();
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

    private static IEnumerable<ProjectXamlFileEntry> EnumerateExplicitXamlIncludes(string projectFilePath, string projectDirectory)
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

            var targetPathMetadata = itemElement.Attribute("TargetPath")?.Value
                ?? itemElement.Attribute("Link")?.Value;
            foreach (var includePath in ExpandProjectIncludePattern(projectDirectory, includeValue))
            {
                yield return new ProjectXamlFileEntry(
                    NormalizePath(includePath),
                    ResolveTargetPath(projectDirectory, includeValue, includePath, targetPathMetadata));
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
        return UriPathHelper.NormalizeFilePath(path);
    }

    private static string? NormalizeWorkspaceRoot(string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        try
        {
            return NormalizePath(workspaceRoot);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeTargetPath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }

    private static string ResolveTargetPath(
        string projectDirectory,
        string includeValue,
        string includePath,
        string? targetPathMetadata)
    {
        if (!string.IsNullOrWhiteSpace(targetPathMetadata))
        {
            return NormalizeTargetPath(targetPathMetadata);
        }

        if (includeValue.IndexOfAny(['*', '?']) >= 0)
        {
            return NormalizeTargetPath(Path.GetRelativePath(projectDirectory, includePath));
        }

        return NormalizeTargetPath(includeValue);
    }

    private static void AddOrUpdateEntry(
        Dictionary<string, ProjectXamlFileEntry> entriesByFilePath,
        ProjectXamlFileEntry entry)
    {
        if (!File.Exists(entry.FilePath))
        {
            return;
        }

        if (!entriesByFilePath.TryGetValue(entry.FilePath, out var existing))
        {
            entriesByFilePath[entry.FilePath] = entry;
            return;
        }

        if (entry.TargetPath.Length > 0)
        {
            entriesByFilePath[entry.FilePath] = entry;
        }
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
        ImmutableArray<ProjectXamlFileEntry> Entries);

    private readonly record struct CachedWorkspaceProjectList(
        DateTimeOffset CachedAtUtc,
        ImmutableArray<string> ProjectPaths);
}
