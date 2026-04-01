using System;
using System.Collections.Concurrent;
using System.IO;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed class XamlLanguageFrameworkResolver
{
    private static readonly ConcurrentDictionary<ProjectFrameworkCacheKey, ProjectFrameworkCacheEntry> ProjectFrameworkCache = new();

    private readonly XamlLanguageFrameworkRegistry _frameworkRegistry;

    public XamlLanguageFrameworkResolver(XamlLanguageFrameworkRegistry frameworkRegistry)
    {
        _frameworkRegistry = frameworkRegistry ?? throw new ArgumentNullException(nameof(frameworkRegistry));
    }

    public XamlLanguageFrameworkInfo Resolve(
        string? explicitFrameworkId,
        string? projectPath,
        Compilation? compilation,
        string filePath,
        string? documentText)
    {
        if (_frameworkRegistry.TryGetById(explicitFrameworkId, out var explicitFramework))
        {
            return explicitFramework;
        }

        if (TryResolveFromProject(projectPath, out var projectFramework))
        {
            return projectFramework;
        }

        if (TryResolveFromCompilation(compilation, out var compilationFramework))
        {
            return compilationFramework;
        }

        if (TryResolveFromDocument(filePath, documentText, out var documentFramework))
        {
            return documentFramework;
        }

        return string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase) &&
               _frameworkRegistry.TryGetById(FrameworkProfileIds.Wpf, out var wpfFramework)
            ? wpfFramework
            : _frameworkRegistry.DefaultFramework;
    }

    private bool TryResolveFromProject(string? projectPath, out XamlLanguageFrameworkInfo framework)
    {
        framework = _frameworkRegistry.DefaultFramework;
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return false;
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        long lastWriteUtcTicks;
        try
        {
            lastWriteUtcTicks = File.GetLastWriteTimeUtc(normalizedProjectPath).Ticks;
        }
        catch
        {
            return false;
        }

        var cacheKey = new ProjectFrameworkCacheKey(_frameworkRegistry.CacheKey, normalizedProjectPath);
        if (ProjectFrameworkCache.TryGetValue(cacheKey, out var cachedEntry) &&
            cachedEntry.LastWriteUtcTicks == lastWriteUtcTicks)
        {
            return _frameworkRegistry.TryGetById(cachedEntry.FrameworkId, out framework);
        }

        string? resolvedFrameworkId = null;
        try
        {
            var projectDocument = XDocument.Load(normalizedProjectPath, LoadOptions.None);
            foreach (var provider in _frameworkRegistry.Providers)
            {
                if (!provider.CanResolveFromProject(projectDocument, normalizedProjectPath))
                {
                    continue;
                }

                resolvedFrameworkId = provider.Framework.Id;
                break;
            }
        }
        catch
        {
            resolvedFrameworkId = null;
        }

        ProjectFrameworkCache[cacheKey] = new ProjectFrameworkCacheEntry(lastWriteUtcTicks, resolvedFrameworkId);
        return _frameworkRegistry.TryGetById(resolvedFrameworkId, out framework);
    }

    private bool TryResolveFromCompilation(Compilation? compilation, out XamlLanguageFrameworkInfo framework)
    {
        framework = _frameworkRegistry.DefaultFramework;
        if (compilation is null)
        {
            return false;
        }

        foreach (var provider in _frameworkRegistry.Providers)
        {
            if (!provider.CanResolveFromCompilation(compilation))
            {
                continue;
            }

            framework = provider.Framework;
            return true;
        }

        return false;
    }

    private bool TryResolveFromDocument(
        string filePath,
        string? documentText,
        out XamlLanguageFrameworkInfo framework)
    {
        framework = _frameworkRegistry.DefaultFramework;
        foreach (var provider in _frameworkRegistry.Providers)
        {
            if (!provider.CanResolveFromDocument(filePath, documentText))
            {
                continue;
            }

            framework = provider.Framework;
            return true;
        }

        return false;
    }

    private readonly record struct ProjectFrameworkCacheKey(string RegistryKey, string ProjectPath);

    private readonly record struct ProjectFrameworkCacheEntry(long LastWriteUtcTicks, string? FrameworkId);
}
