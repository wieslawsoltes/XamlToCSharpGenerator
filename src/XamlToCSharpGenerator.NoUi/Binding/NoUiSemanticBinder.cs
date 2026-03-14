using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.NoUi.Binding;

public sealed class NoUiSemanticBinder : IXamlFrameworkSemanticBinder
{
    public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options,
        XamlTransformConfiguration transformConfiguration)
    {
        _ = transformConfiguration;
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        var rootObject = BindObject(document.RootObject, document, compilation, diagnostics);
        var classModifier = string.IsNullOrWhiteSpace(document.ClassModifier)
            ? "public"
            : document.ClassModifier!.Trim();
        var passTrace = options.TracePasses
            ? ImmutableArray.Create("NoUi.Bind")
            : ImmutableArray<string>.Empty;

        var viewModel = new ResolvedViewModel(
            document,
            BuildNoUiUri(document, options),
            classModifier,
            options.CreateSourceInfo,
            options.HotReloadEnabled,
            options.HotDesignEnabled,
            passTrace,
            EmitNameScopeRegistration: false,
            EmitStaticResourceResolver: false,
            rootObject,
            NamedElements: ImmutableArray<ResolvedNamedElement>.Empty,
            Resources: ImmutableArray<ResolvedResourceDefinition>.Empty,
            Templates: ImmutableArray<ResolvedTemplateDefinition>.Empty,
            CompiledBindings: ImmutableArray<ResolvedCompiledBindingDefinition>.Empty,
            UnsafeAccessors: ImmutableArray<ResolvedUnsafeAccessorDefinition>.Empty,
            Styles: ImmutableArray<ResolvedStyleDefinition>.Empty,
            ControlThemes: ImmutableArray<ResolvedControlThemeDefinition>.Empty,
            Includes: ImmutableArray<ResolvedIncludeDefinition>.Empty,
            HotDesignArtifactKind: ResolvedHotDesignArtifactKind.View,
            HotDesignScopeHints: string.IsNullOrWhiteSpace(document.RootObject.XmlTypeName)
                ? ImmutableArray.Create("control")
                : ImmutableArray.Create("control", document.RootObject.XmlTypeName));

        return (viewModel, diagnostics.ToImmutable());
    }

    private static string BuildNoUiUri(XamlDocumentModel document, GeneratorOptions options)
    {
        var assemblyName = string.IsNullOrWhiteSpace(options.AssemblyName)
            ? "UnknownAssembly"
            : options.AssemblyName!;
        var normalizedTargetPath = document.TargetPath
            .Replace('\\', '/')
            .TrimStart('/');
        return "noui://" + assemblyName + "/" + normalizedTargetPath;
    }

    private static ResolvedObjectNode BindObject(
        XamlObjectNode node,
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var typeName = ResolveTypeName(node, document, compilation, diagnostics);

        var propertyAssignments = node.PropertyAssignments
            .Select(static property => new ResolvedPropertyAssignment(
                property.PropertyName,
                property.Value,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                property.Line,
                property.Column,
                property.Condition,
                ValueKind: ResolvedValueKind.Literal))
            .ToImmutableArray();

        var propertyElementAssignments = node.PropertyElements
            .Select(propertyElement => new ResolvedPropertyElementAssignment(
                propertyElement.PropertyName,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                IsCollectionAdd: false,
                IsDictionaryMerge: false,
                propertyElement.ObjectValues
                    .Select(child => BindObject(child, document, compilation, diagnostics))
                    .ToImmutableArray(),
                propertyElement.Line,
                propertyElement.Column,
                propertyElement.Condition))
            .ToImmutableArray();

        var childObjects = node.ChildObjects
            .Select(child => BindObject(child, document, compilation, diagnostics))
            .ToImmutableArray();

        return new ResolvedObjectNode(
            KeyExpression: node.Key,
            Name: node.Name,
            TypeName: typeName,
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: propertyAssignments,
            PropertyElementAssignments: propertyElementAssignments,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: childObjects,
            ChildAttachmentMode: childObjects.Length > 0 || propertyElementAssignments.Length > 0
                ? ResolvedChildAttachmentMode.ChildrenCollection
                : ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            node.Line,
            node.Column,
            node.Condition);
    }

    private static string ResolveTypeName(
        XamlObjectNode node,
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics)
    {
        var resolvedByNamespace = ResolveTypeByXmlNamespace(node.XmlNamespace, node.XmlTypeName, compilation);
        if (!string.IsNullOrWhiteSpace(resolvedByNamespace))
        {
            return resolvedByNamespace!;
        }

        var resolvedByName = ResolveTypeByName(node.XmlTypeName, compilation);
        if (!string.IsNullOrWhiteSpace(resolvedByName))
        {
            return resolvedByName!;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0100",
            $"NoUI type '{node.XmlTypeName}' from xml namespace '{node.XmlNamespace}' could not be resolved. Falling back to global::System.Object.",
            document.FilePath,
            node.Line,
            node.Column,
            false));
        return "global::System.Object";
    }

    private static string? ResolveTypeByXmlNamespace(string xmlNamespace, string xmlTypeName, Compilation compilation)
    {
        if (string.IsNullOrWhiteSpace(xmlNamespace) || string.IsNullOrWhiteSpace(xmlTypeName))
        {
            return null;
        }

        if (!TryExtractClrNamespace(xmlNamespace, out var clrNamespace))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(clrNamespace))
        {
            return null;
        }

        var metadataName = clrNamespace + "." + xmlTypeName;
        var symbol = compilation.GetTypeByMetadataName(metadataName);
        return symbol is null
            ? null
            : symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool TryExtractClrNamespace(string xmlNamespace, out string clrNamespace)
    {
        return XamlXmlNamespaceSemantics.TryExtractClrNamespace(xmlNamespace, out clrNamespace);
    }

    private static string? ResolveTypeByName(string typeName, Compilation compilation)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var matches = new List<INamedTypeSymbol>();
        CollectTypeMatches(compilation.GlobalNamespace, typeName, matches);
        if (matches.Count == 0)
        {
            return null;
        }

        return matches
            .Select(static symbol => symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .First();
    }

    private static void CollectTypeMatches(
        INamespaceSymbol namespaceSymbol,
        string typeName,
        List<INamedTypeSymbol> matches)
    {
        foreach (var member in namespaceSymbol.GetMembers())
        {
            switch (member)
            {
                case INamespaceSymbol nestedNamespace:
                    CollectTypeMatches(nestedNamespace, typeName, matches);
                    break;
                case INamedTypeSymbol namedType:
                    CollectTypeMatches(namedType, typeName, matches);
                    break;
            }
        }
    }

    private static void CollectTypeMatches(
        INamedTypeSymbol typeSymbol,
        string typeName,
        List<INamedTypeSymbol> matches)
    {
        if (string.Equals(typeSymbol.Name, typeName, StringComparison.Ordinal))
        {
            matches.Add(typeSymbol);
        }

        foreach (var nestedType in typeSymbol.GetTypeMembers())
        {
            CollectTypeMatches(nestedType, typeName, matches);
        }
    }
}
