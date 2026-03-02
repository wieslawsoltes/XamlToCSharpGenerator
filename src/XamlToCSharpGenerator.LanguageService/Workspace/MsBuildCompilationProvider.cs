using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

public sealed class MsBuildCompilationProvider : ICompilationProvider
{
    private static readonly object LocatorGate = new();
    private static bool _locatorRegistered;

    private readonly MSBuildWorkspace _workspace;
    private readonly ConcurrentDictionary<string, Lazy<Task<CompilationSnapshot>>> _projectCompilationCache =
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
        var projectPath = FindNearestProjectPath(filePath, workspaceRoot);
        if (projectPath is null)
        {
            return Task.FromResult(new CompilationSnapshot(
                ProjectPath: null,
                Compilation: null,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty));
        }

        var lazyTask = _projectCompilationCache.GetOrAdd(
            projectPath,
            path => new Lazy<Task<CompilationSnapshot>>(
                () => LoadCompilationAsync(path, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyTask.Value;
    }

    public void Invalidate(string filePath)
    {
        var projectPath = FindNearestProjectPath(filePath, workspaceRoot: null);
        if (projectPath is not null)
        {
            _projectCompilationCache.TryRemove(projectPath, out _);
        }
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }

    private async Task<CompilationSnapshot> LoadCompilationAsync(string projectPath, CancellationToken cancellationToken)
    {
        try
        {
            var project = await _workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is null)
            {
                return new CompilationSnapshot(
                    projectPath,
                    null,
                    ImmutableArray.Create(new LanguageServiceDiagnostic(
                        "AXSGLS0001",
                        "MSBuildWorkspace did not produce a compilation for the project.",
                        EmptyRange,
                        LanguageServiceDiagnosticSeverity.Warning,
                        Source: "AXSGLS")));
            }

            var diagnosticsBuilder = ImmutableArray.CreateBuilder<LanguageServiceDiagnostic>();
            foreach (var workspaceDiagnostic in _workspace.Diagnostics)
            {
                diagnosticsBuilder.Add(new LanguageServiceDiagnostic(
                    "AXSGLS0002",
                    workspaceDiagnostic.Message,
                    EmptyRange,
                    workspaceDiagnostic.Kind == WorkspaceDiagnosticKind.Failure
                        ? LanguageServiceDiagnosticSeverity.Error
                        : LanguageServiceDiagnosticSeverity.Warning,
                    Source: "MSBuildWorkspace"));
            }

            return new CompilationSnapshot(projectPath, compilation, diagnosticsBuilder.ToImmutable());
        }
        catch (Exception ex)
        {
            return new CompilationSnapshot(
                projectPath,
                null,
                ImmutableArray.Create(new LanguageServiceDiagnostic(
                    "AXSGLS0003",
                    "Failed to load project compilation: " + ex.Message,
                    EmptyRange,
                    LanguageServiceDiagnosticSeverity.Error,
                    Source: "MSBuildWorkspace")));
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
            var projectFiles = Directory.GetFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly);
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

        if (boundedWorkspaceRoot is not null && Directory.Exists(boundedWorkspaceRoot))
        {
            var projectFiles = Directory.GetFiles(boundedWorkspaceRoot, "*.csproj", SearchOption.AllDirectories);
            return projectFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        return null;
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

    private static readonly SourceRange EmptyRange = new(
        new SourcePosition(0, 0),
        new SourcePosition(0, 1));
}
