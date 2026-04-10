using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class MarkupTypeConversionService
{
    private readonly Func<string, string> _escape;
    private readonly Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly MarkupContextTokenSet _markupContextTokens;

    public MarkupTypeConversionService(
        Func<string, string> escape,
        Func<Compilation, XamlDocumentModel, string, INamedTypeSymbol?> resolveTypeToken,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        MarkupContextTokenSet markupContextTokens)
    {
        _escape = escape;
        _resolveTypeToken = resolveTypeToken;
        _isTypeAssignableTo = isTypeAssignableTo;
        _markupContextTokens = markupContextTokens;
    }

    public string WrapWithTargetTypeCast(ITypeSymbol targetType, string expression)
    {
        if (targetType is null ||
            targetType.SpecialType == SpecialType.System_Object ||
            string.IsNullOrWhiteSpace(expression))
        {
            return expression;
        }

        return "(" + targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")(" + expression + ")";
    }

    public bool TryConvertByTypeConverter(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression,
        out ResolvedValueRequirements requirements,
        ImmutableArray<AttributeData> converterAttributes)
    {
        expression = string.Empty;
        requirements = ResolvedValueRequirements.None;

        if (!TryResolveTypeConverterType(type, compilation, converterAttributes, out var converterType))
        {
            return false;
        }

        if (!TryBuildConverterInstanceExpression(converterType, type, out var converterInstanceExpression))
        {
            return false;
        }

        if (OverridesContextAwareConvertFrom(converterType))
        {
            expression = WrapWithTargetTypeCast(
                type,
                converterInstanceExpression +
                ".ConvertFrom(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CreateTypeConverterContext(" +
                _markupContextTokens.ServiceProviderToken +
                ", " +
                _markupContextTokens.RootObjectToken +
                ", " +
                _markupContextTokens.IntermediateRootObjectToken +
                ", " +
                _markupContextTokens.TargetObjectToken +
                ", " +
                _markupContextTokens.TargetPropertyToken +
                ", " +
                _markupContextTokens.BaseUriToken +
                ", " +
                _markupContextTokens.ParentStackToken +
                "), global::System.Globalization.CultureInfo.InvariantCulture, \"" +
                _escape(value) +
                "\")");
            requirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
            return true;
        }

        if (HasInvariantStringConverter(converterType))
        {
            expression = WrapWithTargetTypeCast(
                type,
                converterInstanceExpression +
                ".ConvertFromInvariantString(\"" +
                _escape(value) +
                "\")");
            return true;
        }

        return false;
    }

    public bool TryConvertByStaticParseMethod(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        expression = string.Empty;
        if (type.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + _escape(value) + "\"";
            return true;
        }

        if (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Uri")
        {
            expression = "new global::System.Uri(\"" + _escape(value) + "\", global::System.UriKind.RelativeOrAbsolute)";
            return true;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var typeDisplayName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        foreach (var member in namedType.GetMembers("Parse"))
        {
            if (member is not IMethodSymbol method ||
                !method.IsStatic ||
                method.DeclaredAccessibility != Accessibility.Public ||
                method.Parameters.Length < 1 ||
                method.Parameters.Length > 2 ||
                method.Parameters[0].Type.SpecialType != SpecialType.System_String)
            {
                continue;
            }

            if (method.Parameters.Length == 2 &&
                !IsSupportedCultureParameter(method.Parameters[1].Type))
            {
                continue;
            }

            var returnType = method.ReturnType;
            if (!SymbolEqualityComparer.Default.Equals(returnType, namedType) &&
                !_isTypeAssignableTo(returnType, namedType))
            {
                continue;
            }

            expression = method.Parameters.Length == 1
                ? typeDisplayName + ".Parse(\"" + _escape(value) + "\")"
                : typeDisplayName + ".Parse(\"" + _escape(value) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
            return true;
        }

        if (!namedType.IsAbstract)
        {
            foreach (var constructor in namedType.InstanceConstructors)
            {
                if (constructor.DeclaredAccessibility != Accessibility.Public ||
                    constructor.Parameters.Length != 1 ||
                    constructor.Parameters[0].Type.SpecialType != SpecialType.System_String)
                {
                    continue;
                }

                expression = "new " + typeDisplayName + "(\"" + _escape(value) + "\")";
                return true;
            }
        }

        return false;
    }

    private bool TryResolveTypeConverterType(
        ITypeSymbol targetType,
        Compilation compilation,
        ImmutableArray<AttributeData> converterAttributes,
        out INamedTypeSymbol converterType)
    {
        converterType = null!;

        if (!converterAttributes.IsDefaultOrEmpty &&
            TryResolveTypeConverterType(converterAttributes, compilation, out converterType))
        {
            return true;
        }

        return targetType is INamedTypeSymbol namedTargetType &&
               TryResolveTypeConverterType(namedTargetType.GetAttributes(), compilation, out converterType);
    }

    private bool TryResolveTypeConverterType(
        ImmutableArray<AttributeData> attributes,
        Compilation compilation,
        out INamedTypeSymbol converterType)
    {
        if (attributes.IsDefaultOrEmpty)
        {
            converterType = null!;
            return false;
        }

        foreach (var attribute in attributes)
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is null ||
                !IsTypeConverterAttribute(attributeClass))
            {
                continue;
            }

            if (TryResolveTypeConverterType(attribute, compilation, out converterType))
            {
                return true;
            }
        }

        converterType = null!;
        return false;
    }

    private static bool TryResolveTypeConverterType(
        AttributeData attribute,
        Compilation compilation,
        out INamedTypeSymbol converterType)
    {
        if (attribute.ConstructorArguments.Length > 0)
        {
            var argument = attribute.ConstructorArguments[0];
            if (argument.Kind == TypedConstantKind.Type &&
                argument.Value is INamedTypeSymbol typedConverter)
            {
                converterType = typedConverter;
                return true;
            }

            if (argument.Kind == TypedConstantKind.Primitive &&
                argument.Value is string converterTypeName &&
                TryResolveConverterTypeByName(compilation, converterTypeName, out converterType))
            {
                return true;
            }
        }

        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (!namedArgument.Key.Equals("ConverterTypeName", StringComparison.Ordinal) ||
                namedArgument.Value.Value is not string converterTypeName ||
                !TryResolveConverterTypeByName(compilation, converterTypeName, out converterType))
            {
                continue;
            }

            return true;
        }

        converterType = null!;
        return false;
    }

    private static bool TryResolveConverterTypeByName(
        Compilation compilation,
        string converterTypeName,
        out INamedTypeSymbol converterType)
    {
        var trimmed = converterTypeName?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            converterType = null!;
            return false;
        }

        var metadataName = trimmed!;
        var assemblySeparatorIndex = metadataName.IndexOf(',');
        if (assemblySeparatorIndex >= 0)
        {
            metadataName = metadataName.Substring(0, assemblySeparatorIndex).Trim();
        }

        if (metadataName.StartsWith("global::", StringComparison.Ordinal))
        {
            metadataName = metadataName.Substring("global::".Length);
        }

        converterType = compilation.GetTypeByMetadataName(metadataName) ??
                        TryResolveNestedConverterTypeByName(compilation, metadataName);
        return converterType is not null;
    }

    private static INamedTypeSymbol? TryResolveNestedConverterTypeByName(Compilation compilation, string metadataName)
    {
        var currentName = metadataName;
        while (true)
        {
            var lastSeparator = currentName.LastIndexOf('.');
            if (lastSeparator < 0)
            {
                return null;
            }

            currentName = currentName.Substring(0, lastSeparator) + "+" + currentName.Substring(lastSeparator + 1);
            var resolved = compilation.GetTypeByMetadataName(currentName);
            if (resolved is not null)
            {
                return resolved;
            }
        }
    }

    private static bool TryBuildConverterInstanceExpression(
        INamedTypeSymbol converterType,
        ITypeSymbol targetType,
        out string expression)
    {
        var converterTypeName = converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var constructor in converterType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0)
            {
                expression = "new " + converterTypeName + "()";
                return true;
            }

            if (constructor.Parameters.Length == 1 &&
                constructor.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Type")
            {
                expression = "new " +
                             converterTypeName +
                             "(typeof(" +
                             targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                             "))";
                return true;
            }
        }

        expression = string.Empty;
        return false;
    }

    private static bool HasInvariantStringConverter(INamedTypeSymbol converterType)
    {
        foreach (var current in EnumerateTypeHierarchy(converterType))
        {
            foreach (var member in current.GetMembers("ConvertFromInvariantString"))
            {
                if (member is IMethodSymbol method &&
                    !method.IsStatic &&
                    method.DeclaredAccessibility == Accessibility.Public &&
                    method.Parameters.Length == 1 &&
                    method.Parameters[0].Type.SpecialType == SpecialType.System_String)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool OverridesContextAwareConvertFrom(INamedTypeSymbol converterType)
    {
        foreach (var member in converterType.GetMembers("ConvertFrom"))
        {
            if (member is not IMethodSymbol method ||
                method.IsStatic ||
                method.DeclaredAccessibility != Accessibility.Public ||
                method.Parameters.Length != 3)
            {
                continue;
            }

            if (method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
                    "global::System.ComponentModel.ITypeDescriptorContext" ||
                method.Parameters[2].Type.SpecialType != SpecialType.System_Object)
            {
                continue;
            }

            if (IsSupportedCultureParameter(method.Parameters[1].Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedCultureParameter(ITypeSymbol parameterType)
    {
        var typeName = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeName == "global::System.Globalization.CultureInfo" ||
               typeName == "global::System.IFormatProvider";
    }

    private static bool IsTypeConverterAttribute(INamedTypeSymbol attributeType)
    {
        var typeName = attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeName == "global::System.ComponentModel.TypeConverterAttribute" ||
               attributeType.Name == "TypeConverterAttribute";
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchy(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    public bool TryResolveStaticMemberExpression(
        string memberToken,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (!XamlTokenSplitSemantics.TrySplitAtLastSeparator(memberToken, '.', out var typeToken, out var memberName) ||
            string.IsNullOrWhiteSpace(typeToken) ||
            string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        var ownerType = _resolveTypeToken(compilation, document, typeToken);
        if (ownerType is null)
        {
            return false;
        }

        expression = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + memberName;
        return true;
    }

    public bool IsFrameworkPropertyType(ITypeSymbol type, string frameworkPropertyMetadataName)
    {
        for (ITypeSymbol? current = type; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), frameworkPropertyMetadataName, StringComparison.Ordinal) ||
                string.Equals(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::" + frameworkPropertyMetadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
