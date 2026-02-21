using Microsoft.CodeAnalysis;

namespace XamlToCSharpGenerator.Core.Diagnostics;

public static class DiagnosticCatalog
{
    public static readonly DiagnosticDescriptor ParseFailed = new(
        id: "AXSG0001",
        title: "XAML parse failed",
        messageFormat: "{0}",
        category: "AXSG.Parse",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingClassDirective = new(
        id: "AXSG0002",
        title: "x:Class is missing",
        messageFormat: "{0}",
        category: "AXSG.Parse",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PrecompileDirectiveInvalid = new(
        id: "AXSG0003",
        title: "x:Precompile value is invalid",
        messageFormat: "{0}",
        category: "AXSG.Parse",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeResolutionFailed = new(
        id: "AXSG0100",
        title: "Named element type resolution failed",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedProperty = new(
        id: "AXSG0101",
        title: "Property is not supported by source-generated binding",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedLiteralConversion = new(
        id: "AXSG0102",
        title: "Literal conversion is not supported",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChildAttachmentConflict = new(
        id: "AXSG0103",
        title: "Child attachment conflict",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ClassModifierInvalid = new(
        id: "AXSG0104",
        title: "x:ClassModifier is invalid",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ClassModifierMismatch = new(
        id: "AXSG0105",
        title: "x:ClassModifier does not match class declaration",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructionDirectiveInvalid = new(
        id: "AXSG0106",
        title: "Construction directive is invalid",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructionFactoryNotFound = new(
        id: "AXSG0107",
        title: "Construction factory/constructor was not found",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ArrayConstructionInvalid = new(
        id: "AXSG0108",
        title: "x:Array construction is invalid",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompiledBindingRequiresDataType = new(
        id: "AXSG0110",
        title: "Compiled binding requires x:DataType",
        messageFormat: "{0}",
        category: "AXSG.Binding",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompiledBindingPathInvalid = new(
        id: "AXSG0111",
        title: "Compiled binding path is invalid",
        messageFormat: "{0}",
        category: "AXSG.Binding",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeResolutionAmbiguous = new(
        id: "AXSG0112",
        title: "Type resolution is ambiguous",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeResolutionFallbackUsed = new(
        id: "AXSG0113",
        title: "Type resolution compatibility fallback used",
        messageFormat: "{0}",
        category: "AXSG.Semantic",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionalXamlExpressionInvalid = new(
        id: "AXSG0120",
        title: "Conditional XAML expression is invalid",
        messageFormat: "{0}",
        category: "AXSG.Conditional",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StyleSelectorInvalid = new(
        id: "AXSG0300",
        title: "Style selector is invalid",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StyleSetterPropertyInvalid = new(
        id: "AXSG0301",
        title: "Style setter target property is invalid",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ControlThemeTargetTypeInvalid = new(
        id: "AXSG0302",
        title: "Control theme target type is invalid",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ControlThemeSetterPropertyInvalid = new(
        id: "AXSG0303",
        title: "Control theme setter target property is invalid",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateSetterDetected = new(
        id: "AXSG0304",
        title: "Duplicate setter detected",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ControlThemeBasedOnNotFound = new(
        id: "AXSG0305",
        title: "Control theme BasedOn target was not found",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ControlThemeBasedOnCycleDetected = new(
        id: "AXSG0306",
        title: "Control theme BasedOn chain contains a cycle",
        messageFormat: "{0}",
        category: "AXSG.Style",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DataTemplateDataTypeRecommended = new(
        id: "AXSG0500",
        title: "Template should declare x:DataType",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ControlTemplateTargetTypeInvalid = new(
        id: "AXSG0501",
        title: "ControlTemplate target type is invalid",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RequiredTemplatePartMissing = new(
        id: "AXSG0502",
        title: "Required control template part is missing",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TemplatePartWrongType = new(
        id: "AXSG0503",
        title: "Control template part has wrong type",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OptionalTemplatePartMissing = new(
        id: "AXSG0504",
        title: "Optional control template part is not defined",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ItemContainerInsideTemplate = new(
        id: "AXSG0505",
        title: "Item container used inside data template",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TemplateContentTypeInvalid = new(
        id: "AXSG0506",
        title: "Template content root type is invalid",
        messageFormat: "{0}",
        category: "AXSG.Template",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeSourceMissing = new(
        id: "AXSG0400",
        title: "Include source is missing",
        messageFormat: "{0}",
        category: "AXSG.Include",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeSourceInvalid = new(
        id: "AXSG0401",
        title: "Include source is invalid",
        messageFormat: "{0}",
        category: "AXSG.Include",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeMergeTargetUnknown = new(
        id: "AXSG0402",
        title: "Include merge target is unknown",
        messageFormat: "{0}",
        category: "AXSG.Include",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeTargetNotFound = new(
        id: "AXSG0403",
        title: "Include target is not available in source-generated compile set",
        messageFormat: "{0}",
        category: "AXSG.Include",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeCycleDetected = new(
        id: "AXSG0404",
        title: "Include graph contains a cycle",
        messageFormat: "{0}",
        category: "AXSG.Include",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RoutedEventHandlerInvalid = new(
        id: "AXSG0600",
        title: "Routed event handler is invalid",
        messageFormat: "{0}",
        category: "AXSG.Event",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateBuildUriRegistration = new(
        id: "AXSG0601",
        title: "Generated URI registration is duplicated",
        messageFormat: "{0}",
        category: "AXSG.Event",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HotReloadFallbackUsed = new(
        id: "AXSG0700",
        title: "Hot reload fallback source was reused",
        messageFormat: "{0}",
        category: "AXSG.HotReload",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateGeneratedHintName = new(
        id: "AXSG0701",
        title: "Generated source hint name is duplicated",
        messageFormat: "Generator hint '{0}' was emitted more than once. Check duplicate AXAML inputs for '{1}'.",
        category: "AXSG.HotReload",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TransformRuleParseFailed = new(
        id: "AXSG0900",
        title: "Transform rule file is invalid",
        messageFormat: "{0}",
        category: "AXSG.Transform",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TransformRuleEntryInvalid = new(
        id: "AXSG0901",
        title: "Transform rule entry is invalid",
        messageFormat: "{0}",
        category: "AXSG.Transform",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TransformRuleTypeResolutionFailed = new(
        id: "AXSG0902",
        title: "Transform rule type could not be resolved",
        messageFormat: "{0}",
        category: "AXSG.Transform",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TransformRuleDuplicateAlias = new(
        id: "AXSG0903",
        title: "Transform rule alias is duplicated",
        messageFormat: "{0}",
        category: "AXSG.Transform",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompileMetricsSummary = new(
        id: "AXSG0800",
        title: "XAML compile metrics summary",
        messageFormat: "{0}",
        category: "AXSG.Metrics",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CompileMetricsFile = new(
        id: "AXSG0801",
        title: "XAML compile metrics per file",
        messageFormat: "{0}",
        category: "AXSG.Metrics",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EmissionFailed = new(
        id: "AXSG0200",
        title: "Source emission failed",
        messageFormat: "Could not emit generated source for '{0}': {1}",
        category: "AXSG.Emit",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InternalError = new(
        id: "AXSG9000",
        title: "Internal generator error",
        messageFormat: "{0}",
        category: "AXSG.Internal",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
