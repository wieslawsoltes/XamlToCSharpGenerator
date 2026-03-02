using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

internal sealed class HotDesignArtifactClassificationService
{
    internal delegate bool IsTypeAssignableDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);

    private readonly IsTypeAssignableDelegate _isTypeAssignable;

    public HotDesignArtifactClassificationService(IsTypeAssignableDelegate isTypeAssignable)
    {
        _isTypeAssignable = isTypeAssignable ?? throw new ArgumentNullException(nameof(isTypeAssignable));
    }

    public HotDesignArtifactClassification Classify(
        ITypeSymbolCatalog? typeSymbolCatalog,
        XamlDocumentModel document,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<ResolvedTemplateDefinition> templates)
    {
        var rootXmlTypeName = document.RootObject.XmlTypeName;
        var kind = ResolveKind(
            typeSymbolCatalog,
            rootXmlTypeName,
            rootTypeSymbol,
            styles,
            controlThemes,
            templates);
        var scopeHints = BuildScopeHints(kind, rootXmlTypeName);
        return new HotDesignArtifactClassification(kind, scopeHints);
    }

    private ResolvedHotDesignArtifactKind ResolveKind(
        ITypeSymbolCatalog? typeSymbolCatalog,
        string rootXmlTypeName,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<ResolvedTemplateDefinition> templates)
    {
        if (IsRootType(rootXmlTypeName, "Application") ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, TypeContractId.Application))
        {
            return ResolvedHotDesignArtifactKind.Application;
        }

        if (IsRootType(rootXmlTypeName, "ControlTheme") ||
            controlThemes.Length > 0 ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, TypeContractId.ControlTheme))
        {
            return ResolvedHotDesignArtifactKind.ControlTheme;
        }

        if (IsRootType(rootXmlTypeName, "ResourceDictionary") ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, TypeContractId.ResourceDictionary))
        {
            return ResolvedHotDesignArtifactKind.ResourceDictionary;
        }

        if (IsRootTemplateType(rootXmlTypeName) ||
            (templates.Length > 0 && styles.Length == 0 && controlThemes.Length == 0))
        {
            return ResolvedHotDesignArtifactKind.Template;
        }

        if (IsRootType(rootXmlTypeName, "Style") ||
            IsRootType(rootXmlTypeName, "Styles") ||
            styles.Length > 0 ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, TypeContractId.Styles))
        {
            return ResolvedHotDesignArtifactKind.Style;
        }

        return ResolvedHotDesignArtifactKind.View;
    }

    private static ImmutableArray<string> BuildScopeHints(
        ResolvedHotDesignArtifactKind kind,
        string rootXmlTypeName)
    {
        var hints = ImmutableArray.CreateBuilder<string>(2);
        hints.Add(kind switch
        {
            ResolvedHotDesignArtifactKind.Application => "application",
            ResolvedHotDesignArtifactKind.Template => "template",
            ResolvedHotDesignArtifactKind.ControlTheme => "theme",
            ResolvedHotDesignArtifactKind.ResourceDictionary => "resources",
            ResolvedHotDesignArtifactKind.Style => "styles",
            _ => "control"
        });

        if (!string.IsNullOrWhiteSpace(rootXmlTypeName))
        {
            var trimmedXmlTypeName = rootXmlTypeName.Trim();
            if (!trimmedXmlTypeName.Equals(hints[0], StringComparison.OrdinalIgnoreCase))
            {
                hints.Add(trimmedXmlTypeName);
            }
        }

        return hints.ToImmutable();
    }

    private bool IsAssignableToKnownType(
        ITypeSymbolCatalog? typeSymbolCatalog,
        INamedTypeSymbol? rootTypeSymbol,
        TypeContractId contractId)
    {
        if (rootTypeSymbol is null)
        {
            return false;
        }

        var targetType = typeSymbolCatalog?.GetOrDefault(contractId);
        return targetType is not null && _isTypeAssignable(rootTypeSymbol, targetType);
    }

    private static bool IsRootType(string rootXmlTypeName, string expectedTypeName)
    {
        return rootXmlTypeName.Equals(expectedTypeName, StringComparison.Ordinal);
    }

    private static bool IsRootTemplateType(string rootXmlTypeName)
    {
        return IsRootType(rootXmlTypeName, "ControlTemplate") ||
               IsRootType(rootXmlTypeName, "DataTemplate") ||
               IsRootType(rootXmlTypeName, "ItemsPanelTemplate");
    }
}

internal readonly record struct HotDesignArtifactClassification(
    ResolvedHotDesignArtifactKind Kind,
    ImmutableArray<string> ScopeHints);
