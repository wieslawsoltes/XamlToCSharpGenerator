using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Avalonia.Framework;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;
using XamlToCSharpGenerator.LanguageService.Workspace;

namespace XamlToCSharpGenerator.LanguageService.Framework;

public sealed record XamlLanguageFrameworkInfo(
    string Id,
    IXamlFrameworkProfile Profile,
    string DefaultXmlNamespace,
    ImmutableArray<string> XmlnsDefinitionAttributeMetadataNames,
    ImmutableArray<string> XmlnsPrefixAttributeMetadataNames,
    ImmutableArray<string> MarkupExtensionNamespaces,
    string PreferredProjectXamlItemName,
    ImmutableArray<string> ProjectXamlItemNames,
    bool SupportsPseudoClasses = false,
    string? PseudoClassesAttributeMetadataName = null,
    bool SupportsAssemblyResourceUris = false,
    bool IncludeSourceAssemblyClrNamespacesInDefaultXmlNamespace = false);

internal static class XamlLanguageFrameworkCatalog
{
    private const string AvaloniaDefaultXmlNamespace = "https://github.com/avaloniaui";
    private const string PresentationXmlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string MauiDefaultXmlNamespace = "http://schemas.microsoft.com/dotnet/2021/maui";

    public static XamlLanguageFrameworkInfo Avalonia { get; } = new(
        Id: FrameworkProfileIds.Avalonia,
        Profile: AvaloniaFrameworkProfile.Instance,
        DefaultXmlNamespace: AvaloniaDefaultXmlNamespace,
        XmlnsDefinitionAttributeMetadataNames:
        [
            "Avalonia.Metadata.XmlnsDefinitionAttribute",
            "XamlToCSharpGenerator.Runtime.SourceGenXmlnsDefinitionAttribute"
        ],
        XmlnsPrefixAttributeMetadataNames:
        [
            "Avalonia.Metadata.XmlnsPrefixAttribute",
            "XamlToCSharpGenerator.Runtime.SourceGenGlobalXmlnsPrefixAttribute"
        ],
        MarkupExtensionNamespaces:
        [
            "Avalonia.Markup.Xaml.MarkupExtensions",
            "Avalonia.Data",
            "Avalonia.Markup.Xaml",
            "System.Windows.Markup"
        ],
        PreferredProjectXamlItemName: "AvaloniaXaml",
        ProjectXamlItemNames:
        [
            "AvaloniaXaml",
            "Page",
            "ApplicationDefinition",
            "None",
            "Content",
            "EmbeddedResource",
            "AdditionalFiles"
        ],
        SupportsPseudoClasses: true,
        PseudoClassesAttributeMetadataName: "Avalonia.Controls.Metadata.PseudoClassesAttribute",
        SupportsAssemblyResourceUris: true,
        IncludeSourceAssemblyClrNamespacesInDefaultXmlNamespace: true);

    public static XamlLanguageFrameworkInfo Wpf { get; } = CreatePassiveFramework(
        id: FrameworkProfileIds.Wpf,
        defaultXmlNamespace: PresentationXmlNamespace,
        preferredProjectXamlItemName: "Page",
        projectXamlItemNames:
        [
            "Page",
            "ApplicationDefinition",
            "EmbeddedResource",
            "Resource",
            "Content",
            "None",
            "AdditionalFiles"
        ],
        xmlnsDefinitionAttributeMetadataNames:
        [
            "System.Windows.Markup.XmlnsDefinitionAttribute"
        ],
        xmlnsPrefixAttributeMetadataNames:
        [
            "System.Windows.Markup.XmlnsPrefixAttribute"
        ],
        markupExtensionNamespaces:
        [
            "System.Windows",
            "System.Windows.Data",
            "System.Windows.Markup"
        ]);

    public static XamlLanguageFrameworkInfo WinUI { get; } = CreatePassiveFramework(
        id: FrameworkProfileIds.WinUI,
        defaultXmlNamespace: PresentationXmlNamespace,
        preferredProjectXamlItemName: "Page",
        projectXamlItemNames:
        [
            "Page",
            "ApplicationDefinition",
            "PRIResource",
            "Content",
            "None",
            "AdditionalFiles"
        ],
        xmlnsDefinitionAttributeMetadataNames:
        [
            "Microsoft.UI.Xaml.Markup.XmlnsDefinitionAttribute"
        ],
        xmlnsPrefixAttributeMetadataNames:
        [
            "Microsoft.UI.Xaml.Markup.XmlnsPrefixAttribute"
        ],
        markupExtensionNamespaces:
        [
            "Microsoft.UI.Xaml",
            "Microsoft.UI.Xaml.Data",
            "Microsoft.UI.Xaml.Markup",
            "System.Windows.Markup"
        ]);

    public static XamlLanguageFrameworkInfo Maui { get; } = CreatePassiveFramework(
        id: FrameworkProfileIds.Maui,
        defaultXmlNamespace: MauiDefaultXmlNamespace,
        preferredProjectXamlItemName: "MauiXaml",
        projectXamlItemNames:
        [
            "MauiXaml",
            "Page",
            "ApplicationDefinition",
            "EmbeddedResource",
            "Content",
            "None",
            "AdditionalFiles"
        ],
        xmlnsDefinitionAttributeMetadataNames:
        [
            "Microsoft.Maui.Controls.XmlnsDefinitionAttribute",
            "Microsoft.Maui.Controls.Xaml.XmlnsDefinitionAttribute"
        ],
        xmlnsPrefixAttributeMetadataNames:
        [
            "Microsoft.Maui.Controls.XmlnsPrefixAttribute",
            "Microsoft.Maui.Controls.Xaml.XmlnsPrefixAttribute"
        ],
        markupExtensionNamespaces:
        [
            "Microsoft.Maui.Controls",
            "Microsoft.Maui.Controls.Xaml",
            "Microsoft.Maui.Controls.Xaml.MarkupExtensions",
            "System.Windows.Markup"
        ]);

    private static readonly ImmutableDictionary<string, XamlLanguageFrameworkInfo> Frameworks =
        ImmutableDictionary.CreateRange(
            StringComparer.OrdinalIgnoreCase,
            new[]
            {
                KeyValuePair.Create(Avalonia.Id, Avalonia),
                KeyValuePair.Create(Wpf.Id, Wpf),
                KeyValuePair.Create(WinUI.Id, WinUI),
                KeyValuePair.Create(Maui.Id, Maui)
            });
    private static readonly ImmutableHashSet<string> KnownProjectXamlItemNames = Frameworks.Values
        .SelectMany(static framework => framework.ProjectXamlItemNames)
        .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly ImmutableHashSet<string> KnownXmlnsPrefixAttributeMetadataNames = Frameworks.Values
        .SelectMany(static framework => framework.XmlnsPrefixAttributeMetadataNames)
        .ToImmutableHashSet(StringComparer.Ordinal);

    public static XamlLanguageFrameworkInfo Default => Avalonia;

    public static bool TryGetById(string? frameworkId, out XamlLanguageFrameworkInfo framework)
    {
        framework = Default;
        if (string.IsNullOrWhiteSpace(frameworkId))
        {
            return false;
        }

        return Frameworks.TryGetValue(frameworkId.Trim(), out framework!);
    }

    public static bool IsKnownProjectXamlItemName(string? localName)
    {
        return !string.IsNullOrWhiteSpace(localName) &&
               KnownProjectXamlItemNames.Contains(localName.Trim());
    }

    public static bool IsKnownXmlnsPrefixAttribute(string? metadataName)
    {
        return !string.IsNullOrWhiteSpace(metadataName) &&
               KnownXmlnsPrefixAttributeMetadataNames.Contains(metadataName.Trim());
    }

    private static XamlLanguageFrameworkInfo CreatePassiveFramework(
        string id,
        string defaultXmlNamespace,
        string preferredProjectXamlItemName,
        ImmutableArray<string> projectXamlItemNames,
        ImmutableArray<string> xmlnsDefinitionAttributeMetadataNames,
        ImmutableArray<string> xmlnsPrefixAttributeMetadataNames,
        ImmutableArray<string> markupExtensionNamespaces)
    {
        return new XamlLanguageFrameworkInfo(
            Id: id,
            Profile: new PassiveXamlFrameworkProfile(
                id,
                defaultXmlNamespace,
                preferredProjectXamlItemName,
                projectXamlItemNames),
            DefaultXmlNamespace: defaultXmlNamespace,
            XmlnsDefinitionAttributeMetadataNames: xmlnsDefinitionAttributeMetadataNames,
            XmlnsPrefixAttributeMetadataNames: xmlnsPrefixAttributeMetadataNames,
            MarkupExtensionNamespaces: markupExtensionNamespaces,
            PreferredProjectXamlItemName: preferredProjectXamlItemName,
            ProjectXamlItemNames: projectXamlItemNames);
    }

    private sealed class PassiveXamlFrameworkProfile : IXamlFrameworkProfile
    {
        private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        private const string BlendDesignNamespace = "http://schemas.microsoft.com/expression/blend/2008";
        private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";

        private readonly IXamlFrameworkBuildContract _buildContract;

        public PassiveXamlFrameworkProfile(
            string id,
            string defaultXmlNamespace,
            string preferredProjectXamlItemName,
            ImmutableArray<string> projectXamlItemNames)
        {
            Id = id;
            DefaultXmlNamespace = defaultXmlNamespace;
            _buildContract = new PassiveBuildContract(preferredProjectXamlItemName, projectXamlItemNames);
        }

        public string Id { get; }

        private string DefaultXmlNamespace { get; }

        public IXamlFrameworkBuildContract BuildContract => _buildContract;

        public IXamlFrameworkTransformProvider TransformProvider { get; } = new PassiveTransformProvider();

        public IXamlFrameworkSemanticBinder CreateSemanticBinder()
        {
            return PassiveSemanticBinder.Instance;
        }

        public IXamlFrameworkEmitter CreateEmitter()
        {
            return PassiveEmitter.Instance;
        }

        public ImmutableArray<IXamlDocumentEnricher> CreateDocumentEnrichers()
        {
            return ImmutableArray<IXamlDocumentEnricher>.Empty;
        }

        public XamlFrameworkParserSettings BuildParserSettings(Compilation compilation, GeneratorOptions options)
        {
            _ = compilation;
            var globalPrefixes = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            globalPrefixes["x"] = Xaml2006Namespace;
            globalPrefixes["d"] = BlendDesignNamespace;
            globalPrefixes["mc"] = MarkupCompatibilityNamespace;

            return new XamlFrameworkParserSettings(
                globalPrefixes.ToImmutable(),
                allowImplicitDefaultXmlns: true,
                implicitDefaultXmlns: string.IsNullOrWhiteSpace(options.ImplicitDefaultXmlns)
                    ? DefaultXmlNamespace
                    : options.ImplicitDefaultXmlns);
        }
    }

    private sealed class PassiveBuildContract : IXamlFrameworkBuildContract
    {
        private readonly ImmutableHashSet<string> _projectXamlItemNames;
        private readonly string _preferredProjectXamlItemName;

        public PassiveBuildContract(string preferredProjectXamlItemName, ImmutableArray<string> projectXamlItemNames)
        {
            _preferredProjectXamlItemName = preferredProjectXamlItemName;
            _projectXamlItemNames = projectXamlItemNames.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public string SourceItemGroupMetadataName => "build_metadata.AdditionalFiles.SourceItemGroup";

        public string TargetPathMetadataName => "build_metadata.AdditionalFiles.TargetPath";

        public string XamlSourceItemGroup => _preferredProjectXamlItemName;

        public string TransformRuleSourceItemGroup => _preferredProjectXamlItemName + "SourceGenTransformRule";

        public bool IsXamlPath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsXamlSourceItemGroup(string? sourceItemGroup)
        {
            return sourceItemGroup is not null && _projectXamlItemNames.Contains(sourceItemGroup.Trim());
        }

        public bool IsTransformRuleSourceItemGroup(string? sourceItemGroup)
        {
            return string.Equals(sourceItemGroup, TransformRuleSourceItemGroup, StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeSourceItemGroup(string? sourceItemGroup)
        {
            if (string.IsNullOrWhiteSpace(sourceItemGroup))
            {
                return _preferredProjectXamlItemName;
            }

            var normalized = sourceItemGroup.Trim();
            return _projectXamlItemNames.Contains(normalized)
                ? normalized
                : _preferredProjectXamlItemName;
        }
    }

    private sealed class PassiveTransformProvider : IXamlFrameworkTransformProvider
    {
        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
        {
            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
            ImmutableArray<XamlFrameworkTransformRuleResult> files)
        {
            var diagnostics = files.IsDefaultOrEmpty
                ? ImmutableArray<DiagnosticInfo>.Empty
                : files.SelectMany(static item => item.Diagnostics).ToImmutableArray();

            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                diagnostics);
        }
    }

    private sealed class PassiveSemanticBinder : IXamlFrameworkSemanticBinder
    {
        public static PassiveSemanticBinder Instance { get; } = new();

        public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            XamlTransformConfiguration transformConfiguration)
        {
            _ = document;
            _ = compilation;
            _ = options;
            _ = transformConfiguration;
            return (null, ImmutableArray<DiagnosticInfo>.Empty);
        }
    }

    private sealed class PassiveEmitter : IXamlFrameworkEmitter
    {
        public static PassiveEmitter Instance { get; } = new();

        public (string HintName, string Source) Emit(ResolvedViewModel viewModel)
        {
            throw new NotSupportedException("Passive language-service framework profiles do not emit source.");
        }
    }
}

internal sealed class XamlLanguageFrameworkResolver
{
    private static readonly ConcurrentDictionary<string, string?> ProjectFrameworkCache =
        new(StringComparer.OrdinalIgnoreCase);

    public XamlLanguageFrameworkInfo Resolve(
        XamlLanguageServiceOptions options,
        CompilationSnapshot snapshot,
        string filePath,
        string? documentText)
    {
        if (XamlLanguageFrameworkCatalog.TryGetById(options.FrameworkId, out var explicitFramework))
        {
            return explicitFramework;
        }

        if (TryResolveFromProject(snapshot.ProjectPath, out var projectFramework))
        {
            return projectFramework;
        }

        if (TryResolveFromCompilation(snapshot.Compilation, out var compilationFramework))
        {
            return compilationFramework;
        }

        if (TryResolveFromDocument(filePath, documentText, out var documentFramework))
        {
            return documentFramework;
        }

        return string.Equals(Path.GetExtension(filePath), ".xaml", StringComparison.OrdinalIgnoreCase)
            ? XamlLanguageFrameworkCatalog.Wpf
            : XamlLanguageFrameworkCatalog.Default;
    }

    private static bool TryResolveFromProject(string? projectPath, out XamlLanguageFrameworkInfo framework)
    {
        framework = XamlLanguageFrameworkCatalog.Default;
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return false;
        }

        var normalizedProjectPath = Path.GetFullPath(projectPath);
        var cachedFrameworkId = ProjectFrameworkCache.GetOrAdd(
            normalizedProjectPath,
            static path => ResolveProjectFrameworkIdCore(path));

        return XamlLanguageFrameworkCatalog.TryGetById(cachedFrameworkId, out framework);
    }

    private static string? ResolveProjectFrameworkIdCore(string projectPath)
    {
        try
        {
            var projectDocument = XDocument.Load(projectPath, LoadOptions.None);
            string? sdk = projectDocument.Root?.Attribute("Sdk")?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(sdk) &&
                sdk.IndexOf("Microsoft.Maui.Sdk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FrameworkProfileIds.Maui;
            }

            if (HasTrueProperty(projectDocument, "UseMaui") ||
                HasItemElement(projectDocument, "MauiXaml"))
            {
                return FrameworkProfileIds.Maui;
            }

            if (HasTrueProperty(projectDocument, "UseWinUI") ||
                HasTrueProperty(projectDocument, "WindowsAppSDKWinUI") ||
                HasTrueProperty(projectDocument, "UseWinUITools") ||
                HasPackageReference(projectDocument, "Microsoft.WindowsAppSDK"))
            {
                return FrameworkProfileIds.WinUI;
            }

            if (HasTrueProperty(projectDocument, "UseWPF"))
            {
                return FrameworkProfileIds.Wpf;
            }

            if (HasItemElement(projectDocument, "AvaloniaXaml") ||
                HasPackageReference(projectDocument, "Avalonia") ||
                HasPackageReference(projectDocument, "Avalonia.Desktop") ||
                HasPackageReference(projectDocument, "Avalonia.ReactiveUI"))
            {
                return FrameworkProfileIds.Avalonia;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool HasTrueProperty(XDocument document, string propertyName)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, propertyName, StringComparison.OrdinalIgnoreCase))
            .Select(static element => element.Value?.Trim())
            .Any(static value => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasItemElement(XDocument document, string itemName)
    {
        return document
            .Descendants()
            .Any(element => string.Equals(element.Name.LocalName, itemName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasPackageReference(XDocument document, string packageId)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase))
            .Any(element =>
                string.Equals(element.Attribute("Include")?.Value?.Trim(), packageId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "Include", StringComparison.OrdinalIgnoreCase))?.Value?.Trim(),
                    packageId,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveFromCompilation(Compilation? compilation, out XamlLanguageFrameworkInfo framework)
    {
        framework = XamlLanguageFrameworkCatalog.Default;
        if (compilation is null)
        {
            return false;
        }

        if (HasType(compilation, "Microsoft.Maui.Controls.Application") ||
            HasAssembly(compilation, "Microsoft.Maui.Controls"))
        {
            framework = XamlLanguageFrameworkCatalog.Maui;
            return true;
        }

        if (HasType(compilation, "Microsoft.UI.Xaml.Application") ||
            HasAssembly(compilation, "Microsoft.UI.Xaml"))
        {
            framework = XamlLanguageFrameworkCatalog.WinUI;
            return true;
        }

        if (HasType(compilation, "Avalonia.Application") ||
            HasAssemblyPrefix(compilation, "Avalonia"))
        {
            framework = XamlLanguageFrameworkCatalog.Avalonia;
            return true;
        }

        if (HasType(compilation, "System.Windows.Application") ||
            HasAssembly(compilation, "PresentationFramework") ||
            HasAssembly(compilation, "System.Xaml"))
        {
            framework = XamlLanguageFrameworkCatalog.Wpf;
            return true;
        }

        return false;
    }

    private static bool HasType(Compilation compilation, string metadataName)
    {
        return compilation.GetTypeByMetadataName(metadataName) is not null;
    }

    private static bool HasAssembly(Compilation compilation, string assemblyName)
    {
        return compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly =>
            assembly is not null &&
            string.Equals(assembly.Identity.Name, assemblyName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasAssemblyPrefix(Compilation compilation, string assemblyPrefix)
    {
        return compilation.SourceModule.ReferencedAssemblySymbols.Any(assembly =>
            assembly is not null &&
            assembly.Identity.Name.StartsWith(assemblyPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveFromDocument(
        string filePath,
        string? documentText,
        out XamlLanguageFrameworkInfo framework)
    {
        framework = XamlLanguageFrameworkCatalog.Default;
        var extension = Path.GetExtension(filePath);
        if (string.Equals(extension, ".axaml", StringComparison.OrdinalIgnoreCase))
        {
            framework = XamlLanguageFrameworkCatalog.Avalonia;
            return true;
        }

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return false;
        }

        if (documentText.IndexOf("https://github.com/avaloniaui", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            framework = XamlLanguageFrameworkCatalog.Avalonia;
            return true;
        }

        if (documentText.IndexOf("http://schemas.microsoft.com/dotnet/2021/maui", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            framework = XamlLanguageFrameworkCatalog.Maui;
            return true;
        }

        if (documentText.IndexOf("http://schemas.microsoft.com/winfx/2006/xaml/presentation", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            framework = documentText.IndexOf("using:", StringComparison.OrdinalIgnoreCase) >= 0
                ? XamlLanguageFrameworkCatalog.WinUI
                : XamlLanguageFrameworkCatalog.Wpf;
            return true;
        }

        return false;
    }
}
