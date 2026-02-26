using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<ResolvedTemplateDefinition> templates)
    {
        var rootXmlTypeName = document.RootObject.XmlTypeName;
        var kind = ResolveKind(
            compilation,
            rootXmlTypeName,
            rootTypeSymbol,
            styles,
            controlThemes,
            templates);
        var scopeHints = BuildScopeHints(kind, rootXmlTypeName);
        return new HotDesignArtifactClassification(kind, scopeHints);
    }

    private ResolvedHotDesignArtifactKind ResolveKind(
        Compilation compilation,
        string rootXmlTypeName,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<ResolvedStyleDefinition> styles,
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<ResolvedTemplateDefinition> templates)
    {
        if (IsRootType(rootXmlTypeName, "Application") ||
            IsAssignableToKnownType(compilation, rootTypeSymbol, "Avalonia.Application"))
        {
            return ResolvedHotDesignArtifactKind.Application;
        }

        if (IsRootType(rootXmlTypeName, "ControlTheme") ||
            controlThemes.Length > 0 ||
            IsAssignableToKnownType(compilation, rootTypeSymbol, "Avalonia.Styling.ControlTheme"))
        {
            return ResolvedHotDesignArtifactKind.ControlTheme;
        }

        if (IsRootType(rootXmlTypeName, "ResourceDictionary") ||
            IsAssignableToKnownType(compilation, rootTypeSymbol, "Avalonia.Controls.ResourceDictionary"))
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
            IsAssignableToKnownType(compilation, rootTypeSymbol, "Avalonia.Styling.Styles"))
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
        Compilation compilation,
        INamedTypeSymbol? rootTypeSymbol,
        string metadataName)
    {
        if (rootTypeSymbol is null)
        {
            return false;
        }

        var targetType = compilation.GetTypeByMetadataName(metadataName);
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
