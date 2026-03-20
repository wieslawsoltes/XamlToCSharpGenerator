using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.Symbols;

public sealed class AvaloniaTypeIndex
{
    private const string AvaloniaXmlnsDefinitionAttributeMetadataName = "Avalonia.Metadata.XmlnsDefinitionAttribute";
    private const string SourceGenXmlnsDefinitionAttributeMetadataName = "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute";
    private const string AvaloniaPseudoClassesAttributeMetadataName = "Avalonia.Controls.Metadata.PseudoClassesAttribute";
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";

    private static readonly ConditionalWeakTable<Compilation, AvaloniaTypeIndex> Cache = new();

    private readonly ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> _typesByXmlNamespace;
    private readonly ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> _typesByClrNamespace;
    private readonly ImmutableDictionary<string, AvaloniaTypeInfo> _typesByFullTypeName;

    private AvaloniaTypeIndex(
        ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> typesByXmlNamespace,
        ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>> typesByClrNamespace,
        ImmutableDictionary<string, AvaloniaTypeInfo> typesByFullTypeName)
    {
        _typesByXmlNamespace = typesByXmlNamespace;
        _typesByClrNamespace = typesByClrNamespace;
        _typesByFullTypeName = typesByFullTypeName;
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

    public bool TryGetTypeByClrNamespace(string clrNamespace, string xmlTypeName, out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (string.IsNullOrWhiteSpace(clrNamespace) || string.IsNullOrWhiteSpace(xmlTypeName))
        {
            return false;
        }

        if (!_typesByClrNamespace.TryGetValue(clrNamespace, out var byName))
        {
            return false;
        }

        return byName.TryGetValue(xmlTypeName, out typeInfo);
    }

    public bool TryGetTypeByFullTypeName(string fullTypeName, out AvaloniaTypeInfo? typeInfo)
    {
        typeInfo = null;
        if (string.IsNullOrWhiteSpace(fullTypeName))
        {
            return false;
        }

        return _typesByFullTypeName.TryGetValue(fullTypeName, out typeInfo);
    }

    public ImmutableArray<AvaloniaTypeInfo> FindTypesByXmlTypeName(string xmlTypeName)
    {
        if (string.IsNullOrWhiteSpace(xmlTypeName))
        {
            return ImmutableArray<AvaloniaTypeInfo>.Empty;
        }

        var matches = ImmutableArray.CreateBuilder<AvaloniaTypeInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var byName in _typesByXmlNamespace.Values)
        {
            if (!byName.TryGetValue(xmlTypeName, out var typeInfo) ||
                typeInfo is null)
            {
                continue;
            }

            var key = typeInfo.FullTypeName + "\0" + typeInfo.XmlNamespace;
            if (seen.Add(key))
            {
                matches.Add(typeInfo);
            }
        }

        return matches.ToImmutable();
    }

    private static AvaloniaTypeIndex BuildIndex(Compilation compilation)
    {
        var map = BuildXmlNamespaceToClrNamespaceMap(compilation);
        var fallbackNamespaces = BuildFallbackClrNamespaces(compilation);
        if (map.TryGetValue(AvaloniaDefaultXmlNamespace, out var defaultNamespaces))
        {
            map = map.SetItem(AvaloniaDefaultXmlNamespace, defaultNamespaces.Union(fallbackNamespaces));
        }
        else
        {
            map = map.SetItem(AvaloniaDefaultXmlNamespace, fallbackNamespaces);
        }

        var result = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, AvaloniaTypeInfo>>(StringComparer.Ordinal);
        var byClrNamespace = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, AvaloniaTypeInfo>>(StringComparer.Ordinal);
        var byFullTypeName = ImmutableDictionary.CreateBuilder<string, AvaloniaTypeInfo>(StringComparer.Ordinal);

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
                    var typeInfo = BuildTypeInfo(compilation, pair.Key, type);
                    AddTypeByClrNamespace(byClrNamespace, byFullTypeName, typeInfo);

                    if (byTypeName.ContainsKey(xmlTypeName))
                    {
                        continue;
                    }

                    byTypeName[xmlTypeName] = typeInfo;
                }
            }

            result[pair.Key] = byTypeName.ToImmutable();
        }

        PopulateClrNamespaceIndexWithAllAssemblies(compilation, byClrNamespace, byFullTypeName);
        return new AvaloniaTypeIndex(result.ToImmutable(), byClrNamespace.ToImmutable(), byFullTypeName.ToImmutable());
    }

    private static void AddTypeByClrNamespace(
        ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>>.Builder byClrNamespace,
        ImmutableDictionary<string, AvaloniaTypeInfo>.Builder byFullTypeName,
        AvaloniaTypeInfo typeInfo)
    {
        if (!byClrNamespace.TryGetValue(typeInfo.ClrNamespace, out var byTypeName))
        {
            byTypeName = ImmutableDictionary<string, AvaloniaTypeInfo>.Empty.WithComparers(StringComparer.Ordinal);
        }

        if (!byTypeName.ContainsKey(typeInfo.XmlTypeName))
        {
            byClrNamespace[typeInfo.ClrNamespace] = byTypeName.SetItem(typeInfo.XmlTypeName, typeInfo);
        }

        if (!byFullTypeName.ContainsKey(typeInfo.FullTypeName))
        {
            byFullTypeName[typeInfo.FullTypeName] = typeInfo;
        }
    }

    private static void PopulateClrNamespaceIndexWithAllAssemblies(
        Compilation compilation,
        ImmutableDictionary<string, ImmutableDictionary<string, AvaloniaTypeInfo>>.Builder byClrNamespace,
        ImmutableDictionary<string, AvaloniaTypeInfo>.Builder byFullTypeName)
    {
        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                if (type.DeclaredAccessibility != Accessibility.Public || type.IsAbstract)
                {
                    continue;
                }

                var clrNamespace = type.ContainingNamespace.ToDisplayString();
                if (string.IsNullOrWhiteSpace(clrNamespace))
                {
                    continue;
                }

                var xmlTypeName = GetXamlTypeName(type);
                if (byClrNamespace.TryGetValue(clrNamespace, out var byName) &&
                    byName.ContainsKey(xmlTypeName))
                {
                    continue;
                }

                var typeInfo = BuildTypeInfo(compilation, AvaloniaDefaultXmlNamespace, type);
                AddTypeByClrNamespace(byClrNamespace, byFullTypeName, typeInfo);
            }
        }
    }

    private static string GetXamlTypeName(INamedTypeSymbol type)
    {
        var tickIndex = type.Name.IndexOf('`');
        return tickIndex > 0 ? type.Name.Substring(0, tickIndex) : type.Name;
    }

    private static AvaloniaTypeInfo BuildTypeInfo(Compilation compilation, string xmlNamespace, INamedTypeSymbol type)
    {
        var properties = ImmutableArray.CreateBuilder<AvaloniaPropertyInfo>();
        var seenPropertyNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var currentType in EnumerateTypeHierarchy(type))
        {
            foreach (var property in currentType.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.DeclaredAccessibility != Accessibility.Public || property.IsStatic)
                {
                    continue;
                }

                if (property.IsImplicitlyDeclared || !seenPropertyNames.Add(property.Name))
                {
                    continue;
                }

                properties.Add(new AvaloniaPropertyInfo(
                    property.Name,
                    property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    property.SetMethod is not null && property.SetMethod.DeclaredAccessibility == Accessibility.Public,
                    IsAttached: false,
                    SourceLocation: TryCreateSourceLocation(property)));
            }

            foreach (var field in currentType.GetMembers().OfType<IFieldSymbol>())
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
                if (propertyName.Length == 0 || !seenPropertyNames.Add(propertyName))
                {
                    continue;
                }

                properties.Add(new AvaloniaPropertyInfo(
                    propertyName,
                    field.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    IsSettable: true,
                    IsAttached: true,
                    SourceLocation: TryCreateSourceLocation(field)));
            }
        }

        var fullTypeName = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var summary = "`" + fullTypeName + "`";
        var pseudoClasses = BuildPseudoClasses(compilation, type);

        return new AvaloniaTypeInfo(
            XmlTypeName: GetXamlTypeName(type),
            FullTypeName: fullTypeName,
            XmlNamespace: xmlNamespace,
            ClrNamespace: type.ContainingNamespace.ToDisplayString(),
            AssemblyName: type.ContainingAssembly.Identity.Name,
            Properties: properties.OrderBy(static property => property.Name, StringComparer.Ordinal).ToImmutableArray(),
            Summary: summary,
            SourceLocation: TryCreateSourceLocation(type),
            PseudoClasses: pseudoClasses);
    }

    private static ImmutableArray<AvaloniaPseudoClassInfo> BuildPseudoClasses(Compilation compilation, INamedTypeSymbol type)
    {
        var builder = ImmutableArray.CreateBuilder<AvaloniaPseudoClassInfo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var currentType in EnumerateTypeHierarchy(type))
        {
            foreach (var attribute in currentType.GetAttributes())
            {
                if (!IsPseudoClassesAttribute(attribute))
                {
                    continue;
                }

                foreach (var pseudoClass in EnumeratePseudoClassInfos(compilation, currentType, attribute))
                {
                    if (!seen.Add(pseudoClass.Name))
                    {
                        continue;
                    }

                    builder.Add(pseudoClass);
                }
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsPseudoClassesAttribute(AttributeData attribute)
    {
        return string.Equals(
            attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            AvaloniaPseudoClassesAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static IEnumerable<AvaloniaPseudoClassInfo> EnumeratePseudoClassInfos(
        Compilation compilation,
        INamedTypeSymbol declaringType,
        AttributeData attribute)
    {
        foreach (var pseudoClassEntry in EnumeratePseudoClassEntries(attribute))
        {
            var pseudoClassName = pseudoClassEntry.Name;
            if (string.IsNullOrWhiteSpace(pseudoClassName))
            {
                continue;
            }

            yield return new AvaloniaPseudoClassInfo(
                Name: NormalizePseudoClassName(pseudoClassName),
                DeclaringTypeFullName: declaringType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                DeclaringAssemblyName: declaringType.ContainingAssembly.Identity.Name,
                SourceLocation: TryCreatePseudoClassSourceLocation(
                    compilation,
                    attribute,
                    pseudoClassName,
                    pseudoClassEntry.Ordinal));
        }
    }

    private static IEnumerable<(string Name, int Ordinal)> EnumeratePseudoClassEntries(AttributeData attribute)
    {
        var ordinal = 0;
        if (attribute.ConstructorArguments.Length == 1 &&
            attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array)
        {
            foreach (var value in attribute.ConstructorArguments[0].Values)
            {
                if (value.Value is string text)
                {
                    yield return (text, ordinal);
                    ordinal++;
                }
            }

            yield break;
        }

        foreach (var argument in attribute.ConstructorArguments)
        {
            if (argument.Value is string text)
            {
                yield return (text, ordinal);
                ordinal++;
            }
        }
    }

    private static AvaloniaSymbolSourceLocation? TryCreatePseudoClassSourceLocation(
        Compilation compilation,
        AttributeData attribute,
        string pseudoClassName,
        int pseudoClassOrdinal)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax() is not AttributeSyntax attributeSyntax ||
            !TryGetPseudoClassExpression(attributeSyntax, pseudoClassOrdinal, out var expression))
        {
            return null;
        }

        var normalizedPseudoClassName = NormalizePseudoClassName(pseudoClassName);
        var semanticModel = TryGetSemanticModel(compilation, expression.SyntaxTree);
        if (semanticModel is not null &&
            TryCreatePseudoClassArgumentSourceLocation(
                semanticModel,
                expression,
                normalizedPseudoClassName,
                out var sourceLocation))
        {
            return sourceLocation;
        }

        return TryCreateExpressionSourceLocation(expression);
    }

    private static bool TryCreatePseudoClassArgumentSourceLocation(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        string normalizedPseudoClassName,
        out AvaloniaSymbolSourceLocation sourceLocation)
    {
        sourceLocation = default!;
        if (!semanticModel.GetConstantValue(expression).HasValue ||
            semanticModel.GetConstantValue(expression).Value is not string value ||
            !string.Equals(NormalizePseudoClassName(value), normalizedPseudoClassName, StringComparison.Ordinal))
        {
            return false;
        }

        var referencedSymbol = semanticModel.GetSymbolInfo(expression).Symbol;
        if (referencedSymbol is not null &&
            TryCreateSourceLocation(referencedSymbol) is { } symbolSourceLocation)
        {
            sourceLocation = symbolSourceLocation;
            return true;
        }

        if (TryCreateExpressionSourceLocation(expression) is not { } expressionSourceLocation)
        {
            return false;
        }

        sourceLocation = expressionSourceLocation;
        return true;
    }

    private static bool TryGetPseudoClassExpression(
        AttributeSyntax attributeSyntax,
        int pseudoClassOrdinal,
        out ExpressionSyntax expression)
    {
        expression = null!;
        if (attributeSyntax.ArgumentList is null || pseudoClassOrdinal < 0)
        {
            return false;
        }

        var arguments = attributeSyntax.ArgumentList.Arguments;
        if (arguments.Count == 1)
        {
            switch (arguments[0].Expression)
            {
                case ArrayCreationExpressionSyntax { Initializer: { } initializer }
                    when pseudoClassOrdinal < initializer.Expressions.Count:
                    expression = initializer.Expressions[pseudoClassOrdinal];
                    return true;
                case ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer }
                    when pseudoClassOrdinal < initializer.Expressions.Count:
                    expression = initializer.Expressions[pseudoClassOrdinal];
                    return true;
            }
        }

        if (pseudoClassOrdinal < arguments.Count)
        {
            expression = arguments[pseudoClassOrdinal].Expression;
            return true;
        }

        return false;
    }

    private static SemanticModel? TryGetSemanticModel(Compilation compilation, SyntaxTree syntaxTree)
    {
        foreach (var candidate in compilation.SyntaxTrees)
        {
            if (ReferenceEquals(candidate, syntaxTree))
            {
                return compilation.GetSemanticModel(syntaxTree);
            }
        }

        return null;
    }

    private static AvaloniaSymbolSourceLocation? TryCreateExpressionSourceLocation(ExpressionSyntax expression)
    {
        var lineSpan = expression.GetLocation().GetLineSpan();
        if (expression.SyntaxTree.FilePath is null)
        {
            return null;
        }

        return new AvaloniaSymbolSourceLocation(
            UriPathHelper.ToDocumentUri(expression.SyntaxTree.FilePath),
            new SourceRange(
                new SourcePosition(
                    lineSpan.StartLinePosition.Line,
                    lineSpan.StartLinePosition.Character),
                new SourcePosition(
                    lineSpan.EndLinePosition.Line,
                    lineSpan.EndLinePosition.Character)));
    }

    private static string NormalizePseudoClassName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith(":", StringComparison.Ordinal)
            ? trimmed
            : ":" + trimmed;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static AvaloniaSymbolSourceLocation? TryCreateSourceLocation(ISymbol symbol)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree?.FilePath is null)
            {
                continue;
            }

            var lineSpan = location.GetLineSpan();
            var start = new SourcePosition(
                lineSpan.StartLinePosition.Line,
                lineSpan.StartLinePosition.Character);
            var end = new SourcePosition(
                lineSpan.EndLinePosition.Line,
                lineSpan.EndLinePosition.Character);

            return new AvaloniaSymbolSourceLocation(
                UriPathHelper.ToDocumentUri(location.SourceTree.FilePath),
                new SourceRange(start, end));
        }

        return null;
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
