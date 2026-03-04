using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Symbols;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlSourceLinkResolver
{
    private static readonly Guid SourceLinkCustomDebugInfoGuid = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly ConcurrentDictionary<TypeLocationCacheKey, CachedSourceLookupResult> TypeLocationCache = new();
    private static readonly ConcurrentDictionary<PropertyLocationCacheKey, CachedSourceLookupResult> PropertyLocationCache = new();

    public static bool TryResolveTypeLocation(
        XamlAnalysisResult analysis,
        string fullTypeName,
        string? assemblyName,
        out AvaloniaSymbolSourceLocation sourceLocation)
    {
        if (analysis.Compilation is null ||
            string.IsNullOrWhiteSpace(assemblyName) ||
            !TryGetAssemblyPath(analysis.Compilation, assemblyName, out var assemblyPath))
        {
            sourceLocation = default;
            return false;
        }

        var cacheKey = new TypeLocationCacheKey(
            AssemblyPath: Path.GetFullPath(assemblyPath),
            FullTypeName: NormalizeTypeName(fullTypeName));
        if (TypeLocationCache.TryGetValue(cacheKey, out var cached))
        {
            sourceLocation = cached.Location;
            return cached.Found;
        }

        var found = TryResolveTypeLocationCore(assemblyPath, fullTypeName, out sourceLocation);
        TypeLocationCache[cacheKey] = new CachedSourceLookupResult(found, sourceLocation);
        return found;
    }

    private static bool TryResolveTypeLocationCore(
        string assemblyPath,
        string fullTypeName,
        out AvaloniaSymbolSourceLocation sourceLocation)
    {
        sourceLocation = default;
        using var assemblyStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(assemblyStream);
        if (!peReader.HasMetadata)
        {
            return false;
        }

        var metadataReader = peReader.GetMetadataReader();
        if (!TryResolveTypeDefinitionHandle(metadataReader, fullTypeName, out var typeHandle) ||
            !TryGetPortablePdbReader(assemblyPath, peReader, out var pdbProvider, out var pdbReader))
        {
            return false;
        }

        using (pdbProvider)
        {
            if (!TryResolveTypeSequencePoint(pdbReader, metadataReader, typeHandle, out var path, out var line))
            {
                return false;
            }

            TryReadSourceLinkMappings(pdbReader, out var sourceLinkMappings);
            if (!TryResolveSourceUri(sourceLinkMappings, path, out var uri))
            {
                return false;
            }

            var lineIndex = Math.Max(0, line - 1);
            sourceLocation = new AvaloniaSymbolSourceLocation(
                uri,
                new SourceRange(
                    new SourcePosition(lineIndex, 0),
                    new SourcePosition(lineIndex, 1)));
            return true;
        }
    }

    public static bool TryResolvePropertyLocation(
        XamlAnalysisResult analysis,
        string ownerTypeName,
        string propertyName,
        string? assemblyName,
        out AvaloniaSymbolSourceLocation sourceLocation)
    {
        if (analysis.Compilation is null ||
            string.IsNullOrWhiteSpace(assemblyName) ||
            !TryGetAssemblyPath(analysis.Compilation, assemblyName, out var assemblyPath))
        {
            sourceLocation = default;
            return false;
        }

        var cacheKey = new PropertyLocationCacheKey(
            AssemblyPath: Path.GetFullPath(assemblyPath),
            OwnerTypeName: NormalizeTypeName(ownerTypeName),
            PropertyName: propertyName);
        if (PropertyLocationCache.TryGetValue(cacheKey, out var cached))
        {
            sourceLocation = cached.Location;
            return cached.Found;
        }

        var found = TryResolvePropertyLocationCore(assemblyPath, ownerTypeName, propertyName, out sourceLocation);
        PropertyLocationCache[cacheKey] = new CachedSourceLookupResult(found, sourceLocation);
        return found;
    }

    private static bool TryResolvePropertyLocationCore(
        string assemblyPath,
        string ownerTypeName,
        string propertyName,
        out AvaloniaSymbolSourceLocation sourceLocation)
    {
        sourceLocation = default;
        using var assemblyStream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(assemblyStream);
        if (!peReader.HasMetadata)
        {
            return false;
        }

        var metadataReader = peReader.GetMetadataReader();
        if (!TryResolveTypeDefinitionHandle(metadataReader, ownerTypeName, out var typeHandle) ||
            !TryGetPortablePdbReader(assemblyPath, peReader, out var pdbProvider, out var pdbReader))
        {
            return false;
        }

        using (pdbProvider)
        {
            if (!TryResolvePropertySequencePoint(
                    pdbReader,
                    metadataReader,
                    typeHandle,
                    propertyName,
                    out var path,
                    out var line))
            {
                return false;
            }

            TryReadSourceLinkMappings(pdbReader, out var sourceLinkMappings);
            if (!TryResolveSourceUri(sourceLinkMappings, path, out var uri))
            {
                return false;
            }

            var lineIndex = Math.Max(0, line - 1);
            sourceLocation = new AvaloniaSymbolSourceLocation(
                uri,
                new SourceRange(
                    new SourcePosition(lineIndex, 0),
                    new SourcePosition(lineIndex, 1)));
            return true;
        }
    }

    private static bool TryGetAssemblyPath(Compilation compilation, string assemblyName, out string assemblyPath)
    {
        assemblyPath = string.Empty;
        foreach (var reference in compilation.References)
        {
            if (reference is not PortableExecutableReference peReference)
            {
                continue;
            }

            var symbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (symbol is null ||
                !string.Equals(symbol.Identity.Name, assemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            var path = peReference.FilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                continue;
            }

            assemblyPath = ResolveImplementationAssemblyPath(path);
            return true;
        }

        return false;
    }

    private static string ResolveImplementationAssemblyPath(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            return assemblyPath;
        }

        try
        {
            var candidatePath = Path.GetFullPath(assemblyPath);
            var directorySeparator = Path.DirectorySeparatorChar;
            var altSeparator = Path.AltDirectorySeparatorChar;
            var separators = new[] { directorySeparator, altSeparator };

            foreach (var separator in separators)
            {
                var marker = $"{separator}ref{separator}";
                var markerIndex = candidatePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex < 0)
                {
                    continue;
                }

                var implementationPath = candidatePath.Remove(markerIndex, marker.Length)
                    .Insert(markerIndex, $"{separator}lib{separator}");
                if (File.Exists(implementationPath))
                {
                    return implementationPath;
                }
            }

            return candidatePath;
        }
        catch
        {
            return assemblyPath;
        }
    }

    private static bool TryGetPortablePdbReader(
        string assemblyPath,
        PEReader peReader,
        out MetadataReaderProvider provider,
        out MetadataReader metadataReader)
    {
        provider = null!;
        metadataReader = default;

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        if (File.Exists(pdbPath))
        {
            provider = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
            metadataReader = provider.GetMetadataReader();
            return true;
        }

        foreach (var debugEntry in peReader.ReadDebugDirectory())
        {
            if (debugEntry.Type != DebugDirectoryEntryType.EmbeddedPortablePdb)
            {
                continue;
            }

            provider = peReader.ReadEmbeddedPortablePdbDebugDirectoryData(debugEntry);
            metadataReader = provider.GetMetadataReader();
            return true;
        }

        return false;
    }

    private static bool TryResolveTypeDefinitionHandle(
        MetadataReader metadataReader,
        string fullTypeName,
        out TypeDefinitionHandle typeHandle)
    {
        typeHandle = default;
        var targetName = NormalizeTypeName(fullTypeName);
        foreach (var candidate in metadataReader.TypeDefinitions)
        {
            if (!string.Equals(
                    GetNormalizedFullTypeName(metadataReader, candidate),
                    targetName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            typeHandle = candidate;
            return true;
        }

        return false;
    }

    private static bool TryResolveTypeSequencePoint(
        MetadataReader pdbReader,
        MetadataReader metadataReader,
        TypeDefinitionHandle typeHandle,
        out string documentPath,
        out int line)
    {
        documentPath = string.Empty;
        line = 0;
        var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            if (!TryResolveMethodSequencePoint(pdbReader, methodHandle, out var path, out var candidateLine))
            {
                continue;
            }

            documentPath = path;
            line = candidateLine;
            return true;
        }

        return false;
    }

    private static bool TryResolvePropertySequencePoint(
        MetadataReader pdbReader,
        MetadataReader metadataReader,
        TypeDefinitionHandle typeHandle,
        string propertyName,
        out string documentPath,
        out int line)
    {
        documentPath = string.Empty;
        line = 0;
        var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
        var getAccessorName = "get_" + propertyName;
        var setAccessorName = "set_" + propertyName;

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);
            var name = metadataReader.GetString(methodDefinition.Name);
            if (!string.Equals(name, getAccessorName, StringComparison.Ordinal) &&
                !string.Equals(name, setAccessorName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryResolveMethodSequencePoint(pdbReader, methodHandle, out var path, out var candidateLine))
            {
                continue;
            }

            documentPath = path;
            line = candidateLine;
            return true;
        }

        return TryResolveTypeSequencePoint(pdbReader, metadataReader, typeHandle, out documentPath, out line);
    }

    private static bool TryResolveMethodSequencePoint(
        MetadataReader pdbReader,
        MethodDefinitionHandle methodHandle,
        out string documentPath,
        out int line)
    {
        documentPath = string.Empty;
        line = 0;

        var methodDebugInformation = pdbReader.GetMethodDebugInformation(methodHandle.ToDebugInformationHandle());
        var fallbackDocument = methodDebugInformation.Document;
        foreach (var sequencePoint in methodDebugInformation.GetSequencePoints())
        {
            if (sequencePoint.IsHidden || sequencePoint.StartLine == 0)
            {
                continue;
            }

            var documentHandle = sequencePoint.Document.IsNil
                ? fallbackDocument
                : sequencePoint.Document;
            if (documentHandle.IsNil ||
                !TryReadDocumentPath(pdbReader, documentHandle, out documentPath))
            {
                continue;
            }

            line = sequencePoint.StartLine;
            return true;
        }

        return false;
    }

    private static bool TryReadDocumentPath(MetadataReader pdbReader, DocumentHandle documentHandle, out string path)
    {
        path = string.Empty;
        var document = pdbReader.GetDocument(documentHandle);
        var blobReader = pdbReader.GetBlobReader(document.Name);
        if (blobReader.RemainingBytes == 0)
        {
            return false;
        }

        var separator = (char)blobReader.ReadByte();
        var builder = new StringBuilder();
        while (blobReader.RemainingBytes > 0)
        {
            var partHandle = blobReader.ReadBlobHandle();
            var partReader = pdbReader.GetBlobReader(partHandle);
            var part = partReader.ReadUTF8(partReader.RemainingBytes);
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(part);
        }

        if (builder.Length == 0)
        {
            return false;
        }

        path = builder.ToString();
        return true;
    }

    private static bool TryReadSourceLinkMappings(MetadataReader pdbReader, out List<SourceLinkMapping> mappings)
    {
        mappings = new List<SourceLinkMapping>();
        string? json = null;
        foreach (var debugHandle in pdbReader.CustomDebugInformation)
        {
            var debugInfo = pdbReader.GetCustomDebugInformation(debugHandle);
            if (pdbReader.GetGuid(debugInfo.Kind) != SourceLinkCustomDebugInfoGuid)
            {
                continue;
            }

            var blobReader = pdbReader.GetBlobReader(debugInfo.Value);
            json = blobReader.ReadUTF8(blobReader.RemainingBytes);
            break;
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("documents", out var documents) ||
                documents.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in documents.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var urlTemplate = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(urlTemplate))
                {
                    continue;
                }

                mappings.Add(new SourceLinkMapping(
                    NormalizePath(property.Name),
                    urlTemplate));
            }

            return mappings.Count > 0;
        }
        catch
        {
            mappings.Clear();
            return false;
        }
    }

    private static bool TryResolveSourceUri(
        IReadOnlyList<SourceLinkMapping>? mappings,
        string documentPath,
        out string uri)
    {
        uri = string.Empty;
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return false;
        }

        var normalizedPath = NormalizePath(documentPath);
        if (mappings is not null)
        {
            foreach (var mapping in mappings)
            {
                if (!TryApplySourceLinkMapping(mapping, normalizedPath, out var sourceUrl))
                {
                    continue;
                }

                uri = XamlMetadataSymbolUri.CreateSourceLinkUri(sourceUrl);
                return true;
            }
        }

        if (File.Exists(documentPath))
        {
            uri = UriPathHelper.ToDocumentUri(documentPath);
            return true;
        }

        return false;
    }

    private static bool TryApplySourceLinkMapping(SourceLinkMapping mapping, string normalizedPath, out string sourceUrl)
    {
        sourceUrl = string.Empty;
        var wildcardIndex = mapping.DocumentPattern.IndexOf('*');
        if (wildcardIndex < 0)
        {
            if (!string.Equals(mapping.DocumentPattern, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            sourceUrl = mapping.UrlTemplate;
            return true;
        }

        var prefix = mapping.DocumentPattern.Substring(0, wildcardIndex);
        var suffix = wildcardIndex + 1 < mapping.DocumentPattern.Length
            ? mapping.DocumentPattern.Substring(wildcardIndex + 1)
            : string.Empty;
        if (!normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Length < prefix.Length + suffix.Length)
        {
            return false;
        }

        var wildcardValue = normalizedPath.Substring(
            prefix.Length,
            normalizedPath.Length - prefix.Length - suffix.Length);
        sourceUrl = mapping.UrlTemplate.Replace("*", wildcardValue, StringComparison.Ordinal);
        return true;
    }

    private static string GetNormalizedFullTypeName(MetadataReader metadataReader, TypeDefinitionHandle handle)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(handle);
        var localName = StripGenericArity(metadataReader.GetString(typeDefinition.Name));
        if (typeDefinition.GetDeclaringType().IsNil)
        {
            var ns = metadataReader.GetString(typeDefinition.Namespace);
            return string.IsNullOrWhiteSpace(ns)
                ? localName
                : ns + "." + localName;
        }

        return GetNormalizedFullTypeName(metadataReader, typeDefinition.GetDeclaringType()) + "." + localName;
    }

    private static string StripGenericArity(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return string.Empty;
        }

        var index = typeName.IndexOf('`');
        return index > 0 ? typeName.Substring(0, index) : typeName;
    }

    private static string NormalizeTypeName(string fullTypeName)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(fullTypeName.Length);
        var genericDepth = 0;
        foreach (var value in fullTypeName)
        {
            if (value == '<')
            {
                genericDepth++;
                continue;
            }

            if (value == '>')
            {
                if (genericDepth > 0)
                {
                    genericDepth--;
                }

                continue;
            }

            if (genericDepth > 0 || char.IsWhiteSpace(value))
            {
                continue;
            }

            builder.Append(value == '+' ? '.' : value);
        }

        return builder.ToString();
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private readonly record struct TypeLocationCacheKey(
        string AssemblyPath,
        string FullTypeName);

    private readonly record struct PropertyLocationCacheKey(
        string AssemblyPath,
        string OwnerTypeName,
        string PropertyName);

    private readonly record struct CachedSourceLookupResult(
        bool Found,
        AvaloniaSymbolSourceLocation Location);

    private readonly record struct SourceLinkMapping(string DocumentPattern, string UrlTemplate);

}
