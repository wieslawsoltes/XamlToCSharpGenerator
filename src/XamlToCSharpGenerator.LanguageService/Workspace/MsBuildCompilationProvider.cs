using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

public sealed class MsBuildCompilationProvider : ICompilationProvider
{
    private static readonly TimeSpan ProjectResolutionCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly object LocatorGate = new();
    private static bool _locatorRegistered;
    private const string MissingMetadataReferencePrefix =
        "Found project reference without a matching metadata reference:";

    private readonly MSBuildWorkspace _workspace;
    private readonly SemaphoreSlim _workspaceGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Lazy<Task<CompilationSnapshot>>> _projectCompilationCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedProjectPathResolution> _fileProjectPathCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImmutableHashSet<string>> _analyzerOnlyProjectReferenceCache =
        new(StringComparer.OrdinalIgnoreCase);

    public MsBuildCompilationProvider()
    {
        RegisterMsBuildLocator();
        _workspace = MSBuildWorkspace.Create();
    }

    public Task<CompilationSnapshot> GetCompilationAsync(
        string filePath,
        string? workspaceRoot,
        CancellationToken cancellationToken)
    {
        var projectPath = ResolveProjectPath(filePath, workspaceRoot);
        if (projectPath is null)
        {
            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: null,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        projectPath = NormalizePath(projectPath);

        var lazyTask = _projectCompilationCache.GetOrAdd(
            projectPath,
            path => new Lazy<Task<CompilationSnapshot>>(
                () => LoadCompilationAsync(path),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return AwaitCompilationSnapshotAsync(projectPath, lazyTask, cancellationToken);
    }

    public void Invalidate(string filePath)
    {
        string? projectPath = null;
        try
        {
            projectPath = ResolveProjectPath(filePath, workspaceRoot: null);
        }
        catch (Exception ex) when (IsIgnorableProjectPathResolutionException(ex))
        {
            projectPath = null;
        }

        if (projectPath is not null)
        {
            _projectCompilationCache.TryRemove(NormalizePath(projectPath), out _);
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var normalizedFilePath = NormalizePath(filePath);
            foreach (var key in _fileProjectPathCache.Keys)
            {
                if (key.StartsWith(normalizedFilePath + "|", StringComparison.OrdinalIgnoreCase))
                {
                    _fileProjectPathCache.TryRemove(key, out _);
                }
            }
        }
    }

    public void Dispose()
    {
        _workspaceGate.Dispose();
        _workspace.Dispose();
    }

    private async Task<CompilationSnapshot> AwaitCompilationSnapshotAsync(
        string projectPath,
        Lazy<Task<CompilationSnapshot>> lazyTask,
        CancellationToken cancellationToken)
    {
        CompilationSnapshot snapshot;
        if (cancellationToken.CanBeCanceled)
        {
            snapshot = await lazyTask.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            snapshot = await lazyTask.Value.ConfigureAwait(false);
        }

        if (ShouldEvictCompilationSnapshot(snapshot))
        {
            _projectCompilationCache.TryRemove(projectPath, out _);
        }

        return snapshot;
    }

    private async Task<CompilationSnapshot> LoadCompilationAsync(string projectPath)
    {
        try
        {
            await _workspaceGate.WaitAsync().ConfigureAwait(false);
            Project? project;
            try
            {
                project = TryGetLoadedProject(projectPath);
                if (project is null)
                {
                    project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                _workspaceGate.Release();
            }

            var compilation = await project.GetCompilationAsync(CancellationToken.None).ConfigureAwait(false);
            if (compilation is null)
            {
                return new CompilationSnapshot(
                    projectPath,
                    project,
                    null,
                    ImmutableArray.Create(new LanguageServiceDiagnostic(
                        "AXSGLS0001",
                        "MSBuildWorkspace did not produce a compilation for the project.",
                        EmptyRange,
                        LanguageServiceDiagnosticSeverity.Warning,
                        Source: "AXSGLS")));
            }

            var diagnosticsBuilder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>();
            var analyzerOnlyReferences = GetAnalyzerOnlyProjectReferences(projectPath);
            foreach (var workspaceDiagnostic in _workspace.Diagnostics)
            {
                if (ShouldSuppressWorkspaceDiagnostic(workspaceDiagnostic.Message, analyzerOnlyReferences))
                {
                    continue;
                }

                diagnosticsBuilder.Add(new LanguageServiceDiagnostic(
                    "AXSGLS0002",
                    workspaceDiagnostic.Message,
                    EmptyRange,
                    workspaceDiagnostic.Kind == WorkspaceDiagnosticKind.Failure
                        ? LanguageServiceDiagnosticSeverity.Error
                        : LanguageServiceDiagnosticSeverity.Warning,
                    Source: "MSBuildWorkspace"));
            }

            return new CompilationSnapshot(projectPath, project, compilation, diagnosticsBuilder.ToImmutable());
        }
        catch (OperationCanceledException)
        {
            return new CompilationSnapshot(
                projectPath,
                null,
                null,
                ImmutableArray<LanguageServiceDiagnostic>.Empty);
        }
        catch (Exception ex)
        {
            return new CompilationSnapshot(
                projectPath,
                null,
                null,
                ImmutableArray.Create(new LanguageServiceDiagnostic(
                    "AXSGLS0003",
                    "Failed to load project compilation: " + ex.Message,
                    EmptyRange,
                    LanguageServiceDiagnosticSeverity.Error,
                    Source: "MSBuildWorkspace")));
        }
    }

    private static bool ShouldEvictCompilationSnapshot(CompilationSnapshot snapshot)
    {
        if (snapshot.Compilation is not null)
        {
            return false;
        }

        if (snapshot.Diagnostics.IsDefaultOrEmpty)
        {
            return true;
        }

        foreach (var diagnostic in snapshot.Diagnostics)
        {
            if (string.Equals(diagnostic.Code, "AXSGLS0003", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private Project? TryGetLoadedProject(string projectPath)
    {
        var normalizedProjectPath = NormalizePath(projectPath);

        foreach (var project in _workspace.CurrentSolution.Projects)
        {
            if (project.FilePath is null)
            {
                continue;
            }

            if (string.Equals(
                    NormalizePath(project.FilePath),
                    normalizedProjectPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return project;
            }
        }

        return null;
    }

    private ImmutableHashSet<string> GetAnalyzerOnlyProjectReferences(string projectPath)
    {
        return _analyzerOnlyProjectReferenceCache.GetOrAdd(projectPath, static path =>
        {
            try
            {
                var document = XDocument.Load(path);
                var projectDirectory = Path.GetDirectoryName(path);
                if (projectDirectory is null)
                {
                    return ImmutableHashSet<string>.Empty;
                }

                var references = ImmutableHashSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var projectReference in document.Descendants().Where(static element => element.Name.LocalName == "ProjectReference"))
                {
                    var include = projectReference.Attribute("Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }

                    var outputItemType = projectReference.Attribute("OutputItemType")?.Value
                        ?? projectReference.Elements().FirstOrDefault(static element => element.Name.LocalName == "OutputItemType")?.Value;
                    var referenceOutputAssembly = projectReference.Attribute("ReferenceOutputAssembly")?.Value
                        ?? projectReference.Elements().FirstOrDefault(static element => element.Name.LocalName == "ReferenceOutputAssembly")?.Value;
                    var isAnalyzerOutput = string.Equals(outputItemType, "Analyzer", StringComparison.OrdinalIgnoreCase);
                    var excludesMetadataReference = string.Equals(referenceOutputAssembly, "false", StringComparison.OrdinalIgnoreCase);
                    if (!isAnalyzerOutput && !excludesMetadataReference)
                    {
                        continue;
                    }

                    var resolvedPath = Path.GetFullPath(Path.Combine(projectDirectory, include));
                    references.Add(resolvedPath);
                }

                return references.ToImmutable();
            }
            catch
            {
                return ImmutableHashSet<string>.Empty;
            }
        });
    }

    private static bool ShouldSuppressWorkspaceDiagnostic(
        string message,
        ImmutableHashSet<string> analyzerOnlyReferences)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var prefixIndex = message.IndexOf(MissingMetadataReferencePrefix, StringComparison.Ordinal);
        if (prefixIndex < 0)
        {
            return false;
        }

        // This warning is expected for analyzer-only ProjectReference entries and is noisy in editor diagnostics.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AXSG_LANGUAGE_SERVICE_REPORT_MISSING_METADATA_REFERENCE")))
        {
            return true;
        }

        if (analyzerOnlyReferences.Count == 0)
        {
            return false;
        }

        var projectReferencePath = message.Substring(prefixIndex + MissingMetadataReferencePrefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(projectReferencePath))
        {
            return false;
        }

        try
        {
            return analyzerOnlyReferences.Contains(Path.GetFullPath(projectReferencePath));
        }
        catch
        {
            return false;
        }
    }

    private static string? FindNearestProjectPath(string filePath, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var currentDirectory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            return null;
        }

        if (!Directory.Exists(currentDirectory))
        {
            return null;
        }

        string? boundedWorkspaceRoot = null;
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
        {
            try
            {
                boundedWorkspaceRoot = Path.GetFullPath(workspaceRoot);
            }
            catch
            {
                boundedWorkspaceRoot = null;
            }
        }

        while (!string.IsNullOrWhiteSpace(currentDirectory))
        {
            var projectFiles = TryGetProjectFiles(currentDirectory, SearchOption.TopDirectoryOnly);
            if (projectFiles.Length > 0)
            {
                return projectFiles[0];
            }

            if (boundedWorkspaceRoot is not null &&
                string.Equals(Path.GetFullPath(currentDirectory), boundedWorkspaceRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var parent = Directory.GetParent(currentDirectory);
            currentDirectory = parent?.FullName;
        }

        if (boundedWorkspaceRoot is not null &&
            XamlProjectFileDiscoveryService.TryResolveOwningProjectPath(filePath, boundedWorkspaceRoot, out var owningProjectPath))
        {
            return owningProjectPath;
        }

        if (boundedWorkspaceRoot is not null && File.Exists(boundedWorkspaceRoot))
        {
            return boundedWorkspaceRoot;
        }

        if (boundedWorkspaceRoot is not null && Directory.Exists(boundedWorkspaceRoot))
        {
            var projectFiles = TryGetProjectFiles(boundedWorkspaceRoot, SearchOption.AllDirectories);
            return projectFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        return null;
    }

    private static string[] TryGetProjectFiles(string directoryPath, SearchOption searchOption)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                return [];
            }

            return Directory.GetFiles(directoryPath, "*.csproj", searchOption);
        }
        catch (Exception ex) when (IsIgnorableProjectPathResolutionException(ex))
        {
            return [];
        }
    }

    private static void RegisterMsBuildLocator()
    {
        lock (LocatorGate)
        {
            if (_locatorRegistered)
            {
                return;
            }

            var useLocatorValue = Environment.GetEnvironmentVariable("AXSG_LANGUAGE_SERVICE_USE_MSBUILD_LOCATOR");
            if (!string.IsNullOrWhiteSpace(useLocatorValue) &&
                (string.Equals(useLocatorValue, "0", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(useLocatorValue, "false", StringComparison.OrdinalIgnoreCase)))
            {
                _locatorRegistered = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(useLocatorValue))
            {
                // On .NET (Core) hosts, MSBuildWorkspace can discover .NET SDK toolsets without
                // MSBuildLocator. Registering locator here wires an AssemblyResolve path into the
                // SDK folder and can force-load mismatched Microsoft.Extensions.* versions.
                _locatorRegistered = true;
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances().ToArray();
                if (instances.Length > 0)
                {
                    var latest = instances
                        .OrderByDescending(static instance => instance.Version)
                        .First();
                    MSBuildLocator.RegisterInstance(latest);
                }
                else
                {
                    MSBuildLocator.RegisterDefaults();
                }
            }

            _locatorRegistered = true;
        }
    }

    private static string NormalizePath(string path)
    {
        return UriPathHelper.NormalizeFilePath(path);
    }

    private static bool IsIgnorableProjectPathResolutionException(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    }

    private string? ResolveProjectPath(string filePath, string? workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var normalizedFilePath = NormalizePath(filePath);
        var normalizedWorkspaceRoot = NormalizeWorkspaceRoot(workspaceRoot);
        var cacheKey = BuildProjectResolutionCacheKey(normalizedFilePath, normalizedWorkspaceRoot);
        var now = DateTimeOffset.UtcNow;
        if (_fileProjectPathCache.TryGetValue(cacheKey, out var cachedResolution) &&
            now - cachedResolution.CachedAtUtc <= ProjectResolutionCacheTtl)
        {
            return cachedResolution.ProjectPath;
        }

        var resolvedProjectPath = FindNearestProjectPath(normalizedFilePath, normalizedWorkspaceRoot);
        _fileProjectPathCache[cacheKey] = new CachedProjectPathResolution(now, resolvedProjectPath);
        return resolvedProjectPath;
    }

    private static string BuildProjectResolutionCacheKey(string normalizedFilePath, string? normalizedWorkspaceRoot)
    {
        return normalizedFilePath + "|" + (normalizedWorkspaceRoot ?? string.Empty);
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

    private static readonly SourceRange EmptyRange = new(
        new SourcePosition(0, 0),
        new SourcePosition(0, 1));

    private readonly record struct CachedProjectPathResolution(
        DateTimeOffset CachedAtUtc,
        string? ProjectPath);
}
