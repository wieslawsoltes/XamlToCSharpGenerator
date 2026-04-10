using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class TypeResolutionNamespaceDiscoveryService
{
    private readonly ImmutableArray<string> _frameworkDefaultNamespaceCandidateSeed;
    private readonly Func<INamedTypeSymbol?, bool> _isXmlnsDefinitionAttribute;
    private readonly Func<INamedTypeSymbol?, bool> _isFrameworkXmlnsDefinitionAttribute;
    private readonly Func<string, bool> _isFrameworkDefaultXmlNamespace;
    private readonly Func<string, string> _normalizeXmlNamespaceKey;
    private readonly Func<Compilation, INamedTypeSymbol, bool> _isAccessibleTypeCandidate;

    private readonly ConditionalWeakTable<Compilation, NamespaceCandidateCacheEntry> _namespaceCandidateCache = new();
    private readonly ConditionalWeakTable<Compilation, XmlnsDefinitionCacheEntry> _xmlnsDefinitionCache = new();
    private readonly ConditionalWeakTable<Compilation, SourceAssemblyNamespaceCacheEntry> _sourceAssemblyNamespaceCache = new();

    public TypeResolutionNamespaceDiscoveryService(
        IEnumerable<string> frameworkDefaultNamespaceCandidateSeed,
        Func<INamedTypeSymbol?, bool> isXmlnsDefinitionAttribute,
        Func<INamedTypeSymbol?, bool> isFrameworkXmlnsDefinitionAttribute,
        Func<string, bool> isFrameworkDefaultXmlNamespace,
        Func<string, string> normalizeXmlNamespaceKey,
        Func<Compilation, INamedTypeSymbol, bool> isAccessibleTypeCandidate)
    {
        _frameworkDefaultNamespaceCandidateSeed = frameworkDefaultNamespaceCandidateSeed is null
            ? ImmutableArray<string>.Empty
            : ImmutableArray.CreateRange(frameworkDefaultNamespaceCandidateSeed);
        _isXmlnsDefinitionAttribute = isXmlnsDefinitionAttribute ?? throw new ArgumentNullException(nameof(isXmlnsDefinitionAttribute));
        _isFrameworkXmlnsDefinitionAttribute = isFrameworkXmlnsDefinitionAttribute ?? throw new ArgumentNullException(nameof(isFrameworkXmlnsDefinitionAttribute));
        _isFrameworkDefaultXmlNamespace = isFrameworkDefaultXmlNamespace ?? throw new ArgumentNullException(nameof(isFrameworkDefaultXmlNamespace));
        _normalizeXmlNamespaceKey = normalizeXmlNamespaceKey ?? throw new ArgumentNullException(nameof(normalizeXmlNamespaceKey));
        _isAccessibleTypeCandidate = isAccessibleTypeCandidate ?? throw new ArgumentNullException(nameof(isAccessibleTypeCandidate));
    }

    public ImmutableArray<string> GetFrameworkDefaultNamespaceCandidates(Compilation compilation)
    {
        return _namespaceCandidateCache.GetValue(
            compilation,
            currentCompilation => BuildNamespaceCandidateCacheEntry(currentCompilation)).FrameworkDefaultNamespaceCandidates;
    }

    public ImmutableArray<string> GetProjectNamespaceCandidates(Compilation compilation, string rootNamespace)
    {
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            return ImmutableArray<string>.Empty;
        }

        var normalizedRootNamespace = rootNamespace.Trim();
        var sourceNamespaces = _sourceAssemblyNamespaceCache.GetValue(
            compilation,
            currentCompilation => BuildSourceAssemblyNamespaceCacheEntry(currentCompilation)).SourceAssemblyNamespaces;
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddNamespaceCandidate(builder, seen, normalizedRootNamespace);
        foreach (var sourceNamespace in sourceNamespaces)
        {
            if (!sourceNamespace.StartsWith(normalizedRootNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            AddNamespaceCandidate(builder, seen, sourceNamespace);
        }

        return builder.ToImmutable();
    }

    public ImmutableArray<XmlnsDefinitionTarget> GetXmlnsDefinitionTargetsForXmlNamespace(Compilation compilation, string xmlNamespace)
    {
        var normalizedKey = _normalizeXmlNamespaceKey(xmlNamespace);
        var cacheEntry = _xmlnsDefinitionCache.GetValue(
            compilation,
            currentCompilation => BuildXmlnsDefinitionCacheEntry(currentCompilation));
        return cacheEntry.TargetsByXmlNamespace.TryGetValue(normalizedKey, out var targets)
            ? targets
            : ImmutableArray<XmlnsDefinitionTarget>.Empty;
    }

    public ImmutableArray<INamedTypeSymbol> CollectTypeCandidatesFromXmlnsDefinitionTargets(
        Compilation compilation,
        ImmutableArray<XmlnsDefinitionTarget> targets,
        string typeName,
        int? genericArity = null,
        bool extensionSuffix = false)
    {
        if (targets.IsDefaultOrEmpty || string.IsNullOrWhiteSpace(typeName))
        {
            return ImmutableArray<INamedTypeSymbol>.Empty;
        }

        var effectiveTypeName = extensionSuffix
            ? typeName + "Extension"
            : DeterministicTypeResolutionSemantics.AppendGenericArity(typeName, genericArity);
        var candidates = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var assemblies = EnumerateAssemblies(compilation).ToImmutableArray();

        foreach (var target in targets)
        {
            var metadataName = target.ClrNamespace + "." + effectiveTypeName;
            foreach (var assembly in ResolveTargetAssemblies(assemblies, target))
            {
                var candidate = assembly.GetTypeByMetadataName(metadataName);
                if (candidate is null || !_isAccessibleTypeCandidate(compilation, candidate))
                {
                    continue;
                }

                var key = (candidate.ContainingAssembly?.Identity.ToString() ?? string.Empty) + "|" +
                          candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!seenKeys.Add(key))
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }

        return candidates.ToImmutable();
    }

    public static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var seen = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);
        if (seen.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }

        foreach (var referencedAssembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (referencedAssembly is not null && seen.Add(referencedAssembly))
            {
                yield return referencedAssembly;
            }
        }
    }

    private NamespaceCandidateCacheEntry BuildNamespaceCandidateCacheEntry(Compilation compilation)
    {
        var sourceAssemblyNamespaces = _sourceAssemblyNamespaceCache.GetValue(
            compilation,
            currentCompilation => BuildSourceAssemblyNamespaceCacheEntry(currentCompilation)).SourceAssemblyNamespaces;
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in _frameworkDefaultNamespaceCandidateSeed)
        {
            AddNamespaceCandidate(builder, seen, candidate);
        }

        foreach (var candidate in sourceAssemblyNamespaces)
        {
            AddNamespaceCandidate(builder, seen, candidate);
        }

        return new NamespaceCandidateCacheEntry(builder.ToImmutable());
    }

    private XmlnsDefinitionCacheEntry BuildXmlnsDefinitionCacheEntry(Compilation compilation)
    {
        var targetsByXmlNamespace = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XmlnsDefinitionTarget>.Builder>(StringComparer.Ordinal);
        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!_isXmlnsDefinitionAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string clrNamespace)
                {
                    continue;
                }

                var normalizedXmlNamespace = _normalizeXmlNamespaceKey(xmlNamespace);
                if (!targetsByXmlNamespace.TryGetValue(normalizedXmlNamespace, out var targets))
                {
                    targets = ImmutableArray.CreateBuilder<XmlnsDefinitionTarget>();
                    targetsByXmlNamespace[normalizedXmlNamespace] = targets;
                }

                // XmlnsDefinition maps an XML namespace to a CLR namespace, but the
                // type can live in a different referenced assembly than the assembly
                // that contributes the attribute metadata. Restricting resolution to
                // the attribute assembly breaks bridge-style metadata assemblies.
                targets.Add(new XmlnsDefinitionTarget(
                    normalizedXmlNamespace,
                    clrNamespace.Trim(),
                    AssemblyName: null,
                    _isFrameworkXmlnsDefinitionAttribute(attribute.AttributeClass)));
            }
        }

        var result = ImmutableDictionary.CreateBuilder<string, ImmutableArray<XmlnsDefinitionTarget>>(StringComparer.Ordinal);
        foreach (var pair in targetsByXmlNamespace)
        {
            result[pair.Key] = pair.Value.ToImmutable();
        }

        return new XmlnsDefinitionCacheEntry(result.ToImmutable());
    }

    private SourceAssemblyNamespaceCacheEntry BuildSourceAssemblyNamespaceCacheEntry(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectSourceNamespaces(compilation.Assembly.GlobalNamespace, builder, seen);
        return new SourceAssemblyNamespaceCacheEntry(builder.ToImmutable());
    }

    private static void CollectSourceNamespaces(
        INamespaceSymbol currentNamespace,
        ImmutableArray<string>.Builder namespaces,
        HashSet<string> seen)
    {
        foreach (var namespaceMember in currentNamespace.GetNamespaceMembers())
        {
            if (!namespaceMember.IsGlobalNamespace &&
                seen.Add(namespaceMember.ToDisplayString()))
            {
                namespaces.Add(namespaceMember.ToDisplayString());
            }

            CollectSourceNamespaces(namespaceMember, namespaces, seen);
        }
    }

    private static void AddNamespaceCandidate(
        ImmutableArray<string>.Builder builder,
        HashSet<string> seen,
        string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return;
        }

        var normalized = candidate!.Trim();
        if (!normalized.EndsWith(".", StringComparison.Ordinal))
        {
            normalized += ".";
        }

        if (seen.Add(normalized))
        {
            builder.Add(normalized);
        }
    }

    private IEnumerable<IAssemblySymbol> ResolveTargetAssemblies(
        ImmutableArray<IAssemblySymbol> assemblies,
        XmlnsDefinitionTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.AssemblyName))
        {
            foreach (var assembly in assemblies)
            {
                if (string.Equals(assembly.Name, target.AssemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return assembly;
                }
            }

            yield break;
        }

        foreach (var assembly in assemblies)
        {
            yield return assembly;
        }
    }

    private sealed class NamespaceCandidateCacheEntry
    {
        public NamespaceCandidateCacheEntry(ImmutableArray<string> frameworkDefaultNamespaceCandidates)
        {
            FrameworkDefaultNamespaceCandidates = frameworkDefaultNamespaceCandidates;
        }

        public ImmutableArray<string> FrameworkDefaultNamespaceCandidates { get; }
    }

    private sealed class XmlnsDefinitionCacheEntry
    {
        public XmlnsDefinitionCacheEntry(ImmutableDictionary<string, ImmutableArray<XmlnsDefinitionTarget>> targetsByXmlNamespace)
        {
            TargetsByXmlNamespace = targetsByXmlNamespace;
        }

        public ImmutableDictionary<string, ImmutableArray<XmlnsDefinitionTarget>> TargetsByXmlNamespace { get; }
    }

    private sealed class SourceAssemblyNamespaceCacheEntry
    {
        public SourceAssemblyNamespaceCacheEntry(ImmutableArray<string> sourceAssemblyNamespaces)
        {
            SourceAssemblyNamespaces = sourceAssemblyNamespaces;
        }

        public ImmutableArray<string> SourceAssemblyNamespaces { get; }
    }
}
