using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryConvertXamlPrimitiveMarkupExtension(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        expression = string.Empty;
        var kind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
        var rawValue = TryGetNamedMarkupArgument(markup, "Value") ??
                       (markup.PositionalArguments.Length > 0 ? XamlQuotedValueSemantics.TrimAndUnquote(markup.PositionalArguments[0]) : null);
        switch (kind)
        {
            case XamlMarkupExtensionKind.True:
                expression = "true";
                return true;
            case XamlMarkupExtensionKind.False:
                expression = "false";
                return true;
            case XamlMarkupExtensionKind.String:
            {
                var value = rawValue ?? string.Empty;
                expression = "\"" + Escape(value ?? string.Empty) + "\"";
                return true;
            }
            case XamlMarkupExtensionKind.Char:
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
            case XamlMarkupExtensionKind.Byte:
            {
                if (!byte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((byte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.SByte:
            {
                if (!sbyte.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((sbyte)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.Int16:
            {
                if (!short.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((short)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.UInt16:
            {
                if (!ushort.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = "((ushort)" + parsed.ToString(CultureInfo.InvariantCulture) + ")";
                return true;
            }
            case XamlMarkupExtensionKind.Int32:
            {
                if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            case XamlMarkupExtensionKind.UInt32:
            {
                if (!uint.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            }
            case XamlMarkupExtensionKind.Int64:
            {
                if (!long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            }
            case XamlMarkupExtensionKind.UInt64:
            {
                if (!ulong.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            }
            case XamlMarkupExtensionKind.Single:
            case XamlMarkupExtensionKind.Double:
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
                    ? FormatSingleLiteral((float)parsed)
                    : FormatDoubleLiteral(parsed);
                return true;
            }
            case XamlMarkupExtensionKind.Decimal:
            {
                if (!decimal.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                expression = parsed.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
            }
            case XamlMarkupExtensionKind.DateTime:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                if (!XamlDateTimeLiteralSemantics.TryParseRoundtrip(normalizedRawValue, out var parsedDateTime))
                {
                    return false;
                }

                expression = "global::System.DateTime.FromBinary(" +
                             parsedDateTime.ToBinary().ToString(CultureInfo.InvariantCulture) +
                             "L)";
                return true;
            }
            case XamlMarkupExtensionKind.TimeSpan:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                if (!XamlTimeSpanLiteralSemantics.TryParse(normalizedRawValue, out var parsedTimeSpan))
                {
                    return false;
                }

                expression = "global::System.TimeSpan.FromTicks(" +
                             parsedTimeSpan.Ticks.ToString(CultureInfo.InvariantCulture) +
                             "L)";
                return true;
            }
            case XamlMarkupExtensionKind.Uri:
            {
                if (rawValue is null)
                {
                    return false;
                }

                var normalizedRawValue = rawValue.Trim();
                if (normalizedRawValue.Length == 0)
                {
                    return false;
                }

                expression = "new global::System.Uri(\"" + Escape(normalizedRawValue) + "\", global::System.UriKind.RelativeOrAbsolute)";
                return true;
            }
            case XamlMarkupExtensionKind.Null:
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

        var conversionTargetType = targetType ??
                                   ResolveContractType(compilation, TypeContractId.SystemObject) ??
                                   compilation.ObjectType;
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
        if (XamlMarkupExtensionNameSemantics.Classify(markupName) == XamlMarkupExtensionKind.Null)
        {
            return false;
        }

        var markupExtensionBase = ResolveContractType(compilation, TypeContractId.AvaloniaMarkupExtensionBase);
        if (markupExtensionBase is null)
        {
            return false;
        }

        foreach (var candidateToken in XamlMarkupExtensionNameSemantics.EnumerateClrExtensionTypeTokens(markupName))
        {
            var candidateType = ResolveTypeToken(compilation, document, candidateToken, document.ClassNamespace);
            if (candidateType is null)
            {
                continue;
            }

            if (IsTypeAssignableTo(candidateType, markupExtensionBase))
            {
                extensionType = candidateType;
                return true;
            }
        }

        return false;
    }

    private static string WrapWithTargetTypeCast(ITypeSymbol targetType, string expression)
    {
        if (targetType.SpecialType == SpecialType.System_Object)
        {
            return expression;
        }

        var typedTargetExpression = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceMarkupExtensionValue<" +
               typedTargetExpression +
               ">(" +
               expression +
               ")";
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
        if (parseMethod is not null)
        {
            expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         ".Parse(\"" + Escape(value) + "\")";
            return true;
        }

        var parseWithCultureMethod = namedType.GetMembers("Parse")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method =>
                method.IsStatic &&
                method.Parameters.Length == 2 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsCultureAwareParseParameter(method.Parameters[1].Type) &&
                SymbolEqualityComparer.Default.Equals(method.ReturnType, namedType));
        if (parseWithCultureMethod is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ".Parse(\"" + Escape(value) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
        return true;
    }

    private static bool TryConvertByTypeConverter(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression,
        out ResolvedValueRequirements valueRequirements,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        expression = string.Empty;
        valueRequirements = ResolvedValueRequirements.None;
        var converterType = ResolveTypeConverterType(type, compilation, converterAttributes);
        if (converterType is null ||
            converterType.DeclaredAccessibility != Accessibility.Public ||
            converterType.IsAbstract ||
            !IsTypeConverterType(converterType, compilation))
        {
            return false;
        }

        if (!TryBuildTypeConverterConstructionExpression(type, converterType, out var converterConstructionExpression))
        {
            return false;
        }

        if (TryBuildContextAwareTypeConverterExpression(
                type,
                value,
                compilation,
                converterType,
                converterConstructionExpression,
                out expression))
        {
            valueRequirements = ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true);
            return true;
        }

        return TryBuildInvariantStringTypeConverterExpression(
            type,
            value,
            converterConstructionExpression,
            out expression);
    }

    private static bool TryBuildContextAwareTypeConverterExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        INamedTypeSymbol converterType,
        string converterConstructionExpression,
        out string expression)
    {
        expression = string.Empty;
        if (!HasContextAwareTypeConverterContract(converterType, compilation))
        {
            return false;
        }

        expression = "(" +
                     targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ")" +
                     converterConstructionExpression +
                     ".ConvertFrom(" +
                     BuildTypeConverterContextExpression() +
                     ", global::System.Globalization.CultureInfo.InvariantCulture, \"" +
                     Escape(value) +
                     "\")";
        return true;
    }

    private static bool TryBuildInvariantStringTypeConverterExpression(
        ITypeSymbol targetType,
        string value,
        string converterConstructionExpression,
        out string expression)
    {
        expression = "(" +
                     targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ")" +
                     converterConstructionExpression +
                     ".ConvertFromInvariantString(\"" +
                     Escape(value) +
                     "\")";
        return true;
    }

    private static bool TryBuildTypeConverterConstructionExpression(
        ITypeSymbol targetType,
        INamedTypeSymbol converterType,
        out string expression)
    {
        var converterTypeExpression = converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (HasPublicParameterlessConstructor(converterType))
        {
            expression = "new " + converterTypeExpression + "()";
            return true;
        }

        if (HasPublicConstructorWithParameterTypes(converterType, "global::System.Type"))
        {
            expression = "new " +
                         converterTypeExpression +
                         "(typeof(" +
                         targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "))";
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool HasContextAwareTypeConverterContract(INamedTypeSymbol converterType, Compilation compilation)
    {
        var typeDescriptorContextType = compilation.GetTypeByMetadataName("System.ComponentModel.ITypeDescriptorContext");
        var frameworkTypeConverterType = compilation.GetTypeByMetadataName(TypeConverterMetadataName);
        if (typeDescriptorContextType is null || frameworkTypeConverterType is null)
        {
            return false;
        }

        for (var current = converterType;
             current is not null && !SymbolEqualityComparer.Default.Equals(current, frameworkTypeConverterType);
             current = current.BaseType)
        {
            foreach (var method in current.GetMembers("ConvertFrom").OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.Parameters.Length != 3)
                {
                    continue;
                }

                if (!IsTypeAssignableTo(method.Parameters[0].Type, typeDescriptorContextType) ||
                    !IsCultureAwareParseParameter(method.Parameters[1].Type) ||
                    method.Parameters[2].Type.SpecialType != SpecialType.System_Object)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static string BuildTypeConverterContextExpression()
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CreateTypeConverterContext(" +
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
    }

    private static INamedTypeSymbol? ResolveTypeConverterType(
        ITypeSymbol targetType,
        Compilation compilation,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        var propertyLevelConverterType = ResolveTypeConverterTypeFromAttributes(converterAttributes, compilation);
        if (propertyLevelConverterType is not null)
        {
            return propertyLevelConverterType;
        }

        var typeSymbol = UnwrapNullableNamedType(targetType);
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            var converterType = ResolveTypeConverterTypeFromAttributes(current.GetAttributes(), compilation);
            if (converterType is not null)
            {
                return converterType;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveTypeConverterTypeFromAttributes(
        ImmutableArray<AttributeData> attributes,
        Compilation compilation)
    {
        if (attributes.IsDefaultOrEmpty)
        {
            return null;
        }

        for (var index = 0; index < attributes.Length; index++)
        {
            var attribute = attributes[index];
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "global::" + TypeConverterAttributeMetadataName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            var resolvedType = ResolveTypeConverterTypeFromArgument(attribute.ConstructorArguments[0], compilation);
            if (resolvedType is not null)
            {
                return resolvedType;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveTypeConverterTypeFromArgument(
        TypedConstant argument,
        Compilation compilation)
    {
        if (argument.Kind == TypedConstantKind.Type &&
            argument.Value is INamedTypeSymbol converterType)
        {
            return converterType;
        }

        if (argument.Value is string typeName &&
            !string.IsNullOrWhiteSpace(typeName))
        {
            return ResolveTypeConverterTypeByName(compilation, typeName);
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveTypeConverterTypeByName(Compilation compilation, string typeName)
    {
        if (!TryParseTypeConverterTypeName(typeName, out var metadataTypeName, out var assemblySimpleName))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(assemblySimpleName))
        {
            if (string.Equals(compilation.AssemblyName, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
            {
                return compilation.Assembly.GetTypeByMetadataName(metadataTypeName);
            }

            foreach (var reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol ||
                    !string.Equals(assemblySymbol.Name, assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return assemblySymbol.GetTypeByMetadataName(metadataTypeName);
            }

            return null;
        }

        return compilation.GetTypeByMetadataName(metadataTypeName);
    }

    private static bool TryParseTypeConverterTypeName(
        string typeName,
        out string metadataTypeName,
        out string? assemblySimpleName)
    {
        metadataTypeName = string.Empty;
        assemblySimpleName = null;
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return false;
        }

        var trimmedTypeName = typeName.Trim();
        var splitIndex = FindAssemblyQualifiedTypeSeparator(trimmedTypeName);
        if (splitIndex < 0)
        {
            metadataTypeName = NormalizeTypeConverterMetadataName(trimmedTypeName);
            return metadataTypeName.Length > 0;
        }

        metadataTypeName = NormalizeTypeConverterMetadataName(trimmedTypeName.Substring(0, splitIndex));
        if (metadataTypeName.Length == 0)
        {
            return false;
        }

        var assemblyQualifier = trimmedTypeName.Substring(splitIndex + 1).Trim();
        if (assemblyQualifier.Length == 0)
        {
            return true;
        }

        var assemblyDelimiterIndex = assemblyQualifier.IndexOf(',');
        assemblySimpleName = (assemblyDelimiterIndex >= 0
                ? assemblyQualifier.Substring(0, assemblyDelimiterIndex)
                : assemblyQualifier)
            .Trim();
        return assemblySimpleName.Length > 0;
    }

    private static int FindAssemblyQualifiedTypeSeparator(string typeName)
    {
        var bracketDepth = 0;
        for (var index = 0; index < typeName.Length; index++)
        {
            switch (typeName[index])
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (bracketDepth > 0)
                    {
                        bracketDepth--;
                    }

                    break;
                case ',' when bracketDepth == 0:
                    return index;
            }
        }

        return -1;
    }

    private static string NormalizeTypeConverterMetadataName(string typeName)
    {
        var trimmedTypeName = typeName.Trim();
        return trimmedTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? trimmedTypeName.Substring("global::".Length)
            : trimmedTypeName;
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
    {
        return type.InstanceConstructors.Any(static constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public &&
            constructor.Parameters.Length == 0);
    }

    private static bool IsTypeConverterType(INamedTypeSymbol candidateType, Compilation compilation)
    {
        var typeConverterType = compilation.GetTypeByMetadataName(TypeConverterMetadataName);
        return typeConverterType is not null && IsTypeAssignableTo(candidateType, typeConverterType);
    }

    private static INamedTypeSymbol? UnwrapNullableNamedType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0] as INamedTypeSymbol;
        }

        return type as INamedTypeSymbol;
    }

    private static bool RequiresObjectInitializer(IPropertySymbol property, ResolvedValueRequirements valueRequirements)
    {
        return property.SetMethod?.IsInitOnly == true &&
               !valueRequirements.RequiresMarkupContext;
    }

    private static bool IsCultureAwareParseParameter(ITypeSymbol type)
    {
        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return fullyQualifiedTypeName is
            "global::System.IFormatProvider" or
            "global::System.IFormatProvider?" or
            "global::System.Globalization.CultureInfo" or
            "global::System.Globalization.CultureInfo?";
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

    private static bool TryResolveStaticMemberExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string memberToken,
        out string expression)
    {
        expression = string.Empty;
        if (!XamlTokenSplitSemantics.TrySplitAtLastSeparator(
                memberToken,
                '.',
                out var ownerToken,
                out var memberName))
        {
            return false;
        }

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
}
