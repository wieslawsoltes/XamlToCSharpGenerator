using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static ResolvedTransformExtensions BuildResolvedTransformExtensions(
        Compilation compilation,
        XamlTransformConfiguration configuration,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        return TransformExtensionResolutionService.Resolve(
            compilation,
            configuration,
            diagnostics,
            options,
            FrameworkProfileIds.Avalonia,
            SourceGenXamlTypeAliasAttributeMetadataName,
            SourceGenXamlPropertyAliasAttributeMetadataName,
            SourceGenXamlFrameworkPropertyAliasAttributeMetadataName,
            SourceGenXamlAvaloniaPropertyAliasAttributeMetadataName);
    }

    private static PropertyAliasResolution ResolvePropertyAlias(
        INamedTypeSymbol? targetType,
        string propertyToken)
    {
        var normalizedPropertyName = NormalizePropertyName(propertyToken);
        var extensions = ActiveTransformExtensions.Value;
        if (targetType is null ||
            extensions is null ||
            extensions.PropertyAliases.IsDefaultOrEmpty)
        {
            return new PropertyAliasResolution(normalizedPropertyName);
        }

        return PropertyAliasResolutionService.Resolve(
            extensions.PropertyAliases,
            targetType,
            propertyToken);
    }

    private static int GetPropertyAliasTargetMatchScore(ResolvedPropertyAliasRule rule, INamedTypeSymbol targetType)
    {
        if (rule.TargetTypeName == "*")
        {
            return 1;
        }

        if (rule.TargetTypeSymbol is null)
        {
            return -1;
        }

        if (SymbolEqualityComparer.Default.Equals(targetType, rule.TargetTypeSymbol))
        {
            return 3;
        }

        return TypeSymbolLookupSemanticsService.IsTypeAssignableTo(targetType, rule.TargetTypeSymbol)
            ? 2
            : -1;
    }

    private static string PropertyNameFromField(string fieldName)
    {
        return XamlTokenSplitSemantics.TrimTerminalSuffix(fieldName, "Property");
    }

    private sealed class BindCustomTransformsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P000-BindCustomTransforms";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create(
                "TransformerConfiguration",
                "Avalonia.Metadata.XmlnsDefinitionAttribute");

        public void Execute(BindingTransformContext context)
        {
            context.TransformExtensions = BuildResolvedTransformExtensions(
                context.Compilation,
                context.TransformConfiguration,
                context.Diagnostics,
                context.Options);
            ActiveTransformExtensions.Value = context.TransformExtensions;
        }
    }

    private sealed class BindNamedElementsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P001-BindNamedElements";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create("AXSG-P010-BindRootObject", "XNameTransformer");

        public void Execute(BindingTransformContext context)
        {
            context.NamedElements.Clear();

            if (context.RootObject is null)
            {
                return;
            }

            var fieldModifierLookup = BuildNamedFieldModifierLookup(context.Document.NamedElements);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);
            CollectResolvedNamedElements(
                context.RootObject,
                context.NamedElements,
                context.Compilation,
                context.Document,
                context.Diagnostics,
                context.Options,
                fieldModifierLookup,
                seenNames);
        }
    }

    private static Dictionary<string, string> BuildNamedFieldModifierLookup(
        ImmutableArray<XamlNamedElement> namedElements)
    {
        var fieldModifierLookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var namedElement in namedElements)
        {
            if (string.IsNullOrWhiteSpace(namedElement.Name) ||
                fieldModifierLookup.ContainsKey(namedElement.Name))
            {
                continue;
            }

            fieldModifierLookup[namedElement.Name] = string.IsNullOrWhiteSpace(namedElement.FieldModifier)
                ? "internal"
                : namedElement.FieldModifier!;
        }

        return fieldModifierLookup;
    }

    private static void CollectResolvedNamedElements(
        ResolvedObjectNode node,
        ImmutableArray<ResolvedNamedElement>.Builder namedElements,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options,
        IReadOnlyDictionary<string, string> fieldModifierLookup,
        HashSet<string> seenNames)
    {
        if (ConditionalXamlEvaluationService.ShouldSkipBranch(
                node.Condition,
                compilation,
                document,
                diagnostics,
                options))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.Name) &&
            seenNames.Add(node.Name!))
        {
            var fieldModifier = fieldModifierLookup.TryGetValue(node.Name!, out var requestedModifier) &&
                                !string.IsNullOrWhiteSpace(requestedModifier)
                ? requestedModifier
                : "internal";
            namedElements.Add(new ResolvedNamedElement(
                Name: node.Name!,
                TypeName: node.TypeName,
                FieldModifier: fieldModifier,
                Line: node.Line,
                Column: node.Column));
        }

        foreach (var child in node.Children)
        {
            CollectResolvedNamedElements(
                child,
                namedElements,
                compilation,
                document,
                diagnostics,
                options,
                fieldModifierLookup,
                seenNames);
        }

        foreach (var propertyElementAssignment in node.PropertyElementAssignments)
        {
            foreach (var objectValue in propertyElementAssignment.ObjectValues)
            {
                CollectResolvedNamedElements(
                    objectValue,
                    namedElements,
                    compilation,
                    document,
                    diagnostics,
                    options,
                    fieldModifierLookup,
                    seenNames);
            }
        }
    }

    private sealed class BindRootObjectPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P010-BindRootObject";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create(
                "AvaloniaXamlIlClassesTransformer",
                "AvaloniaXamlIlResolveClassesPropertiesTransformer",
                "AvaloniaXamlIlAvaloniaPropertyResolver",
                "AvaloniaXamlIlTransformInstanceAttachedProperties",
                "AvaloniaXamlIlTransformRoutedEvent");

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
                unsafeAccessors: context.UnsafeAccessors,
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
            ImmutableArray.Create(
                "AvaloniaXamlResourceTransformer",
                "AvaloniaXamlIlEnsureResourceDictionaryCapacityTransformer");

        public void Execute(BindingTransformContext context)
        {
            context.Resources = ResourceDefinitionBindingService.BindResources(
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
            ImmutableArray.Create(
                "AvaloniaXamlIlControlTemplateTargetTypeMetadataTransformer",
                "AvaloniaXamlIlControlTemplatePartsChecker",
                "AvaloniaXamlIlControlTemplatePriorityTransformer",
                "AvaloniaXamlIlDataTemplateWarningsTransformer");

        public void Execute(BindingTransformContext context)
        {
            context.Templates = TemplateDefinitionBindingService.BindTemplates(
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
            ImmutableArray.Create(
                "AvaloniaXamlIlSelectorTransformer",
                "AvaloniaXamlIlSetterTransformer",
                "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
                "AvaloniaXamlIlDuplicateSettersChecker",
                "AvaloniaXamlIlStyleValidatorTransformer");

        public void Execute(BindingTransformContext context)
        {
            context.Styles = BindStyles(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings,
                context.UnsafeAccessors);
        }
    }

    private sealed class BindControlThemesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P050-BindControlThemes";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create(
                "AvaloniaXamlIlControlThemeTransformer",
                "AvaloniaXamlIlSetterTransformer",
                "AvaloniaXamlIlSetterTargetTypeMetadataTransformer",
                "AvaloniaXamlIlDuplicateSettersChecker");

        public void Execute(BindingTransformContext context)
        {
            context.ControlThemes = BindControlThemes(
                context.Document,
                context.Compilation,
                context.Diagnostics,
                context.Options,
                context.CompiledBindings,
                context.UnsafeAccessors);
        }
    }

    private sealed class BindIncludesPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P060-BindIncludes";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create(
                "AvaloniaXamlIncludeTransformer",
                "XamlMergeResourceGroupTransformer");

        public void Execute(BindingTransformContext context)
        {
            context.Includes = BindIncludes(
                context.Document,
                context.Compilation,
                context.BuildUri,
                context.Diagnostics,
                context.Options);
        }
    }

    private sealed class FinalizeViewModelPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P900-Finalize";

        public ImmutableArray<string> UpstreamTransformerIds =>
            ImmutableArray.Create(
                "AddNameScopeRegistration",
                "AvaloniaXamlIlRootObjectScope",
                "AvaloniaXamlIlAddSourceInfoTransformer");

        public void Execute(BindingTransformContext context)
        {
            var root = context.RootObject ?? BindObjectNode(
                node: context.Document.RootObject,
                compilation: context.Compilation,
                diagnostics: context.Diagnostics,
                document: context.Document,
                options: context.Options,
                compiledBindings: context.CompiledBindings,
                unsafeAccessors: context.UnsafeAccessors,
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

            context.HasXBind = context.HasXBind || DocumentContainsXBind(context.Document);
            var typeSymbolCatalog = GetActiveTypeSymbolCatalog(context.Compilation);
            context.EmitNameScopeRegistration = typeSymbolCatalog?.GetOrDefault(TypeContractId.NameScope) is not null &&
                                                typeSymbolCatalog.GetOrDefault(TypeContractId.StyledElement) is not null &&
                                                context.NamedElements.Count > 0;
            context.EmitStaticResourceResolver = RequiresStaticResourceResolver(
                root,
                context.Styles,
                context.ControlThemes);
            var hotDesignClassification = HotDesignArtifactClassificationService.Classify(
                typeSymbolCatalog,
                context.Document,
                context.ClassSymbol,
                context.Styles,
                context.ControlThemes,
                context.Templates);

            context.ViewModel = new ResolvedViewModel(
                Document: context.Document,
                BuildUri: context.BuildUri,
                ClassModifier: context.ClassModifier,
                CreateSourceInfo: context.Options.CreateSourceInfo,
                EnableHotReload: context.Options.HotReloadEnabled,
                EnableHotDesign: context.Options.HotDesignEnabled,
                PassExecutionTrace: context.Options.TracePasses
                    ? context.PassExecutionTrace.ToImmutableArray()
                    : ImmutableArray<string>.Empty,
                EmitNameScopeRegistration: context.EmitNameScopeRegistration,
                EmitStaticResourceResolver: context.EmitStaticResourceResolver,
                HasXBind: context.HasXBind,
                RootObject: root,
                NamedElements: context.NamedElements.ToImmutable(),
                Resources: context.Resources,
                Templates: context.Templates,
                CompiledBindings: context.CompiledBindings.ToImmutable(),
                UnsafeAccessors: context.UnsafeAccessors.ToImmutable(),
                Styles: context.Styles,
                ControlThemes: context.ControlThemes,
                Includes: context.Includes,
                HotDesignArtifactKind: hotDesignClassification.Kind,
                HotDesignScopeHints: hotDesignClassification.ScopeHints);
        }
    }

    private static bool DocumentContainsXBind(XamlDocumentModel document)
    {
        return NodeContainsXBind(document.RootObject);
    }

    private static bool NodeContainsXBind(XamlObjectNode node)
    {
        if (XamlMarkupExtensionNameSemantics.Classify(node.XmlTypeName) == XamlMarkupExtensionKind.XBind)
        {
            return true;
        }

        foreach (var assignment in node.PropertyAssignments)
        {
            if (TryParseXBindMarkup(assignment.Value, out _))
            {
                return true;
            }
        }

        foreach (var constructorArgument in node.ConstructorArguments)
        {
            if (NodeContainsXBind(constructorArgument))
            {
                return true;
            }
        }

        foreach (var child in node.ChildObjects)
        {
            if (NodeContainsXBind(child))
            {
                return true;
            }
        }

        foreach (var propertyElement in node.PropertyElements)
        {
            foreach (var value in propertyElement.ObjectValues)
            {
                if (NodeContainsXBind(value))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
