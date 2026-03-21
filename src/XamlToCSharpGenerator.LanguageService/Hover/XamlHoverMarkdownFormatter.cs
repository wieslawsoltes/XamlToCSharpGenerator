using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Symbols;

namespace XamlToCSharpGenerator.LanguageService.Hover;

internal static class XamlHoverMarkdownFormatter
{
    public static string FormatElement(AvaloniaTypeInfo typeInfo)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Element**\n\n`{typeInfo.FullTypeName}`\n\nXML namespace: `{typeInfo.XmlNamespace}`\n\nAssembly: `{typeInfo.AssemblyName}`");
    }

    public static string FormatType(string heading, AvaloniaTypeInfo typeInfo)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**{heading}**\n\n`{typeInfo.FullTypeName}`\n\nXML namespace: `{typeInfo.XmlNamespace}`\n\nAssembly: `{typeInfo.AssemblyName}`");
    }

    public static string FormatResolvedType(string heading, XamlResolvedTypeReference typeReference)
    {
        var assemblyName = string.IsNullOrWhiteSpace(typeReference.AssemblyName)
            ? "Unknown"
            : typeReference.AssemblyName;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**{heading}**\n\n`{typeReference.FullTypeName}`\n\nAssembly: `{assemblyName}`");
    }

    public static string FormatProperty(AvaloniaTypeInfo ownerType, AvaloniaPropertyInfo propertyInfo)
    {
        var propertyKind = propertyInfo.IsAttached ? "Attached Property" : "Property";
        var settableState = propertyInfo.IsSettable ? "settable" : "read-only";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**{propertyKind}**\n\n`{ownerType.FullTypeName}.{propertyInfo.Name} : {propertyInfo.TypeName}`\n\nOwner: `{ownerType.FullTypeName}`\n\nState: `{settableState}`");
    }

    public static string FormatSymbol(string heading, ISymbol symbol)
    {
        var display = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        if (symbol.ContainingType is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"**{heading}**\n\n`{display}`");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**{heading}**\n\n`{display}`\n\nDeclaring type: `{symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}`");
    }

    public static string FormatMarkupExtension(
        string extensionToken,
        XamlMarkupExtensionKind kind,
        XamlResolvedTypeReference? resolvedTypeReference)
    {
        var description = kind switch
        {
            XamlMarkupExtensionKind.Binding => "Reflection binding markup extension.",
            XamlMarkupExtensionKind.CompiledBinding => "Compiled binding markup extension.",
            XamlMarkupExtensionKind.XBind => "x:Bind compiled binding markup extension.",
            XamlMarkupExtensionKind.ReflectionBinding => "Reflection binding markup extension.",
            XamlMarkupExtensionKind.StaticResource => "Static resource lookup markup extension.",
            XamlMarkupExtensionKind.DynamicResource => "Dynamic resource lookup markup extension.",
            XamlMarkupExtensionKind.TemplateBinding => "Template binding markup extension.",
            XamlMarkupExtensionKind.RelativeSource => "Relative source markup extension.",
            XamlMarkupExtensionKind.Type => "Type reference markup extension.",
            XamlMarkupExtensionKind.Null => "Null literal markup extension.",
            XamlMarkupExtensionKind.Reference => "Named element reference markup extension.",
            XamlMarkupExtensionKind.ResolveByName => "Resolve-by-name markup extension.",
            _ => "Markup extension."
        };

        if (resolvedTypeReference is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"**Markup Extension**\n\n`{extensionToken}`\n\n{description}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Markup Extension**\n\n`{extensionToken}`\n\nCLR type: `{resolvedTypeReference.Value.FullTypeName}`\n\n{description}");
    }

    public static string FormatBindingArgument(string extensionToken, string argumentName, bool isCompiledBinding)
    {
        var bindingKind = XamlMarkupExtensionNameSemantics.Classify(extensionToken) switch
        {
            XamlMarkupExtensionKind.XBind => "x:Bind",
            XamlMarkupExtensionKind.CompiledBinding => "CompiledBinding",
            _ => isCompiledBinding ? "CompiledBinding" : "Binding"
        };
        var description = argumentName switch
        {
            "Path" => "Binding path resolved against the current binding source.",
            "ElementName" => "Binds against a named element in the current namescope.",
            "RelativeSource" => "Uses a relative binding source such as Self or AncestorType.",
            "Source" => "Sets an explicit binding source object.",
            "Mode" => "Controls the binding update direction.",
            "BindBack" => "Specifies the update callback used for TwoWay x:Bind.",
            "DataType" => "Overrides the x:Bind source type for the current scope.",
            "Converter" => "Applies a value converter to the resolved source value.",
            "ConverterCulture" => "Culture passed to the converter.",
            "ConverterLanguage" => "Alias for ConverterCulture in x:Bind.",
            "ConverterParameter" => "Additional converter parameter value.",
            "StringFormat" or "Format" => "Formats the bound value before assigning it to the target.",
            "FallbackValue" or "Fallback" => "Value used when binding resolution fails.",
            "TargetNullValue" or "NullValue" => "Value used when the resolved binding value is null.",
            "Delay" => "Delays source updates for editable targets.",
            "Priority" or "BindingPriority" => "Sets the Avalonia binding priority.",
            "UpdateSourceTrigger" or "Trigger" => "Controls when source updates are pushed back.",
            _ => "Binding argument."
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**{bindingKind} Argument**\n\n`{argumentName}`\n\n{description}");
    }

    public static string FormatResourceKey(string key, string kind, string xmlTypeName)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Resource Key**\n\n`{key}`\n\nKind: `{kind}`\n\nValue type: `{xmlTypeName}`");
    }

    public static string FormatNamedElement(XamlNamedElement namedElement)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Named Element**\n\n`{namedElement.Name}`\n\nElement type: `{namedElement.XmlTypeName}`");
    }

    public static string FormatStyleClass(string className, string? typeContextToken)
    {
        if (string.IsNullOrWhiteSpace(typeContextToken))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"**Style Class**\n\n`.{className}`");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Style Class**\n\n`.{className}`\n\nSelector type context: `{typeContextToken}`");
    }

    public static string FormatPseudoClass(string pseudoClassName, string? declaringTypeFullName)
    {
        var displayName = pseudoClassName.StartsWith(":", StringComparison.Ordinal)
            ? pseudoClassName
            : ":" + pseudoClassName;
        if (string.IsNullOrWhiteSpace(declaringTypeFullName))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"**Pseudoclass**\n\n`{displayName}`");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**Pseudoclass**\n\n`{displayName}`\n\nDeclaring type: `{declaringTypeFullName}`");
    }

    public static string FormatDirective(string directiveName)
    {
        var description = directiveName switch
        {
            "x:Class" => "Associates the root object with a generated CLR partial class.",
            "x:DataType" => "Declares the compiled-binding source type for the current XAML scope.",
            "x:Name" => "Creates a named element in the current XAML namescope.",
            "x:Key" => "Declares a resource key.",
            "x:CompileBindings" => "Enables or disables compiled bindings for the current scope.",
            _ => "XAML directive."
        };

        return string.Create(
            CultureInfo.InvariantCulture,
            $"**XAML Directive**\n\n`{directiveName}`\n\n{description}");
    }
}
