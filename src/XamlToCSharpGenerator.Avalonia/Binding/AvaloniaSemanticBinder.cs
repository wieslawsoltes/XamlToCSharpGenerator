using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed class AvaloniaSemanticBinder : IXamlSemanticBinder
{
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string MarkupContextServiceProviderToken = "__AXSG_CTX_SERVICE_PROVIDER__";
    private const string MarkupContextRootObjectToken = "__AXSG_CTX_ROOT_OBJECT__";
    private const string MarkupContextIntermediateRootObjectToken = "__AXSG_CTX_INTERMEDIATE_ROOT_OBJECT__";
    private const string MarkupContextTargetObjectToken = "__AXSG_CTX_TARGET_OBJECT__";
    private const string MarkupContextTargetPropertyToken = "__AXSG_CTX_TARGET_PROPERTY__";
    private const string MarkupContextBaseUriToken = "__AXSG_CTX_BASE_URI__";
    private const string MarkupContextParentStackToken = "__AXSG_CTX_PARENT_STACK__";

    private static readonly string[] AvaloniaDefaultNamespaceCandidates =
    [
        "Avalonia.Controls.",
        "Avalonia.Markup.Xaml.Templates.",
        "Avalonia.Markup.Xaml.Styling.",
        "Avalonia.Styling.",
        "Avalonia.Controls.Templates.",
        "Avalonia.Layout.",
        "Avalonia.Media.",
        "Avalonia.Media.Imaging.",
        "Avalonia.Animation.",
        "Avalonia."
    ];

    private static readonly ItemContainerTypeMapping[] KnownItemContainerTypeMappings =
    [
        new("Avalonia.Controls.ListBox", "Avalonia.Controls.ListBoxItem"),
        new("Avalonia.Controls.ComboBox", "Avalonia.Controls.ComboBoxItem"),
        new("Avalonia.Controls.Menu", "Avalonia.Controls.MenuItem"),
        new("Avalonia.Controls.MenuItem", "Avalonia.Controls.MenuItem"),
        new("Avalonia.Controls.Primitives.TabStrip", "Avalonia.Controls.Primitives.TabStripItem"),
        new("Avalonia.Controls.TabControl", "Avalonia.Controls.TabItem"),
        new("Avalonia.Controls.TreeView", "Avalonia.Controls.TreeViewItem")
    ];

    private static readonly ImmutableArray<IAvaloniaTransformPass> TransformPasses =
    [
        new BindNamedElementsPass(),
        new BindRootObjectPass(),
        new BindResourcesPass(),
        new BindTemplatesPass(),
        new BindStylesPass(),
        new BindControlThemesPass(),
        new BindIncludesPass(),
        new FinalizeViewModelPass()
    ];

    public (ResolvedViewModel? ViewModel, ImmutableArray<DiagnosticInfo> Diagnostics) Bind(
        XamlDocumentModel document,
        Compilation compilation,
        GeneratorOptions options)
    {
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        var assemblyName = options.AssemblyName ?? compilation.AssemblyName ?? "UnknownAssembly";
        var uri = "avares://" + assemblyName + "/" + document.TargetPath;

        var context = new BindingTransformContext(
            document,
            compilation,
            options,
            uri,
            diagnostics);

        ExecuteTransformPasses(context);

        return (context.ViewModel, diagnostics.ToImmutable());
    }

    private static void ExecuteTransformPasses(BindingTransformContext context)
    {
        foreach (var pass in TransformPasses)
        {
            if (context.Options.TracePasses)
            {
                var upstream = pass.UpstreamTransformerIds.Length == 0
                    ? "none"
                    : string.Join(", ", pass.UpstreamTransformerIds);
                context.PassExecutionTrace.Add(pass.PassId + " => " + upstream);
            }

            pass.Execute(context);
        }
    }

    private interface IAvaloniaTransformPass
    {
        string PassId { get; }

        ImmutableArray<string> UpstreamTransformerIds { get; }

        void Execute(BindingTransformContext context);
    }

    private sealed class BindingTransformContext
    {
        public BindingTransformContext(
            XamlDocumentModel document,
            Compilation compilation,
            GeneratorOptions options,
            string buildUri,
            ImmutableArray<DiagnosticInfo>.Builder diagnostics)
        {
            Document = document;
            Compilation = compilation;
            Options = options;
            BuildUri = buildUri;
            Diagnostics = diagnostics;
            NamedElements = ImmutableArray.CreateBuilder<ResolvedNamedElement>(document.NamedElements.Length);
            CompiledBindings = ImmutableArray.CreateBuilder<ResolvedCompiledBindingDefinition>();
            Resources = ImmutableArray<ResolvedResourceDefinition>.Empty;
            Templates = ImmutableArray<ResolvedTemplateDefinition>.Empty;
            Styles = ImmutableArray<ResolvedStyleDefinition>.Empty;
            ControlThemes = ImmutableArray<ResolvedControlThemeDefinition>.Empty;
            Includes = ImmutableArray<ResolvedIncludeDefinition>.Empty;
        }

        public XamlDocumentModel Document { get; }

        public Compilation Compilation { get; }

        public GeneratorOptions Options { get; }

        public string BuildUri { get; }

        public ImmutableArray<DiagnosticInfo>.Builder Diagnostics { get; }

        public ImmutableArray<ResolvedNamedElement>.Builder NamedElements { get; }

        public ImmutableArray<ResolvedCompiledBindingDefinition>.Builder CompiledBindings { get; }

        public List<string> PassExecutionTrace { get; } = new();

        public INamedTypeSymbol? ClassSymbol { get; set; }

        public string ClassModifier { get; set; } = "internal";

        public ResolvedObjectNode? RootObject { get; set; }

        public ImmutableArray<ResolvedResourceDefinition> Resources { get; set; }

        public ImmutableArray<ResolvedTemplateDefinition> Templates { get; set; }

        public ImmutableArray<ResolvedStyleDefinition> Styles { get; set; }

        public ImmutableArray<ResolvedControlThemeDefinition> ControlThemes { get; set; }

        public ImmutableArray<ResolvedIncludeDefinition> Includes { get; set; }

        public bool EmitNameScopeRegistration { get; set; }

        public bool EmitStaticResourceResolver { get; set; }

        public ResolvedViewModel? ViewModel { get; set; }
    }

    private sealed class BindNamedElementsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P001-BindNamedElements";

        public ImmutableArray<string> UpstreamTransformerIds => ["XNameTransformer"];

        public void Execute(BindingTransformContext context)
        {
            foreach (var namedElement in context.Document.NamedElements)
            {
                var resolvedType = ResolveTypeName(
                    context.Compilation,
                    namedElement.XmlNamespace,
                    namedElement.XmlTypeName,
                    out var _) ?? "global::Avalonia.Controls.Control";

                context.NamedElements.Add(new ResolvedNamedElement(
                    Name: namedElement.Name,
                    TypeName: resolvedType,
                    FieldModifier: namedElement.FieldModifier ?? "internal",
                    Line: namedElement.Line,
                    Column: namedElement.Column));
            }
        }
    }

    private sealed class BindRootObjectPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P010-BindRootObject";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlClassesTransformer",
            "AvaloniaXamlIlResolveClassesPropertiesTransformer",
            "AvaloniaXamlIlAvaloniaPropertyResolver",
            "AvaloniaXamlIlTransformInstanceAttachedProperties",
            "AvaloniaXamlIlTransformRoutedEvent"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.ClassSymbol = context.Document.IsClassBacked
                ? context.Compilation.GetTypeByMetadataName(context.Document.ClassFullName!)
                : null;
            context.ClassModifier = ResolveGeneratedClassModifier(
                context.Document,
                context.ClassSymbol,
                context.Diagnostics,
                context.Options);

            var rootDataType = ResolveTypeFromTypeExpression(
                context.Compilation,
                context.Document,
                context.Document.RootObject.DataType,
                context.Document.ClassNamespace);

            var rootCompileBindings = context.Document.RootObject.CompileBindings ?? context.Options.UseCompiledBindingsByDefault;

            context.RootObject = BindObjectNode(
                node: context.Document.RootObject,
                compilation: context.Compilation,
                diagnostics: context.Diagnostics,
                document: context.Document,
                options: context.Options,
                compiledBindings: context.CompiledBindings,
                inheritedCompileBindingsEnabled: rootCompileBindings,
                inheritedDataType: rootDataType,
                inheritedSetterTargetType: null,
                inheritedBindingPriorityScope: BindingPriorityScope.None,
                forcedType: context.ClassSymbol,
                rootTypeSymbol: context.ClassSymbol);
        }
    }

    private sealed class BindResourcesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P020-BindResources";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlResourceTransformer",
            "AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Resources = BindResources(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class BindTemplatesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P030-BindTemplates";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer",
            "AvaloniaXamlIlControlTemplatePartsChecker",
            "AvaloniaXamlIlControlTemplatePriorityTransformer",
            "AvaloniaXamlIlDataTemplateWarningsTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Templates = BindTemplates(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class BindStylesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P040-BindStyles";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlSelectorTransformer",
            "AvaloniaXamlIlSetterTransformer",
            "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
            "AvaloniaXamlIlDuplicateSettersChecker",
            "AvaloniaXamlIlStyleValidatorTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Styles = BindStyles(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings);
        }
    }

    private sealed class BindControlThemesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P050-BindControlThemes";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIlControlThemeTransformer",
            "AvaloniaXamlIlSetterTransformer",
            "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
            "AvaloniaXamlIlDuplicateSettersChecker"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.ControlThemes = BindControlThemes(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings);
        }
    }

    private sealed class BindIncludesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P060-BindIncludes";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AvaloniaXamlIncludeTransformer",
            "XamlMergeResourceGroupTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            context.Includes = BindIncludes(
                context.Document,
                context.BuildUri,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class FinalizeViewModelPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P900-Finalize";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "AddNameScopeRegistration",
            "AvaloniaXamlIlRootObjectScope",
            "AvaloniaXamlIlAddSourceInfoTransformer"
        ];

        public void Execute(BindingTransformContext context)
        {
            var root = context.RootObject ?? BindObjectNode(
                node: context.Document.RootObject,
                compilation: context.Compilation,
                diagnostics: context.Diagnostics,
                document: context.Document,
                options: context.Options,
                compiledBindings: context.CompiledBindings,
                inheritedCompileBindingsEnabled: context.Document.RootObject.CompileBindings ?? context.Options.UseCompiledBindingsByDefault,
                inheritedDataType: ResolveTypeFromTypeExpression(
                    context.Compilation,
                    context.Document,
                    context.Document.RootObject.DataType,
                    context.Document.ClassNamespace),
                inheritedSetterTargetType: null,
                inheritedBindingPriorityScope: BindingPriorityScope.None,
                forcedType: context.ClassSymbol,
                rootTypeSymbol: context.ClassSymbol);

            context.EmitNameScopeRegistration = context.Compilation.GetTypeByMetadataName("Avalonia.Controls.NameScope") is not null &&
                                                context.Compilation.GetTypeByMetadataName("Avalonia.StyledElement") is not null &&
                                                context.NamedElements.Count > 0;
            context.EmitStaticResourceResolver = RequiresStaticResourceResolver(root);

            context.ViewModel = new ResolvedViewModel(
                Document: context.Document,
                BuildUri: context.BuildUri,
                ClassModifier: context.ClassModifier,
                CreateSourceInfo: context.Options.CreateSourceInfo,
                EnableHotReload: context.Options.HotReloadEnabled,
                PassExecutionTrace: context.Options.TracePasses
                    ? context.PassExecutionTrace.ToImmutableArray()
                    : ImmutableArray<string>.Empty,
                EmitNameScopeRegistration: context.EmitNameScopeRegistration,
                EmitStaticResourceResolver: context.EmitStaticResourceResolver,
                RootObject: root,
                NamedElements: context.NamedElements.ToImmutable(),
                Resources: context.Resources,
                Templates: context.Templates,
                CompiledBindings: context.CompiledBindings.ToImmutable(),
                Styles: context.Styles,
                ControlThemes: context.ControlThemes,
                Includes: context.Includes);
        }
    }

    private static string ResolveGeneratedClassModifier(
        XamlDocumentModel document,
        INamedTypeSymbol? classSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var normalizedRequested = NormalizeClassModifier(document.ClassModifier);
        if (!string.IsNullOrWhiteSpace(document.ClassModifier) && normalizedRequested is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0104",
                $"x:ClassModifier value '{document.ClassModifier}' is not supported.",
                document.FilePath,
                1,
                1,
                options.StrictMode));
        }

        if (classSymbol is not null)
        {
            var symbolModifier = ToCSharpClassModifier(classSymbol.DeclaredAccessibility);
            if (normalizedRequested is not null &&
                !normalizedRequested.Equals(symbolModifier, StringComparison.Ordinal))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0105",
                    $"x:ClassModifier '{normalizedRequested}' does not match class declaration accessibility '{symbolModifier}'.",
                    document.FilePath,
                    1,
                    1,
                    options.StrictMode));
            }

            return symbolModifier;
        }

        return normalizedRequested ?? "internal";
    }

    private static INamedTypeSymbol? ResolveCurrentSetterTargetType(
        INamedTypeSymbol? nodeType,
        XamlObjectNode node,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? inheritedSetterTargetType)
    {
        if (nodeType is null)
        {
            return inheritedSetterTargetType;
        }

        if (nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Avalonia.Styling.ControlTheme")
        {
            var targetTypeValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("TargetType", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(targetTypeValue))
            {
                var targetType = ResolveTypeFromTypeExpression(compilation, document, targetTypeValue, document.ClassNamespace);
                if (targetType is not null)
                {
                    return targetType;
                }
            }

            return inheritedSetterTargetType;
        }

        if (nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Avalonia.Styling.Style")
        {
            var selectorValue = node.PropertyAssignments
                .FirstOrDefault(assignment => NormalizePropertyName(assignment.PropertyName).Equals("Selector", StringComparison.Ordinal))
                ?.Value;
            if (!string.IsNullOrWhiteSpace(selectorValue))
            {
                var selectorTypeToken = TryExtractSelectorTypeToken(selectorValue!);
                if (!string.IsNullOrWhiteSpace(selectorTypeToken))
                {
                    var selectorType = ResolveTypeToken(compilation, document, selectorTypeToken!, document.ClassNamespace);
                    if (selectorType is not null)
                    {
                        return selectorType;
                    }
                }
            }

            return inheritedSetterTargetType;
        }

        return inheritedSetterTargetType;
    }

    private static INamedTypeSymbol? ResolveObjectTypeSymbol(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode node)
    {
        if (node.TypeArguments.IsDefaultOrEmpty)
        {
            return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        }

        var resolvedTypeArguments = new List<ITypeSymbol>(node.TypeArguments.Length);
        foreach (var argumentToken in node.TypeArguments)
        {
            var resolvedTypeArgument = ResolveTypeToken(compilation, document, argumentToken, document.ClassNamespace);
            if (resolvedTypeArgument is null)
            {
                return ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
            }

            resolvedTypeArguments.Add(resolvedTypeArgument);
        }

        var genericType = ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName, node.TypeArguments.Length) ??
                          ResolveTypeSymbol(compilation, node.XmlNamespace, node.XmlTypeName);
        if (genericType is null)
        {
            return null;
        }

        if (genericType.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.Construct(resolvedTypeArguments.ToArray());
        }

        if (genericType.OriginalDefinition.TypeParameters.Length == resolvedTypeArguments.Count)
        {
            return genericType.OriginalDefinition.Construct(resolvedTypeArguments.ToArray());
        }

        return genericType;
    }

    private static ResolvedObjectNode BindObjectNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? forcedType = null,
        INamedTypeSymbol? rootTypeSymbol = null)
    {
        var symbol = forcedType ?? ResolveObjectTypeSymbol(compilation, document, node);
        var typeName = symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";

        if (IsXamlArrayNode(node))
        {
            return BindXamlArrayNode(
                node,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol);
        }

        var compileBindingsEnabled = node.CompileBindings ?? inheritedCompileBindingsEnabled;
        var nodeDataType = ResolveTypeFromTypeExpression(compilation, document, node.DataType, document.ClassNamespace) ?? inheritedDataType;
        var currentSetterTargetType = ResolveCurrentSetterTargetType(
            symbol,
            node,
            compilation,
            document,
            inheritedSetterTargetType);
        var currentBindingPriorityScope = ResolveCurrentBindingPriorityScope(
            symbol,
            compilation,
            inheritedBindingPriorityScope);
        var contentPropertyName = FindContentPropertyName(symbol);
        var inferredSetterValueType = TryResolveSetterValueType(
            symbol,
            node.PropertyAssignments,
            compilation,
            document,
            currentSetterTargetType);

        var assignments = ImmutableArray.CreateBuilder<ResolvedPropertyAssignment>();
        var propertyElementAssignments = ImmutableArray.CreateBuilder<ResolvedPropertyElementAssignment>();
        var eventSubscriptions = ImmutableArray.CreateBuilder<ResolvedEventSubscription>();
        foreach (var assignment in node.PropertyAssignments)
        {
            if (symbol is null)
            {
                continue;
            }

            if (assignment.IsAttached)
            {
                if (TryBindAttachedPropertyAssignment(
                        assignment,
                        symbol,
                        typeName,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentBindingPriorityScope,
                        out var attachedAssignment))
                {
                    if (attachedAssignment is not null)
                    {
                        assignments.Add(attachedAssignment);
                    }

                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Attached property '{assignment.PropertyName}' could not be resolved on this scope.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedPropertyName = NormalizePropertyName(assignment.PropertyName);
            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is not null &&
                property.SetMethod is null &&
                TryBindCollectionLiteralPropertyAssignment(
                    symbol,
                    property,
                    assignment,
                    compilation,
                    out var collectionLiteralAssignment))
            {
                if (collectionLiteralAssignment is not null)
                {
                    propertyElementAssignments.Add(collectionLiteralAssignment);
                }

                continue;
            }

            if (property is not null && property.SetMethod is not null)
            {
                if (TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
                {
                    if (TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            assignment.Line,
                            assignment.Column,
                            options.StrictMode))
                    {
                        continue;
                    }

                    var shouldCompileBinding = CanUseCompiledBinding(bindingMarkup) &&
                                               (bindingMarkup.IsCompiledBinding || compileBindingsEnabled);
                    if (shouldCompileBinding)
                    {
                        if (nodeDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for '{property.Name}' requires x:DataType in scope.",
                                document.FilePath,
                                assignment.Line,
                                assignment.Column,
                                options.StrictMode));
                            continue;
                        }

                        if (!TryBuildCompiledBindingAccessorExpression(
                                compilation,
                                document,
                                nodeDataType,
                                bindingMarkup.Path,
                                out var accessorExpression,
                                out var normalizedPath,
                                out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{nodeDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                assignment.Line,
                                assignment.Column,
                                options.StrictMode));
                            continue;
                        }

                        compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                            TargetTypeName: typeName,
                            TargetPropertyName: property.Name,
                            Path: normalizedPath,
                            SourceTypeName: nodeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            AccessorExpression: accessorExpression,
                            IsSetterBinding: false,
                            Line: assignment.Line,
                            Column: assignment.Column));
                    }

                    if (TryBindAvaloniaPropertyAssignment(
                            symbol,
                            typeName,
                            normalizedPropertyName,
                            assignment,
                            compilation,
                            document,
                            options,
                            diagnostics,
                            compiledBindings,
                            compileBindingsEnabled,
                            nodeDataType,
                            property.Type,
                            currentBindingPriorityScope,
                            out var bindingAssignment,
                            allowCompiledBindingRegistration: false))
                    {
                        if (bindingAssignment is not null)
                        {
                            assignments.Add(bindingAssignment);
                        }

                        continue;
                    }

                    if (TryBuildBindingValueExpression(
                            compilation,
                            document,
                            bindingMarkup,
                            property.Type,
                            currentSetterTargetType,
                            currentBindingPriorityScope,
                            out var runtimeBindingExpression))
                    {
                        assignments.Add(new ResolvedPropertyAssignment(
                            PropertyName: property.Name,
                            ValueExpression: runtimeBindingExpression,
                            AvaloniaPropertyOwnerTypeName: null,
                            AvaloniaPropertyFieldName: null,
                            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            BindingPriorityExpression: null,
                            Line: assignment.Line,
                            Column: assignment.Column));
                        continue;
                    }

                    if (shouldCompileBinding)
                    {
                        continue;
                    }
                }

                if (currentBindingPriorityScope == BindingPriorityScope.Template &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        out var templatePriorityAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (templatePriorityAssignment is not null)
                    {
                        assignments.Add(templatePriorityAssignment);
                    }

                    continue;
                }

                if (LooksLikeMarkupExtension(assignment.Value) &&
                    TryBindAvaloniaPropertyAssignment(
                        symbol,
                        typeName,
                        normalizedPropertyName,
                        assignment,
                        compilation,
                        document,
                        options,
                        diagnostics,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        property.Type,
                        currentBindingPriorityScope,
                        out var markupExtensionAssignment,
                        allowCompiledBindingRegistration: false))
                {
                    if (markupExtensionAssignment is not null)
                    {
                        assignments.Add(markupExtensionAssignment);
                    }

                    continue;
                }

                var conversionTargetType = property.Type;
                if (property.Name.Equals("Value", StringComparison.Ordinal) &&
                    inferredSetterValueType is not null &&
                    conversionTargetType.SpecialType == SpecialType.System_Object)
                {
                    conversionTargetType = inferredSetterValueType;
                }

                if (!TryConvertValueExpression(
                        assignment.Value,
                        conversionTargetType,
                        compilation,
                        document,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var valueExpression))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0102",
                        $"Could not convert literal '{assignment.Value}' for '{property.Name}' on '{symbol.ToDisplayString()}'.",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    continue;
                }

                assignments.Add(new ResolvedPropertyAssignment(
                    PropertyName: property.Name,
                    ValueExpression: valueExpression,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    BindingPriorityExpression: null,
                    Line: assignment.Line,
                    Column: assignment.Column));
                continue;
            }

            if (TryBindEventSubscription(
                    symbol,
                    assignment,
                    compilation,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out var eventSubscription))
            {
                if (eventSubscription is not null)
                {
                    eventSubscriptions.Add(eventSubscription);
                }

                continue;
            }

            if (TryBindAvaloniaPropertyAssignment(
                    symbol,
                    typeName,
                    normalizedPropertyName,
                    assignment,
                    compilation,
                    document,
                    options,
                    diagnostics,
                    compiledBindings,
                    compileBindingsEnabled,
                    nodeDataType,
                    property?.Type,
                    currentBindingPriorityScope,
                    out var fallbackAssignment))
            {
                if (fallbackAssignment is not null)
                {
                    assignments.Add(fallbackAssignment);
                }

                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property '{assignment.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        var children = ImmutableArray.CreateBuilder<ResolvedObjectNode>();
        foreach (var child in node.ChildObjects)
        {
            children.Add(BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol));
        }

        ResolvedChildAttachmentMode? explicitAttachment = null;
        string? explicitContentPropertyName = null;
        foreach (var propertyElement in node.PropertyElements)
        {
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                propertyElement.PropertyName.Equals(contentPropertyName, StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.Content;
                explicitContentPropertyName = contentPropertyName;
                foreach (var value in propertyElement.ObjectValues)
                {
                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (propertyElement.PropertyName.Equals("Children", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ChildrenCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (propertyElement.PropertyName.Equals("Items", StringComparison.Ordinal))
            {
                explicitAttachment = ResolvedChildAttachmentMode.ItemsCollection;
                foreach (var value in propertyElement.ObjectValues)
                {
                    children.Add(BindObjectNode(
                        value,
                        compilation,
                        diagnostics,
                        document,
                        options,
                        compiledBindings,
                        compileBindingsEnabled,
                        nodeDataType,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        rootTypeSymbol: rootTypeSymbol));
                }

                continue;
            }

            if (symbol is null)
            {
                continue;
            }

            var normalizedPropertyName = NormalizePropertyName(propertyElement.PropertyName);
            var property = FindProperty(symbol, normalizedPropertyName);
            if (property is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Property element '{propertyElement.PropertyName}' was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    propertyElement.Line,
                    propertyElement.Column,
                    options.StrictMode));
                continue;
            }

            ValidateItemContainerInsideTemplateWarning(
                symbol,
                property,
                propertyElement,
                compilation,
                document,
                diagnostics,
                options);

            if (propertyElement.ObjectValues.Length == 0)
            {
                continue;
            }

            var elementValues = ImmutableArray.CreateBuilder<ResolvedObjectNode>(propertyElement.ObjectValues.Length);
            foreach (var value in propertyElement.ObjectValues)
            {
                elementValues.Add(BindObjectNode(
                    value,
                    compilation,
                    diagnostics,
                    document,
                    options,
                    compiledBindings,
                    compileBindingsEnabled,
                    nodeDataType,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    rootTypeSymbol: rootTypeSymbol));
            }

            var elementValuesArray = elementValues.ToImmutable();
            var canMergeDictionaryProperty = CanMergeDictionaryProperty(symbol, property.Name);
            if (canMergeDictionaryProperty &&
                TryBuildKeyedDictionaryMergeContainer(
                    property,
                    elementValuesArray,
                    propertyElement.Line,
                    propertyElement.Column,
                    out var dictionaryMergeContainer))
            {
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: ImmutableArray.Create(dictionaryMergeContainer),
                    Line: propertyElement.Line,
                    Column: propertyElement.Column));
                continue;
            }

            if (property.SetMethod is not null)
            {
                if (elementValuesArray.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column));
                continue;
            }

            if (CanAddToCollectionProperty(symbol, property.Name))
            {
                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    BindingPriorityExpression: null,
                    IsCollectionAdd: true,
                    IsDictionaryMerge: false,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column));
                continue;
            }

            if (canMergeDictionaryProperty)
            {
                if (elementValuesArray.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Dictionary property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: null,
                    AvaloniaPropertyFieldName: null,
                    BindingPriorityExpression: null,
                    IsCollectionAdd: false,
                    IsDictionaryMerge: true,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column));
                continue;
            }

            if (TryFindAvaloniaPropertyField(symbol, property.Name, out var ownerType, out var propertyField))
            {
                if (elementValuesArray.Length != 1)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0103",
                        $"Avalonia property element '{propertyElement.PropertyName}' requires exactly one object value.",
                        document.FilePath,
                        propertyElement.Line,
                        propertyElement.Column,
                        options.StrictMode));
                    continue;
                }

                propertyElementAssignments.Add(new ResolvedPropertyElementAssignment(
                    PropertyName: property.Name,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        symbol,
                        propertyField,
                        compilation,
                        currentBindingPriorityScope),
                    IsCollectionAdd: false,
                    IsDictionaryMerge: false,
                    ObjectValues: elementValuesArray,
                    Line: propertyElement.Line,
                    Column: propertyElement.Column));
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0101",
                $"Property element '{propertyElement.PropertyName}' is not supported on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                propertyElement.Line,
                propertyElement.Column,
                options.StrictMode));
        }

        string? factoryExpression = null;
        if (TryBuildExplicitConstructionExpression(
                node,
                symbol,
                typeName,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                compileBindingsEnabled,
                nodeDataType,
                currentSetterTargetType,
                currentBindingPriorityScope,
                rootTypeSymbol,
                out var explicitConstructionExpression))
        {
            factoryExpression = explicitConstructionExpression;
        }

        if (symbol is not null &&
            string.IsNullOrWhiteSpace(factoryExpression) &&
            !string.IsNullOrWhiteSpace(node.TextContent) &&
            children.Count == 0 &&
            propertyElementAssignments.Count == 0)
        {
            var inlineTextContent = node.TextContent!.Trim();
            var handledAsContentProperty = false;
            if (!string.IsNullOrWhiteSpace(contentPropertyName) &&
                !HasResolvedPropertyAssignment(assignments, contentPropertyName!))
            {
                var contentProperty = FindProperty(symbol, contentPropertyName!);
                if (contentProperty?.SetMethod is not null &&
                    TryConvertValueExpression(
                        inlineTextContent,
                        contentProperty.Type,
                        compilation,
                        document,
                        currentSetterTargetType,
                        currentBindingPriorityScope,
                        out var inlineContentExpression))
                {
                    assignments.Add(new ResolvedPropertyAssignment(
                        PropertyName: contentProperty.Name,
                        ValueExpression: inlineContentExpression,
                        AvaloniaPropertyOwnerTypeName: null,
                        AvaloniaPropertyFieldName: null,
                        ClrPropertyOwnerTypeName: contentProperty.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ClrPropertyTypeName: contentProperty.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BindingPriorityExpression: null,
                        Line: node.Line,
                        Column: node.Column));
                    handledAsContentProperty = true;
                }
            }

            if (!handledAsContentProperty &&
                assignments.Count == 0 &&
                TryConvertValueExpression(
                    inlineTextContent,
                    symbol,
                    compilation,
                    document,
                    currentSetterTargetType,
                    currentBindingPriorityScope,
                    out var inlineFactoryExpression))
            {
                factoryExpression = inlineFactoryExpression;
            }
        }

        var (defaultAttachmentMode, defaultContentPropertyName) = DetermineChildAttachment(symbol);
        var attachmentMode = explicitAttachment ?? defaultAttachmentMode;
        var resolvedContentPropertyName = attachmentMode == ResolvedChildAttachmentMode.Content
            ? explicitContentPropertyName ?? defaultContentPropertyName
            : null;

        if (attachmentMode == ResolvedChildAttachmentMode.Content && children.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0103",
                "More than one child object was found for a Content attachment target. Only the first child will be attached.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
        }

        if (attachmentMode == ResolvedChildAttachmentMode.DictionaryAdd)
        {
            foreach (var child in children)
            {
                if (!string.IsNullOrWhiteSpace(child.KeyExpression))
                {
                    continue;
                }

                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Type '{typeName}' requires x:Key for child objects added via dictionary Add(key, value).",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                break;
            }
        }

        var useServiceProviderConstructor = ShouldUseServiceProviderConstructor(symbol);
        var useTopDownInitialization = IsUsableDuringInitialization(symbol);

        return new ResolvedObjectNode(
            KeyExpression: string.IsNullOrWhiteSpace(node.Key) ? null : "\"" + Escape(node.Key!) + "\"",
            Name: node.Name,
            TypeName: typeName,
            FactoryExpression: factoryExpression,
            UseServiceProviderConstructor: useServiceProviderConstructor,
            UseTopDownInitialization: useTopDownInitialization,
            PropertyAssignments: assignments.ToImmutable(),
            PropertyElementAssignments: propertyElementAssignments.ToImmutable(),
            EventSubscriptions: eventSubscriptions.ToImmutable(),
            Children: children.ToImmutable(),
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: resolvedContentPropertyName,
            Line: node.Line,
            Column: node.Column);
    }

    private static bool IsXamlArrayNode(XamlObjectNode node)
    {
        return node.XmlNamespace == Xaml2006.NamespaceName &&
               node.XmlTypeName.Equals("Array", StringComparison.Ordinal);
    }

    private static ResolvedObjectNode BindXamlArrayNode(
        XamlObjectNode node,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol)
    {
        var elementType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            node.ArrayItemType,
            document.ClassNamespace);
        if (elementType is null && node.TypeArguments.Length > 0)
        {
            elementType = ResolveTypeToken(compilation, document, node.TypeArguments[0], document.ClassNamespace);
        }

        var elementTypeName = elementType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
        var valueExpressions = new List<string>(node.ChildObjects.Length);

        foreach (var child in node.ChildObjects)
        {
            var boundChild = BindObjectNode(
                child,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!TryBuildInlineResolvedObjectExpression(boundChild, out var inlineChildExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0108",
                    "x:Array values must be inline-constructable objects when used in source-generated construction.",
                    document.FilePath,
                    child.Line,
                    child.Column,
                    options.StrictMode));
                continue;
            }

            valueExpressions.Add(inlineChildExpression);
        }

        var factoryExpression = valueExpressions.Count == 0
            ? "global::System.Array.Empty<" + elementTypeName + ">()"
            : "new " + elementTypeName + "[] { " + string.Join(", ", valueExpressions) + " }";

        return new ResolvedObjectNode(
            KeyExpression: string.IsNullOrWhiteSpace(node.Key) ? null : "\"" + Escape(node.Key!) + "\"",
            Name: node.Name,
            TypeName: "global::System.Array",
            FactoryExpression: factoryExpression,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: node.Line,
            Column: node.Column);
    }

    private static bool TryBuildExplicitConstructionExpression(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        string typeName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        out string? expression)
    {
        expression = null;
        var hasConstructionDirectives = node.ConstructorArguments.Length > 0 ||
                                        !string.IsNullOrWhiteSpace(node.FactoryMethod);
        if (!hasConstructionDirectives)
        {
            return false;
        }

        if (symbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0106",
                $"Could not resolve type '{node.XmlTypeName}' for construction directives.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        var arguments = new List<string>(node.ConstructorArguments.Length);
        foreach (var argumentNode in node.ConstructorArguments)
        {
            var resolvedArgument = BindObjectNode(
                argumentNode,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!TryBuildInlineResolvedObjectExpression(resolvedArgument, out var argumentExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0106",
                    "x:Arguments value is not inline-constructable for source-generated construction.",
                    document.FilePath,
                    argumentNode.Line,
                    argumentNode.Column,
                    options.StrictMode));
                return true;
            }

            arguments.Add(argumentExpression);
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryMethod))
        {
            var factoryMethod = TryFindMatchingFactoryMethod(symbol, node.FactoryMethod!, arguments.Count);
            if (factoryMethod is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0107",
                    $"x:FactoryMethod '{node.FactoryMethod}' with {arguments.Count} argument(s) was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                return true;
            }

            expression = factoryMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         factoryMethod.Name +
                         "(" +
                         string.Join(", ", arguments) +
                         ")";
            return true;
        }

        var constructor = TryFindMatchingConstructor(symbol, arguments.Count);
        if (constructor is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0107",
                $"No constructor with {arguments.Count} argument(s) was found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        expression = "new " + typeName + "(" + string.Join(", ", arguments) + ")";
        return true;
    }

    private static bool TryBuildInlineResolvedObjectExpression(
        ResolvedObjectNode node,
        out string expression)
    {
        expression = string.Empty;
        if (!string.IsNullOrWhiteSpace(node.FactoryExpression))
        {
            if (ContainsMarkupContextTokens(node.FactoryExpression!))
            {
                return false;
            }

            expression = node.FactoryExpression!;
            return true;
        }

        if (node.UseServiceProviderConstructor ||
            node.Children.Length > 0 ||
            node.PropertyElementAssignments.Length > 0 ||
            node.EventSubscriptions.Length > 0)
        {
            return false;
        }

        if (node.PropertyAssignments.Length == 0)
        {
            expression = "new " + node.TypeName + "()";
            return true;
        }

        var initializers = new List<string>(node.PropertyAssignments.Length);
        foreach (var assignment in node.PropertyAssignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyOwnerTypeName) ||
                !string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyFieldName) ||
                ContainsMarkupContextTokens(assignment.ValueExpression))
            {
                return false;
            }

            initializers.Add(assignment.PropertyName + " = " + assignment.ValueExpression);
        }

        expression = "new " + node.TypeName + "() { " + string.Join(", ", initializers) + " }";
        return true;
    }

    private static bool ContainsMarkupContextTokens(string expression)
    {
        return expression.Contains(MarkupContextServiceProviderToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextRootObjectToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextIntermediateRootObjectToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextTargetObjectToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextTargetPropertyToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextBaseUriToken, StringComparison.Ordinal) ||
               expression.Contains(MarkupContextParentStackToken, StringComparison.Ordinal);
    }

    private static IMethodSymbol? TryFindMatchingFactoryMethod(INamedTypeSymbol type, string methodName, int argumentCount)
    {
        return type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsGenericMethod &&
                !method.ReturnsVoid)
            .Where(method => method.Parameters.Length == argumentCount)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IMethodSymbol? TryFindMatchingConstructor(INamedTypeSymbol type, int argumentCount)
    {
        return type.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic)
            .Where(constructor => constructor.Parameters.Length == argumentCount)
            .OrderBy(static constructor => constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static ImmutableArray<ResolvedStyleDefinition> BindStyles(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var styles = ImmutableArray.CreateBuilder<ResolvedStyleDefinition>(document.Styles.Length);

        foreach (var style in document.Styles)
        {
            var selector = style.Selector.Trim();
            INamedTypeSymbol? targetType = null;

            if (string.IsNullOrWhiteSpace(selector))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0300",
                    "Style selector cannot be empty.",
                    document.FilePath,
                    style.SelectorLine,
                    style.SelectorColumn,
                    options.StrictMode));
            }
            else
            {
                var selectorValidation = SelectorSyntaxValidator.Validate(selector);
                if (!selectorValidation.IsValid)
                {
                    var (line, column) = AdvanceLineAndColumn(
                        style.SelectorLine,
                        style.SelectorColumn,
                        selector,
                        selectorValidation.ErrorOffset);
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0300",
                        "Unable to parse selector: " + selectorValidation.ErrorMessage,
                        document.FilePath,
                        line,
                        column,
                        options.StrictMode));
                }
                else
                {
                    targetType = TryResolveSelectorTargetType(
                        compilation,
                        document,
                        selectorValidation.Branches,
                        out var unresolvedTypeToken,
                        out var unresolvedTypeOffset);
                    if (!string.IsNullOrWhiteSpace(unresolvedTypeToken))
                    {
                        var (line, column) = AdvanceLineAndColumn(
                            style.SelectorLine,
                            style.SelectorColumn,
                            selector,
                            unresolvedTypeOffset);
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0300",
                            $"Style selector target '{unresolvedTypeToken}' could not be resolved.",
                            document.FilePath,
                            line,
                            column,
                            options.StrictMode));
                    }
                }
            }

            var styleDataType = ResolveTypeFromTypeExpression(compilation, document, style.DataType, document.ClassNamespace);
            var compileBindingsEnabled = style.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(style.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in style.Setters)
            {
                var normalizedPropertyName = NormalizePropertyName(setter.PropertyName);
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0301",
                            $"Style setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                            document.FilePath,
                            setter.Line,
                            setter.Column,
                            options.StrictMode));
                    }
                    else
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                if (!seenSetterProperties.Add(resolvedPropertyName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"Style setter property '{resolvedPropertyName}' is duplicated in selector '{selector}'.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = "\"" + Escape(setter.Value) + "\"";
                var setterValueType = targetProperty?.Type;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                if (targetType is not null &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var stylePropertyOwnerType, out var stylePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        stylePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = stylePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(stylePropertyField.Type);
                }

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (styleDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for style setter '{setter.PropertyName}' requires x:DataType on the style.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     styleDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{styleDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = styleDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                if (TryConvertValueExpression(
                        setter.Value,
                        conversionTargetType,
                        compilation,
                        document,
                        targetType,
                        BindingPriorityScope.Style,
                        out var convertedSetterValue))
                {
                    valueExpression = convertedSetterValue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column));
            }

            styles.Add(new ResolvedStyleDefinition(
                Key: style.Key,
                Selector: selector,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Setters: setters.ToImmutable(),
                RawXaml: style.RawXaml,
                Line: style.Line,
                Column: style.Column));
        }

        return styles.ToImmutable();
    }

    private static ImmutableArray<ResolvedControlThemeDefinition> BindControlThemes(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings)
    {
        var controlThemes = ImmutableArray.CreateBuilder<ResolvedControlThemeDefinition>(document.ControlThemes.Length);

        foreach (var controlTheme in document.ControlThemes)
        {
            var targetType = ResolveTypeFromTypeExpression(
                compilation,
                document,
                controlTheme.TargetType,
                document.ClassNamespace);
            var themeVariant = string.IsNullOrWhiteSpace(controlTheme.ThemeVariant)
                ? null
                : controlTheme.ThemeVariant!.Trim();

            if (!string.IsNullOrWhiteSpace(controlTheme.TargetType) && targetType is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0302",
                    $"ControlTheme target type '{controlTheme.TargetType}' could not be resolved.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }

            var themeDataType = ResolveTypeFromTypeExpression(compilation, document, controlTheme.DataType, document.ClassNamespace);
            var compileBindingsEnabled = controlTheme.CompileBindings ?? options.UseCompiledBindingsByDefault;

            var setters = ImmutableArray.CreateBuilder<ResolvedSetterDefinition>(controlTheme.Setters.Length);
            var seenSetterProperties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var setter in controlTheme.Setters)
            {
                var normalizedPropertyName = NormalizePropertyName(setter.PropertyName);
                var resolvedPropertyName = normalizedPropertyName;
                IPropertySymbol? targetProperty = null;

                if (targetType is not null)
                {
                    targetProperty = FindProperty(targetType, normalizedPropertyName);
                    if (targetProperty is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0303",
                            $"ControlTheme setter property '{setter.PropertyName}' was not found on '{targetType.ToDisplayString()}'.",
                            document.FilePath,
                            setter.Line,
                            setter.Column,
                            options.StrictMode));
                    }
                    else
                    {
                        resolvedPropertyName = targetProperty.Name;
                    }
                }

                if (!seenSetterProperties.Add(resolvedPropertyName))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0304",
                        $"ControlTheme setter property '{resolvedPropertyName}' is duplicated.",
                        document.FilePath,
                        setter.Line,
                        setter.Column,
                        options.StrictMode));
                }

                var valueExpression = "\"" + Escape(setter.Value) + "\"";
                var setterValueType = targetProperty?.Type;
                string? setterPropertyOwnerTypeName = null;
                string? setterPropertyFieldName = null;
                if (targetType is not null &&
                    TryFindAvaloniaPropertyField(targetType, resolvedPropertyName, out var themePropertyOwnerType, out var themePropertyField))
                {
                    setterPropertyOwnerTypeName =
                        themePropertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    setterPropertyFieldName = themePropertyField.Name;
                    setterValueType ??= TryGetAvaloniaPropertyValueType(themePropertyField.Type);
                }

                var isCompiledBinding = false;
                string? compiledBindingPath = null;
                string? compiledBindingSourceType = null;

                if (TryParseBindingMarkup(setter.Value, out var bindingMarkup))
                {
                    if (!TryReportBindingSourceConflict(
                            bindingMarkup,
                            diagnostics,
                            document,
                            setter.Line,
                            setter.Column,
                            options.StrictMode) &&
                        CanUseCompiledBinding(bindingMarkup) &&
                        (bindingMarkup.IsCompiledBinding || compileBindingsEnabled))
                    {
                        if (themeDataType is null)
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0110",
                                $"Compiled binding for control theme setter '{setter.PropertyName}' requires x:DataType on the control theme.",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else if (!TryBuildCompiledBindingAccessorExpression(
                                     compilation,
                                     document,
                                     themeDataType,
                                     bindingMarkup.Path,
                                     out var accessorExpression,
                                     out var normalizedPath,
                                     out var errorMessage))
                        {
                            diagnostics.Add(new DiagnosticInfo(
                                "AXSG0111",
                                $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{themeDataType.ToDisplayString()}': {errorMessage}",
                                document.FilePath,
                                setter.Line,
                                setter.Column,
                                options.StrictMode));
                        }
                        else
                        {
                            var targetTypeName = targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object";
                            compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                                TargetTypeName: targetTypeName,
                                TargetPropertyName: resolvedPropertyName,
                                Path: normalizedPath,
                                SourceTypeName: themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                AccessorExpression: accessorExpression,
                                IsSetterBinding: true,
                                Line: setter.Line,
                                Column: setter.Column));

                            isCompiledBinding = true;
                            compiledBindingPath = normalizedPath;
                            compiledBindingSourceType = themeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        }
                    }
                }

                var conversionTargetType = setterValueType ?? compilation.GetSpecialType(SpecialType.System_Object);
                if (TryConvertValueExpression(
                        setter.Value,
                        conversionTargetType,
                        compilation,
                        document,
                        targetType,
                        BindingPriorityScope.Style,
                        out var convertedSetterValue))
                {
                    valueExpression = convertedSetterValue;
                }

                setters.Add(new ResolvedSetterDefinition(
                    PropertyName: resolvedPropertyName,
                    ValueExpression: valueExpression,
                    IsCompiledBinding: isCompiledBinding,
                    CompiledBindingPath: compiledBindingPath,
                    CompiledBindingSourceTypeName: compiledBindingSourceType,
                    AvaloniaPropertyOwnerTypeName: setterPropertyOwnerTypeName,
                    AvaloniaPropertyFieldName: setterPropertyFieldName,
                    Line: setter.Line,
                    Column: setter.Column));
            }

            controlThemes.Add(new ResolvedControlThemeDefinition(
                Key: controlTheme.Key,
                TargetTypeName: targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                BasedOn: controlTheme.BasedOn,
                ThemeVariant: themeVariant,
                Setters: setters.ToImmutable(),
                RawXaml: controlTheme.RawXaml,
                Line: controlTheme.Line,
                Column: controlTheme.Column));
        }

        var resolvedControlThemes = controlThemes.ToImmutable();
        ValidateControlThemeBasedOnChains(
            resolvedControlThemes,
            diagnostics,
            document,
            options);

        return resolvedControlThemes;
    }

    private static void ValidateControlThemeBasedOnChains(
        ImmutableArray<ResolvedControlThemeDefinition> controlThemes,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options)
    {
        if (controlThemes.IsDefaultOrEmpty)
        {
            return;
        }

        var byKey = new Dictionary<string, ResolvedControlThemeDefinition>(StringComparer.Ordinal);
        foreach (var controlTheme in controlThemes)
        {
            if (string.IsNullOrWhiteSpace(controlTheme.Key))
            {
                continue;
            }

            byKey[controlTheme.Key.Trim()] = controlTheme;
        }

        foreach (var controlTheme in controlThemes)
        {
            var basedOnKey = TryExtractControlThemeBasedOnKey(controlTheme.BasedOn);
            if (string.IsNullOrWhiteSpace(basedOnKey))
            {
                continue;
            }

            if (!byKey.ContainsKey(basedOnKey))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0305",
                    $"ControlTheme '{controlTheme.Key ?? "<unnamed>"}' references missing BasedOn theme key '{basedOnKey}'.",
                    document.FilePath,
                    controlTheme.Line,
                    controlTheme.Column,
                    options.StrictMode));
            }
        }

        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in byKey.Keys)
        {
            DetectCycle(key, key);
        }

        void DetectCycle(string key, string startKey)
        {
            if (!byKey.TryGetValue(key, out var currentTheme))
            {
                return;
            }

            if (state.TryGetValue(key, out var currentState))
            {
                if (currentState == 2)
                {
                    return;
                }

                if (currentState == 1)
                {
                    var cycleKey = startKey + "->" + key;
                    if (emitted.Add(cycleKey))
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0306",
                            $"ControlTheme BasedOn chain contains a cycle at key '{key}'.",
                            document.FilePath,
                            currentTheme.Line,
                            currentTheme.Column,
                            true));
                    }

                    return;
                }
            }

            state[key] = 1;
            var basedOnKey = TryExtractControlThemeBasedOnKey(currentTheme.BasedOn);
            if (!string.IsNullOrWhiteSpace(basedOnKey) && byKey.ContainsKey(basedOnKey))
            {
                DetectCycle(basedOnKey, startKey);
            }

            state[key] = 2;
        }
    }

    private static string? TryExtractControlThemeBasedOnKey(string? basedOnExpression)
    {
        if (string.IsNullOrWhiteSpace(basedOnExpression))
        {
            return null;
        }

        var trimmed = basedOnExpression.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) &&
            trimmed.EndsWith("}", StringComparison.Ordinal) &&
            TryParseMarkupExtension(trimmed, out var markup))
        {
            var markupName = markup.Name.ToLowerInvariant();
            if (markupName != "staticresource" && markupName != "dynamicresource")
            {
                return null;
            }

            var resourceKey = TryGetNamedMarkupArgument(markup, "ResourceKey", "Key");
            if (string.IsNullOrWhiteSpace(resourceKey) && markup.PositionalArguments.Length > 0)
            {
                resourceKey = Unquote(markup.PositionalArguments[0]);
            }

            return string.IsNullOrWhiteSpace(resourceKey)
                ? null
                : resourceKey.Trim();
        }

        return Unquote(trimmed);
    }

    private static ImmutableArray<ResolvedIncludeDefinition> BindIncludes(
        XamlDocumentModel document,
        string currentDocumentUri,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var includes = ImmutableArray.CreateBuilder<ResolvedIncludeDefinition>(document.Includes.Length);

        foreach (var include in document.Includes)
        {
            if (string.IsNullOrWhiteSpace(include.Source))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0400",
                    $"Include '{include.Kind}' is missing Source.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            var normalizedIncludeSource = NormalizeIncludeSourceForResolution(include.Source);
            if (!Uri.TryCreate(normalizedIncludeSource, UriKind.RelativeOrAbsolute, out var sourceUri))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0401",
                    $"Include source '{include.Source}' is not a valid URI.",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    options.StrictMode));
                continue;
            }

            if (include.MergeTarget == "Unknown")
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0402",
                    $"Include '{include.Kind}' is outside known merge groups (MergedDictionaries/Styles).",
                    document.FilePath,
                    include.Line,
                    include.Column,
                    false));
            }

            var resolvedIncludeUri = ResolveIncludedBuildUri(
                normalizedIncludeSource,
                document.TargetPath,
                currentDocumentUri,
                out var isProjectLocalInclude);

            includes.Add(new ResolvedIncludeDefinition(
                Kind: include.Kind,
                Source: include.Source,
                MergeTarget: include.MergeTarget,
                IsAbsoluteUri: sourceUri.IsAbsoluteUri,
                ResolvedSourceUri: resolvedIncludeUri,
                IsProjectLocal: isProjectLocalInclude,
                RawXaml: include.RawXaml,
                Line: include.Line,
                Column: include.Column));
        }

        return includes.ToImmutable();
    }

    private static string? ResolveIncludedBuildUri(
        string includeSource,
        string currentTargetPath,
        string currentDocumentUri,
        out bool isProjectLocal)
    {
        isProjectLocal = false;
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return null;
        }

        var trimmedSource = includeSource.Trim();
        if (!Uri.TryCreate(currentDocumentUri, UriKind.Absolute, out var currentUri) ||
            !currentUri.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var currentAssembly = currentUri.Host;
        if (string.IsNullOrWhiteSpace(currentAssembly))
        {
            return null;
        }

        if (trimmedSource.StartsWith("/", StringComparison.Ordinal))
        {
            var normalizedRootedPath = NormalizeIncludePath(trimmedSource.TrimStart('/'));
            if (normalizedRootedPath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedRootedPath;
        }

        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var absoluteSource))
        {
            if (!absoluteSource.Scheme.Equals("avares", StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            if (!string.Equals(absoluteSource.Host, currentAssembly, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteSource.ToString();
            }

            var normalizedAbsolutePath = NormalizeIncludePath(absoluteSource.AbsolutePath.TrimStart('/'));
            if (normalizedAbsolutePath.Length == 0)
            {
                return null;
            }

            isProjectLocal = true;
            return "avares://" + currentAssembly + "/" + normalizedAbsolutePath;
        }

        var normalizedCurrentPath = NormalizeIncludePath(currentTargetPath);
        var currentDirectory = GetIncludeDirectory(normalizedCurrentPath);
        var normalizedIncludePath = trimmedSource.StartsWith("/", StringComparison.Ordinal)
            ? NormalizeIncludePath(trimmedSource.TrimStart('/'))
            : NormalizeIncludePath(CombineIncludePath(currentDirectory, trimmedSource));

        if (normalizedIncludePath.Length == 0)
        {
            return null;
        }

        isProjectLocal = true;
        return "avares://" + currentAssembly + "/" + normalizedIncludePath;
    }

    private static string NormalizeIncludeSourceForResolution(string includeSource)
    {
        if (string.IsNullOrWhiteSpace(includeSource))
        {
            return includeSource;
        }

        var trimmedSource = includeSource.Trim();
        if (!TryParseMarkupExtension(trimmedSource, out var markup))
        {
            return trimmedSource;
        }

        var markupName = markup.Name.ToLowerInvariant();
        if (markupName is not ("x:uri" or "uri"))
        {
            return trimmedSource;
        }

        var uriToken = markup.NamedArguments.TryGetValue("Uri", out var explicitUri)
            ? explicitUri
            : markup.NamedArguments.TryGetValue("Value", out var explicitValue)
                ? explicitValue
                : markup.PositionalArguments.Length > 0
                    ? markup.PositionalArguments[0]
                    : null;

        return string.IsNullOrWhiteSpace(uriToken)
            ? trimmedSource
            : Unquote(uriToken!).Trim();
    }

    private static string GetIncludeDirectory(string targetPath)
    {
        var lastSeparator = targetPath.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return string.Empty;
        }

        return targetPath.Substring(0, lastSeparator);
    }

    private static string CombineIncludePath(string baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return relativePath;
        }

        return baseDirectory + "/" + relativePath;
    }

    private static string NormalizeIncludePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalizedSeparators = path.Replace('\\', '/');
        var parts = normalizedSeparators.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return string.Join("/", stack);
    }

    private static ImmutableArray<ResolvedResourceDefinition> BindResources(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedResourceDefinition>(document.Resources.Length);

        foreach (var resource in document.Resources)
        {
            var typeName = ResolveTypeName(compilation, resource.XmlNamespace, resource.XmlTypeName, out var symbol);
            if (symbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0100",
                    $"Could not resolve resource type '{resource.XmlTypeName}' for key '{resource.Key}'. Falling back to System.Object metadata.",
                    document.FilePath,
                    resource.Line,
                    resource.Column,
                    false));
                typeName = "global::System.Object";
            }

            result.Add(new ResolvedResourceDefinition(
                Key: resource.Key,
                TypeName: typeName!,
                RawXaml: resource.RawXaml,
                Line: resource.Line,
                Column: resource.Column));
        }

        return result.ToImmutable();
    }

    private static ImmutableArray<ResolvedTemplateDefinition> BindTemplates(
        XamlDocumentModel document,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var result = ImmutableArray.CreateBuilder<ResolvedTemplateDefinition>(document.Templates.Length);

        foreach (var template in document.Templates)
        {
            string? targetTypeName = null;
            INamedTypeSymbol? controlTemplateTargetType = null;

            if (!template.Kind.EndsWith("Template", StringComparison.Ordinal))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0101",
                    $"Template kind '{template.Kind}' is not recognized as a template node.",
                    document.FilePath,
                    template.Line,
                    template.Column,
                    false));
                continue;
            }

            if ((template.Kind.Equals("DataTemplate", StringComparison.Ordinal) ||
                 template.Kind.Equals("TreeDataTemplate", StringComparison.Ordinal)) &&
                string.IsNullOrWhiteSpace(template.DataType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0500",
                    $"Template '{template.Kind}' should declare x:DataType for compiled-binding safety.",
                    document.FilePath,
                    template.Line,
                    template.Column,
                    options.StrictMode));
            }

            if (template.Kind.Equals("ControlTemplate", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(template.TargetType))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0501",
                        "ControlTemplate requires TargetType for source-generated validation.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        options.StrictMode));
                }
                else
                {
                    controlTemplateTargetType = ResolveTypeFromTypeExpression(
                        compilation,
                        document,
                        template.TargetType,
                        document.ClassNamespace);
                    if (controlTemplateTargetType is null)
                    {
                        diagnostics.Add(new DiagnosticInfo(
                            "AXSG0501",
                            $"ControlTemplate target type '{template.TargetType}' could not be resolved.",
                            document.FilePath,
                            template.Line,
                            template.Column,
                            options.StrictMode));
                    }
                    else
                    {
                        targetTypeName = controlTemplateTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        ValidateControlTemplateParts(
                            template,
                            controlTemplateTargetType,
                            compilation,
                            document,
                            diagnostics,
                            options);
                    }
                }
            }

            ValidateTemplateContentRootType(
                template,
                compilation,
                document,
                diagnostics,
                options);

            result.Add(new ResolvedTemplateDefinition(
                Kind: template.Kind,
                Key: template.Key,
                TargetTypeName: targetTypeName,
                DataType: template.DataType,
                RawXaml: template.RawXaml,
                Line: template.Line,
                Column: template.Column));
        }

        return result.ToImmutable();
    }

    private static void ValidateControlTemplateParts(
        XamlTemplateDefinition template,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedParts = ResolveControlTemplatePartExpectations(targetType);
        if (expectedParts.Count == 0)
        {
            return;
        }

        if (!TryCollectControlTemplateNamedParts(template.RawXaml, compilation, out var actualParts))
        {
            return;
        }

        foreach (var pair in expectedParts)
        {
            var partName = pair.Key;
            var expectation = pair.Value;
            if (!actualParts.TryGetValue(partName, out var actual))
            {
                if (expectation.IsRequired)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0502",
                        $"Required template part with x:Name '{partName}' must be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        true));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0504",
                        $"Optional template part with x:Name '{partName}' can be defined on '{targetType.Name}' ControlTemplate.",
                        document.FilePath,
                        template.Line,
                        template.Column,
                        options.StrictMode));
                }

                continue;
            }

            if (expectation.ExpectedType is not null &&
                actual.Type is not null &&
                !IsTypeAssignableTo(actual.Type, expectation.ExpectedType))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0503",
                    $"Template part '{partName}' is expected to be assignable to '{expectation.ExpectedType.Name}', but actual type is {actual.Type.Name}.",
                    document.FilePath,
                    actual.Line,
                    actual.Column,
                    true));
            }
        }
    }

    private static void ValidateTemplateContentRootType(
        XamlTemplateDefinition template,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        var expectedTypeMetadataName = template.Kind switch
        {
            "ItemsPanelTemplate" => "Avalonia.Controls.Panel",
            "ControlTemplate" => "Avalonia.Controls.Control",
            "DataTemplate" => "Avalonia.Controls.Control",
            "TreeDataTemplate" => "Avalonia.Controls.Control",
            _ => null
        };

        if (expectedTypeMetadataName is null)
        {
            return;
        }

        var expectedType = compilation.GetTypeByMetadataName(expectedTypeMetadataName);
        if (expectedType is null)
        {
            return;
        }

        if (!TryGetTemplateContentRootElement(template.RawXaml, out var contentRoot))
        {
            return;
        }

        var actualType = ResolveTypeSymbol(compilation, contentRoot.Name.NamespaceName, contentRoot.Name.LocalName);
        if (actualType is null || IsTypeAssignableTo(actualType, expectedType))
        {
            return;
        }

        var lineInfo = (IXmlLineInfo)contentRoot;
        var line = template.Line;
        var column = template.Column;
        if (lineInfo.HasLineInfo())
        {
            line = template.Line + Math.Max(0, lineInfo.LineNumber - 1);
            column = lineInfo.LineNumber <= 1
                ? template.Column + Math.Max(0, lineInfo.LinePosition - 1)
                : Math.Max(1, lineInfo.LinePosition);
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0506",
            $"Template '{template.Kind}' content root '{contentRoot.Name.LocalName}' is expected to be assignable to '{expectedType.Name}', but actual type is '{actualType.Name}'.",
            document.FilePath,
            line,
            column,
            options.StrictMode));
    }

    private static bool TryGetTemplateContentRootElement(string rawXaml, out XElement contentRoot)
    {
        contentRoot = default!;
        try
        {
            var parsed = XDocument.Parse(rawXaml, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            if (parsed.Root is not { } root)
            {
                return false;
            }

            foreach (var child in root.Elements())
            {
                if (child.Name.NamespaceName == root.Name.NamespaceName &&
                    child.Name.LocalName.Contains(".", StringComparison.Ordinal))
                {
                    if (!child.Name.LocalName.EndsWith(".Content", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var contentChild = child.Elements().FirstOrDefault();
                    if (contentChild is not null)
                    {
                        contentRoot = contentChild;
                        return true;
                    }

                    continue;
                }

                contentRoot = child;
                return true;
            }

            return false;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static void ValidateItemContainerInsideTemplateWarning(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyElement propertyElement,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (!property.Name.Equals("ItemTemplate", StringComparison.Ordinal) &&
            !property.Name.Equals("DataTemplates", StringComparison.Ordinal))
        {
            return;
        }

        var itemsControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ItemsControl");
        if (itemsControlType is null || !IsTypeAssignableTo(objectType, itemsControlType))
        {
            return;
        }

        var knownContainerType = ResolveKnownItemContainerType(objectType, compilation);
        if (knownContainerType is null)
        {
            return;
        }

        var contentControlType = compilation.GetTypeByMetadataName("Avalonia.Controls.ContentControl");
        if (contentControlType is null)
        {
            return;
        }

        foreach (var templateNode in propertyElement.ObjectValues)
        {
            if (!IsDataTemplateNode(templateNode))
            {
                continue;
            }

            var contentNode = TryGetTemplateContentNode(templateNode);
            if (contentNode is null)
            {
                continue;
            }

            var contentType = ResolveTypeSymbol(compilation, contentNode.XmlNamespace, contentNode.XmlTypeName);
            if (contentType is null ||
                !IsTypeAssignableTo(contentType, contentControlType) ||
                !IsTypeAssignableTo(contentType, knownContainerType))
            {
                continue;
            }

            diagnostics.Add(new DiagnosticInfo(
                "AXSG0505",
                $"Unexpected '{knownContainerType.Name}' inside of '{objectType.Name}.{property.Name}'. '{objectType.Name}.{property.Name}' defines template of the container content, not the container itself.",
                document.FilePath,
                contentNode.Line,
                contentNode.Column,
                options.StrictMode));
        }
    }

    private static INamedTypeSymbol? ResolveKnownItemContainerType(
        INamedTypeSymbol itemsControlType,
        Compilation compilation)
    {
        foreach (var mapping in KnownItemContainerTypeMappings)
        {
            var mappedControlType = compilation.GetTypeByMetadataName(mapping.ItemsControlMetadataName);
            if (mappedControlType is null || !IsTypeAssignableTo(itemsControlType, mappedControlType))
            {
                continue;
            }

            return compilation.GetTypeByMetadataName(mapping.ItemContainerMetadataName);
        }

        return null;
    }

    private static bool IsDataTemplateNode(XamlObjectNode node)
    {
        return node.XmlTypeName is "DataTemplate" or "TreeDataTemplate";
    }

    private static XamlObjectNode? TryGetTemplateContentNode(XamlObjectNode templateNode)
    {
        foreach (var propertyElement in templateNode.PropertyElements)
        {
            if ((propertyElement.PropertyName.Equals("Content", StringComparison.Ordinal) ||
                 propertyElement.PropertyName.EndsWith(".Content", StringComparison.Ordinal)) &&
                propertyElement.ObjectValues.Length > 0)
            {
                return propertyElement.ObjectValues[0];
            }
        }

        if (templateNode.ChildObjects.Length > 0)
        {
            return templateNode.ChildObjects[0];
        }

        return null;
    }

    private static Dictionary<string, TemplatePartExpectation> ResolveControlTemplatePartExpectations(INamedTypeSymbol targetType)
    {
        var result = new Dictionary<string, TemplatePartExpectation>(StringComparer.Ordinal);
        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!attribute.AttributeClass?.Name.Equals("TemplatePartAttribute", StringComparison.Ordinal) ?? true)
                {
                    continue;
                }

                var partName = TryGetTemplatePartName(attribute);
                if (string.IsNullOrWhiteSpace(partName) || result.ContainsKey(partName!))
                {
                    continue;
                }

                result.Add(partName!, new TemplatePartExpectation(
                    expectedType: TryGetTemplatePartType(attribute),
                    isRequired: TryGetTemplatePartIsRequired(attribute)));
            }
        }

        return result;
    }

    private static string? TryGetTemplatePartName(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Name", StringComparison.Ordinal) &&
                namedArgument.Value.Value is string namedName &&
                !string.IsNullOrWhiteSpace(namedName))
            {
                return namedName;
            }
        }

        if (attribute.ConstructorArguments.Length > 0 &&
            attribute.ConstructorArguments[0].Value is string ctorName &&
            !string.IsNullOrWhiteSpace(ctorName))
        {
            return ctorName;
        }

        return null;
    }

    private static ITypeSymbol? TryGetTemplatePartType(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("Type", StringComparison.Ordinal) &&
                namedArgument.Value.Kind == TypedConstantKind.Type &&
                namedArgument.Value.Value is ITypeSymbol namedType)
            {
                return namedType;
            }
        }

        if (attribute.ConstructorArguments.Length > 1 &&
            attribute.ConstructorArguments[1].Kind == TypedConstantKind.Type &&
            attribute.ConstructorArguments[1].Value is ITypeSymbol ctorType)
        {
            return ctorType;
        }

        return null;
    }

    private static bool TryGetTemplatePartIsRequired(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key.Equals("IsRequired", StringComparison.Ordinal) &&
                namedArgument.Value.Value is bool isRequired)
            {
                return isRequired;
            }
        }

        return false;
    }

    private static bool TryCollectControlTemplateNamedParts(
        string rawXaml,
        Compilation compilation,
        out ImmutableDictionary<string, TemplatePartActual> parts)
    {
        var result = ImmutableDictionary.CreateBuilder<string, TemplatePartActual>(StringComparer.Ordinal);
        try
        {
            var document = XDocument.Parse(rawXaml, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
            if (document.Root is not { } root)
            {
                parts = ImmutableDictionary<string, TemplatePartActual>.Empty;
                return false;
            }

            CollectControlTemplateNamedPartsRecursive(root, compilation, result, isTemplateRoot: true);
            parts = result.ToImmutable();
            return true;
        }
        catch (XmlException)
        {
            parts = ImmutableDictionary<string, TemplatePartActual>.Empty;
            return false;
        }
    }

    private static void CollectControlTemplateNamedPartsRecursive(
        XElement element,
        Compilation compilation,
        ImmutableDictionary<string, TemplatePartActual>.Builder result,
        bool isTemplateRoot)
    {
        if (!isTemplateRoot && IsTemplateLikeElement(element.Name.LocalName))
        {
            return;
        }

        var xNameAttribute = element.Attributes().FirstOrDefault(attribute =>
            attribute.Name == Xaml2006 + "Name");
        var nameAttribute = xNameAttribute ?? element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "Name");
        if (nameAttribute is not null &&
            !string.IsNullOrWhiteSpace(nameAttribute.Value) &&
            !result.ContainsKey(nameAttribute.Value))
        {
            var type = ResolveTypeSymbol(compilation, element.Name.NamespaceName, element.Name.LocalName);
            var lineInfo = (IXmlLineInfo)nameAttribute;
            if (!lineInfo.HasLineInfo())
            {
                lineInfo = (IXmlLineInfo)element;
            }

            result[nameAttribute.Value] = new TemplatePartActual(
                type: type,
                line: lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                column: lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1);
        }

        foreach (var child in element.Elements())
        {
            CollectControlTemplateNamedPartsRecursive(child, compilation, result, isTemplateRoot: false);
        }
    }

    private static bool IsTemplateLikeElement(string localName)
    {
        return localName.EndsWith("Template", StringComparison.Ordinal);
    }

    private static bool TryParseBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionName = markup.Name;
        if (!extensionName.Equals("Binding", StringComparison.OrdinalIgnoreCase) &&
            !extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, out bindingMarkup);
    }

    private static bool TryParseReflectionBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionName = markup.Name;
        if (!extensionName.Equals("ReflectionBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, out bindingMarkup);
    }

    private static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        string extensionName,
        out BindingMarkup bindingMarkup)
    {
        var path = string.Empty;
        if (markup.NamedArguments.TryGetValue("Path", out var explicitPath))
        {
            path = Unquote(explicitPath);
        }
        else if (markup.PositionalArguments.Length > 0)
        {
            path = Unquote(markup.PositionalArguments[0]);
        }

        var parsedMarkup = new BindingMarkup(
            isCompiledBinding: extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase),
            path: path,
            mode: TryGetNamedMarkupArgument(markup, "Mode"),
            elementName: TryGetNamedMarkupArgument(markup, "ElementName"),
            relativeSource: markup.NamedArguments.TryGetValue("RelativeSource", out var relativeSourceValue) &&
                            TryParseRelativeSourceMarkup(relativeSourceValue, out var relativeSourceMarkup)
                ? relativeSourceMarkup
                : null,
            source: TryGetNamedMarkupArgument(markup, "Source"),
            converter: TryGetNamedMarkupArgument(markup, "Converter"),
            converterCulture: TryGetNamedMarkupArgument(markup, "ConverterCulture"),
            converterParameter: TryGetNamedMarkupArgument(markup, "ConverterParameter"),
            stringFormat: TryGetNamedMarkupArgument(markup, "StringFormat", "Format"),
            fallbackValue: TryGetNamedMarkupArgument(markup, "FallbackValue", "Fallback"),
            targetNullValue: TryGetNamedMarkupArgument(markup, "TargetNullValue", "NullValue"),
            delay: TryGetNamedMarkupArgument(markup, "Delay"),
            priority: TryGetNamedMarkupArgument(markup, "Priority", "BindingPriority"),
            updateSourceTrigger: TryGetNamedMarkupArgument(markup, "UpdateSourceTrigger", "Trigger"),
            hasSourceConflict: false,
            sourceConflictMessage: null);

        bindingMarkup = NormalizeBindingQuerySyntax(parsedMarkup);

        return true;
    }

    private static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, params string[] argumentNames)
    {
        foreach (var argumentName in argumentNames)
        {
            if (markup.NamedArguments.TryGetValue(argumentName, out var value))
            {
                return Unquote(value);
            }
        }

        return null;
    }

    private static BindingMarkup NormalizeBindingQuerySyntax(BindingMarkup bindingMarkup)
    {
        if (CountExplicitBindingSources(bindingMarkup) > 1)
        {
            return CreateBindingSourceConflict(
                bindingMarkup,
                "Only one binding source may be specified. Use only one of ElementName, RelativeSource, or Source.");
        }

        var normalizedBindingMarkup = bindingMarkup;
        if (TryExtractReferenceElementName(normalizedBindingMarkup.Source, out var referenceElementName))
        {
            normalizedBindingMarkup = new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedBindingMarkup.Path,
                mode: normalizedBindingMarkup.Mode,
                elementName: referenceElementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: null,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        var trimmedPath = normalizedBindingMarkup.Path.Trim();
        if (trimmedPath.Length == 0)
        {
            return normalizedBindingMarkup;
        }

        if (TryParseElementNameQuery(trimmedPath, out var elementName, out var normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '#name' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: elementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseSelfQuery(trimmedPath, out var selfRelativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$self' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: selfRelativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseParentQuery(trimmedPath, out var relativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$parent' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: relativeSource,
                source: normalizedBindingMarkup.Source,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        return normalizedBindingMarkup;
    }

    private static bool HasExplicitBindingSource(BindingMarkup bindingMarkup)
    {
        return CountExplicitBindingSources(bindingMarkup) > 0;
    }

    private static int CountExplicitBindingSources(BindingMarkup bindingMarkup)
    {
        var sourceCount = 0;
        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            sourceCount++;
        }

        if (bindingMarkup.RelativeSource is not null)
        {
            sourceCount++;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            sourceCount++;
        }

        return sourceCount;
    }

    private static BindingMarkup CreateBindingSourceConflict(BindingMarkup bindingMarkup, string message)
    {
        if (bindingMarkup.HasSourceConflict)
        {
            return bindingMarkup;
        }

        return new BindingMarkup(
            isCompiledBinding: bindingMarkup.IsCompiledBinding,
            path: bindingMarkup.Path,
            mode: bindingMarkup.Mode,
            elementName: bindingMarkup.ElementName,
            relativeSource: bindingMarkup.RelativeSource,
            source: bindingMarkup.Source,
            converter: bindingMarkup.Converter,
            converterCulture: bindingMarkup.ConverterCulture,
            converterParameter: bindingMarkup.ConverterParameter,
            stringFormat: bindingMarkup.StringFormat,
            fallbackValue: bindingMarkup.FallbackValue,
            targetNullValue: bindingMarkup.TargetNullValue,
            delay: bindingMarkup.Delay,
            priority: bindingMarkup.Priority,
            updateSourceTrigger: bindingMarkup.UpdateSourceTrigger,
            hasSourceConflict: true,
            sourceConflictMessage: message);
    }

    private static bool TryExtractReferenceElementName(string? sourceValue, out string elementName)
    {
        elementName = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceValue) ||
            !TryParseMarkupExtension(sourceValue!, out var sourceMarkup))
        {
            return false;
        }

        var sourceMarkupName = sourceMarkup.Name.Trim();
        if (!sourceMarkupName.Equals("x:Reference", StringComparison.OrdinalIgnoreCase) &&
            !sourceMarkupName.Equals("Reference", StringComparison.OrdinalIgnoreCase) &&
            !sourceMarkupName.Equals("ResolveByName", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawName = TryGetNamedMarkupArgument(sourceMarkup, "Name", "ElementName") ??
                      (sourceMarkup.PositionalArguments.Length > 0 ? Unquote(sourceMarkup.PositionalArguments[0]) : null);
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        elementName = rawName!.Trim();
        return elementName.Length > 0;
    }

    private static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        elementName = string.Empty;
        normalizedPath = string.Empty;

        if (!path.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var index = 1;
        while (index < path.Length && IsIdentifierPart(path[index]))
        {
            index++;
        }

        if (index == 1)
        {
            return false;
        }

        elementName = path.Substring(1, index - 1);
        if (index == path.Length)
        {
            normalizedPath = ".";
            return true;
        }

        if (path[index] != '.')
        {
            return false;
        }

        normalizedPath = path.Substring(index + 1).Trim();
        if (normalizedPath.Length == 0)
        {
            normalizedPath = ".";
        }

        return true;
    }

    private static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!path.StartsWith("$self", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$self".Length;
        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

        relativeSource = new RelativeSourceMarkup(
            mode: "Self",
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    private static bool TryParseParentQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!path.StartsWith("$parent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$parent".Length;
        string? ancestorTypeToken = null;
        int? ancestorLevel = 1;

        if (index < path.Length && path[index] == '[')
        {
            var closingBracket = path.IndexOf(']', index + 1);
            if (closingBracket <= index + 1)
            {
                return false;
            }

            var inside = path.Substring(index + 1, closingBracket - index - 1).Trim();
            if (inside.Length > 0)
            {
                var separators = inside.Split(new[] { ',', ';' }, 2, StringSplitOptions.None);
                var firstPart = separators[0].Trim();
                if (separators.Length == 1)
                {
                    if (int.TryParse(firstPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSingleLevel) &&
                        parsedSingleLevel > 0)
                    {
                        ancestorLevel = parsedSingleLevel;
                        ancestorTypeToken = null;
                    }
                    else
                    {
                        ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    }
                }
                else
                {
                    ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    if (int.TryParse(separators[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel) &&
                        parsedLevel > 0)
                    {
                        ancestorLevel = parsedLevel;
                    }
                }
            }

            index = closingBracket + 1;
        }

        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

        relativeSource = new RelativeSourceMarkup(
            mode: "FindAncestor",
            ancestorTypeToken: ancestorTypeToken,
            ancestorLevel: ancestorLevel,
            tree: null);
        return true;
    }

    private static bool CanUseCompiledBinding(BindingMarkup bindingMarkup)
    {
        return !bindingMarkup.HasSourceConflict &&
               string.IsNullOrWhiteSpace(bindingMarkup.ElementName) &&
               bindingMarkup.RelativeSource is null &&
               string.IsNullOrWhiteSpace(bindingMarkup.Source);
    }

    private static bool TryBuildCompiledBindingAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        out string accessorExpression,
        out string normalizedPath,
        out string errorMessage)
    {
        accessorExpression = "source";
        normalizedPath = string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath.Trim();
        errorMessage = string.Empty;

        if (normalizedPath == ".")
        {
            return true;
        }

        if (!TryParseCompiledBindingPathSegments(normalizedPath, out var segments, out var leadingNotCount, out errorMessage))
        {
            return false;
        }

        if (segments.Count == 0)
        {
            normalizedPath = ".";
            return true;
        }

        var expressionBuilder = "source";
        ITypeSymbol? currentType = sourceType;
        var normalizedSegments = new List<string>(segments.Count);

        for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                errorMessage = "intermediate segment is not a named CLR type";
                return false;
            }

            if (segment.AcceptsNull &&
                !CanUseNullConditionalAccess(currentType))
            {
                errorMessage = $"null-conditional access '?.' is not valid on '{currentType.ToDisplayString()}'";
                return false;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    errorMessage = "attached property owner type token is missing";
                    return false;
                }

                var ownerTypeToken = segment.AttachedOwnerTypeToken!;
                var ownerType = ResolveTypeToken(compilation, document, ownerTypeToken, document.ClassNamespace);
                if (ownerType is null)
                {
                    errorMessage = $"attached property owner type '{ownerTypeToken}' could not be resolved";
                    return false;
                }

                var getterMethod = FindAttachedPropertyGetterMethod(ownerType, segment.MemberName, currentType);
                if (getterMethod is null || getterMethod.ReturnsVoid)
                {
                    errorMessage = $"attached property getter 'Get{segment.MemberName}' was not found on '{ownerType.ToDisplayString()}'";
                    return false;
                }

                var ownerTypeName = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (segment.AcceptsNull)
                {
                    expressionBuilder = "(" +
                                        expressionBuilder +
                                        " is null ? default : " +
                                        ownerTypeName +
                                        "." +
                                        getterMethod.Name +
                                        "(" +
                                        expressionBuilder +
                                        "))";
                }
                else
                {
                    expressionBuilder = ownerTypeName + "." + getterMethod.Name + "(" + expressionBuilder + ")";
                }

                currentType = getterMethod.ReturnType;
                var attachedNormalizedSegment = "(" + ownerTypeToken + "." + segment.MemberName + ")";
                if (normalizedSegments.Count == 0)
                {
                    normalizedSegments.Add(attachedNormalizedSegment);
                }
                else
                {
                    var separator = segment.AcceptsNull ? "?." : ".";
                    normalizedSegments.Add(separator + attachedNormalizedSegment);
                }

                foreach (var indexerToken in segment.Indexers)
                {
                    if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                    {
                        return false;
                    }

                    expressionBuilder += "[" + indexerExpression + "]";
                    normalizedSegments[normalizedSegments.Count - 1] += "[" + normalizedIndexerToken + "]";
                    currentType = resultType;
                }

                var attachedSegmentIndex = normalizedSegments.Count - 1;
                var updatedAttachedSegment = normalizedSegments[attachedSegmentIndex];
                if (!TryApplyStreamOperators(
                        compilation,
                        segment.StreamCount,
                        ref currentType,
                        ref expressionBuilder,
                        ref updatedAttachedSegment,
                        out errorMessage))
                {
                    return false;
                }

                normalizedSegments[attachedSegmentIndex] = updatedAttachedSegment;
                continue;
            }

            var accessor = segment.AcceptsNull ? "?." : ".";
            var property = segment.IsMethodCall ? null : FindProperty(currentNamedType, segment.MemberName);
            var method = segment.IsMethodCall
                ? null
                : FindParameterlessMethod(currentNamedType, segment.MemberName);
            if (property is null && method is null)
            {
                if (segment.IsMethodCall &&
                    TryResolveMethodInvocation(
                        currentNamedType,
                        segment.MemberName,
                        segment.MethodArguments,
                        out var methodInvocationExpression,
                        out var methodInvocationNormalizedSegment,
                        out var methodReturnType,
                        out errorMessage))
                {
                    expressionBuilder += accessor + methodInvocationExpression;
                    currentType = methodReturnType;
                    var segmentSeparator = normalizedSegments.Count == 0
                        ? string.Empty
                        : segment.AcceptsNull ? "?." : ".";
                    var normalizedInvocationSegment = segmentSeparator + methodInvocationNormalizedSegment;
                    if (!TryApplyStreamOperators(
                            compilation,
                            segment.StreamCount,
                            ref currentType,
                            ref expressionBuilder,
                            ref normalizedInvocationSegment,
                            out errorMessage))
                    {
                        return false;
                    }

                    normalizedSegments.Add(normalizedInvocationSegment);
                    continue;
                }

                if (segment.IsMethodCall && !string.IsNullOrWhiteSpace(errorMessage))
                {
                    return false;
                }

                errorMessage = $"segment '{segment.MemberName}' was not found as a property or parameterless method on '{currentNamedType.ToDisplayString()}'";
                return false;
            }

            string normalizedSegment;
            if (property is not null)
            {
                expressionBuilder += accessor + property.Name;
                currentType = property.Type;
                normalizedSegment = property.Name;
            }
            else
            {
                if (method is null || method.ReturnsVoid)
                {
                    errorMessage = $"method segment '{segment.MemberName}' is not a supported parameterless method with a return value";
                    return false;
                }

                expressionBuilder += accessor + method.Name + "()";
                currentType = method.ReturnType;
                normalizedSegment = method.Name + "()";
            }

            foreach (var indexerToken in segment.Indexers)
            {
                if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                {
                    return false;
                }

                expressionBuilder += "[" + indexerExpression + "]";
                normalizedSegment += "[" + normalizedIndexerToken + "]";
                currentType = resultType;
            }

            if (!string.IsNullOrWhiteSpace(segment.CastTypeToken))
            {
                var castTypeToken = segment.CastTypeToken!;
                var castType = ResolveTypeToken(compilation, document, castTypeToken, document.ClassNamespace);
                if (castType is null)
                {
                    errorMessage = $"cast type '{castTypeToken}' could not be resolved";
                    return false;
                }

                expressionBuilder = "((" +
                                    castType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                    ")" +
                                    expressionBuilder +
                                    ")";
                normalizedSegment = "((" + castTypeToken + ")" + normalizedSegment + ")";
                currentType = castType;
            }

            if (!TryApplyStreamOperators(
                    compilation,
                    segment.StreamCount,
                    ref currentType,
                    ref expressionBuilder,
                    ref normalizedSegment,
                    out errorMessage))
            {
                return false;
            }

            if (normalizedSegments.Count == 0)
            {
                normalizedSegments.Add(normalizedSegment);
            }
            else
            {
                var separator = segment.AcceptsNull ? "?." : ".";
                normalizedSegments.Add(separator + normalizedSegment);
            }
        }

        if (leadingNotCount > 0)
        {
            for (var i = 0; i < leadingNotCount; i++)
            {
                expressionBuilder = "(!global::System.Convert.ToBoolean(" + expressionBuilder + "))";
            }
        }

        accessorExpression = expressionBuilder;
        if (normalizedSegments.Count > 0)
        {
            normalizedPath = string.Join(string.Empty, normalizedSegments);
            if (leadingNotCount > 0)
            {
                normalizedPath = new string('!', leadingNotCount) + normalizedPath;
            }
        }
        return true;
    }

    private static bool TryParseCompiledBindingPathSegments(
        string path,
        out List<CompiledPathSegment> segments,
        out int leadingNotCount,
        out string errorMessage)
    {
        segments = new List<CompiledPathSegment>();
        leadingNotCount = 0;
        errorMessage = string.Empty;

        var index = 0;
        while (index < path.Length && path[index] == '!')
        {
            leadingNotCount++;
            index++;
        }

        if (leadingNotCount > 0 && index >= path.Length)
        {
            errorMessage = "compiled binding path cannot end after '!'";
            return false;
        }

        var nextSegmentAcceptsNull = false;
        while (index < path.Length)
        {
            while (index < path.Length && path[index] == '.')
            {
                index++;
            }

            if (index >= path.Length)
            {
                break;
            }

            if (!TryParseCompiledBindingMemberSegment(
                    path,
                    ref index,
                    out var memberName,
                    out var castTypeToken,
                    out var isMethodCall,
                    out var methodArguments,
                    out var isAttachedProperty,
                    out var attachedOwnerTypeToken,
                    out errorMessage))
            {
                return false;
            }

            var indexers = ImmutableArray.CreateBuilder<string>();
            while (index < path.Length && path[index] == '[')
            {
                index++;
                var tokenStart = index;
                var bracketDepth = 1;
                while (index < path.Length && bracketDepth > 0)
                {
                    var ch = path[index];
                    if (ch == '[')
                    {
                        bracketDepth++;
                        index++;
                        continue;
                    }

                    if (ch == ']')
                    {
                        bracketDepth--;
                        if (bracketDepth == 0)
                        {
                            break;
                        }
                    }

                    index++;
                }

                if (index >= path.Length || path[index] != ']')
                {
                    errorMessage = "unterminated indexer segment";
                    return false;
                }

                var tokenLength = index - tokenStart;
                if (tokenLength <= 0)
                {
                    errorMessage = "empty indexer segment is not supported";
                    return false;
                }

                var token = path.Substring(tokenStart, tokenLength).Trim();
                if (token.Length == 0)
                {
                    errorMessage = "empty indexer segment is not supported";
                    return false;
                }

                indexers.Add(token);
                index++;
            }

            var streamCount = 0;
            while (index < path.Length && path[index] == '^')
            {
                streamCount++;
                index++;
            }

            segments.Add(new CompiledPathSegment(
                memberName,
                indexers.ToImmutable(),
                castTypeToken,
                isMethodCall,
                nextSegmentAcceptsNull,
                methodArguments,
                isAttachedProperty,
                attachedOwnerTypeToken,
                streamCount));
            nextSegmentAcceptsNull = false;

            if (index >= path.Length)
            {
                break;
            }

            if (path[index] == '.')
            {
                index++;
                continue;
            }

            if (path[index] == '?' &&
                index + 1 < path.Length &&
                path[index + 1] == '.')
            {
                index += 2;
                nextSegmentAcceptsNull = true;
                continue;
            }

            errorMessage = $"unexpected token '{path[index]}' in binding path";
            return false;
        }

        return true;
    }

    private static bool TryParseCompiledBindingMemberSegment(
        string path,
        ref int index,
        out string memberName,
        out string? castTypeToken,
        out bool isMethodCall,
        out ImmutableArray<string> methodArguments,
        out bool isAttachedProperty,
        out string? attachedOwnerTypeToken,
        out string errorMessage)
    {
        memberName = string.Empty;
        castTypeToken = null;
        isMethodCall = false;
        methodArguments = ImmutableArray<string>.Empty;
        isAttachedProperty = false;
        attachedOwnerTypeToken = null;
        errorMessage = string.Empty;

        if (index >= path.Length)
        {
            errorMessage = "expected member segment";
            return false;
        }

        var castRequiresSegmentClosure = false;
        if (path[index] == '(')
        {
            if (index + 1 < path.Length && path[index + 1] != '(')
            {
                var attachedClosing = path.IndexOf(')', index + 1);
                if (attachedClosing > index + 1)
                {
                    var attachedInner = path.Substring(index + 1, attachedClosing - index - 1).Trim();
                    var attachedSeparator = attachedInner.LastIndexOf('.');
                    var isAttachedTail = attachedClosing + 1 >= path.Length ||
                                         path[attachedClosing + 1] == '.' ||
                                         path[attachedClosing + 1] == '?' ||
                                         path[attachedClosing + 1] == '[';
                    if (attachedSeparator > 0 &&
                        attachedSeparator < attachedInner.Length - 1 &&
                        isAttachedTail)
                    {
                        attachedOwnerTypeToken = attachedInner.Substring(0, attachedSeparator).Trim();
                        memberName = attachedInner.Substring(attachedSeparator + 1).Trim();
                        if (attachedOwnerTypeToken.Length == 0 || memberName.Length == 0)
                        {
                            errorMessage = "invalid attached property segment";
                            return false;
                        }

                        isAttachedProperty = true;
                        index = attachedClosing + 1;
                        return true;
                    }
                }
            }

            if (index + 1 < path.Length && path[index + 1] == '(')
            {
                castRequiresSegmentClosure = true;
                index += 2;
            }
            else
            {
                index++;
            }

            var castStart = index;
            while (index < path.Length && path[index] != ')')
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "unterminated cast segment";
                return false;
            }

            castTypeToken = path.Substring(castStart, index - castStart).Trim();
            if (castTypeToken.Length == 0)
            {
                errorMessage = "cast type token cannot be empty";
                return false;
            }

            index++;
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }
        }

        if (index >= path.Length || !IsIdentifierStart(path[index]))
        {
            errorMessage = "member name expected in binding path segment";
            return false;
        }

        var nameStart = index;
        index++;
        while (index < path.Length && IsIdentifierPart(path[index]))
        {
            index++;
        }

        memberName = path.Substring(nameStart, index - nameStart);

        if (index < path.Length && path[index] == '(')
        {
            if (!TryParseMethodArguments(path, ref index, out methodArguments, out errorMessage))
            {
                return false;
            }

            isMethodCall = true;
        }

        if (castRequiresSegmentClosure)
        {
            while (index < path.Length && char.IsWhiteSpace(path[index]))
            {
                index++;
            }

            if (index >= path.Length || path[index] != ')')
            {
                errorMessage = "expected ')' to close casted member segment";
                return false;
            }

            index++;
        }

        return true;
    }

    private static bool TryParseMethodArguments(
        string path,
        ref int index,
        out ImmutableArray<string> methodArguments,
        out string errorMessage)
    {
        methodArguments = ImmutableArray<string>.Empty;
        errorMessage = string.Empty;

        if (index >= path.Length || path[index] != '(')
        {
            errorMessage = "method argument list must start with '('";
            return false;
        }

        index++;
        var argumentStart = index;
        var parenthesisDepth = 1;
        var inQuote = false;
        var quoteChar = '\0';

        while (index < path.Length && parenthesisDepth > 0)
        {
            var ch = path[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                index++;
                continue;
            }

            if (ch == '(')
            {
                parenthesisDepth++;
            }
            else if (ch == ')')
            {
                parenthesisDepth--;
                if (parenthesisDepth == 0)
                {
                    break;
                }
            }

            index++;
        }

        if (index >= path.Length || parenthesisDepth != 0)
        {
            errorMessage = "unterminated method argument list";
            return false;
        }

        var argumentsText = path.Substring(argumentStart, index - argumentStart).Trim();
        index++;

        if (argumentsText.Length == 0)
        {
            return true;
        }

        var arguments = ImmutableArray.CreateBuilder<string>();
        foreach (var token in SplitTopLevel(argumentsText, ','))
        {
            var argument = token.Trim();
            if (argument.Length == 0)
            {
                errorMessage = "method argument list contains an empty argument";
                return false;
            }

            arguments.Add(argument);
        }

        methodArguments = arguments.ToImmutable();
        return true;
    }

    private static bool CanUseNullConditionalAccess(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        return false;
    }

    private static bool TryApplyStreamOperators(
        Compilation compilation,
        int streamCount,
        ref ITypeSymbol? currentType,
        ref string expressionBuilder,
        ref string normalizedSegment,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (streamCount <= 0)
        {
            return true;
        }

        for (var index = 0; index < streamCount; index++)
        {
            if (currentType is null)
            {
                errorMessage = "stream operator '^' cannot be applied because the current segment type is unknown";
                return false;
            }

            if (TryResolveTaskStreamType(compilation, currentType, out var taskResultType, out var useGenericTaskUnwrap))
            {
                if (useGenericTaskUnwrap)
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask<" +
                        taskResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                        ">(" +
                        expressionBuilder +
                        ")";
                }
                else
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask(" +
                        expressionBuilder +
                        ")";
                }

                normalizedSegment += "^";
                currentType = taskResultType;
                continue;
            }

            if (TryResolveObservableStreamType(compilation, currentType, out var observableElementType))
            {
                expressionBuilder =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapObservable<" +
                    observableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    ">(" +
                    expressionBuilder +
                    ")";
                normalizedSegment += "^";
                currentType = observableElementType;
                continue;
            }

            errorMessage =
                $"stream operator '^' is not supported for type '{currentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
            return false;
        }

        return true;
    }

    private static bool TryResolveTaskStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol resultType,
        out bool useGenericTaskUnwrap)
    {
        resultType = compilation.GetSpecialType(SpecialType.System_Object);
        useGenericTaskUnwrap = false;

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var taskOfType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        if (taskOfType is not null &&
            TryFindConstructedGenericType(namedSourceType, taskOfType, out var taskOfConstructedType) &&
            taskOfConstructedType.TypeArguments.Length == 1)
        {
            resultType = taskOfConstructedType.TypeArguments[0];
            useGenericTaskUnwrap = true;
            return true;
        }

        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        if (taskType is not null && IsTypeAssignableTo(sourceType, taskType))
        {
            resultType = compilation.GetSpecialType(SpecialType.System_Object);
            useGenericTaskUnwrap = false;
            return true;
        }

        return false;
    }

    private static bool TryResolveObservableStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol elementType)
    {
        elementType = compilation.GetSpecialType(SpecialType.System_Object);

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var observableType = compilation.GetTypeByMetadataName("System.IObservable`1");
        if (observableType is null)
        {
            return false;
        }

        if (!TryFindConstructedGenericType(namedSourceType, observableType, out var constructedObservableType) ||
            constructedObservableType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = constructedObservableType.TypeArguments[0];
        return true;
    }

    private static bool TryFindConstructedGenericType(
        INamedTypeSymbol sourceType,
        INamedTypeSymbol genericTypeDefinition,
        out INamedTypeSymbol constructedType)
    {
        if (sourceType.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(sourceType.OriginalDefinition, genericTypeDefinition))
        {
            constructedType = sourceType;
            return true;
        }

        foreach (var interfaceType in sourceType.AllInterfaces)
        {
            if (interfaceType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = interfaceType;
                return true;
            }
        }

        for (var currentType = sourceType.BaseType; currentType is not null; currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(currentType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = currentType;
                return true;
            }
        }

        constructedType = null!;
        return false;
    }

    private static bool TryBuildIndexerExpression(
        ITypeSymbol collectionType,
        string rawIndexerToken,
        out string indexerExpression,
        out string normalizedIndexerToken,
        out ITypeSymbol resultType,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        normalizedIndexerToken = rawIndexerToken;

        if (collectionType is IArrayTypeSymbol arrayType)
        {
            if (!int.TryParse(rawIndexerToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var arrayIndex))
            {
                indexerExpression = string.Empty;
                resultType = collectionType;
                errorMessage = $"array index '{rawIndexerToken}' is not a valid integer";
                return false;
            }

            indexerExpression = arrayIndex.ToString(CultureInfo.InvariantCulture);
            normalizedIndexerToken = indexerExpression;
            resultType = arrayType.ElementType;
            return true;
        }

        if (collectionType is not INamedTypeSymbol namedCollectionType)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not support indexers";
            return false;
        }

        var indexer = namedCollectionType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsIndexer &&
                                        property.Parameters.Length == 1 &&
                                        property.GetMethod is not null);
        if (indexer is null)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not define a supported indexer";
            return false;
        }

        if (!TryConvertIndexerToken(rawIndexerToken, indexer.Parameters[0].Type, out indexerExpression, out normalizedIndexerToken))
        {
            resultType = collectionType;
            errorMessage = $"indexer token '{rawIndexerToken}' is incompatible with '{indexer.Parameters[0].Type.ToDisplayString()}'";
            return false;
        }

        resultType = indexer.Type;
        return true;
    }

    private static bool TryResolveMethodInvocation(
        INamedTypeSymbol targetType,
        string methodName,
        ImmutableArray<string> methodArguments,
        out string invocationExpression,
        out string normalizedSegment,
        out ITypeSymbol returnType,
        out string errorMessage)
    {
        invocationExpression = string.Empty;
        normalizedSegment = string.Empty;
        returnType = targetType;
        errorMessage = string.Empty;

        var candidates = new List<IMethodSymbol>();
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.ReturnsVoid ||
                    method.IsGenericMethod ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
                    method.Parameters.Length != methodArguments.Length)
                {
                    continue;
                }

                candidates.Add(method);
            }
        }

        if (candidates.Count == 0)
        {
            errorMessage = $"method segment '{methodName}' has no compatible overloads on '{targetType.ToDisplayString()}'";
            return false;
        }

        var bestScore = int.MaxValue;
        var bestObjectParameterCount = int.MaxValue;
        var bestSortKey = string.Empty;
        string[]? bestArgumentExpressions = null;
        string[]? bestNormalizedArguments = null;
        IMethodSymbol? bestCandidate = null;

        foreach (var candidate in candidates.OrderBy(method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            var argumentExpressions = new string[methodArguments.Length];
            var normalizedArguments = new string[methodArguments.Length];
            var candidateScore = 0;
            var objectParameterCount = 0;
            var canUseCandidate = true;

            for (var i = 0; i < methodArguments.Length; i++)
            {
                var parameter = candidate.Parameters[i];
                if (parameter.Type.SpecialType == SpecialType.System_Object)
                {
                    objectParameterCount++;
                }

                if (!TryConvertMethodArgumentToken(
                        methodArguments[i],
                        parameter.Type,
                        out var argumentExpression,
                        out var normalizedArgument,
                        out var conversionCost))
                {
                    canUseCandidate = false;
                    break;
                }

                candidateScore += conversionCost;
                argumentExpressions[i] = BuildTypedInvocationArgument(parameter.Type, argumentExpression);
                normalizedArguments[i] = normalizedArgument;
            }

            if (!canUseCandidate)
            {
                continue;
            }

            var candidateSortKey = candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (bestCandidate is null ||
                candidateScore < bestScore ||
                (candidateScore == bestScore && objectParameterCount < bestObjectParameterCount) ||
                (candidateScore == bestScore &&
                 objectParameterCount == bestObjectParameterCount &&
                 string.Compare(candidateSortKey, bestSortKey, StringComparison.Ordinal) < 0))
            {
                bestCandidate = candidate;
                bestScore = candidateScore;
                bestObjectParameterCount = objectParameterCount;
                bestSortKey = candidateSortKey;
                bestArgumentExpressions = argumentExpressions;
                bestNormalizedArguments = normalizedArguments;
            }
        }

        if (bestCandidate is not null && bestArgumentExpressions is not null && bestNormalizedArguments is not null)
        {
            invocationExpression = bestCandidate.Name + "(" + string.Join(", ", bestArgumentExpressions) + ")";
            normalizedSegment = bestCandidate.Name + "(" + string.Join(", ", bestNormalizedArguments) + ")";
            returnType = bestCandidate.ReturnType;
            return true;
        }

        errorMessage = $"method segment '{methodName}' arguments do not match available overloads on '{targetType.ToDisplayString()}'";
        return false;
    }

    private static bool TryConvertMethodArgumentToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken,
        out int conversionCost)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        conversionCost = int.MaxValue;
        var tokenWasQuoted = IsQuotedLiteral(normalizedToken);

        if (parameterType is INamedTypeSymbol nullableType &&
            IsNullableValueType(nullableType))
        {
            if (normalizedToken.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            if (TryConvertMethodArgumentToken(
                    rawToken,
                    nullableType.TypeArguments[0],
                    out expression,
                    out normalizedToken,
                    out var underlyingCost))
            {
                conversionCost = underlyingCost + 1;
                return true;
            }

            return false;
        }

        if (normalizedToken.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            if (parameterType.IsReferenceType)
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            return false;
        }

        var unquotedToken = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Char &&
            unquotedToken.Length == 1)
        {
            expression = "'" + EscapeChar(unquotedToken[0]) + "'";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Boolean &&
            bool.TryParse(unquotedToken, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            int.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int64 &&
            long.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
            normalizedToken = longValue.ToString(CultureInfo.InvariantCulture);
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Double &&
            double.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Single &&
            float.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            expression = floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
            normalizedToken = floatValue.ToString("R", CultureInfo.InvariantCulture);
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Object)
        {
            if (bool.TryParse(unquotedToken, out boolValue))
            {
                expression = boolValue ? "true" : "false";
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (int.TryParse(unquotedToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                expression = intValue.ToString(CultureInfo.InvariantCulture);
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (double.TryParse(unquotedToken, NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = 60;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum &&
            parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(unquotedToken, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                conversionCost = 0;
                return true;
            }
        }

        return false;
    }

    private static string BuildTypedInvocationArgument(ITypeSymbol parameterType, string expression)
    {
        if (parameterType.TypeKind == TypeKind.Dynamic)
        {
            return expression;
        }

        return "(" +
               parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               ")(" +
               expression +
               ")";
    }

    private static bool IsNullableValueType(INamedTypeSymbol type)
    {
        return type.IsGenericType &&
               type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
               type.TypeArguments.Length == 1;
    }

    private static bool TryConvertIndexerToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        var token = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(token) + "\"";
            normalizedToken = token;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum && parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                return true;
            }
        }

        return false;
    }

    private static bool IsIdentifierStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) || ch == '_';
    }

    private static bool IsSelectorTokenStart(char ch)
    {
        return char.IsLetter(ch) || ch == '_';
    }

    private static bool IsSelectorTokenPart(char ch)
    {
        if (IsSelectorTokenStart(ch) || ch == '-')
        {
            return true;
        }

        var category = char.GetUnicodeCategory(ch);
        return category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.ConnectorPunctuation
            or UnicodeCategory.Format
            or UnicodeCategory.DecimalDigitNumber;
    }

    private static ITypeSymbol? TryResolveSetterValueType(
        INamedTypeSymbol? objectType,
        ImmutableArray<XamlPropertyAssignment> propertyAssignments,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType)
    {
        if (!IsSetterType(objectType))
        {
            return null;
        }

        foreach (var assignment in propertyAssignments)
        {
            if (assignment.IsAttached)
            {
                continue;
            }

            if (!NormalizePropertyName(assignment.PropertyName).Equals("Property", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryResolveAvaloniaPropertyValueTypeFromToken(
                    assignment.Value,
                    compilation,
                    document,
                    defaultOwnerType,
                    out var valueType))
            {
                return valueType;
            }
        }

        return null;
    }

    private static bool IsSetterType(INamedTypeSymbol? objectType)
    {
        return objectType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               "global::Avalonia.Styling.Setter";
    }

    private static bool TryResolveAvaloniaPropertyValueTypeFromToken(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out ITypeSymbol? valueType)
    {
        valueType = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var token = rawValue.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        INamedTypeSymbol? ownerType = defaultOwnerType;
        var propertyToken = token;
        var separatorIndex = token.LastIndexOf('.');
        if (separatorIndex > 0 && separatorIndex < token.Length - 1)
        {
            var ownerToken = token.Substring(0, separatorIndex);
            propertyToken = token.Substring(separatorIndex + 1);
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
        }

        if (propertyToken.EndsWith("Property", StringComparison.Ordinal))
        {
            propertyToken = propertyToken.Substring(0, propertyToken.Length - "Property".Length);
        }

        if (ownerType is null)
        {
            return false;
        }

        if (!TryFindAvaloniaPropertyField(ownerType, propertyToken, out _, out var propertyField))
        {
            return false;
        }

        valueType = TryGetAvaloniaPropertyValueType(propertyField.Type);
        return valueType is not null;
    }

    private static (ResolvedChildAttachmentMode Mode, string? ContentPropertyName) DetermineChildAttachment(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return (ResolvedChildAttachmentMode.None, null);
        }

        var contentPropertyName = FindContentPropertyName(symbol);
        var contentProperty = !string.IsNullOrWhiteSpace(contentPropertyName)
            ? FindProperty(symbol, contentPropertyName!)
            : null;
        if (contentProperty?.SetMethod is not null)
        {
            return (ResolvedChildAttachmentMode.Content, contentProperty.Name);
        }

        var implicitContentProperty = FindProperty(symbol, "Content");
        if (implicitContentProperty?.SetMethod is not null)
        {
            return (ResolvedChildAttachmentMode.Content, implicitContentProperty.Name);
        }

        if (IsStyleBaseType(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        if (CanAddToCollectionProperty(symbol, "Children"))
        {
            return (ResolvedChildAttachmentMode.ChildrenCollection, null);
        }

        if (CanAddToCollectionProperty(symbol, "Items"))
        {
            return (ResolvedChildAttachmentMode.ItemsCollection, null);
        }

        if (HasDictionaryAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DictionaryAdd, null);
        }

        if (HasDirectAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        return (ResolvedChildAttachmentMode.None, null);
    }

    private static bool IsStyleBaseType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Styling.StyleBase")
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindContentPropertyName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.SetMethod is null)
                {
                    continue;
                }

                if (property.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Avalonia.Metadata.ContentAttribute" ||
                        attribute.AttributeClass?.Name == "ContentAttribute"))
                {
                    return property.Name;
                }
            }
        }

        return null;
    }

    private static BindingPriorityScope ResolveCurrentBindingPriorityScope(
        INamedTypeSymbol? nodeType,
        Compilation compilation,
        BindingPriorityScope inheritedScope)
    {
        if (nodeType is null)
        {
            return inheritedScope;
        }

        var typeName = nodeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (typeName is "global::Avalonia.Styling.Style" or "global::Avalonia.Styling.ControlTheme")
        {
            return BindingPriorityScope.Style;
        }

        if (typeName is "global::Avalonia.Markup.Xaml.Templates.ControlTemplate"
            or "global::Avalonia.Markup.Xaml.Templates.ItemsPanelTemplate"
            or "global::Avalonia.Markup.Xaml.Templates.Template")
        {
            return BindingPriorityScope.Template;
        }

        var iControlTemplate = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IControlTemplate");
        if (iControlTemplate is not null &&
            IsTypeAssignableTo(nodeType, iControlTemplate))
        {
            return BindingPriorityScope.Template;
        }

        return inheritedScope;
    }

    private static bool HasDirectAddMethod(INamedTypeSymbol type)
    {
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(type))
        {
            var hasAdd = current.GetMembers("Add").OfType<IMethodSymbol>().Any(method =>
                !method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.DeclaredAccessibility == Accessibility.Public);

            if (hasAdd)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDictionaryAddMethod(INamedTypeSymbol type)
    {
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(type))
        {
            var hasAdd = current.GetMembers("Add").OfType<IMethodSymbol>().Any(method =>
                !method.IsStatic &&
                method.Parameters.Length == 2 &&
                method.DeclaredAccessibility == Accessibility.Public);

            if (hasAdd)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchyAndInterfaces(INamedTypeSymbol type)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (visited.Add(current))
            {
                yield return current;
            }

            foreach (var interfaceType in current.AllInterfaces)
            {
                if (visited.Add(interfaceType))
                {
                    yield return interfaceType;
                }
            }
        }
    }

    private static bool TryBuildKeyedDictionaryMergeContainer(
        IPropertySymbol property,
        ImmutableArray<ResolvedObjectNode> values,
        int line,
        int column,
        out ResolvedObjectNode container)
    {
        container = null!;
        if (values.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.KeyExpression))
            {
                return false;
            }
        }

        container = new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            FactoryExpression: null,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: values,
            ChildAttachmentMode: ResolvedChildAttachmentMode.DictionaryAdd,
            ContentPropertyName: null,
            Line: line,
            Column: column);
        return true;
    }

    private static bool CanAddToCollectionProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        return HasDirectAddMethod(namedType);
    }

    private static bool CanMergeDictionaryProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        if (property.GetMethod is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        return HasDictionaryAddMethod(namedType);
    }

    private static bool TryBindCollectionLiteralPropertyAssignment(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        out ResolvedPropertyElementAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (LooksLikeMarkupExtension(assignment.Value))
        {
            return false;
        }

        if (!property.Name.Equals("Classes", StringComparison.Ordinal))
        {
            return false;
        }

        var styledElementType = compilation.GetTypeByMetadataName("Avalonia.StyledElement");
        if (styledElementType is null || !IsTypeAssignableTo(objectType, styledElementType))
        {
            return false;
        }

        if (property.Type is not INamedTypeSymbol propertyType || !HasDirectAddMethod(propertyType))
        {
            return false;
        }

        var classTokens = SplitClassTokens(assignment.Value);
        var values = ImmutableArray.CreateBuilder<ResolvedObjectNode>(classTokens.Length);
        foreach (var classToken in classTokens)
        {
            values.Add(new ResolvedObjectNode(
                KeyExpression: null,
                Name: null,
                TypeName: "global::System.String",
                FactoryExpression: "\"" + Escape(classToken) + "\"",
                UseServiceProviderConstructor: false,
                UseTopDownInitialization: false,
                PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
                PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
                EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
                Children: ImmutableArray<ResolvedObjectNode>.Empty,
                ChildAttachmentMode: ResolvedChildAttachmentMode.None,
                ContentPropertyName: null,
                Line: assignment.Line,
                Column: assignment.Column));
        }

        resolvedAssignment = new ResolvedPropertyElementAssignment(
            PropertyName: property.Name,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            BindingPriorityExpression: null,
            IsCollectionAdd: true,
            IsDictionaryMerge: false,
            ObjectValues: values.ToImmutable(),
            Line: assignment.Line,
            Column: assignment.Column);
        return true;
    }

    private static ImmutableArray<string> SplitClassTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ImmutableArray<string>.Empty;
        }

        var rawTokens = value.Split(
            [' ', '\t', '\r', '\n', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawTokens.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(rawTokens.Length);
        foreach (var token in rawTokens)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                builder.Add(token);
            }
        }

        return builder.ToImmutable();
    }

    private static bool TryBindAttachedPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        string targetTypeName,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;

        var separator = assignment.PropertyName.LastIndexOf('.');
        if (separator <= 0 || separator >= assignment.PropertyName.Length - 1)
        {
            return false;
        }

        var ownerToken = assignment.PropertyName.Substring(0, separator);
        var attachedPropertyName = assignment.PropertyName.Substring(separator + 1);
        var ownerType = ResolveTypeSymbol(compilation, assignment.XmlNamespace, ownerToken)
                        ?? ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null)
        {
            return false;
        }

        return TryBindAvaloniaPropertyAssignment(
            targetType,
            targetTypeName,
            attachedPropertyName,
            assignment,
            compilation,
            document,
            options,
            diagnostics,
            compiledBindings,
            compileBindingsEnabled,
            nodeDataType,
            fallbackValueType: null,
            bindingPriorityScope,
            out resolvedAssignment,
            allowCompiledBindingRegistration: true,
            explicitOwnerType: ownerType);
    }

    private static bool TryBindEventSubscription(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = NormalizePropertyName(assignment.PropertyName);

        if (FindEvent(targetType, eventName) is { } eventSymbol)
        {
            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            var isCompatible = HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, eventSymbol.Type);
            if (!isCompatible)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.ClrEvent,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column);
            return true;
        }

        if (TryFindStaticEventField(
                targetType,
                eventName,
                out var routedEventOwnerType,
                out var routedEventField))
        {
            if (!TryResolveRoutedEventHandlerType(routedEventField.Type, compilation, out var routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event definition '{routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{routedEventField.Name}' is not compatible with Avalonia routed events.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (!HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RoutedEventFieldName: routedEventField.Name,
                RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: assignment.Line,
                Column: assignment.Column);
            return true;
        }

        return false;
    }

    private static bool TryBindAvaloniaPropertyAssignment(
        INamedTypeSymbol targetType,
        string targetTypeName,
        string propertyName,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        ITypeSymbol? fallbackValueType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool allowCompiledBindingRegistration = true,
        INamedTypeSymbol? explicitOwnerType = null)
    {
        resolvedAssignment = null;

        if (!TryFindAvaloniaPropertyField(
                explicitOwnerType ?? targetType,
                propertyName,
                out var ownerType,
                out var propertyField))
        {
            return false;
        }

        if (TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
        {
            if (TryReportBindingSourceConflict(
                    bindingMarkup,
                    diagnostics,
                    document,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode))
            {
                return true;
            }

            var shouldCompileBinding = CanUseCompiledBinding(bindingMarkup) &&
                                       (bindingMarkup.IsCompiledBinding || compileBindingsEnabled);
            if (shouldCompileBinding)
            {
                if (nodeDataType is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0110",
                        $"Compiled binding for '{propertyName}' requires x:DataType in scope.",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                if (!TryBuildCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        nodeDataType,
                        bindingMarkup.Path,
                        out var accessorExpression,
                        out var normalizedPath,
                        out var errorMessage))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0111",
                        $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{nodeDataType.ToDisplayString()}': {errorMessage}",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                if (allowCompiledBindingRegistration)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: normalizedPath,
                        SourceTypeName: nodeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        AccessorExpression: accessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column));
                }
            }

            if (TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    targetType,
                    bindingPriorityScope,
                    out var bindingExpression))
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: bindingExpression,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                        bindingPriorityScope),
                    Line: assignment.Line,
                    Column: assignment.Column);
            }

            return shouldCompileBinding || resolvedAssignment is not null;
        }

        var valueType = fallbackValueType ?? TryGetAvaloniaPropertyValueType(propertyField.Type);
        if ((valueType is null || !TryConvertValueExpression(assignment.Value, valueType, compilation, document, null, bindingPriorityScope, out var valueExpression)) &&
            !TryConvertUntypedValueExpression(assignment.Value, out valueExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{assignment.Value}' for Avalonia property '{propertyName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: propertyName,
            ValueExpression: valueExpression,
            AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            AvaloniaPropertyFieldName: propertyField.Name,
            ClrPropertyOwnerTypeName: null,
            ClrPropertyTypeName: null,
            BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                targetType,
                propertyField,
                compilation,
                bindingPriorityScope),
            Line: assignment.Line,
            Column: assignment.Column);
        return true;
    }

    private static bool TryReportBindingSourceConflict(
        BindingMarkup bindingMarkup,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        bool strictMode)
    {
        if (!bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0111",
            bindingMarkup.SourceConflictMessage ?? "Binding source configuration is invalid.",
            document.FilePath,
            line,
            column,
            strictMode));
        return true;
    }

    private static bool TryBuildBindingValueExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol propertyType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (!CanAssignBindingValue(propertyType, compilation))
        {
            return false;
        }

        if (TryBuildRuntimeBindingExpression(
                compilation,
                document,
                bindingMarkup,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        return false;
    }

    private static bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (compilation.GetTypeByMetadataName("Avalonia.Data.Binding") is not INamedTypeSymbol bindingType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\")";
            return true;
        }

        expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static bool TryBuildReflectionBindingExtensionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension") is not INamedTypeSymbol reflectionBindingExtensionType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression =
                "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
                Escape(normalizedPath) +
                "\")";
            return true;
        }

        expression =
            "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
            Escape(normalizedPath) +
            "\") { " +
            string.Join(", ", initializerParts) +
            " }";
        return true;
    }

    private static bool TryConvertOnPlatformExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression))
        {
            return false;
        }

        if (!TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Windows"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var windowsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "macOS", "MacOS", "OSX"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var macOsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Linux"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var linuxExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Android"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var androidExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "iOS", "IOS"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var iosExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Browser"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var browserExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnPlatform(" +
            defaultExpression +
            ", " +
            windowsExpression +
            ", " +
            macOsExpression +
            ", " +
            linuxExpression +
            ", " +
            androidExpression +
            ", " +
            iosExpression +
            ", " +
            browserExpression +
            ")");
        return true;
    }

    private static bool TryConvertOnFormFactorExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Desktop"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var desktopExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Mobile"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var mobileExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "TV"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var tvExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(" +
            defaultExpression +
            ", " +
            desktopExpression +
            ", " +
            mobileExpression +
            ", " +
            tvExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ")");
        return true;
    }

    private static bool TryConvertMarkupOptionValueExpression(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = "null";
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return true;
        }

        return TryConvertValueExpression(
            Unquote(rawToken!),
            targetType,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }

    private static void AddBindingInitializerPart(
        INamedTypeSymbol bindingType,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        List<string> initializerParts)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        if (!TryGetWritableProperty(bindingType, propertyName, out var property))
        {
            return;
        }

        var normalizedToken = Unquote(rawValue!);
        if (!TryConvertValueExpression(
                normalizedToken,
                property.Type,
                compilation,
                document,
                setterTargetType,
                BindingPriorityScope.None,
                out var valueExpression))
        {
            return;
        }

        initializerParts.Add(propertyName + " = " + valueExpression);
    }

    private static bool TryGetWritableProperty(INamedTypeSymbol type, string propertyName, out IPropertySymbol property)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var candidate = current.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(item => !item.IsStatic &&
                                        !item.IsIndexer &&
                                        item.SetMethod is not null);
            if (candidate is not null)
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private static bool TryBuildRelativeSourceExpression(
        RelativeSourceMarkup relativeSourceMarkup,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (compilation.GetTypeByMetadataName("Avalonia.Data.RelativeSource") is null)
        {
            return false;
        }

        var mode = relativeSourceMarkup.Mode;
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = !string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken)
                ? "FindAncestor"
                : "Self";
        }

        if (!TryMapRelativeSourceMode(mode!, out var relativeSourceModeExpression))
        {
            return false;
        }

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken))
        {
            var ancestorType = ResolveTypeToken(compilation, document, relativeSourceMarkup.AncestorTypeToken!, document.ClassNamespace);
            if (ancestorType is null)
            {
                return false;
            }

            initializerParts.Add("AncestorType = typeof(" +
                                 ancestorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                 ")");
        }

        if (relativeSourceMarkup.AncestorLevel.HasValue)
        {
            initializerParts.Add("AncestorLevel = " + relativeSourceMarkup.AncestorLevel.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.Tree) &&
            TryMapTreeType(relativeSourceMarkup.Tree!, out var treeTypeExpression))
        {
            initializerParts.Add("Tree = " + treeTypeExpression);
        }

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ")";
            return true;
        }

        expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static bool TryMapBindingMode(string modeToken, out string expression)
    {
        expression = string.Empty;
        var normalized = modeToken.Trim();
        if (normalized.StartsWith("global::Avalonia.Data.BindingMode.", StringComparison.Ordinal))
        {
            expression = normalized;
            return true;
        }

        expression = normalized.ToLowerInvariant() switch
        {
            "default" => "global::Avalonia.Data.BindingMode.Default",
            "oneway" => "global::Avalonia.Data.BindingMode.OneWay",
            "twoway" => "global::Avalonia.Data.BindingMode.TwoWay",
            "onewaytosource" => "global::Avalonia.Data.BindingMode.OneWayToSource",
            "onetime" => "global::Avalonia.Data.BindingMode.OneTime",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static bool TryMapRelativeSourceMode(string modeToken, out string expression)
    {
        expression = modeToken.Trim().ToLowerInvariant() switch
        {
            "self" => "global::Avalonia.Data.RelativeSourceMode.Self",
            "templatedparent" => "global::Avalonia.Data.RelativeSourceMode.TemplatedParent",
            "datacontext" => "global::Avalonia.Data.RelativeSourceMode.DataContext",
            "findancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            "ancestor" => "global::Avalonia.Data.RelativeSourceMode.FindAncestor",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static bool TryMapTreeType(string treeToken, out string expression)
    {
        expression = treeToken.Trim().ToLowerInvariant() switch
        {
            "visual" => "global::Avalonia.Data.TreeType.Visual",
            "logical" => "global::Avalonia.Data.TreeType.Logical",
            _ => string.Empty
        };

        return expression.Length > 0;
    }

    private static string? GetDefaultBindingPriorityToken(BindingPriorityScope scope)
    {
        return scope switch
        {
            BindingPriorityScope.Style => "Style",
            BindingPriorityScope.Template => "Template",
            _ => null
        };
    }

    private static string? GetSetValueBindingPriorityExpression(
        INamedTypeSymbol targetType,
        IFieldSymbol propertyField,
        Compilation compilation,
        BindingPriorityScope scope)
    {
        if (scope != BindingPriorityScope.Template)
        {
            return null;
        }

        if (!IsStyledOrAttachedAvaloniaProperty(propertyField))
        {
            return null;
        }

        if (!HasSetValueWithPriorityOverload(targetType, compilation))
        {
            return null;
        }

        return "global::Avalonia.Data.BindingPriority.Template";
    }

    private static bool IsStyledOrAttachedAvaloniaProperty(IFieldSymbol propertyField)
    {
        for (var current = propertyField.Type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name is "StyledProperty" or "AvaloniaAttachedProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSetValueWithPriorityOverload(INamedTypeSymbol targetType, Compilation compilation)
    {
        var bindingPriorityType = compilation.GetTypeByMetadataName("Avalonia.Data.BindingPriority");
        if (bindingPriorityType is null)
        {
            return false;
        }

        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("SetValue").OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.Parameters.Length != 3)
                {
                    continue;
                }

                if (!IsAvaloniaPropertyType(method.Parameters[0].Type))
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[2].Type, bindingPriorityType))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool CanAssignBindingValue(ITypeSymbol propertyType, Compilation compilation)
    {
        if (propertyType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        var iBindingType = compilation.GetTypeByMetadataName("Avalonia.Data.IBinding");
        if (iBindingType is null)
        {
            return false;
        }

        if (SymbolEqualityComparer.Default.Equals(propertyType, iBindingType))
        {
            return true;
        }

        if (propertyType is INamedTypeSymbol namedPropertyType &&
            namedPropertyType.AllInterfaces.Any(interfaceType => SymbolEqualityComparer.Default.Equals(interfaceType, iBindingType)))
        {
            return true;
        }

        for (var current = propertyType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, iBindingType))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? TryGetAvaloniaPropertyValueType(ITypeSymbol propertyFieldType)
    {
        if (propertyFieldType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.IsGenericType &&
            namedType.Name == "AvaloniaProperty" &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.Name == "AvaloniaProperty" &&
                current.TypeArguments.Length == 1)
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool TryFindAvaloniaPropertyField(
        INamedTypeSymbol ownerType,
        string propertyName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField)
    {
        var fieldName = propertyName + "Property";
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is not null)
            {
                resolvedOwnerType = current;
                propertyField = field;
                return true;
            }
        }

        resolvedOwnerType = ownerType;
        propertyField = null!;
        return false;
    }

    private static bool TryConvertUntypedValueExpression(string value, out string expression)
    {
        var trimmed = value.Trim();

        if (bool.TryParse(trimmed, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            expression = doubleValue.ToString("R", CultureInfo.InvariantCulture);
            return true;
        }

        expression = "\"" + Escape(trimmed) + "\"";
        return true;
    }

    private static bool TryParseHandlerName(string value, out string? handlerName)
    {
        handlerName = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.IndexOf('.') >= 0)
        {
            return false;
        }

        if (!IsIdentifier(trimmed))
        {
            return false;
        }

        handlerName = trimmed;
        return true;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var ch = value[index];
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasCompatibleInstanceMethod(
        INamedTypeSymbol type,
        string methodName,
        ITypeSymbol delegateType)
    {
        if (delegateType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod)
        {
            return HasInstanceMethod(type, methodName);
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsMethodCompatibleWithDelegate(method, invokeMethod))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasInstanceMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary);
            if (method is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMethodCompatibleWithDelegate(
        IMethodSymbol candidate,
        IMethodSymbol delegateInvoke)
    {
        if (candidate.Parameters.Length != delegateInvoke.Parameters.Length)
        {
            return false;
        }

        if (delegateInvoke.ReturnsVoid != candidate.ReturnsVoid)
        {
            return false;
        }

        if (!delegateInvoke.ReturnsVoid &&
            !IsTypeAssignableTo(candidate.ReturnType, delegateInvoke.ReturnType))
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < delegateInvoke.Parameters.Length; parameterIndex++)
        {
            var delegateParameter = delegateInvoke.Parameters[parameterIndex];
            var candidateParameter = candidate.Parameters[parameterIndex];
            if (!IsTypeAssignableTo(delegateParameter.Type, candidateParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindStaticEventField(
        INamedTypeSymbol targetType,
        string eventName,
        out INamedTypeSymbol ownerType,
        out IFieldSymbol eventField)
    {
        var fieldName = eventName + "Event";
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is null)
            {
                continue;
            }

            ownerType = current;
            eventField = field;
            return true;
        }

        ownerType = targetType;
        eventField = null!;
        return false;
    }

    private static bool TryResolveRoutedEventHandlerType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol handlerType)
    {
        handlerType = compilation.GetTypeByMetadataName("System.Delegate") ?? compilation.ObjectType;
        if (!TryGetRoutedEventArgsType(routedEventType, compilation, out var routedEventArgsType))
        {
            return false;
        }

        var eventHandlerType = compilation.GetTypeByMetadataName("System.EventHandler`1");
        var eventArgsBaseType = compilation.GetTypeByMetadataName("System.EventArgs");
        if (eventHandlerType is INamedTypeSymbol eventHandlerNamed &&
            eventArgsBaseType is not null &&
            IsTypeAssignableTo(routedEventArgsType, eventArgsBaseType))
        {
            handlerType = eventHandlerNamed.Construct(routedEventArgsType);
            return true;
        }

        var routedEventHandlerType = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEventHandler");
        if (routedEventHandlerType is not null)
        {
            handlerType = routedEventHandlerType;
            return true;
        }

        return true;
    }

    private static bool TryGetRoutedEventArgsType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol routedEventArgsType)
    {
        routedEventArgsType = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEventArgs")
                              ?? compilation.GetTypeByMetadataName("System.EventArgs")
                              ?? compilation.ObjectType;

        if (routedEventType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var routedEventTypeSymbol = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent");
        var genericRoutedEventTypeSymbol = compilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent`1");
        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            var isGenericRoutedEvent = genericRoutedEventTypeSymbol is not null &&
                                       SymbolEqualityComparer.Default.Equals(
                                           current.OriginalDefinition,
                                           genericRoutedEventTypeSymbol);
            if (!isGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                current.IsGenericType &&
                current.TypeArguments.Length == 1)
            {
                isGenericRoutedEvent = true;
            }

            if (isGenericRoutedEvent && current.TypeArguments.Length == 1)
            {
                routedEventArgsType = current.TypeArguments[0];
                return true;
            }

            var isNonGenericRoutedEvent = routedEventTypeSymbol is not null &&
                                          SymbolEqualityComparer.Default.Equals(current, routedEventTypeSymbol);
            if (!isNonGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                !current.IsGenericType)
            {
                isNonGenericRoutedEvent = true;
            }

            if (isNonGenericRoutedEvent)
            {
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? FindParameterlessMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary &&
                member.Parameters.Length == 0);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindAttachedPropertyGetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol targetType)
    {
        var getterName = "Get" + propertyName;
        foreach (var method in ownerType.GetMembers(getterName).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic ||
                method.MethodKind != MethodKind.Ordinary ||
                method.Parameters.Length != 1)
            {
                continue;
            }

            if (IsTypeAssignableTo(targetType, method.Parameters[0].Type))
            {
                return method;
            }
        }

        return null;
    }

    private static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is INamedTypeSymbol sourceNamed)
        {
            for (INamedTypeSymbol? current = sourceNamed; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, targetType))
                {
                    return true;
                }

                foreach (var implementedInterface in current.Interfaces)
                {
                    if (SymbolEqualityComparer.Default.Equals(implementedInterface, targetType))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string propertyName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static string NormalizePropertyName(string propertyName)
    {
        var separator = propertyName.LastIndexOf('.');
        return separator < 0 ? propertyName : propertyName.Substring(separator + 1);
    }

    private static bool HasResolvedPropertyAssignment(
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValueExpression(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (TryConvertMarkupExtensionExpression(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (type is INamedTypeSymbol nullableType &&
            nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nullableType.TypeArguments.Length == 1)
        {
            if (value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                expression = "null";
                return true;
            }

            return TryConvertValueExpression(
                value,
                nullableType.TypeArguments[0],
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression);
        }

        if (IsAvaloniaPropertyType(type) &&
            TryResolveAvaloniaPropertyReferenceExpression(value, compilation, document, setterTargetType, out expression))
        {
            return true;
        }

        if (IsAvaloniaSelectorType(type) &&
            TryBuildSimpleSelectorExpression(value, compilation, document, setterTargetType, out expression))
        {
            return true;
        }

        var escaped = Escape(value);

        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullyQualifiedTypeName is "global::System.Globalization.CultureInfo" or "global::System.Globalization.CultureInfo?")
        {
            expression = "global::System.Globalization.CultureInfo.GetCultureInfo(\"" + escaped + "\")";
            return true;
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + escaped + "\"";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Boolean && bool.TryParse(value, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int32 && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int64 && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Double && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
        {
            expression = doubleValue.ToString("R", CultureInfo.InvariantCulture) + "d";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Single && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue))
        {
            expression = floatValue.ToString("R", CultureInfo.InvariantCulture) + "f";
            return true;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(value, StringComparison.OrdinalIgnoreCase));

            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                return true;
            }
        }

        if (type.ToDisplayString() == "System.Uri")
        {
            expression = "new global::System.Uri(\"" + escaped + "\", global::System.UriKind.RelativeOrAbsolute)";
            return true;
        }

        if (TryConvertAvaloniaBrushExpression(type, value, compilation, out expression))
        {
            return true;
        }

        if (TryConvertByStaticParseMethod(type, value, out expression))
        {
            return true;
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            expression = "\"" + escaped + "\"";
            return true;
        }

        return false;
    }

    private static bool TryConvertAvaloniaBrushExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (targetType.SpecialType == SpecialType.System_Object ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var iBrushType = compilation.GetTypeByMetadataName("Avalonia.Media.IBrush");
        var brushType = compilation.GetTypeByMetadataName("Avalonia.Media.Brush");
        if (iBrushType is null || brushType is null)
        {
            return false;
        }

        if (!IsTypeAssignableTo(iBrushType, targetType))
        {
            return false;
        }

        expression = "global::Avalonia.Media.Brush.Parse(\"" + Escape(value.Trim()) + "\")";
        return true;
    }

    private static bool IsAvaloniaSelectorType(ITypeSymbol type)
    {
        var display = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return display == "global::Avalonia.Styling.Selector" ||
               display == "global::Avalonia.Styling.Selector?";
    }

    private static bool TryBuildSimpleSelectorExpression(
        string selector,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        out string expression)
    {
        expression = string.Empty;
        var text = selector.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var branchExpressions = ImmutableArray.CreateBuilder<string>();
        foreach (var branch in SplitTopLevel(text, ','))
        {
            var trimmedBranch = branch.Trim();
            if (trimmedBranch.Length == 0)
            {
                return false;
            }

            if (!TryBuildSimpleSelectorBranchExpression(
                    trimmedBranch,
                    compilation,
                    document,
                    selectorTypeFallback,
                    out var branchExpression))
            {
                return false;
            }

            branchExpressions.Add(branchExpression);
        }

        if (branchExpressions.Count == 0)
        {
            return false;
        }

        expression = branchExpressions.Count == 1
            ? branchExpressions[0]
            : "global::Avalonia.Styling.Selectors.Or(" + string.Join(", ", branchExpressions) + ")";
        return true;
    }

    private static bool TryBuildSimpleSelectorBranchExpression(
        string selectorBranch,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        out string expression)
    {
        expression = string.Empty;
        if (!TryTokenizeSelectorBranch(selectorBranch, out var segments))
        {
            return false;
        }

        var currentExpression = "null";
        var hasExpression = false;
        foreach (var segment in segments)
        {
            if (segment.Combinator != SelectorCombinatorKind.None)
            {
                if (!hasExpression)
                {
                    return false;
                }

                currentExpression = segment.Combinator switch
                {
                    SelectorCombinatorKind.Descendant => "global::Avalonia.Styling.Selectors.Descendant(" + currentExpression + ")",
                    SelectorCombinatorKind.Child => "global::Avalonia.Styling.Selectors.Child(" + currentExpression + ")",
                    SelectorCombinatorKind.Template => "global::Avalonia.Styling.Selectors.Template(" + currentExpression + ")",
                    _ => currentExpression
                };
            }

            var segmentText = segment.Text.Trim();
            if (segmentText.Length == 0)
            {
                return false;
            }

            var segmentApplied = false;
            var index = 0;
            INamedTypeSymbol? selectorTypeHint = selectorTypeFallback;
            while (index < segmentText.Length && segmentText[index] == '^')
            {
                currentExpression = "global::Avalonia.Styling.Selectors.Nesting(" + (hasExpression ? currentExpression : "null") + ")";
                hasExpression = true;
                segmentApplied = true;
                index++;
            }

            while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
            {
                index++;
            }

            var isWildcard = false;
            if (index < segmentText.Length && segmentText[index] == '*')
            {
                isWildcard = true;
                index++;
            }

            if (TryReadSelectorTypeToken(segmentText, ref index, out var typeToken))
            {
                if (!string.IsNullOrWhiteSpace(typeToken))
                {
                    var resolvedType = ResolveTypeToken(compilation, document, typeToken!, document.ClassNamespace);
                    if (resolvedType is null)
                    {
                        return false;
                    }

                    currentExpression = "global::Avalonia.Styling.Selectors.OfType(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", typeof(" +
                                        resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                        "))";
                    selectorTypeHint = resolvedType as INamedTypeSymbol;
                    hasExpression = true;
                    segmentApplied = true;
                }
            }
            else
            {
                return false;
            }

            while (index < segmentText.Length)
            {
                while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
                {
                    index++;
                }

                if (index >= segmentText.Length)
                {
                    break;
                }

                var tokenType = segmentText[index];
                if (tokenType == '[')
                {
                    if (!TryReadBalancedSelectorContent(segmentText, ref index, '[', ']', out var predicateText) ||
                        !TryApplySelectorPropertyPredicate(
                            predicateText,
                            compilation,
                            document,
                            selectorTypeHint,
                            ref currentExpression,
                            ref hasExpression))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (tokenType != '.' && tokenType != '#' && tokenType != ':')
                {
                    return false;
                }

                index++;
                if (index >= segmentText.Length || !IsSelectorTokenStart(segmentText[index]))
                {
                    return false;
                }

                var tokenStart = index;
                while (index < segmentText.Length &&
                       IsSelectorTokenPart(segmentText[index]))
                {
                    index++;
                }

                if (tokenStart == index)
                {
                    return false;
                }

                var tokenValue = segmentText.Substring(tokenStart, index - tokenStart);
                if (tokenType == ':' && index < segmentText.Length && segmentText[index] == '(')
                {
                    if (!TryReadBalancedSelectorContent(segmentText, ref index, '(', ')', out var pseudoArgument) ||
                        !TryApplySelectorPseudoFunction(
                            tokenValue,
                            pseudoArgument,
                            compilation,
                            document,
                            selectorTypeFallback,
                            ref currentExpression,
                            ref hasExpression,
                            ref selectorTypeHint))
                    {
                        return false;
                    }

                    segmentApplied = true;
                    continue;
                }

                if (tokenType == '.')
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Class(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \"" +
                                        Escape(tokenValue) +
                                        "\")";
                }
                else if (tokenType == '#')
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Name(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \"" +
                                        Escape(tokenValue) +
                                        "\")";
                }
                else
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Class(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", \":" +
                                        Escape(tokenValue) +
                                        "\")";
                }

                hasExpression = true;
                segmentApplied = true;
            }

            if (!segmentApplied && isWildcard)
            {
                var styledElementType = compilation.GetTypeByMetadataName("Avalonia.StyledElement");
                if (styledElementType is not null)
                {
                    currentExpression = "global::Avalonia.Styling.Selectors.Is(" +
                                        (hasExpression ? currentExpression : "null") +
                                        ", typeof(" +
                                        styledElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                        "))";
                    selectorTypeHint = styledElementType;
                    hasExpression = true;
                    segmentApplied = true;
                }
            }

            if (!segmentApplied)
            {
                return false;
            }
        }

        if (!hasExpression)
        {
            return false;
        }

        expression = currentExpression;
        return true;
    }

    private static bool TryReadBalancedSelectorContent(
        string text,
        ref int index,
        char openChar,
        char closeChar,
        out string content)
    {
        content = string.Empty;
        if (index >= text.Length || text[index] != openChar)
        {
            return false;
        }

        index++;
        var contentStart = index;
        var depth = 1;
        var inQuote = false;
        var quoteChar = '\0';

        while (index < text.Length)
        {
            var ch = text[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                index++;
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                index++;
                continue;
            }

            if (ch == openChar)
            {
                depth++;
                index++;
                continue;
            }

            if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                {
                    content = text.Substring(contentStart, index - contentStart);
                    index++;
                    return true;
                }

                index++;
                continue;
            }

            index++;
        }

        return false;
    }

    private static bool TryApplySelectorPseudoFunction(
        string pseudoName,
        string pseudoArgument,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? selectorTypeFallback,
        ref string currentExpression,
        ref bool hasExpression,
        ref INamedTypeSymbol? selectorTypeHint)
    {
        var normalizedPseudoName = pseudoName.Trim().ToLowerInvariant();
        if (normalizedPseudoName.Length == 0)
        {
            return false;
        }

        var previousExpression = hasExpression ? currentExpression : "null";
        if (normalizedPseudoName == "is")
        {
            var typeToken = pseudoArgument.Trim().Replace('|', ':');
            if (typeToken.Length == 0)
            {
                return false;
            }

            var resolvedType = ResolveTypeToken(compilation, document, typeToken, document.ClassNamespace);
            if (resolvedType is null)
            {
                return false;
            }

            currentExpression = "global::Avalonia.Styling.Selectors.Is(" +
                                previousExpression +
                                ", typeof(" +
                                resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                "))";
            selectorTypeHint = resolvedType as INamedTypeSymbol;
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "not")
        {
            if (!TryBuildSimpleSelectorExpression(
                    pseudoArgument,
                    compilation,
                    document,
                    selectorTypeHint ?? selectorTypeFallback,
                    out var argumentExpression))
            {
                return false;
            }

            currentExpression = "global::Avalonia.Styling.Selectors.Not(" +
                                previousExpression +
                                ", " +
                                argumentExpression +
                                ")";
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "nth-child" &&
            TryParseNthChildPseudoArguments(pseudoArgument, out var nthChildStep, out var nthChildOffset))
        {
            currentExpression = "global::Avalonia.Styling.Selectors.NthChild(" +
                                previousExpression +
                                ", " +
                                nthChildStep.ToString(CultureInfo.InvariantCulture) +
                                ", " +
                                nthChildOffset.ToString(CultureInfo.InvariantCulture) +
                                ")";
            hasExpression = true;
            return true;
        }

        if (normalizedPseudoName == "nth-last-child" &&
            TryParseNthChildPseudoArguments(pseudoArgument, out var nthLastChildStep, out var nthLastChildOffset))
        {
            currentExpression = "global::Avalonia.Styling.Selectors.NthLastChild(" +
                                previousExpression +
                                ", " +
                                nthLastChildStep.ToString(CultureInfo.InvariantCulture) +
                                ", " +
                                nthLastChildOffset.ToString(CultureInfo.InvariantCulture) +
                                ")";
            hasExpression = true;
            return true;
        }

        return false;
    }

    private static bool TryParseNthChildPseudoArguments(string pseudoArgument, out int step, out int offset)
    {
        step = 0;
        offset = 0;

        var text = pseudoArgument.Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return false;
        }

        if (text == "odd")
        {
            step = 2;
            offset = 1;
            return true;
        }

        if (text == "even")
        {
            step = 2;
            offset = 0;
            return true;
        }

        var compact = text.Replace(" ", string.Empty);
        var nIndex = compact.IndexOf('n');
        if (nIndex < 0)
        {
            if (!int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
            {
                return false;
            }

            step = 0;
            return true;
        }

        var stepToken = compact.Substring(0, nIndex);
        if (stepToken.Length == 0 || stepToken == "+")
        {
            step = 1;
        }
        else if (stepToken == "-")
        {
            step = -1;
        }
        else if (!int.TryParse(stepToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out step))
        {
            return false;
        }

        var offsetToken = compact.Substring(nIndex + 1);
        if (offsetToken.Length == 0)
        {
            offset = 0;
            return true;
        }

        if (!int.TryParse(offsetToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out offset))
        {
            return false;
        }

        return true;
    }

    private static bool TryApplySelectorPropertyPredicate(
        string predicateText,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        ref string currentExpression,
        ref bool hasExpression)
    {
        if (!TryParseSelectorPropertyPredicate(predicateText, out var propertyToken, out var rawValue))
        {
            return false;
        }

        if (!TryResolveAvaloniaPropertyReferenceExpression(
                propertyToken,
                compilation,
                document,
                defaultOwnerType,
                out var propertyExpression))
        {
            return false;
        }

        if (!TryConvertUntypedValueExpression(Unquote(rawValue), out var valueExpression))
        {
            return false;
        }

        currentExpression = "global::Avalonia.Styling.Selectors.PropertyEquals(" +
                            (hasExpression ? currentExpression : "null") +
                            ", " +
                            propertyExpression +
                            ", " +
                            valueExpression +
                            ")";
        hasExpression = true;
        return true;
    }

    private static bool TryParseSelectorPropertyPredicate(
        string predicateText,
        out string propertyToken,
        out string rawValue)
    {
        propertyToken = string.Empty;
        rawValue = string.Empty;
        if (string.IsNullOrWhiteSpace(predicateText))
        {
            return false;
        }

        var equalsIndex = IndexOfTopLevel(predicateText, '=');
        if (equalsIndex <= 0 || equalsIndex >= predicateText.Length - 1)
        {
            return false;
        }

        var propertyText = predicateText.Substring(0, equalsIndex).Trim();
        rawValue = predicateText.Substring(equalsIndex + 1).Trim();
        if (propertyText.Length == 0 || rawValue.Length == 0)
        {
            return false;
        }

        if (propertyText.StartsWith("(", StringComparison.Ordinal) &&
            propertyText.EndsWith(")", StringComparison.Ordinal) &&
            propertyText.Length > 2)
        {
            propertyText = propertyText.Substring(1, propertyText.Length - 2).Trim();
        }

        propertyToken = propertyText.Replace('|', ':');
        return propertyToken.Length > 0;
    }

    private static bool TryTokenizeSelectorBranch(string selectorBranch, out ImmutableArray<SelectorBranchSegment> segments)
    {
        segments = ImmutableArray<SelectorBranchSegment>.Empty;
        var builder = ImmutableArray.CreateBuilder<SelectorBranchSegment>();
        var index = 0;
        var hasSegment = false;
        var pendingCombinator = SelectorCombinatorKind.None;

        while (index < selectorBranch.Length)
        {
            var consumedWhitespace = false;
            while (index < selectorBranch.Length && char.IsWhiteSpace(selectorBranch[index]))
            {
                consumedWhitespace = true;
                index++;
            }

            if (index >= selectorBranch.Length)
            {
                break;
            }

            if (consumedWhitespace &&
                hasSegment &&
                pendingCombinator == SelectorCombinatorKind.None)
            {
                pendingCombinator = SelectorCombinatorKind.Descendant;
            }

            if (selectorBranch[index] == '>')
            {
                if (!hasSegment)
                {
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Child;
                index++;
                continue;
            }

            if (IsSelectorTemplateAxisAt(selectorBranch, index))
            {
                if (!hasSegment)
                {
                    return false;
                }

                pendingCombinator = SelectorCombinatorKind.Template;
                index += SelectorTemplateAxisToken.Length;
                continue;
            }

            var segmentStart = index;
            var bracketDepth = 0;
            var parenthesisDepth = 0;
            var inQuote = false;
            var quoteChar = '\0';

            while (index < selectorBranch.Length)
            {
                var ch = selectorBranch[index];
                if (inQuote)
                {
                    if (ch == quoteChar)
                    {
                        inQuote = false;
                    }

                    index++;
                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    inQuote = true;
                    quoteChar = ch;
                    index++;
                    continue;
                }

                if (ch == '[')
                {
                    bracketDepth++;
                    index++;
                    continue;
                }

                if (ch == ']')
                {
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    index++;
                    continue;
                }

                if (ch == '(')
                {
                    parenthesisDepth++;
                    index++;
                    continue;
                }

                if (ch == ')')
                {
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    index++;
                    continue;
                }

                if (bracketDepth == 0 && parenthesisDepth == 0)
                {
                    if (char.IsWhiteSpace(ch) || ch == '>' || IsSelectorTemplateAxisAt(selectorBranch, index))
                    {
                        break;
                    }
                }

                index++;
            }

            var segmentText = selectorBranch.Substring(segmentStart, index - segmentStart).Trim();
            if (segmentText.Length == 0)
            {
                return false;
            }

            builder.Add(new SelectorBranchSegment(segmentText, pendingCombinator));
            pendingCombinator = SelectorCombinatorKind.None;
            hasSegment = true;
        }

        if (builder.Count == 0 || pendingCombinator != SelectorCombinatorKind.None)
        {
            return false;
        }

        segments = builder.ToImmutable();
        return true;
    }

    private static bool TryReadSelectorTypeToken(string segmentText, ref int index, out string? typeToken)
    {
        typeToken = null;
        while (index < segmentText.Length && char.IsWhiteSpace(segmentText[index]))
        {
            index++;
        }

        if (index >= segmentText.Length || !IsIdentifierStart(segmentText[index]))
        {
            return true;
        }

        var aliasStart = index;
        index++;
        while (index < segmentText.Length && IsIdentifierPart(segmentText[index]))
        {
            index++;
        }

        var aliasOrType = segmentText.Substring(aliasStart, index - aliasStart);
        if (index < segmentText.Length && segmentText[index] == '|')
        {
            index++;
            if (index >= segmentText.Length || !IsIdentifierStart(segmentText[index]))
            {
                return false;
            }

            var typeStart = index;
            index++;
            while (index < segmentText.Length && IsIdentifierPart(segmentText[index]))
            {
                index++;
            }

            var typeName = segmentText.Substring(typeStart, index - typeStart);
            typeToken = aliasOrType + ":" + typeName;
            return true;
        }

        typeToken = aliasOrType;
        return true;
    }

    private static bool IsSelectorTemplateAxisAt(string text, int index)
    {
        if (index < 0 || index + SelectorTemplateAxisToken.Length > text.Length)
        {
            return false;
        }

        return text.Substring(index, SelectorTemplateAxisToken.Length)
            .Equals(SelectorTemplateAxisToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryConvertMarkupExtensionExpression(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        if (TryConvertXamlPrimitiveMarkupExtension(markup, targetType, out expression))
        {
            return true;
        }

        switch (markup.Name.ToLowerInvariant())
        {
            case "binding":
            case "compiledbinding":
            {
                if (!TryParseBindingMarkup(value, out var bindingMarkup))
                {
                    return false;
                }

                if (bindingMarkup.HasSourceConflict)
                {
                    return false;
                }

                return TryBuildBindingValueExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    targetType,
                    setterTargetType,
                    bindingPriorityScope,
                    out expression);
            }
            case "x:null":
            case "null":
            {
                expression = "null";
                return true;
            }
            case "x:type":
            case "type":
            {
                var typeToken = markup.NamedArguments.TryGetValue("Type", out var explicitType)
                    ? explicitType
                    : markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName)
                        ? explicitTypeName
                        : markup.PositionalArguments.Length > 0
                            ? markup.PositionalArguments[0]
                            : null;
                if (string.IsNullOrWhiteSpace(typeToken))
                {
                    return false;
                }

                var resolvedType = ResolveTypeToken(compilation, document, Unquote(typeToken!), document.ClassNamespace);
                if (resolvedType is null)
                {
                    return false;
                }

                expression = "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")";
                return true;
            }
            case "x:static":
            case "static":
            {
                var memberToken = markup.NamedArguments.TryGetValue("Member", out var explicitMember)
                    ? explicitMember
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(memberToken))
                {
                    return false;
                }

                if (!TryResolveStaticMemberExpression(compilation, document, Unquote(memberToken!), out expression))
                {
                    return false;
                }

                return true;
            }
            case "staticresource":
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                var keyExpression = "\"" + Escape(Unquote(keyToken!)) + "\"";
                var staticResourceExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideStaticResource(" +
                    keyExpression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                expression = WrapWithTargetTypeCast(targetType, staticResourceExpression);
                return true;
            }
            case "dynamicresource":
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension") is null)
                {
                    return false;
                }

                expression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(\"" +
                    Escape(Unquote(keyToken!)) +
                    "\", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                return true;
            }
            case "reflectionbinding":
            {
                if (!TryParseReflectionBindingMarkup(value, out var reflectionBindingMarkup))
                {
                    return false;
                }

                if (reflectionBindingMarkup.HasSourceConflict ||
                    !TryBuildReflectionBindingExtensionExpression(
                        compilation,
                        document,
                        reflectionBindingMarkup,
                        setterTargetType,
                        bindingPriorityScope,
                        out var reflectionBindingExtensionExpression))
                {
                    return false;
                }

                expression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(" +
                    reflectionBindingExtensionExpression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                return true;
            }
            case "relativesource":
            {
                if (!TryParseRelativeSourceMarkup(value, out var relativeSourceMarkup) ||
                    !TryBuildRelativeSourceExpression(relativeSourceMarkup, compilation, document, out var relativeSourceExpression))
                {
                    return false;
                }

                expression = WrapWithTargetTypeCast(targetType, relativeSourceExpression);
                return true;
            }
            case "onplatform":
            {
                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.OnPlatformExtension") is null)
                {
                    return false;
                }

                if (!TryConvertOnPlatformExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out expression))
                {
                    return false;
                }

                return true;
            }
            case "onformfactor":
            {
                if (compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtensions.OnFormFactorExtension") is null)
                {
                    return false;
                }

                if (!TryConvertOnFormFactorExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out expression))
                {
                    return false;
                }

                return true;
            }
            case "x:reference":
            case "reference":
            case "resolvebyname":
            {
                var referenceName = TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                                    (markup.PositionalArguments.Length > 0 ? Unquote(markup.PositionalArguments[0]) : null);
                if (string.IsNullOrWhiteSpace(referenceName))
                {
                    return false;
                }

                var resolveExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
                    Escape(referenceName!.Trim()) +
                    "\", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                if (!targetType.IsReferenceType &&
                    targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T &&
                    targetType.SpecialType != SpecialType.System_Object)
                {
                    return false;
                }

                expression = WrapWithTargetTypeCast(targetType, resolveExpression);
                return true;
            }
            case "templatebinding":
            {
                var propertyToken = markup.NamedArguments.TryGetValue("Property", out var explicitProperty)
                    ? explicitProperty
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(propertyToken) || setterTargetType is null)
                {
                    return false;
                }

                if (!TryResolveAvaloniaPropertyReferenceExpression(
                        Unquote(propertyToken!),
                        compilation,
                        document,
                        setterTargetType,
                        out var propertyExpression))
                {
                    return false;
                }

                if (compilation.GetTypeByMetadataName("Avalonia.Data.TemplateBinding") is null)
                {
                    return false;
                }

                expression = "new global::Avalonia.Data.TemplateBinding(" + propertyExpression + ")";
                return true;
            }
            default:
                return TryConvertGenericMarkupExtensionExpression(
                    markup,
                    targetType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out expression);
        }
    }

    private static bool TryConvertXamlPrimitiveMarkupExtension(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        expression = string.Empty;
        var name = markup.Name.Trim().ToLowerInvariant();
        var rawValue = TryGetNamedMarkupArgument(markup, "Value") ??
                       (markup.PositionalArguments.Length > 0 ? Unquote(markup.PositionalArguments[0]) : null);
        switch (name)
        {
            case "x:true":
            case "true":
                expression = "true";
                return true;
            case "x:false":
            case "false":
                expression = "false";
                return true;
            case "x:string":
            case "string":
            {
                var value = rawValue ?? string.Empty;
                expression = "\"" + Escape(value ?? string.Empty) + "\"";
                return true;
            }
            case "x:char":
            case "char":
            {
                if (string.IsNullOrEmpty(rawValue))
                {
                    return false;
                }

                var trimmedValue = rawValue!.Trim();
                if (trimmedValue.StartsWith("\\u", StringComparison.OrdinalIgnoreCase) &&
                    trimmedValue.Length == 6 &&
                    int.TryParse(trimmedValue.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var unicodeCode))
                {
                    expression = "'\\u" + unicodeCode.ToString("x4", CultureInfo.InvariantCulture) + "'";
                    return true;
                }

                if (trimmedValue.Length != 1)
                {
                    return false;
                }

                expression = "'" + EscapeChar(trimmedValue[0]) + "'";
                return true;
            }
            case "x:byte":
            case "byte":
            {
                if (!byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((byte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:sbyte":
            case "sbyte":
            {
                if (!sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((sbyte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:int16":
            case "int16":
            {
                if (!short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((short)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:uint16":
            case "uint16":
            {
                if (!ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((ushort)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case "x:int32":
            case "int32":
            {
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            case "x:uint32":
            case "uint32":
            {
                if (!uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            }
            case "x:int64":
            case "int64":
            {
                if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            }
            case "x:uint64":
            case "uint64":
            {
                if (!ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            }
            case "x:single":
            case "single":
            case "x:double":
            case "double":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                if (!double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = targetType.SpecialType == SpecialType.System_Single
                    ? parsed.ToString("R", CultureInfo.InvariantCulture) + "f"
                    : parsed.ToString("R", CultureInfo.InvariantCulture) + "d";
                return true;
            }
            case "x:decimal":
            case "decimal":
            {
                if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
            }
            case "x:datetime":
            case "datetime":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "global::System.DateTime.Parse(\"" + Escape(rawValue.Trim()) + "\", global::System.Globalization.CultureInfo.InvariantCulture, global::System.Globalization.DateTimeStyles.RoundtripKind)";
                return true;
            }
            case "x:timespan":
            case "timespan":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "global::System.TimeSpan.Parse(\"" + Escape(rawValue.Trim()) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
                return true;
            }
            case "x:uri":
            case "uri":
            {
                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return false;
                }

                expression = "new global::System.Uri(\"" + Escape(rawValue.Trim()) + "\", global::System.UriKind.RelativeOrAbsolute)";
                return true;
            }
            case "x:null":
            case "null":
                expression = "null";
                return true;
            default:
                return false;
        }
    }

    private static bool TryConvertGenericMarkupExtensionExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryResolveMarkupExtensionType(compilation, document, markup.Name, out var extensionType))
        {
            return false;
        }

        if (extensionType is null)
        {
            return false;
        }

        var constructor = extensionType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                !candidate.IsStatic)
            .Where(candidate => candidate.Parameters.Length == markup.PositionalArguments.Length)
            .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();

        if (constructor is null && markup.PositionalArguments.Length > 0)
        {
            return false;
        }

        var positionalExpressions = new List<string>(markup.PositionalArguments.Length);
        for (var index = 0; index < markup.PositionalArguments.Length; index++)
        {
            var positionalArgument = markup.PositionalArguments[index];
            var positionalTargetType = constructor?.Parameters[index].Type;
            if (!TryConvertMarkupArgumentExpression(
                    positionalArgument,
                    positionalTargetType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var positionalExpression))
            {
                return false;
            }

            positionalExpressions.Add(positionalExpression);
        }

        var constructorExpression = "new " + extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "(" +
                                    string.Join(", ", positionalExpressions) +
                                    ")";

        var initializerExpressions = new List<string>();
        foreach (var namedArgument in markup.NamedArguments)
        {
            var property = extensionType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(candidate =>
                    candidate.SetMethod is not null &&
                    candidate.Name.Equals(namedArgument.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                return false;
            }

            if (!TryConvertMarkupArgumentExpression(
                    namedArgument.Value,
                    property.Type,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var propertyExpression))
            {
                return false;
            }

            initializerExpressions.Add(property.Name + " = " + propertyExpression);
        }

        if (initializerExpressions.Count > 0)
        {
            constructorExpression += " { " + string.Join(", ", initializerExpressions) + " }";
        }

        var runtimeProvideValueExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideMarkupExtension(" +
            constructorExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";

        expression = WrapWithTargetTypeCast(targetType, runtimeProvideValueExpression);
        return true;
    }

    private static bool TryConvertMarkupArgumentExpression(
        string rawValue,
        ITypeSymbol? targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        var value = rawValue.Trim();
        if (value.Length == 0)
        {
            expression = "null";
            return true;
        }

        var conversionTargetType = targetType ?? compilation.GetTypeByMetadataName("System.Object");
        if (conversionTargetType is null)
        {
            return false;
        }

        if (TryConvertValueExpression(
                Unquote(value),
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (TryConvertMarkupExtensionExpression(
                value,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            expression = "\"" + Escape(Unquote(value)) + "\"";
            return true;
        }

        return false;
    }

    private static bool TryResolveMarkupExtensionType(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType)
    {
        extensionType = null;
        var token = markupName.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        token = token switch
        {
            "x:Null" or "x:null" => string.Empty,
            _ => token
        };
        if (token.Length == 0)
        {
            return false;
        }

        if (!token.EndsWith("Extension", StringComparison.Ordinal))
        {
            token += "Extension";
        }

        extensionType = ResolveTypeToken(compilation, document, token, document.ClassNamespace);
        if (extensionType is null)
        {
            return false;
        }

        var markupExtensionBase = compilation.GetTypeByMetadataName("Avalonia.Markup.Xaml.MarkupExtension");
        if (markupExtensionBase is null)
        {
            return false;
        }

        return IsTypeAssignableTo(extensionType, markupExtensionBase);
    }

    private static string WrapWithTargetTypeCast(ITypeSymbol targetType, string expression)
    {
        if (targetType.SpecialType == SpecialType.System_Object)
        {
            return expression;
        }

        return "(" + targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + expression;
    }

    private static bool TryConvertByStaticParseMethod(ITypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var parseMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseMethod is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ".Parse(\"" + Escape(value) + "\")";
        return true;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveAvaloniaPropertyReferenceExpression(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out string expression)
    {
        expression = string.Empty;
        var token = rawValue.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var separatorIndex = token.LastIndexOf('.');
        var ownerType = defaultOwnerType;
        var propertyName = token;

        if (separatorIndex > 0 && separatorIndex < token.Length - 1)
        {
            var ownerToken = token.Substring(0, separatorIndex);
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
            propertyName = token.Substring(separatorIndex + 1);
        }

        if (ownerType is null ||
            !TryFindAvaloniaPropertyField(ownerType, propertyName, out var resolvedOwnerType, out var propertyField))
        {
            return false;
        }

        expression = resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + propertyField.Name;
        return true;
    }

    private static bool TryResolveStaticMemberExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string memberToken,
        out string expression)
    {
        expression = string.Empty;
        var separatorIndex = memberToken.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= memberToken.Length - 1)
        {
            return false;
        }

        var ownerToken = memberToken.Substring(0, separatorIndex);
        var memberName = memberToken.Substring(separatorIndex + 1);
        var ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null)
        {
            return false;
        }

        var staticField = ownerType.GetMembers(memberName)
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => field.IsStatic);
        if (staticField is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticField.Name;
            return true;
        }

        var staticProperty = ownerType.GetMembers(memberName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.IsStatic);
        if (staticProperty is not null)
        {
            expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + staticProperty.Name;
            return true;
        }

        return false;
    }

    private static bool RequiresStaticResourceResolver(ResolvedObjectNode root)
    {
        return HasStaticResourceExpression(root);
    }

    private static bool HasStaticResourceExpression(ResolvedObjectNode node)
    {
        foreach (var assignment in node.PropertyAssignments)
        {
            if (assignment.ValueExpression.Contains("__ResolveStaticResource(", StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var propertyElement in node.PropertyElementAssignments)
        {
            foreach (var value in propertyElement.ObjectValues)
            {
                if (HasStaticResourceExpression(value))
                {
                    return true;
                }
            }
        }

        foreach (var child in node.Children)
        {
            if (HasStaticResourceExpression(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        markupExtension = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) ||
            !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            return false;
        }

        var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
        if (inner.Length == 0)
        {
            return false;
        }

        var headLength = 0;
        while (headLength < inner.Length &&
               !char.IsWhiteSpace(inner[headLength]) &&
               inner[headLength] != ',')
        {
            headLength++;
        }

        var name = inner.Substring(0, headLength).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        var argumentsText = headLength < inner.Length ? inner.Substring(headLength).Trim() : string.Empty;
        if (argumentsText.StartsWith(",", StringComparison.Ordinal))
        {
            argumentsText = argumentsText.Substring(1).TrimStart();
        }

        var positional = ImmutableArray.CreateBuilder<string>();
        var named = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(argumentsText))
        {
            foreach (var token in SplitTopLevel(argumentsText, ','))
            {
                var argument = token.Trim();
                if (argument.Length == 0)
                {
                    continue;
                }

                var equalsIndex = IndexOfTopLevel(argument, '=');
                if (equalsIndex > 0)
                {
                    var key = argument.Substring(0, equalsIndex).Trim();
                    var argumentValue = argument.Substring(equalsIndex + 1).Trim();
                    if (key.Length == 0)
                    {
                        positional.Add(argument);
                        continue;
                    }

                    named[key] = argumentValue;
                    continue;
                }

                positional.Add(argument);
            }
        }

        markupExtension = new MarkupExtensionInfo(name, positional.ToImmutable(), named.ToImmutable());
        return true;
    }

    private static bool LooksLikeMarkupExtension(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.StartsWith("{", StringComparison.Ordinal) &&
               trimmed.EndsWith("}", StringComparison.Ordinal);
    }

    private static bool TryParseRelativeSourceMarkup(string value, out RelativeSourceMarkup relativeSourceMarkup)
    {
        relativeSourceMarkup = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (TryParseMarkupExtension(value, out var markupExtension))
        {
            if (!markupExtension.Name.Equals("RelativeSource", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var mode = markupExtension.NamedArguments.TryGetValue("Mode", out var explicitMode)
                ? Unquote(explicitMode)
                : markupExtension.PositionalArguments.Length > 0
                    ? Unquote(markupExtension.PositionalArguments[0])
                    : null;

            int? ancestorLevel = null;
            if ((markupExtension.NamedArguments.TryGetValue("AncestorLevel", out var rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("FindAncestor", out rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("Level", out rawAncestorLevel)) &&
                int.TryParse(Unquote(rawAncestorLevel), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAncestorLevel))
            {
                ancestorLevel = parsedAncestorLevel;
            }

            var ancestorType = markupExtension.NamedArguments.TryGetValue("AncestorType", out var rawAncestorType)
                ? Unquote(rawAncestorType)
                : null;
            var tree = markupExtension.NamedArguments.TryGetValue("Tree", out var rawTree)
                ? Unquote(rawTree)
                : markupExtension.NamedArguments.TryGetValue("TreeType", out var rawTreeType)
                    ? Unquote(rawTreeType)
                : null;

            relativeSourceMarkup = new RelativeSourceMarkup(mode, ancestorType, ancestorLevel, tree);
            return true;
        }

        relativeSourceMarkup = new RelativeSourceMarkup(
            mode: Unquote(value.Trim()),
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    private static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var result = new List<string>();
        var start = 0;
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }
                    break;
                default:
                    if (ch == separator &&
                        braceDepth == 0 &&
                        bracketDepth == 0 &&
                        parenthesisDepth == 0)
                    {
                        result.Add(value.Substring(start, index - start));
                        start = index + 1;
                    }
                    break;
            }
        }

        if (start <= value.Length)
        {
            result.Add(value.Substring(start));
        }

        return result;
    }

    private static int IndexOfTopLevel(string value, char token)
    {
        var braceDepth = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var inQuote = false;
        var quoteChar = '\0';

        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (inQuote)
            {
                if (ch == quoteChar)
                {
                    inQuote = false;
                }

                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuote = true;
                quoteChar = ch;
                continue;
            }

            switch (ch)
            {
                case '{':
                    braceDepth++;
                    continue;
                case '}':
                    if (braceDepth > 0)
                    {
                        braceDepth--;
                    }

                    continue;
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }

                    continue;
            }

            if (ch == token &&
                braceDepth == 0 &&
                bracketDepth == 0 &&
                parenthesisDepth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }

    private static bool IsQuotedLiteral(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
                (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\''));
    }

    private static string EscapeChar(char value)
    {
        return value switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            '\0' => "\\0",
            _ => value.ToString()
        };
    }

    private static INamedTypeSymbol? ResolveTypeFromTypeExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? typeExpression,
        string? fallbackClrNamespace)
    {
        if (string.IsNullOrWhiteSpace(typeExpression))
        {
            return null;
        }

        var token = ExtractTypeToken(typeExpression!);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        return ResolveTypeToken(compilation, document, token, fallbackClrNamespace);
    }

    private static string ExtractTypeToken(string typeExpression)
    {
        var trimmed = typeExpression.Trim();

        if (trimmed.StartsWith("{x:Type", StringComparison.OrdinalIgnoreCase) &&
            trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            var inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
            var firstSpace = inner.IndexOf(' ');
            if (firstSpace >= 0)
            {
                var payload = inner.Substring(firstSpace + 1).Trim();

                if (payload.StartsWith("TypeName=", StringComparison.OrdinalIgnoreCase))
                {
                    payload = payload.Substring("TypeName=".Length).Trim();
                }
                else if (payload.StartsWith("Type=", StringComparison.OrdinalIgnoreCase))
                {
                    payload = payload.Substring("Type=".Length).Trim();
                }

                return payload;
            }
        }

        return trimmed;
    }

    private static string? TryExtractSelectorTypeToken(string selector)
    {
        var validation = SelectorSyntaxValidator.Validate(selector);
        if (!validation.IsValid || validation.Branches.Length != 1)
        {
            return null;
        }

        return validation.Branches[0].LastTypeToken;
    }

    private static INamedTypeSymbol? TryResolveSelectorTargetType(
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<SelectorSyntaxValidator.BranchInfo> branches,
        out string? unresolvedTypeToken,
        out int unresolvedTypeOffset)
    {
        unresolvedTypeToken = null;
        unresolvedTypeOffset = 0;
        if (branches.Length == 0)
        {
            return null;
        }

        var resolvedTypes = ImmutableArray.CreateBuilder<INamedTypeSymbol>(branches.Length);
        foreach (var branch in branches)
        {
            if (string.IsNullOrWhiteSpace(branch.LastTypeToken))
            {
                return null;
            }

            var resolved = ResolveTypeToken(compilation, document, branch.LastTypeToken!, document.ClassNamespace);
            if (resolved is null)
            {
                unresolvedTypeToken = branch.LastTypeToken;
                unresolvedTypeOffset = branch.LastTypeOffset;
                return null;
            }

            resolvedTypes.Add(resolved);
        }

        if (resolvedTypes.Count == 0)
        {
            return null;
        }

        var targetType = resolvedTypes[0];
        for (var index = 1; index < resolvedTypes.Count && targetType is not null; index++)
        {
            var candidate = resolvedTypes[index];
            while (targetType is not null && !IsTypeAssignableTo(candidate, targetType))
            {
                targetType = targetType.BaseType;
            }
        }

        return targetType;
    }

    private static (int Line, int Column) AdvanceLineAndColumn(
        int startLine,
        int startColumn,
        string source,
        int offset)
    {
        var line = Math.Max(1, startLine);
        var column = Math.Max(1, startColumn);
        if (string.IsNullOrEmpty(source) || offset <= 0)
        {
            return (line, column);
        }

        var cappedOffset = Math.Min(offset, source.Length);
        for (var index = 0; index < cappedOffset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    private const string SelectorTemplateAxisToken = "/template/";

    private static IEnumerable<string> SplitSelectorSegments(string selectorBranch)
    {
        var segments = new List<string>();
        var start = 0;
        var bracketDepth = 0;
        var parenthesisDepth = 0;

        for (var index = 0; index < selectorBranch.Length; index++)
        {
            var ch = selectorBranch[index];
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    continue;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }
                    continue;
                case '(':
                    parenthesisDepth++;
                    continue;
                case ')':
                    if (parenthesisDepth > 0)
                    {
                        parenthesisDepth--;
                    }
                    continue;
            }

            if (bracketDepth > 0 || parenthesisDepth > 0)
            {
                continue;
            }

            if (ch == '>')
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                start = index + 1;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                while (index + 1 < selectorBranch.Length && char.IsWhiteSpace(selectorBranch[index + 1]))
                {
                    index++;
                }

                start = index + 1;
                continue;
            }

            if (IsSelectorTemplateAxisAt(selectorBranch, index))
            {
                AddSelectorSegment(selectorBranch, start, index, segments);
                index += SelectorTemplateAxisToken.Length - 1;
                start = index + 1;
            }
        }

        AddSelectorSegment(selectorBranch, start, selectorBranch.Length, segments);
        return segments;
    }

    private static string? TryExtractLeadingSelectorTypeToken(string selectorSegment)
    {
        if (string.IsNullOrWhiteSpace(selectorSegment))
        {
            return null;
        }

        var text = selectorSegment.Trim();
        var index = 0;
        while (index < text.Length && (text[index] == '^' || char.IsWhiteSpace(text[index])))
        {
            index++;
        }

        if (index >= text.Length || text[index] == '*' || !IsIdentifierStart(text[index]))
        {
            return null;
        }

        var aliasStart = index;
        index++;
        while (index < text.Length && IsIdentifierPart(text[index]))
        {
            index++;
        }

        var aliasOrType = text.Substring(aliasStart, index - aliasStart);
        if (index < text.Length && text[index] == '|')
        {
            index++;
            if (index >= text.Length || !IsIdentifierStart(text[index]))
            {
                return null;
            }

            var typeStart = index;
            index++;
            while (index < text.Length && IsIdentifierPart(text[index]))
            {
                index++;
            }

            var typeName = text.Substring(typeStart, index - typeStart);
            return aliasOrType + ":" + typeName;
        }

        return aliasOrType;
    }

    private static void AddSelectorSegment(string value, int start, int end, List<string> segments)
    {
        var length = end - start;
        if (length <= 0)
        {
            return;
        }

        var segment = value.Substring(start, length).Trim();
        if (segment.Length > 0)
        {
            segments.Add(segment);
        }
    }

    private static INamedTypeSymbol? ResolveTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string? fallbackClrNamespace)
    {
        var normalized = token.Trim();
        if (normalized.StartsWith("global::", StringComparison.Ordinal))
        {
            normalized = normalized.Substring("global::".Length);
        }

        if (TryParseGenericTypeToken(normalized, out var genericTypeToken, out var genericArgumentTokens))
        {
            var genericType = ResolveTypeToken(compilation, document, genericTypeToken, fallbackClrNamespace);
            if (genericType is not null &&
                genericArgumentTokens.Length > 0)
            {
                var resolvedArguments = new List<ITypeSymbol>(genericArgumentTokens.Length);
                foreach (var genericArgumentToken in genericArgumentTokens)
                {
                    var resolvedArgument = ResolveTypeToken(compilation, document, genericArgumentToken, fallbackClrNamespace);
                    if (resolvedArgument is null)
                    {
                        resolvedArguments.Clear();
                        break;
                    }

                    resolvedArguments.Add(resolvedArgument);
                }

                if (resolvedArguments.Count == genericArgumentTokens.Length)
                {
                    if (genericType.TypeParameters.Length == resolvedArguments.Count)
                    {
                        return genericType.Construct(resolvedArguments.ToArray());
                    }

                    if (genericType.OriginalDefinition.TypeParameters.Length == resolvedArguments.Count)
                    {
                        return genericType.OriginalDefinition.Construct(resolvedArguments.ToArray());
                    }
                }
            }
        }

        var colonIndex = normalized.IndexOf(':');
        if (colonIndex > 0)
        {
            var prefix = normalized.Substring(0, colonIndex);
            var typeName = normalized.Substring(colonIndex + 1);

            if (document.XmlNamespaces.TryGetValue(prefix, out var xmlNamespace))
            {
                return ResolveTypeSymbol(compilation, xmlNamespace, typeName);
            }
        }

        if (normalized.IndexOf('.') >= 0)
        {
            var direct = compilation.GetTypeByMetadataName(normalized);
            if (direct is not null)
            {
                return direct;
            }
        }

        if (!string.IsNullOrWhiteSpace(fallbackClrNamespace))
        {
            var inFallbackNamespace = compilation.GetTypeByMetadataName(fallbackClrNamespace + "." + normalized);
            if (inFallbackNamespace is not null)
            {
                return inFallbackNamespace;
            }
        }

        if (document.XmlNamespaces.TryGetValue(string.Empty, out var defaultXmlNamespace))
        {
            var inDefaultXmlNamespace = ResolveTypeSymbol(compilation, defaultXmlNamespace, normalized);
            if (inDefaultXmlNamespace is not null)
            {
                return inDefaultXmlNamespace;
            }
        }

        return compilation.GetTypeByMetadataName("Avalonia.Controls." + normalized);
    }

    private static bool TryParseGenericTypeToken(
        string token,
        out string typeToken,
        out ImmutableArray<string> argumentTokens)
    {
        typeToken = string.Empty;
        argumentTokens = ImmutableArray<string>.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var normalized = token.Trim();
        var openingIndex = IndexOfTopLevel(normalized, '(');
        if (openingIndex <= 0 || !normalized.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var typePart = normalized.Substring(0, openingIndex).Trim();
        var argumentPart = normalized.Substring(openingIndex + 1, normalized.Length - openingIndex - 2).Trim();
        if (typePart.Length == 0 || argumentPart.Length == 0)
        {
            return false;
        }

        var parsedArguments = SplitTopLevel(argumentPart, ',')
            .Select(static argument => argument.Trim())
            .Where(static argument => argument.Length > 0)
            .ToImmutableArray();
        if (parsedArguments.Length == 0)
        {
            return false;
        }

        typeToken = typePart;
        argumentTokens = parsedArguments;
        return true;
    }

    private static string? ResolveTypeName(Compilation compilation, string xmlNamespace, string xmlTypeName, out INamedTypeSymbol? symbol)
    {
        symbol = ResolveTypeSymbol(compilation, xmlNamespace, xmlTypeName);
        return symbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(Compilation compilation, string xmlNamespace, string xmlTypeName)
    {
        return ResolveTypeSymbol(compilation, xmlNamespace, xmlTypeName, genericArity: null);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        int? genericArity)
    {
        if (TryResolveXamlDirectiveType(compilation, xmlNamespace, xmlTypeName, out var intrinsicType))
        {
            return intrinsicType;
        }

        var metadataName = TryBuildMetadataName(xmlNamespace, xmlTypeName, genericArity);
        if (metadataName is not null)
        {
            var resolved = compilation.GetTypeByMetadataName(metadataName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        if (xmlNamespace == "https://github.com/avaloniaui" || xmlNamespace == "https://github.com/avaloniaui/")
        {
            foreach (var namespacePrefix in AvaloniaDefaultNamespaceCandidates)
            {
                var candidateName = genericArity.HasValue && genericArity.Value > 0
                    ? namespacePrefix + AppendGenericArity(xmlTypeName, genericArity.Value)
                    : namespacePrefix + xmlTypeName;
                var candidate = compilation.GetTypeByMetadataName(candidateName);
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool TryResolveXamlDirectiveType(
        Compilation compilation,
        string xmlNamespace,
        string xmlTypeName,
        out INamedTypeSymbol? symbol)
    {
        symbol = null;
        if (xmlNamespace != Xaml2006.NamespaceName)
        {
            return false;
        }

        var normalizedTypeName = xmlTypeName.Trim();
        var metadataName = normalizedTypeName switch
        {
            "String" => "System.String",
            "Boolean" or "Bool" => "System.Boolean",
            "Char" => "System.Char",
            "Byte" => "System.Byte",
            "SByte" => "System.SByte",
            "Int16" => "System.Int16",
            "UInt16" => "System.UInt16",
            "Int32" => "System.Int32",
            "UInt32" => "System.UInt32",
            "Int64" => "System.Int64",
            "UInt64" => "System.UInt64",
            "Single" or "Float" => "System.Single",
            "Double" => "System.Double",
            "Decimal" => "System.Decimal",
            "DateTime" => "System.DateTime",
            "TimeSpan" => "System.TimeSpan",
            "Object" => "System.Object",
            "Array" => "System.Array",
            "Type" => "System.Type",
            "Uri" => "System.Uri",
            "Null" => "System.Object",
            _ => null
        };

        if (metadataName is null)
        {
            return false;
        }

        symbol = compilation.GetTypeByMetadataName(metadataName);
        return symbol is not null;
    }

    private static string? TryBuildMetadataName(string xmlNamespace, string xmlTypeName, int? genericArity)
    {
        if (xmlNamespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            var segment = xmlNamespace.Substring("clr-namespace:".Length);
            var separatorIndex = segment.IndexOf(';');
            var clrNamespace = separatorIndex < 0 ? segment : segment.Substring(0, separatorIndex);
            if (!string.IsNullOrWhiteSpace(clrNamespace))
            {
                return clrNamespace + "." + AppendGenericArity(xmlTypeName, genericArity);
            }
        }

        if (xmlNamespace == "https://github.com/avaloniaui" || xmlNamespace == "https://github.com/avaloniaui/")
        {
            return "Avalonia.Controls." + AppendGenericArity(xmlTypeName, genericArity);
        }

        return null;
    }

    private static string AppendGenericArity(string xmlTypeName, int? genericArity)
    {
        if (!genericArity.HasValue || genericArity.Value <= 0 || xmlTypeName.IndexOf('`') >= 0)
        {
            return xmlTypeName;
        }

        return xmlTypeName + "`" + genericArity.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizeClassModifier(string? classModifier)
    {
        if (string.IsNullOrWhiteSpace(classModifier))
        {
            return null;
        }

        var normalized = classModifier!.Trim().ToLowerInvariant();
        return normalized switch
        {
            "public" => "public",
            "internal" => "internal",
            "private" => "private",
            "protected" => "protected",
            "protected internal" => "protected internal",
            "private protected" => "private protected",
            "notpublic" => "internal",
            _ => null,
        };
    }

    private static string ToCSharpClassModifier(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal",
        };
    }

    private static bool ShouldUseServiceProviderConstructor(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        var publicInstanceConstructors = symbol.InstanceConstructors
            .Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public)
            .ToImmutableArray();
        if (publicInstanceConstructors.Length == 0)
        {
            return false;
        }

        if (publicInstanceConstructors.Any(static ctor => ctor.Parameters.Length == 0))
        {
            return false;
        }

        return publicInstanceConstructors.Any(IsSingleServiceProviderConstructor);
    }

    private static bool IsSingleServiceProviderConstructor(IMethodSymbol constructor)
    {
        if (constructor.Parameters.Length != 1)
        {
            return false;
        }

        var parameterType = constructor.Parameters[0].Type;
        return parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                   .Equals("global::System.IServiceProvider", StringComparison.Ordinal);
    }

    private static bool IsUsableDuringInitialization(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return false;
        }

        for (var current = symbol; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attributeType = attribute.AttributeClass;
                if (attributeType is null)
                {
                    continue;
                }

                var metadataName = attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!metadataName.Equals("global::Avalonia.Metadata.UsableDuringInitializationAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length == 0)
                {
                    return true;
                }

                var first = attribute.ConstructorArguments[0];
                if (first.Kind == TypedConstantKind.Primitive && first.Value is bool flag)
                {
                    return flag;
                }

                return true;
            }
        }

        return false;
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", string.Empty)
            .Replace("\n", "\\n");
    }

    private enum SelectorCombinatorKind
    {
        None,
        Descendant,
        Child,
        Template
    }

    private enum BindingPriorityScope
    {
        None,
        Style,
        Template
    }

    private readonly struct SelectorBranchSegment
    {
        public SelectorBranchSegment(string text, SelectorCombinatorKind combinator)
        {
            Text = text;
            Combinator = combinator;
        }

        public string Text { get; }

        public SelectorCombinatorKind Combinator { get; }
    }

    private readonly struct ItemContainerTypeMapping
    {
        public ItemContainerTypeMapping(string itemsControlMetadataName, string itemContainerMetadataName)
        {
            ItemsControlMetadataName = itemsControlMetadataName;
            ItemContainerMetadataName = itemContainerMetadataName;
        }

        public string ItemsControlMetadataName { get; }

        public string ItemContainerMetadataName { get; }
    }

    private readonly struct TemplatePartExpectation
    {
        public TemplatePartExpectation(ITypeSymbol? expectedType, bool isRequired)
        {
            ExpectedType = expectedType;
            IsRequired = isRequired;
        }

        public ITypeSymbol? ExpectedType { get; }

        public bool IsRequired { get; }
    }

    private readonly struct TemplatePartActual
    {
        public TemplatePartActual(INamedTypeSymbol? type, int line, int column)
        {
            Type = type;
            Line = line;
            Column = column;
        }

        public INamedTypeSymbol? Type { get; }

        public int Line { get; }

        public int Column { get; }
    }

    private readonly struct MarkupExtensionInfo
    {
        public MarkupExtensionInfo(
            string name,
            ImmutableArray<string> positionalArguments,
            ImmutableDictionary<string, string> namedArguments)
        {
            Name = name;
            PositionalArguments = positionalArguments;
            NamedArguments = namedArguments;
        }

        public string Name { get; }

        public ImmutableArray<string> PositionalArguments { get; }

        public ImmutableDictionary<string, string> NamedArguments { get; }
    }

    private readonly struct CompiledPathSegment
    {
        public CompiledPathSegment(
            string memberName,
            ImmutableArray<string> indexers,
            string? castTypeToken,
            bool isMethodCall,
            bool acceptsNull,
            ImmutableArray<string> methodArguments,
            bool isAttachedProperty,
            string? attachedOwnerTypeToken,
            int streamCount)
        {
            MemberName = memberName;
            Indexers = indexers;
            CastTypeToken = castTypeToken;
            IsMethodCall = isMethodCall;
            AcceptsNull = acceptsNull;
            MethodArguments = methodArguments;
            IsAttachedProperty = isAttachedProperty;
            AttachedOwnerTypeToken = attachedOwnerTypeToken;
            StreamCount = streamCount;
        }

        public string MemberName { get; }

        public ImmutableArray<string> Indexers { get; }

        public string? CastTypeToken { get; }

        public bool IsMethodCall { get; }

        public bool AcceptsNull { get; }

        public ImmutableArray<string> MethodArguments { get; }

        public bool IsAttachedProperty { get; }

        public string? AttachedOwnerTypeToken { get; }

        public int StreamCount { get; }
    }

    private readonly struct RelativeSourceMarkup
    {
        public RelativeSourceMarkup(string? mode, string? ancestorTypeToken, int? ancestorLevel, string? tree)
        {
            Mode = mode;
            AncestorTypeToken = ancestorTypeToken;
            AncestorLevel = ancestorLevel;
            Tree = tree;
        }

        public string? Mode { get; }

        public string? AncestorTypeToken { get; }

        public int? AncestorLevel { get; }

        public string? Tree { get; }
    }

    private readonly struct BindingMarkup
    {
        public BindingMarkup(
            bool isCompiledBinding,
            string path,
            string? mode,
            string? elementName,
            RelativeSourceMarkup? relativeSource,
            string? source,
            string? converter,
            string? converterCulture,
            string? converterParameter,
            string? stringFormat,
            string? fallbackValue,
            string? targetNullValue,
            string? delay,
            string? priority,
            string? updateSourceTrigger,
            bool hasSourceConflict,
            string? sourceConflictMessage)
        {
            IsCompiledBinding = isCompiledBinding;
            Path = path;
            Mode = mode;
            ElementName = elementName;
            RelativeSource = relativeSource;
            Source = source;
            Converter = converter;
            ConverterCulture = converterCulture;
            ConverterParameter = converterParameter;
            StringFormat = stringFormat;
            FallbackValue = fallbackValue;
            TargetNullValue = targetNullValue;
            Delay = delay;
            Priority = priority;
            UpdateSourceTrigger = updateSourceTrigger;
            HasSourceConflict = hasSourceConflict;
            SourceConflictMessage = sourceConflictMessage;
        }

        public bool IsCompiledBinding { get; }

        public string Path { get; }

        public string? Mode { get; }

        public string? ElementName { get; }

        public RelativeSourceMarkup? RelativeSource { get; }

        public string? Source { get; }

        public string? Converter { get; }

        public string? ConverterCulture { get; }

        public string? ConverterParameter { get; }

        public string? StringFormat { get; }

        public string? FallbackValue { get; }

        public string? TargetNullValue { get; }

        public string? Delay { get; }

        public string? Priority { get; }

        public string? UpdateSourceTrigger { get; }

        public bool HasSourceConflict { get; }

        public string? SourceConflictMessage { get; }
    }
}
