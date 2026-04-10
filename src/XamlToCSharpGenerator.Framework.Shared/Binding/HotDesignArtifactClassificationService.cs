using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class HotDesignArtifactClassificationService
{
    public delegate bool IsTypeAssignableDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);

    private readonly IsTypeAssignableDelegate _isTypeAssignable;
    private readonly HotDesignArtifactClassificationRules _rules;

    public HotDesignArtifactClassificationService(
        IsTypeAssignableDelegate isTypeAssignable,
        HotDesignArtifactClassificationRules rules)
    {
        _isTypeAssignable = isTypeAssignable ?? throw new ArgumentNullException(nameof(isTypeAssignable));
        _rules = rules;
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
        var kind = ResolveKind(typeSymbolCatalog, rootXmlTypeName, rootTypeSymbol, styles, controlThemes, templates);
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
        if (MatchesRootType(_rules.ApplicationRootTypeNames, rootXmlTypeName) ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, _rules.ApplicationTypeContractId))
        {
            return ResolvedHotDesignArtifactKind.Application;
        }

        if (MatchesRootType(_rules.ControlThemeRootTypeNames, rootXmlTypeName) ||
            controlThemes.Length > 0 ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, _rules.ControlThemeTypeContractId))
        {
            return ResolvedHotDesignArtifactKind.ControlTheme;
        }

        if (MatchesRootType(_rules.ResourceDictionaryRootTypeNames, rootXmlTypeName) ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, _rules.ResourceDictionaryTypeContractId))
        {
            return ResolvedHotDesignArtifactKind.ResourceDictionary;
        }

        if (MatchesRootType(_rules.TemplateRootTypeNames, rootXmlTypeName) ||
            (templates.Length > 0 && styles.Length == 0 && controlThemes.Length == 0))
        {
            return ResolvedHotDesignArtifactKind.Template;
        }

        if (MatchesRootType(_rules.StyleRootTypeNames, rootXmlTypeName) ||
            styles.Length > 0 ||
            IsAssignableToKnownType(typeSymbolCatalog, rootTypeSymbol, _rules.StyleTypeContractId))
        {
            return ResolvedHotDesignArtifactKind.Style;
        }

        return ResolvedHotDesignArtifactKind.View;
    }

    private ImmutableArray<string> BuildScopeHints(
        ResolvedHotDesignArtifactKind kind,
        string rootXmlTypeName)
    {
        var hints = ImmutableArray.CreateBuilder<string>(2);
        hints.Add(kind switch
        {
            ResolvedHotDesignArtifactKind.Application => _rules.ApplicationScopeHint,
            ResolvedHotDesignArtifactKind.Template => _rules.TemplateScopeHint,
            ResolvedHotDesignArtifactKind.ControlTheme => _rules.ControlThemeScopeHint,
            ResolvedHotDesignArtifactKind.ResourceDictionary => _rules.ResourceDictionaryScopeHint,
            ResolvedHotDesignArtifactKind.Style => _rules.StyleScopeHint,
            _ => _rules.ViewScopeHint
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

    private static bool MatchesRootType(ImmutableArray<string> knownRootTypeNames, string rootXmlTypeName)
    {
        foreach (var knownRootTypeName in knownRootTypeNames)
        {
            if (string.Equals(rootXmlTypeName, knownRootTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public readonly record struct HotDesignArtifactClassification(
    ResolvedHotDesignArtifactKind Kind,
    ImmutableArray<string> ScopeHints);

public readonly record struct HotDesignArtifactClassificationRules(
    ImmutableArray<string> ApplicationRootTypeNames,
    TypeContractId ApplicationTypeContractId,
    ImmutableArray<string> StyleRootTypeNames,
    TypeContractId StyleTypeContractId,
    ImmutableArray<string> ResourceDictionaryRootTypeNames,
    TypeContractId ResourceDictionaryTypeContractId,
    ImmutableArray<string> ControlThemeRootTypeNames,
    TypeContractId ControlThemeTypeContractId,
    ImmutableArray<string> TemplateRootTypeNames,
    string ViewScopeHint,
    string ApplicationScopeHint,
    string StyleScopeHint,
    string ResourceDictionaryScopeHint,
    string ControlThemeScopeHint,
    string TemplateScopeHint);
