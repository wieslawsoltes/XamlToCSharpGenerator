using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Workspace;

/// <summary>
/// Avalonia-specific factory for the Tier-1 (fast) compilation snapshot used by
/// <see cref="TieredCompilationProvider"/>.
///
/// <para>
/// Builds a lightweight Roslyn compilation from the project's Avalonia build
/// artifacts (the <c>Avalonia/references</c> file emitted by the Avalonia SDK
/// MSBuild targets), so the editor can offer Avalonia control and attribute
/// completions immediately — without waiting for MSBuild to evaluate the full
/// project.
/// </para>
///
/// <para>
/// The cache is global (single file) and version-aware. It stores only the
/// latest Avalonia version seen; older versions are never overwritten.
/// </para>
///
/// <para>
/// Usage: call <see cref="FindReferencesFile"/> to locate the references
/// artifact, then <see cref="GetAvaloniaVersionFromReferencesFile"/> to detect
/// the version, then <see cref="TryLoadFromCache"/> for a cache hit or
/// <see cref="BuildFastSnapshot"/> for a cache miss. Finally, call
/// <see cref="PersistToDisk"/> to update the global cache (which respects the
/// version constraint: only writes if new version >= cached version).
/// </para>
/// </summary>
public static class AvaloniaFastCompilationProvider
{
    private const string AvaloniaXmlNamespace = "https://github.com/avaloniaui";
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        WriteIndented = false
    };

    /// <summary>
    /// Probes for the <c>Avalonia/references</c> artifact file emitted by the
    /// Avalonia SDK in one of the common intermediate output paths.
    /// </summary>
    /// <returns>
    /// The full path to the <c>Avalonia/references</c> file, or null if not found.
    /// </returns>
    public static string? FindReferencesFile(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return null;
        }

        try
        {
            // Find the first .csproj file in the workspace
            var projectFile = TieredCompilationProvider.FindFirstProjectFile(workspaceRoot);
            if (projectFile is null)
            {
                return null;
            }

            var projectDir = Path.GetDirectoryName(projectFile);
            if (projectDir is null)
            {
                return null;
            }

            // Probe common Intermediate output paths
            var probePaths = new[]
            {
                Path.Combine(projectDir, "obj", "Debug", "*", "Avalonia", "references"),
                Path.Combine(projectDir, "obj", "Release", "*", "Avalonia", "references"),
                Path.Combine(projectDir, "artifacts", "obj", "*", "debug*", "Avalonia", "references"),
            };

            foreach (var pattern in probePaths)
            {
                var dir = Path.GetDirectoryName(pattern);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                var matches = Directory.GetFiles(dir, "references", SearchOption.AllDirectories);
                var match = matches.FirstOrDefault();
                if (match is not null && File.Exists(match))
                {
                    return match;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the <c>Avalonia/references</c> file and detects the Avalonia
    /// version by examining <c>Avalonia.Base.dll</c>'s FileVersionInfo.
    /// </summary>
    /// <returns>
    /// The Avalonia semantic version (e.g., "11.3.7"), or null if the file
    /// cannot be read or Avalonia.Base.dll is not found in the references.
    /// </returns>
    public static string? GetAvaloniaVersionFromReferencesFile(string referencesFilePath)
    {
        try
        {
            if (!File.Exists(referencesFilePath))
            {
                return null;
            }

            var lines = File.ReadAllLines(referencesFilePath);
            var avaloniaBaseDll = lines.FirstOrDefault(
                line => Path.GetFileName(line).Equals("Avalonia.Base.dll", StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(line));

            if (string.IsNullOrWhiteSpace(avaloniaBaseDll))
            {
                return null;
            }

            var versionInfo = FileVersionInfo.GetVersionInfo(avaloniaBaseDll);
            return !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                ? versionInfo.ProductVersion
                : versionInfo.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to load a previously-cached fast snapshot for the given Avalonia version.
    /// Uses the cache if its version >= project version (newer caches work for older projects).
    /// Returns null if the cache file doesn't exist, cached version is older, or any read fails.
    /// </summary>
    public static CompilationSnapshot? TryLoadFromCache(string avaloniaVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(avaloniaVersion))
            {
                return null;
            }

            var filePath = GetCacheFilePath();
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<Tier1CachePayload>(json, CacheJsonOptions);

            if (payload?.Version != 1 || string.IsNullOrWhiteSpace(payload?.AvaloniaVersion) ||
                !IsVersionGreaterOrEqual(payload!.AvaloniaVersion, avaloniaVersion))
            {
                return null;
            }

            // Rebuild compilation from the cached reference paths
            var referencePaths = (payload.CachedReferencePaths ?? Array.Empty<string>())
                .Where(File.Exists)
                .ToArray();

            if (referencePaths.Length == 0)
            {
                return null;
            }

            var refs = referencePaths
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName: "AvaloniaCore",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                references: refs);

            // Rebuild type index from cached data
            var mapBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AvaloniaTypeInfo>>(StringComparer.Ordinal);
            foreach (var ns in payload.XmlNamespaces ?? Array.Empty<CachedNamespace>())
            {
                if (string.IsNullOrWhiteSpace(ns.XmlNamespace))
                {
                    continue;
                }

                var types = (ns.Types ?? Array.Empty<CachedType>())
                    .Select(t => new AvaloniaTypeInfo(
                        XmlTypeName: t.XmlTypeName ?? string.Empty,
                        FullTypeName: t.FullTypeName ?? string.Empty,
                        XmlNamespace: ns.XmlNamespace!,
                        ClrNamespace: t.ClrNamespace ?? string.Empty,
                        AssemblyName: t.AssemblyName ?? string.Empty,
                        Properties: (t.Properties ?? Array.Empty<CachedProperty>())
                            .Select(p => new AvaloniaPropertyInfo(
                                Name: p.Name ?? string.Empty,
                                TypeName: p.TypeName ?? string.Empty,
                                IsSettable: p.IsSettable,
                                IsAttached: p.IsAttached,
                                SourceLocation: null))
                            .ToImmutableArray(),
                        Summary: t.Summary ?? string.Empty,
                        SourceLocation: null,
                        PseudoClasses: ImmutableArray<AvaloniaPseudoClassInfo>.Empty))
                    .ToImmutableArray();

                if (!types.IsDefaultOrEmpty)
                {
                    mapBuilder[ns.XmlNamespace!] = types;
                }
            }

            var map = mapBuilder.ToImmutable();
            if (map.IsEmpty)
            {
                return null;
            }

            AvaloniaTypeIndex.TryPrimeCache(compilation, map);

            return new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: compilation,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the Avalonia-core Tier-1 snapshot from a references file.
    /// Returns null if the file cannot be read or no valid references are found.
    /// </summary>
    public static CompilationSnapshot? BuildFastSnapshot(string referencesFilePath)
    {
        try
        {
            if (!File.Exists(referencesFilePath))
            {
                return null;
            }

            var referencePaths = File.ReadAllLines(referencesFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && File.Exists(line))
                .ToArray();

            if (referencePaths.Length == 0)
            {
                Console.Error.WriteLine("[AXSG-LS] Avalonia fast snapshot skipped — no valid DLL references found in references file.");
                return null;
            }

            var refs = referencePaths
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName: "AvaloniaCore",
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                references: refs);

            Console.Error.WriteLine(
                $"[AXSG-LS] Avalonia core (Tier-1) compilation built with {refs.Count} references.");

            // Try to load cached type index if it exists
            _ = TryPrimeTypeIndexFromDisk(compilation, referencePaths);

            return new CompilationSnapshot(
                ProjectPath: null,
                Project: null,
                Compilation: compilation,
                Diagnostics: ImmutableArray<LanguageServiceDiagnostic>.Empty);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AXSG-LS] Failed to build Avalonia fast snapshot: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Persists the type index to disk, but only if the new version is >= the
    /// cached version (implements the "never downgrade" policy). Overwrites the
    /// cache file on successful write, which evicts the previous version.
    /// </summary>
    public static void PersistToDisk(Compilation compilation, string avaloniaVersion)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(avaloniaVersion))
            {
                return;
            }

            // Check if we should overwrite the existing cache
            var existingVersion = GetCachedAvaloniaVersion();
            if (!string.IsNullOrWhiteSpace(existingVersion) &&
                !IsVersionGreaterOrEqual(avaloniaVersion, existingVersion))
            {
                Console.Error.WriteLine(
                    $"[AXSG-LS] Tier-1 cache: skipping write for {avaloniaVersion} (cache has {existingVersion}).");
                return;
            }

            var index = AvaloniaTypeIndex.Create(compilation);
            var exported = index.ExportXmlNamespaceTypes(new[] { AvaloniaXmlNamespace });

            if (!exported.TryGetValue(AvaloniaXmlNamespace, out var avaloniaTypes) || avaloniaTypes.IsDefaultOrEmpty)
            {
                Console.Error.WriteLine("[AXSG-LS] Tier-1 cache: no Avalonia types to persist.");
                return;
            }

            var referencePaths = compilation.References
                .OfType<PortableExecutableReference>()
                .Select(static r => r.FilePath)
                .Where(static p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(static p => p!)
                .ToArray();

            var payload = new Tier1CachePayload
            {
                Version = 1,
                AvaloniaVersion = avaloniaVersion,
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                CachedReferencePaths = referencePaths,
                XmlNamespaces = new[]
                {
                    new CachedNamespace
                    {
                        XmlNamespace = AvaloniaXmlNamespace,
                        Types = avaloniaTypes.Select(ToCachedType).ToArray()
                    }
                }
            };

            var filePath = GetCacheFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            File.WriteAllText(filePath, JsonSerializer.Serialize(payload, CacheJsonOptions), Encoding.UTF8);

            Console.Error.WriteLine(
                $"[AXSG-LS] Tier-1 cache persisted ({avaloniaTypes.Length} types) for Avalonia {avaloniaVersion} at {filePath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AXSG-LS] Tier-1 cache persist failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the Avalonia version stored in the cache file, or null if the file doesn't exist.
    /// </summary>
    private static string? GetCachedAvaloniaVersion()
    {
        try
        {
            var filePath = GetCacheFilePath();
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<Tier1CachePayload>(json, CacheJsonOptions);
            return payload?.AvaloniaVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Tries to load cached type index from disk (based on reference path hash).
    /// Returns null on any failure; does not throw.
    /// </summary>
    private static string? TryPrimeTypeIndexFromDisk(Compilation compilation, IReadOnlyList<string> referencePaths)
    {
        try
        {
            var filePath = GetCacheFilePath();
            if (!File.Exists(filePath))
            {
                return "[AXSG-LS] Tier-1 type index cache miss: no cache file.";
            }

            var json = File.ReadAllText(filePath);
            var payload = JsonSerializer.Deserialize<Tier1CachePayload>(json, CacheJsonOptions);
            if (payload?.Version != 1)
            {
                return "[AXSG-LS] Tier-1 type index cache miss: version mismatch.";
            }

            // Validate that reference paths match (if cached)
            var cachedRefs = (payload.CachedReferencePaths ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentRefs = referencePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!cachedRefs.SetEquals(currentRefs))
            {
                return "[AXSG-LS] Tier-1 type index cache miss: reference paths changed.";
            }

            var mapBuilder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<AvaloniaTypeInfo>>(StringComparer.Ordinal);
            foreach (var ns in payload.XmlNamespaces ?? Array.Empty<CachedNamespace>())
            {
                if (string.IsNullOrWhiteSpace(ns.XmlNamespace))
                {
                    continue;
                }

                var primeTypes = (ns.Types ?? Array.Empty<CachedType>())
                    .Select(t => new AvaloniaTypeInfo(
                        XmlTypeName: t.XmlTypeName ?? string.Empty,
                        FullTypeName: t.FullTypeName ?? string.Empty,
                        XmlNamespace: ns.XmlNamespace!,
                        ClrNamespace: t.ClrNamespace ?? string.Empty,
                        AssemblyName: t.AssemblyName ?? string.Empty,
                        Properties: (t.Properties ?? Array.Empty<CachedProperty>())
                            .Select(p => new AvaloniaPropertyInfo(
                                Name: p.Name ?? string.Empty,
                                TypeName: p.TypeName ?? string.Empty,
                                IsSettable: p.IsSettable,
                                IsAttached: p.IsAttached,
                                SourceLocation: null))
                            .ToImmutableArray(),
                        Summary: t.Summary ?? string.Empty,
                        SourceLocation: null,
                        PseudoClasses: ImmutableArray<AvaloniaPseudoClassInfo>.Empty))
                    .ToImmutableArray();

                if (!primeTypes.IsDefaultOrEmpty)
                {
                    mapBuilder[ns.XmlNamespace!] = primeTypes;
                }
            }

            var map = mapBuilder.ToImmutable();
            if (map.IsEmpty)
            {
                return "[AXSG-LS] Tier-1 type index cache miss: no usable type data.";
            }

            AvaloniaTypeIndex.TryPrimeCache(compilation, map);
            var count = map.TryGetValue(AvaloniaXmlNamespace, out var avaloniaTypes) ? avaloniaTypes.Length : 0;
            return $"[AXSG-LS] Tier-1 type index cache hit: loaded {count} Avalonia types from disk.";
        }
        catch (Exception ex)
        {
            return $"[AXSG-LS] Tier-1 type index cache read failed: {ex.Message}";
        }
    }

    private static string GetCacheFilePath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "LeXtudio", "axaml-ls");
        return Path.Combine(root, "tier1-cache.json");
    }

    private static CachedType ToCachedType(AvaloniaTypeInfo typeInfo)
    {
        return new CachedType
        {
            XmlTypeName = typeInfo.XmlTypeName,
            FullTypeName = typeInfo.FullTypeName,
            ClrNamespace = typeInfo.ClrNamespace,
            AssemblyName = typeInfo.AssemblyName,
            Summary = typeInfo.Summary,
            Properties = typeInfo.Properties
                .Select(p => new CachedProperty
                {
                    Name = p.Name,
                    TypeName = p.TypeName,
                    IsSettable = p.IsSettable,
                    IsAttached = p.IsAttached,
                })
                .ToArray()
        };
    }

    /// <summary>
    /// Returns true if newVersion >= cachedVersion, using semantic versioning.
    /// Returns true if either version cannot be parsed (fail-safe to permit upgrade).
    /// </summary>
    private static bool IsVersionGreaterOrEqual(string newVersion, string cachedVersion)
    {
        try
        {
            return Version.TryParse(newVersion, out var nv) && Version.TryParse(cachedVersion, out var cv)
                ? nv >= cv
                : true;
        }
        catch
        {
            return true;
        }
    }

    // =========================================================================
    // Cache payload classes (same schema as WPF)
    // =========================================================================

    private sealed class Tier1CachePayload
    {
        public int Version { get; set; }
        public string? AvaloniaVersion { get; set; }
        public string? CreatedUtc { get; set; }
        public string[]? CachedReferencePaths { get; set; }
        public CachedNamespace[]? XmlNamespaces { get; set; }
    }

    private sealed class CachedNamespace
    {
        public string? XmlNamespace { get; set; }
        public CachedType[]? Types { get; set; }
    }

    private sealed class CachedType
    {
        public string? XmlTypeName { get; set; }
        public string? FullTypeName { get; set; }
        public string? ClrNamespace { get; set; }
        public string? AssemblyName { get; set; }
        public string? Summary { get; set; }
        public CachedProperty[]? Properties { get; set; }
    }

    private sealed class CachedProperty
    {
        public string? Name { get; set; }
        public string? TypeName { get; set; }
        public bool IsSettable { get; set; }
        public bool IsAttached { get; set; }
    }
}
