using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Parsing;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.SignatureHelp;

public sealed class XamlSignatureHelpService
{
    private static readonly MarkupExpressionParser MarkupParser = new();
    private static readonly ImmutableDictionary<string, SignatureDefinition> SignatureDefinitions =
        CreateSignatureDefinitions();

    public XamlSignatureHelp? GetSignatureHelp(XamlAnalysisResult analysis, SourcePosition position)
    {
        if (!XamlXmlSourceRangeService.TryFindAttributeAtPosition(
                analysis.Document.Text,
                analysis.XmlDocument,
                position,
                out _,
                out var attribute,
                out _,
                out var attributeValueRange))
        {
            return null;
        }

        var absoluteOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, position);
        var valueStartOffset = TextCoordinateHelper.GetOffset(analysis.Document.Text, attributeValueRange.Start);
        if (absoluteOffset < valueStartOffset ||
            !XamlMarkupExtensionSpanParser.TryParse(attribute.Value, valueStartOffset, out var markupSpan) ||
            absoluteOffset < markupSpan.Start ||
            absoluteOffset > markupSpan.Start + markupSpan.Length ||
            !MarkupParser.TryParseMarkupExtension(attribute.Value, out var markup))
        {
            return null;
        }

        if (!SignatureDefinitions.TryGetValue(NormalizeSignatureName(markup.Name), out var definition))
        {
            return null;
        }

        var signature = new XamlSignatureInformation(
            definition.Label,
            definition.Documentation,
            definition.Parameters);
        var activeParameter = ResolveActiveParameter(markupSpan, markup, absoluteOffset, definition);
        return new XamlSignatureHelp(
            [signature],
            ActiveSignature: 0,
            ActiveParameter: activeParameter);
    }

    private static int ResolveActiveParameter(
        MarkupSpanInfo markupSpan,
        MarkupExtensionInfo markup,
        int absoluteOffset,
        SignatureDefinition definition)
    {
        if (definition.Parameters.IsDefaultOrEmpty)
        {
            return 0;
        }

        if (markupSpan.Arguments.IsDefaultOrEmpty)
        {
            return 0;
        }

        var activeArgumentIndex = 0;
        for (var index = 0; index < markupSpan.Arguments.Length; index++)
        {
            var argument = markupSpan.Arguments[index];
            var argumentEnd = argument.Start + Math.Max(argument.Length, 0);
            activeArgumentIndex = index;
            if (absoluteOffset <= argumentEnd)
            {
                break;
            }
        }

        if (activeArgumentIndex >= markup.Arguments.Length)
        {
            activeArgumentIndex = markup.Arguments.Length - 1;
        }

        if (activeArgumentIndex < 0)
        {
            return 0;
        }

        var activeArgument = markup.Arguments[activeArgumentIndex];
        if (activeArgument.IsNamed && !string.IsNullOrWhiteSpace(activeArgument.Name))
        {
            for (var parameterIndex = 0; parameterIndex < definition.ParameterNames.Length; parameterIndex++)
            {
                if (string.Equals(
                        definition.ParameterNames[parameterIndex],
                        activeArgument.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return parameterIndex;
                }
            }
        }

        return Math.Min(activeArgumentIndex, definition.Parameters.Length - 1);
    }

    private static string NormalizeSignatureName(string name)
    {
        return name.Trim();
    }

    private static ImmutableDictionary<string, SignatureDefinition> CreateSignatureDefinitions()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, SignatureDefinition>(StringComparer.OrdinalIgnoreCase);
        Add(builder, "Binding", "Binding(path, Mode, Source, RelativeSource, ElementName, Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue)",
            "Avalonia binding markup extension.",
            ("path", "The source property path."),
            ("Mode", "Binding mode."),
            ("Source", "Explicit binding source."),
            ("RelativeSource", "Relative source binding descriptor."),
            ("ElementName", "Named element source."),
            ("Converter", "Value converter."),
            ("ConverterParameter", "Additional converter parameter."),
            ("StringFormat", "Output string format."),
            ("FallbackValue", "Fallback value when binding fails."),
            ("TargetNullValue", "Value used when the binding result is null."));
        Add(builder, "CompiledBinding", "CompiledBinding(path, Mode, Source, RelativeSource, ElementName, Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue)",
            "Compiled binding markup extension.",
            ("path", "The compiled binding path."),
            ("Mode", "Binding mode."),
            ("Source", "Explicit binding source."),
            ("RelativeSource", "Relative source binding descriptor."),
            ("ElementName", "Named element source."),
            ("Converter", "Value converter."),
            ("ConverterParameter", "Additional converter parameter."),
            ("StringFormat", "Output string format."),
            ("FallbackValue", "Fallback value when binding fails."),
            ("TargetNullValue", "Value used when the binding result is null."));
        Add(builder, "ReflectionBinding", "ReflectionBinding(path, Mode, Source, RelativeSource, ElementName, Converter, ConverterParameter, StringFormat, FallbackValue, TargetNullValue)",
            "Reflection binding markup extension.",
            ("path", "The reflection binding path."),
            ("Mode", "Binding mode."),
            ("Source", "Explicit binding source."),
            ("RelativeSource", "Relative source binding descriptor."),
            ("ElementName", "Named element source."),
            ("Converter", "Value converter."),
            ("ConverterParameter", "Additional converter parameter."),
            ("StringFormat", "Output string format."),
            ("FallbackValue", "Fallback value when binding fails."),
            ("TargetNullValue", "Value used when the binding result is null."));
        Add(builder, "x:Bind", "x:Bind(path, Mode, BindBack, ElementName, RelativeSource, Source, DataType, Converter, ConverterCulture, ConverterLanguage, ConverterParameter, StringFormat, FallbackValue, TargetNullValue, Delay, Priority, UpdateSourceTrigger)",
            "Compiled x:Bind markup extension.",
            ("path", "The x:Bind path or function expression."),
            ("Mode", "Binding mode. Defaults to OneTime unless overridden by x:DefaultBindMode."),
            ("BindBack", "Explicit bind-back method for TwoWay bindings."),
            ("ElementName", "Optional named element source for the default x:Bind receiver."),
            ("RelativeSource", "Optional relative source descriptor for the default x:Bind receiver."),
            ("Source", "Optional explicit source object for the default x:Bind receiver."),
            ("DataType", "Optional explicit source type."),
            ("Converter", "Value converter."),
            ("ConverterCulture", "Converter culture."),
            ("ConverterLanguage", "Alias for ConverterCulture."),
            ("ConverterParameter", "Additional converter parameter."),
            ("StringFormat", "Output string format."),
            ("FallbackValue", "Fallback value when the binding fails."),
            ("TargetNullValue", "Value used when the binding result is null."),
            ("Delay", "Optional source update delay."),
            ("Priority", "Binding priority."),
            ("UpdateSourceTrigger", "Target-to-source update behavior."));
        Add(builder, "StaticResource", "StaticResource(key)", "Static resource lookup markup extension.",
            ("key", "The resource key to resolve."));
        Add(builder, "DynamicResource", "DynamicResource(key)", "Dynamic resource lookup markup extension.",
            ("key", "The resource key to resolve."));
        Add(builder, "TemplateBinding", "TemplateBinding(property, Converter, ConverterParameter)",
            "Template binding markup extension.",
            ("property", "The templated parent property to bind."),
            ("Converter", "Value converter."),
            ("ConverterParameter", "Additional converter parameter."));
        Add(builder, "RelativeSource", "RelativeSource(Mode, AncestorType, AncestorLevel, Tree)",
            "Relative source markup extension.",
            ("Mode", "Relative source mode."),
            ("AncestorType", "Ancestor type filter."),
            ("AncestorLevel", "Ancestor level filter."),
            ("Tree", "Logical or visual tree selection."));
        Add(builder, "x:Type", "x:Type(TypeName)", "Type reference markup extension.",
            ("TypeName", "The referenced type name."));
        Add(builder, "Reference", "Reference(Name)", "Named element reference markup extension.",
            ("Name", "The referenced named element."));
        Add(builder, "ResolveByName", "ResolveByName(Name)", "Resolve-by-name markup extension.",
            ("Name", "The referenced named element."));
        Add(builder, "OnPlatform", "OnPlatform(Default, Windows, macOS, Linux, iOS, Android, Browser)",
            "Platform-specific value markup extension.",
            ("Default", "Default value."),
            ("Windows", "Windows-specific value."),
            ("macOS", "macOS-specific value."),
            ("Linux", "Linux-specific value."),
            ("iOS", "iOS-specific value."),
            ("Android", "Android-specific value."),
            ("Browser", "Browser-specific value."));
        Add(builder, "OnFormFactor", "OnFormFactor(Default, Desktop, Mobile, Tablet)",
            "Form-factor-specific value markup extension.",
            ("Default", "Default value."),
            ("Desktop", "Desktop-specific value."),
            ("Mobile", "Mobile-specific value."),
            ("Tablet", "Tablet-specific value."));
        return builder.ToImmutable();
    }

    private static void Add(
        ImmutableDictionary<string, SignatureDefinition>.Builder builder,
        string name,
        string label,
        string documentation,
        params (string Name, string Documentation)[] parameters)
    {
        var parameterInfos = ImmutableArray.CreateBuilder<XamlParameterInformation>(parameters.Length);
        var parameterNames = ImmutableArray.CreateBuilder<string>(parameters.Length);
        foreach (var parameter in parameters)
        {
            parameterInfos.Add(new XamlParameterInformation(parameter.Name, parameter.Documentation));
            parameterNames.Add(parameter.Name);
        }

        builder[name] = new SignatureDefinition(
            label,
            documentation,
            parameterInfos.ToImmutable(),
            parameterNames.ToImmutable());
    }

    private sealed record SignatureDefinition(
        string Label,
        string Documentation,
        ImmutableArray<XamlParameterInformation> Parameters,
        ImmutableArray<string> ParameterNames);
}
