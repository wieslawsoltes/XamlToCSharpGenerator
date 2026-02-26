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
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
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
        var typeAliases = new Dictionary<TypeAliasKey, INamedTypeSymbol>();
        var propertyAliases = new Dictionary<string, ResolvedPropertyAliasRule>(StringComparer.OrdinalIgnoreCase);
        var inputTypeAliases = EnumerateTypeAliases(configuration, compilation);
        var inputPropertyAliases = EnumeratePropertyAliases(configuration, compilation);

        foreach (var typeAlias in inputTypeAliases)
        {
            if (string.IsNullOrWhiteSpace(typeAlias.XmlNamespace) ||
                string.IsNullOrWhiteSpace(typeAlias.XamlTypeName) ||
                string.IsNullOrWhiteSpace(typeAlias.ClrTypeName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Type alias entries require non-empty XML namespace, XAML type name, and CLR type name.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    options.StrictMode));
                continue;
            }

            var clrTypeName = NormalizeMetadataTypeName(typeAlias.ClrTypeName);
            var clrType = compilation.GetTypeByMetadataName(clrTypeName);
            if (clrType is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0902",
                    $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' targets unknown CLR type '{typeAlias.ClrTypeName}'.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    options.StrictMode));
                continue;
            }

            var key = new TypeAliasKey(typeAlias.XmlNamespace.Trim(), typeAlias.XamlTypeName.Trim());
            if (typeAliases.TryGetValue(key, out var existing))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    $"Type alias '{typeAlias.XmlNamespace}:{typeAlias.XamlTypeName}' overrides previous mapping to '{existing.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    typeAlias.Source,
                    typeAlias.Line,
                    typeAlias.Column,
                    false));
            }

            typeAliases[key] = clrType;
        }

        foreach (var propertyAlias in inputPropertyAliases)
        {
            if (string.IsNullOrWhiteSpace(propertyAlias.XamlPropertyName) ||
                string.IsNullOrWhiteSpace(propertyAlias.TargetTypeName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    "Property alias entries require non-empty target type and XAML property token.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    options.StrictMode));
                continue;
            }

            var targetTypeToken = propertyAlias.TargetTypeName.Trim();
            var targetTypeIsWildcard = targetTypeToken == "*";
            INamedTypeSymbol? targetTypeSymbol = null;
            if (!targetTypeIsWildcard)
            {
                targetTypeSymbol = compilation.GetTypeByMetadataName(NormalizeMetadataTypeName(targetTypeToken));
                if (targetTypeSymbol is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0902",
                        $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' targets unknown CLR type '{propertyAlias.TargetTypeName}'.",
                        propertyAlias.Source,
                        propertyAlias.Line,
                        propertyAlias.Column,
                        options.StrictMode));
                    continue;
                }
            }

            INamedTypeSymbol? avaloniaOwnerTypeSymbol = null;
            string? avaloniaOwnerTypeName = null;
            var avaloniaPropertyOwnerTypeFromPayload =
                propertyAlias.GetFrameworkPropertyOwnerTypeName(FrameworkProfileIds.Avalonia);
            var avaloniaPropertyFieldFromPayload =
                propertyAlias.GetFrameworkPropertyFieldName(FrameworkProfileIds.Avalonia);
            if (!string.IsNullOrWhiteSpace(avaloniaPropertyOwnerTypeFromPayload))
            {
                avaloniaOwnerTypeName = NormalizeMetadataTypeName(avaloniaPropertyOwnerTypeFromPayload!);
                avaloniaOwnerTypeSymbol = compilation.GetTypeByMetadataName(avaloniaOwnerTypeName);
                if (avaloniaOwnerTypeSymbol is null)
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0902",
                        $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' targets unknown Avalonia property owner type '{avaloniaPropertyOwnerTypeFromPayload}'.",
                        propertyAlias.Source,
                        propertyAlias.Line,
                        propertyAlias.Column,
                        options.StrictMode));
                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(propertyAlias.ClrPropertyName) &&
                (avaloniaOwnerTypeSymbol is null ||
                 string.IsNullOrWhiteSpace(avaloniaPropertyFieldFromPayload)))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0901",
                    $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' must declare either CLR property mapping or Avalonia owner+field mapping.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    options.StrictMode));
                continue;
            }

            var key = BuildPropertyAliasLookupKey(targetTypeToken, propertyAlias.XamlPropertyName);
            var resolvedAlias = new ResolvedPropertyAliasRule(
                targetTypeToken,
                targetTypeSymbol,
                propertyAlias.XamlPropertyName.Trim(),
                string.IsNullOrWhiteSpace(propertyAlias.ClrPropertyName)
                    ? null
                    : NormalizePropertyName(propertyAlias.ClrPropertyName),
                avaloniaOwnerTypeName,
                avaloniaOwnerTypeSymbol,
                avaloniaPropertyFieldFromPayload,
                propertyAlias.Source,
                propertyAlias.Line,
                propertyAlias.Column);

            if (propertyAliases.ContainsKey(key))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0903",
                    $"Property alias '{propertyAlias.TargetTypeName}:{propertyAlias.XamlPropertyName}' overrides an earlier mapping.",
                    propertyAlias.Source,
                    propertyAlias.Line,
                    propertyAlias.Column,
                    false));
            }

            propertyAliases[key] = resolvedAlias;
        }

        return new ResolvedTransformExtensions(
            typeAliases.ToImmutableDictionary(),
            propertyAliases.Values.ToImmutableArray());
    }

    private static IEnumerable<XamlTypeAliasRule> EnumerateTypeAliases(
        XamlTransformConfiguration configuration,
        Compilation compilation)
    {
        foreach (var alias in configuration.TypeAliases)
        {
            yield return alias;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (!IsSourceGenTypeAliasAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                if (attribute.ConstructorArguments.Length < 3 ||
                    attribute.ConstructorArguments[0].Value is not string xmlNamespace ||
                    attribute.ConstructorArguments[1].Value is not string xamlTypeName ||
                    attribute.ConstructorArguments[2].Value is not string clrTypeName)
                {
                    continue;
                }

                var (source, line, column) = ResolveAttributeLocation(attribute);
                yield return new XamlTypeAliasRule(
                    xmlNamespace,
                    xamlTypeName,
                    clrTypeName,
                    source,
                    line,
                    column);
            }
        }
    }

    private static IEnumerable<XamlPropertyAliasRule> EnumeratePropertyAliases(
        XamlTransformConfiguration configuration,
        Compilation compilation)
    {
        foreach (var alias in configuration.PropertyAliases)
        {
            yield return alias;
        }

        foreach (var assembly in EnumerateAssemblies(compilation))
        {
            foreach (var attribute in assembly.GetAttributes())
            {
                if (IsSourceGenPropertyAliasAttribute(attribute.AttributeClass))
                {
                    if (attribute.ConstructorArguments.Length < 3 ||
                        attribute.ConstructorArguments[0].Value is not string targetTypeName ||
                        attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
                        attribute.ConstructorArguments[2].Value is not string clrPropertyName)
                    {
                        continue;
                    }

                    var (source, line, column) = ResolveAttributeLocation(attribute);
                    yield return new XamlPropertyAliasRule(
                        targetTypeName,
                        xamlPropertyName,
                        clrPropertyName,
                        null,
                        null,
                        source,
                        line,
                        column);
                }
                else if (IsSourceGenAvaloniaPropertyAliasAttribute(attribute.AttributeClass))
                {
                    if (attribute.ConstructorArguments.Length < 4 ||
                        attribute.ConstructorArguments[0].Value is not string targetTypeName ||
                        attribute.ConstructorArguments[1].Value is not string xamlPropertyName ||
                        attribute.ConstructorArguments[2].Value is not string ownerTypeName ||
                        attribute.ConstructorArguments[3].Value is not string fieldName)
                    {
                        continue;
                    }

                    var (source, line, column) = ResolveAttributeLocation(attribute);
                    yield return new XamlPropertyAliasRule(
                        targetTypeName,
                        xamlPropertyName,
                        null,
                        ownerTypeName,
                        fieldName,
                        source,
                        line,
                        column);
                }
            }
        }
    }

    private static (string Source, int Line, int Column) ResolveAttributeLocation(AttributeData attribute)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation();
        if (location is null)
        {
            return ("<assembly>", 1, 1);
        }

        var lineSpan = location.GetLineSpan();
        return (
            lineSpan.Path ?? "<assembly>",
            lineSpan.StartLinePosition.Line + 1,
            lineSpan.StartLinePosition.Character + 1);
    }

    private static string NormalizeMetadataTypeName(string typeName)
    {
        return XamlTypeTokenSemantics.TrimGlobalQualifier(typeName);
    }

    private static bool IsSourceGenTypeAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlTypeAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static bool IsSourceGenPropertyAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlPropertyAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static bool IsSourceGenAvaloniaPropertyAliasAttribute(INamedTypeSymbol? attributeType)
    {
        return string.Equals(
            attributeType?.ToDisplayString(),
            SourceGenXamlAvaloniaPropertyAliasAttributeMetadataName,
            StringComparison.Ordinal);
    }

    private static string BuildPropertyAliasLookupKey(string targetTypeName, string xamlPropertyName)
    {
        return targetTypeName.Trim() + "|" + xamlPropertyName.Trim();
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
            return new PropertyAliasResolution(normalizedPropertyName, null, null);
        }

        ResolvedPropertyAliasRule? matchedRule = null;
        var matchedScore = int.MinValue;
        foreach (var rule in extensions.PropertyAliases)
        {
            var targetMatchScore = GetPropertyAliasTargetMatchScore(rule, targetType);
            if (targetMatchScore < 0)
            {
                continue;
            }

            var tokenMatchScore = GetPropertyAliasTokenMatchScore(rule.XamlPropertyName, propertyToken, normalizedPropertyName);
            if (tokenMatchScore < 0)
            {
                continue;
            }

            var score = (targetMatchScore * 10) + tokenMatchScore;
            if (score <= matchedScore)
            {
                continue;
            }

            matchedScore = score;
            matchedRule = rule;
        }

        if (matchedRule is null)
        {
            return new PropertyAliasResolution(normalizedPropertyName, null, null);
        }

        var ruleValue = matchedRule.Value;
        if (!string.IsNullOrWhiteSpace(ruleValue.ClrPropertyName))
        {
            return new PropertyAliasResolution(
                NormalizePropertyName(ruleValue.ClrPropertyName!),
                null,
                null);
        }

        var avaloniaPropertyName = !string.IsNullOrWhiteSpace(ruleValue.AvaloniaPropertyFieldName)
            ? PropertyNameFromField(ruleValue.AvaloniaPropertyFieldName!)
            : normalizedPropertyName;
        return new PropertyAliasResolution(
            avaloniaPropertyName,
            ruleValue.AvaloniaPropertyOwnerType,
            ruleValue.AvaloniaPropertyFieldName);
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

        return IsTypeAssignableTo(targetType, rule.TargetTypeSymbol)
            ? 2
            : -1;
    }

    private static int GetPropertyAliasTokenMatchScore(
        string configuredToken,
        string authoredToken,
        string normalizedAuthoredToken)
    {
        if (configuredToken.Equals(authoredToken, StringComparison.Ordinal))
        {
            return 3;
        }

        if (configuredToken.Equals(authoredToken, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        var normalizedConfigured = NormalizePropertyName(configuredToken);
        if (normalizedConfigured.Equals(normalizedAuthoredToken, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return -1;
    }

    private static string PropertyNameFromField(string fieldName)
    {
        return XamlTokenSplitSemantics.TrimTerminalSuffix(fieldName, "Property");
    }

    private sealed class BindCustomTransformsPass : IAvaloniaTransformPass
    {
        public string PassId => "AXSG-P000-BindCustomTransforms";

        public ImmutableArray<string> UpstreamTransformerIds =>
        [
            "TransformerConfiguration",
            "Avalonia.Metadata.XmlnsDefinitionAttribute"
        ];

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

        public ImmutableArray<string> UpstreamTransformerIds => ["AXSG-P010-BindRootObject", "XNameTransformer"];

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
        IReadOnlyDictionary<string, string> fieldModifierLookup,
        HashSet<string> seenNames)
    {
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
            CollectResolvedNamedElements(child, namedElements, fieldModifierLookup, seenNames);
        }

        foreach (var propertyElementAssignment in node.PropertyElementAssignments)
        {
            foreach (var objectValue in propertyElementAssignment.ObjectValues)
            {
                CollectResolvedNamedElements(objectValue, namedElements, fieldModifierLookup, seenNames);
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
            context.EmitStaticResourceResolver = RequiresStaticResourceResolver(
                root,
                context.Styles,
                context.ControlThemes);
            var hotDesignClassification = HotDesignArtifactClassificationService.Classify(
                context.Compilation,
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
                RootObject: root,
                NamedElements: context.NamedElements.ToImmutable(),
                Resources: context.Resources,
                Templates: context.Templates,
                CompiledBindings: context.CompiledBindings.ToImmutable(),
                Styles: context.Styles,
                ControlThemes: context.ControlThemes,
                Includes: context.Includes,
                HotDesignArtifactKind: hotDesignClassification.Kind,
                HotDesignScopeHints: hotDesignClassification.ScopeHints);
        }
    }
}
