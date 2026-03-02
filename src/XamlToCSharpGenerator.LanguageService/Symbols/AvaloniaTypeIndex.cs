using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed class AvaloniaTypeIndex
{
    private const string AvaloniaXmlnsDefinitionAttributeMetadataName = "Avalonia.Metadata.XmlnsDefinitionAttribute";
    private const string SourceGenXmlnsDefinitionAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute";
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";

    private static readonly ConditionalWeakTable<Compilation, AvaloniaTypeIndex> Cache = new();

    private readonly ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> _typesByXmlNamespace;

    private AvaloniaTypeIndex(ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> typesByXmlNamespace)
    {
        _typesByXmlNamespace = typesByXmlNamespace;
    }

    public static AvaloniaTypeIndex Create(Compilation compilation)
    {
        if (compilation is null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        return Cache.GetValue(compilation, BuildIndex);
    }

    public ImmutableArray<AvaloniaTypeInfo> GetTypes(string xmlNamespace)
    {
        if (string.IsNullOrWhiteSpace(xmlNamespace))
        {
            return ImmutableArray<AvaloniaTypeInfo>.Empty;
        }

        if (!_typesByXmlNamespace.TryGetValue(xmlNamespace, out var types))
        {
            return ImmutableArray<AvaloniaTypeInfo>.Empty;
        }

        return types.Values.ToImmutableArray();
    }

    public bool TryGetType(string xmlNamespace, string xmlTypeName, out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (string.IsNullOrWhiteSpace(xmlNamespace) || string.IsNullOrWhiteSpace(xmlTypeName))
        {
            return false;
        }

        if (!_typesByXmlNamespace.TryGetValue(xmlNamespace, out var byName))
        {
            return false;
        }

        return byName.TryGetValue(xmlTypeName, out typeInfo);
    }

    private static AvaloniaTypeIndex BuildIndex(Compilation compilation)
    {
        var map = BuildXmlNamespaceToClrNamespaceMap(compilation);
        if (!map.ContainsKey(AvaloniaDefaultXmlNamespace))
        {
            map = map.SetItem(AvaloniaDefaultXmlNamespace, BuildFallbackClrNamespaces(compilation));
        }

        var result = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, AvaloniaTypeInfo>>(StringComparer.Ordinal);

        foreach (var pair in map)
        {
            var byTypeName = ImmutableDictionary.CreateBuilder<string, AvaloniaTypeInfo>(StringComparer.Ordinal);
            foreach (var clrNamespace in pair.Value)
            {
                foreach (var type in EnumerateTypesByClrNamespace(compilation, clrNamespace))
                {
                    if (type.DeclaredAccessibility != Accessibility.Public)
                    {
                        continue;
                    }

                    if (type.IsAbstract)
                    {
                        continue;
                    }

                    var xmlTypeName = GetXamlTypeName(type);
                    if (byTypeName.ContainsKey(xmlTypeName))
                    {
                        continue;
                    }

                    byTypeName[xmlTypeName] = BuildTypeInfo(pair.Key, type);
                }
            }

            result[pair.Key] = byTypeName.ToImmutable();
        }

        return new AvaloniaTypeIndex(result.ToImmutable());
    }

    private static string GetXamlTypeName(INamedTypeSymbol type)
    {
        var tickIndex = type.Name.IndexOf('`');
        return tickIndex > 0 ? type.Name.Substring(0, tickIndex) : type.Name;
    }

    private static AvaloniaTypeInfo BuildTypeInfo(string xmlNamespace, INamedTypeSymbol type)
    {
        var properties = ImmutableArray.CreateBuilder<AvaloniaPropertyInfo>();

        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic)
            {
                continue;
            }

            if (property.IsImplicitlyDeclared)
            {
                continue;
            }

            properties.Add(new AvaloniaPropertyInfo(
                property.Name,
                property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                property.SetMethod is not null && property.SetMethod.DeclaredAccessibility == Accessibility.Public,
                IsAttached: false));
        }

        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.DeclaredAccessibility != Accessibility.Public || !field.IsStatic)
            {
                continue;
            }

            if (!field.Name.EndsWith("Property", StringComparison.Ordinal))
            {
                continue;
            }

            var propertyName = field.Name.Substring(0, field.Name.Length - "Property".Length);
            if (propertyName.Length == 0)
            {
                continue;
            }

            if (properties.Any(value => value.Name == propertyName))
            {
                continue;
            }

            properties.Add(new AvaloniaPropertyInfo(
                propertyName,
                field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                IsSettable: true,
                IsAttached: true));
        }

        var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var summary = "`" + fullTypeName + "`";

        return new AvaloniaTypeInfo(
            XmlTypeName: GetXamlTypeName(type),
            FullTypeName: fullTypeName,
            XmlNamespace: xmlNamespace,
            ClrNamespace: type.ContainingNamespace.ToDisplayString(),
            Properties: properties.OrderBy(static property => property.Name, StringComparer.Ordinal).ToImmutableArray(),
            Summary: summary);
    }

    private static ImmutableDictionary<string, ImmutableHashSet<string>> BuildXmlNamespaceToClrNamespaceMap(Compilation compilation)
    {
        var map = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>.Builder>(StringComparer.Ordinal);

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                var attributeMetadataName = attribute.AttributeClass?.ToDisplayString();
                if (!string.Equals(attributeMetadataName, AvaloniaXmlnsDefinitionAttributeMetadataName, StringComparison.Ordinal) &&
                    !string.Equals(attributeMetadataName, SourceGenXmlnsDefinitionAttributeMetadataName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 2)
                {
                    continue;
                }

                if (attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    string.IsNullOrWhiteSpace(xmlNamespace) ||
                    attribute.ConstructorArguments[1].Value is not string clrNamespace ||
                    string.IsNullOrWhiteSpace(clrNamespace))
                {
                    continue;
                }

                if (!map.TryGetValue(xmlNamespace, out var setBuilder))
                {
                    setBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
                    map[xmlNamespace] = setBuilder;
                }

                setBuilder.Add(clrNamespace);
            }
        }

        var immutableMap = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in map)
        {
            immutableMap[entry.Key] = entry.Value.ToImmutable();
        }

        return immutableMap.ToImmutable();
    }

    private static ImmutableHashSet<string> BuildFallbackClrNamespaces(Compilation compilation)
    {
        var namespaces = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var type in EnumerateTypes(compilation.Assembly.GlobalNamespace))
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var ns = type.ContainingNamespace.ToDisplayString();
            if (string.IsNullOrWhiteSpace(ns))
            {
                continue;
            }

            namespaces.Add(ns);
        }

        return namespaces.ToImmutable();
    }

    private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
    {
        var visited = new HashSet<IAssemblySymbol>(SymbolEqualityComparer.Default);

        if (visited.Add(compilation.Assembly))
        {
            yield return compilation.Assembly;
        }

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            if (assembly is not null && visited.Add(assembly))
            {
                yield return assembly;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypesByClrNamespace(Compilation compilation, string clrNamespace)
    {
        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                if (string.Equals(
                        type.ContainingNamespace.ToDisplayString(),
                        clrNamespace,
                        StringComparison.Ordinal))
                {
                    yield return type;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol @namespace)
    {
        foreach (var namespaceMember in @namespace.GetNamespaceMembers())
        {
            foreach (var nested in EnumerateTypes(namespaceMember))
            {
                yield return nested;
            }
        }

        foreach (var type in @namespace.GetTypeMembers())
        {
            yield return type;

            foreach (var nested in EnumerateNestedTypes(type))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateNestedTypes(INamedTypeSymbol type)
    {
        foreach (var nested in type.GetTypeMembers())
        {
            yield return nested;
            foreach (var deepNested in EnumerateNestedTypes(nested))
            {
                yield return deepNested;
            }
        }
    }
}
