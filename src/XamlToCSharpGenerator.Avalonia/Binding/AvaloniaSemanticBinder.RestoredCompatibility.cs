using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static INamedTypeSymbol? ResolveTypeToken(
        Compilation compilation,
        XamlDocumentModel document,
        string token)
    {
        return ResolveTypeToken(compilation, document, token, document.ClassNamespace);
    }

    private static INamedTypeSymbol? ResolveTypeSymbol(
        Compilation compilation,
        XamlDocumentModel document,
        string xmlNamespace,
        string xmlTypeName)
    {
        _ = document;
        return ResolveTypeSymbol(compilation, xmlNamespace, xmlTypeName);
    }

    private static string WrapWithTargetTypeCast(string expression, ITypeSymbol targetType)
    {
        return MarkupTypeConversionSemanticsService.WrapWithTargetTypeCast(targetType, expression);
    }

    private static string TryBuildRelativeSourceExpression(RelativeSourceMarkup relativeSource, ITypeSymbol targetType)
    {
        _ = targetType;

        var modeToken = string.IsNullOrWhiteSpace(relativeSource.Mode) &&
                        !string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken)
            ? "FindAncestor"
            : relativeSource.Mode;
        if (string.IsNullOrWhiteSpace(modeToken) ||
            !AvaloniaBindingEnumSemantics.TryMapRelativeSourceModeToken(modeToken!, out var modeExpression))
        {
            modeExpression = "global::Avalonia.Data.RelativeSourceMode.Self";
        }

        var initializerParts = new List<string>();
        if (relativeSource.AncestorLevel is int ancestorLevel)
        {
            initializerParts.Add("AncestorLevel = " + ancestorLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(relativeSource.Tree) &&
            AvaloniaBindingEnumSemantics.TryMapTreeTypeToken(relativeSource.Tree!, out var treeExpression))
        {
            initializerParts.Add("Tree = " + treeExpression);
        }

        var expression = "new global::Avalonia.Data.RelativeSource(" + modeExpression + ")";
        if (initializerParts.Count > 0)
        {
            expression += " { " + string.Join(", ", initializerParts) + " }";
        }

        return expression;
    }

    private static bool TryBuildRuntimeXamlFragmentExpression(
        string value,
        string baseUri,
        out string expression)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            !RuntimeXamlFragmentDetectionService.IsValidFragment(trimmed))
        {
            expression = string.Empty;
            return false;
        }

        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideRuntimeXamlValue(\"" +
            Escape(trimmed) +
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
            ", \"" +
            Escape(baseUri) +
            "\", " +
            MarkupContextParentStackToken +
            ")";
        return true;
    }

    private static XamlObjectNode? TryGetTemplateContentNode(XamlObjectNode templateNode)
    {
        if (templateNode.ChildObjects.Length > 0)
        {
            return templateNode.ChildObjects[0];
        }

        if (templateNode.PropertyElements.Length > 0 &&
            templateNode.PropertyElements[0].ObjectValues.Length > 0)
        {
            return templateNode.PropertyElements[0].ObjectValues[0];
        }

        return null;
    }

    private static string? NormalizeObjectNodeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? FindContentPropertyName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (HasAvaloniaContentAttribute(property))
                {
                    return property.Name;
                }
            }
        }

        if (TypeSymbolLookupSemanticsService.FindPublicProperty(type, "Content") is not null)
        {
            return "Content";
        }

        if (TypeSymbolLookupSemanticsService.FindPublicProperty(type, "Children") is not null)
        {
            return "Children";
        }

        if (TypeSymbolLookupSemanticsService.FindPublicProperty(type, "Items") is not null)
        {
            return "Items";
        }

        return null;
    }

    private static bool HasAvaloniaContentAttribute(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass?.Name == "ContentAttribute" &&
                attributeClass.ContainingNamespace.ToDisplayString() == "Avalonia.Metadata")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMarkupExtensionObjectNodeType(
        INamedTypeSymbol? symbol,
        string typeName,
        Compilation compilation)
    {
        _ = typeName;
        if (symbol is null)
        {
            return false;
        }

        var markupExtensionBaseType = ResolveContractType(compilation, TypeContractId.AvaloniaMarkupExtensionBase);
        if (markupExtensionBaseType is not null &&
            TypeSymbolLookupSemanticsService.IsTypeAssignableTo(symbol, markupExtensionBaseType))
        {
            return true;
        }

        foreach (var contractId in new[] { TypeContractId.OnPlatformExtension, TypeContractId.OnFormFactorExtension })
        {
            var extensionType = ResolveContractType(compilation, contractId);
            if (extensionType is not null &&
                TypeSymbolLookupSemanticsService.IsTypeAssignableTo(symbol, extensionType))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ResolveObjectNodeNameScopeRegistration(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        Compilation compilation)
    {
        if (!string.IsNullOrWhiteSpace(node.Name))
        {
            if (TryParseMarkupExtension(node.Name!, out _))
            {
                return null;
            }

            var explicitName = NormalizeObjectNodeName(node.Name);
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
        }

        if (!NameScopeRegistrationSemanticsService.SupportsRegistrationFromNameProperty(
                symbol,
                ActiveTypeSymbolCatalog.Value))
        {
            return null;
        }

        if (NameScopeRegistrationParsingService.TryGetNodeNameScopeRegistration(node, out var registeredName))
        {
            return NormalizeObjectNodeName(registeredName);
        }

        _ = compilation;
        return null;
    }

    private static bool TryResolveBindingSourceTypeForScopeInference(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? nodeType,
        out INamedTypeSymbol? sourceType,
        out bool requiresAmbientDataType)
    {
        requiresAmbientDataType = false;
        sourceType = null;

        if (bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        bindingMarkup = NormalizeBindingQuerySyntax(bindingMarkup);

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            sourceType = ResolveNamedElementBindingSourceType(
                compilation,
                document,
                bindingMarkup.ElementName!);
            return sourceType is not null;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            return false;
        }

        return TryResolveCompiledBindingSourceType(
            compilation,
            document,
            bindingMarkup,
            inheritedDataType,
            nodeType,
            out sourceType,
            out requiresAmbientDataType,
            out _);
    }

    private static bool TryResolveBindingMarkupDataType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        out INamedTypeSymbol? sourceType,
        out bool hasExplicitDataType)
    {
        sourceType = null;
        hasExplicitDataType = !string.IsNullOrWhiteSpace(bindingMarkup.DataType);
        if (!hasExplicitDataType)
        {
            return false;
        }

        sourceType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            bindingMarkup.DataType,
            document.ClassNamespace);
        return sourceType is not null;
    }

    private static INamedTypeSymbol? ResolveNamedElementBindingSourceType(
        Compilation compilation,
        XamlDocumentModel document,
        string elementName)
    {
        if (!string.Equals(document.RootObject.Name, elementName, StringComparison.Ordinal))
        {
            return null;
        }

        if (document.IsClassBacked)
        {
            var classSymbol = compilation.GetTypeByMetadataName(document.ClassFullName!);
            if (classSymbol is not null)
            {
                return classSymbol;
            }
        }

        return ResolveTypeSymbol(
            compilation,
            document,
            document.RootObject.XmlNamespace,
            document.RootObject.XmlTypeName);
    }

    private static bool TryResolveSetterValueWithPolicy(
        string rawValue,
        ITypeSymbol conversionTargetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        bool strictMode,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        string propertyName,
        string ownerDisplayName,
        int line,
        int column,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedValueConversionResult resolution,
        INamedTypeSymbol? selectorNestingTypeHint,
        bool setterContext,
        ImmutableArray<AttributeData> converterAttributes)
    {
        _ = propertyName;
        _ = ownerDisplayName;
        _ = selectorNestingTypeHint;
        _ = setterContext;

        if (TryResolveSetterValueWithSharedPolicy(
                rawValue,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                (int)bindingPriorityScope,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallbackDuringConversion,
                allowCompatibilityStringLiteralFallback,
                converterAttributes,
                out resolution))
        {
            return true;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0102",
            $"Could not convert value '{rawValue}'. Strategy={(strictMode ? "StrictError" : "CompatibilityFallback")}.",
            document.FilePath,
            line,
            column,
            strictMode));
        return false;
    }

    private static bool CanAssignBindingValue(ITypeSymbol propertyType, Compilation compilation)
    {
        if (propertyType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        var bindingBase = ResolveContractType(compilation, TypeContractId.AvaloniaBindingBase);
        var bindingInterface = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface);
        return (bindingBase is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(propertyType, bindingBase)) ||
               (bindingInterface is not null && TypeSymbolLookupSemanticsService.IsTypeAssignableTo(propertyType, bindingInterface));
    }

    private static bool TryBuildBindingValueExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol targetType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        _ = targetType;
        return TryBuildRuntimeBindingExpression(
            compilation,
            document,
            bindingMarkup,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }

    private static bool TryResolveAvaloniaPropertyValueTypeFromToken(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out ITypeSymbol? valueType)
    {
        return TryResolveAvaloniaPropertyReferenceExpression(
            rawValue,
            compilation,
            document,
            defaultOwnerType,
            out _,
            out valueType);
    }

    private static bool TryFindAvaloniaPropertyField(
        INamedTypeSymbol ownerType,
        string propertyName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField,
        string? explicitFieldName = null)
    {
        var candidateFieldName = string.IsNullOrWhiteSpace(explicitFieldName)
            ? NormalizePropertyName(propertyName) + "Property"
            : explicitFieldName!;

        for (var current = ownerType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(candidateFieldName).OfType<IFieldSymbol>().FirstOrDefault();
            if (field is null)
            {
                continue;
            }

            resolvedOwnerType = current;
            propertyField = field;
            return true;
        }

        resolvedOwnerType = ownerType;
        propertyField = null!;
        return false;
    }

    private static ITypeSymbol? TryGetAvaloniaPropertyValueType(ITypeSymbol propertyFieldType)
    {
        for (var current = propertyFieldType as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (!current.IsGenericType || current.TypeArguments.Length == 0)
            {
                continue;
            }

            return current.TypeArguments[current.TypeArguments.Length - 1];
        }

        return null;
    }

    private static ResolvedFrameworkPropertyOperation CreateAvaloniaFrameworkPropertyOperation(
        string propertyOwnerTypeName,
        string propertyFieldName,
        string? valuePriorityExpression = null)
    {
        return new ResolvedFrameworkPropertyOperation(
            FrameworkProfileIds.Avalonia,
            propertyOwnerTypeName,
            propertyFieldName,
            valuePriorityExpression);
    }

    private static ResolvedFrameworkPropertyOperation CreateAvaloniaFrameworkPropertyOperation(
        INamedTypeSymbol propertyOwnerType,
        IFieldSymbol propertyField,
        string? valuePriorityExpression = null)
    {
        return CreateAvaloniaFrameworkPropertyOperation(
            propertyOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            propertyField.Name,
            valuePriorityExpression);
    }

    private static string? GetSetValueBindingPriorityExpression(
        INamedTypeSymbol targetType,
        IFieldSymbol propertyField,
        Compilation compilation,
        BindingPriorityScope bindingPriorityScope)
    {
        _ = propertyField;

        var priorityToken = GetDefaultBindingPriorityToken(bindingPriorityScope);
        if (priorityToken is null)
        {
            return null;
        }

        var bindingPriorityType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingPriority);
        if (bindingPriorityType is null ||
            !HasSetValuePriorityOverload(targetType, bindingPriorityType))
        {
            return null;
        }

        return "global::Avalonia.Data.BindingPriority." + priorityToken;
    }

    private static bool HasSetValuePriorityOverload(
        INamedTypeSymbol targetType,
        INamedTypeSymbol bindingPriorityType)
    {
        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(targetType))
        {
            foreach (var member in current.GetMembers("SetValue"))
            {
                if (member is not IMethodSymbol method ||
                    method.IsStatic ||
                    method.DeclaredAccessibility != Accessibility.Public ||
                    method.Parameters.Length != 3)
                {
                    continue;
                }

                if (!TypeNameMatches(
                        method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        "global::Avalonia.AvaloniaProperty"))
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

    private static bool TryConvertAvaloniaSpecificLiteralExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        _ = document;
        _ = setterTargetType;
        _ = bindingPriorityScope;

        if (TryConvertAvaloniaIntrinsicLiteralExpression(type, value, compilation, out expression))
        {
            return true;
        }

        if (TryConvertDeterministicSolidColorBrushExpression(type, value, compilation, out expression))
        {
            return true;
        }

        if (TryConvertDeterministicTransformOperationsExpression(type, value, compilation, out expression))
        {
            return true;
        }

        if (TryConvertAvaloniaCursorExpression(type, value, compilation, out expression))
        {
            return true;
        }

        if (TryConvertAvaloniaKeyGestureExpression(type, value, compilation, out expression))
        {
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool TryConvertAvaloniaIntrinsicLiteralExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var typeName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (NormalizeTypeNameForComparison(typeName))
        {
            case "Avalonia.Thickness":
                return TryConvertThicknessLiteralExpression(namedType, value, out expression);

            case "Avalonia.CornerRadius":
                return TryConvertCornerRadiusLiteralExpression(namedType, value, out expression);

            case "Avalonia.Point":
                return TryConvertPointLikeLiteralExpression(namedType, value, out expression);

            case "Avalonia.Vector":
                return TryConvertPointLikeLiteralExpression(namedType, value, out expression);

            case "Avalonia.Size":
                return TryConvertSizeLiteralExpression(namedType, value, out expression);

            case "Avalonia.Rect":
                return TryConvertRectLiteralExpression(namedType, value, out expression);

            case "Avalonia.Matrix":
                return TryConvertMatrixLiteralExpression(namedType, value, out expression);

            case "Avalonia.Vector3D":
                return TryConvertVector3DLiteralExpression(namedType, value, out expression);

            case "Avalonia.PixelPoint":
                return TryConvertPixelPointLiteralExpression(namedType, value, out expression);

            case "Avalonia.PixelSize":
                return TryConvertPixelSizeLiteralExpression(namedType, value, out expression);

            case "Avalonia.PixelRect":
                return TryConvertPixelRectLiteralExpression(namedType, value, out expression);

            case "Avalonia.Controls.GridLength":
                return TryConvertGridLengthLiteralExpression(namedType, value, out expression);

            case "Avalonia.RelativePoint":
                return TryConvertRelativePointLiteralExpression(namedType, value, out expression);

            case "Avalonia.RelativeScalar":
                return TryConvertRelativeScalarLiteralExpression(namedType, value, out expression);

            case "Avalonia.RelativeRect":
                return TryConvertRelativeRectLiteralExpression(namedType, value, out expression);

            case "Avalonia.Media.Color":
                return TryResolveAvaloniaColorExpression(namedType, value, out expression);

            case "Avalonia.Animation.Cue":
                return TryConvertCueLiteralExpression(namedType, value, out expression);

            case "Avalonia.Animation.KeySpline":
                return TryConvertKeySplineLiteralExpression(namedType, value, out expression);

            default:
                return false;
        }
    }

    private static bool TryConvertThicknessLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseThickness(
                value,
                out var count,
                out var left,
                out var top,
                out var right,
                out var bottom))
        {
            return false;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        expression = count == 1
            ? "new " + typeName + "(" + FormatDoubleLiteral(left) + ")"
            : "new " +
              typeName +
              "(" +
              FormatDoubleLiteral(left) +
              ", " +
              FormatDoubleLiteral(top) +
              ", " +
              FormatDoubleLiteral(right) +
              ", " +
              FormatDoubleLiteral(bottom) +
              ")";
        return true;
    }

    private static bool TryConvertCornerRadiusLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseCornerRadius(
                value,
                out var count,
                out var topLeft,
                out var topRight,
                out var bottomRight,
                out var bottomLeft))
        {
            return false;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        expression = count == 1
            ? "new " + typeName + "(" + FormatDoubleLiteral(topLeft) + ")"
            : "new " +
              typeName +
              "(" +
              FormatDoubleLiteral(topLeft) +
              ", " +
              FormatDoubleLiteral(topRight) +
              ", " +
              FormatDoubleLiteral(bottomRight) +
              ", " +
              FormatDoubleLiteral(bottomLeft) +
              ")";
        return true;
    }

    private static bool TryConvertPointLikeLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParsePoint(value, out var x, out var y))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(x) +
                     ", " +
                     FormatDoubleLiteral(y) +
                     ")";
        return true;
    }

    private static bool TryConvertSizeLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseSize(value, out var width, out var height))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(width) +
                     ", " +
                     FormatDoubleLiteral(height) +
                     ")";
        return true;
    }

    private static bool TryConvertRectLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseRect(value, out var x, out var y, out var width, out var height))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(x) +
                     ", " +
                     FormatDoubleLiteral(y) +
                     ", " +
                     FormatDoubleLiteral(width) +
                     ", " +
                     FormatDoubleLiteral(height) +
                     ")";
        return true;
    }

    private static bool TryConvertMatrixLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseMatrix(
                value,
                out var count,
                out var m11,
                out var m12,
                out var m21,
                out var m22,
                out var m31,
                out var m32,
                out var m13,
                out var m23,
                out var m33))
        {
            return false;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        expression = count == 6
            ? "new " +
              typeName +
              "(" +
              FormatDoubleLiteral(m11) +
              ", " +
              FormatDoubleLiteral(m12) +
              ", " +
              FormatDoubleLiteral(m21) +
              ", " +
              FormatDoubleLiteral(m22) +
              ", " +
              FormatDoubleLiteral(m31) +
              ", " +
              FormatDoubleLiteral(m32) +
              ")"
            : "new " +
              typeName +
              "(" +
              FormatDoubleLiteral(m11) +
              ", " +
              FormatDoubleLiteral(m12) +
              ", " +
              FormatDoubleLiteral(m13) +
              ", " +
              FormatDoubleLiteral(m21) +
              ", " +
              FormatDoubleLiteral(m22) +
              ", " +
              FormatDoubleLiteral(m23) +
              ", " +
              FormatDoubleLiteral(m31) +
              ", " +
              FormatDoubleLiteral(m32) +
              ", " +
              FormatDoubleLiteral(m33) +
              ")";
        return true;
    }

    private static bool TryConvertVector3DLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseVector3D(value, out var x, out var y, out var z))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(x) +
                     ", " +
                     FormatDoubleLiteral(y) +
                     ", " +
                     FormatDoubleLiteral(z) +
                     ")";
        return true;
    }

    private static bool TryConvertPixelPointLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelPoint(value, out var x, out var y))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     x.ToString(CultureInfo.InvariantCulture) +
                     ", " +
                     y.ToString(CultureInfo.InvariantCulture) +
                     ")";
        return true;
    }

    private static bool TryConvertPixelSizeLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelSize(value, out var width, out var height))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     width.ToString(CultureInfo.InvariantCulture) +
                     ", " +
                     height.ToString(CultureInfo.InvariantCulture) +
                     ")";
        return true;
    }

    private static bool TryConvertPixelRectLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelRect(value, out var x, out var y, out var width, out var height))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     x.ToString(CultureInfo.InvariantCulture) +
                     ", " +
                     y.ToString(CultureInfo.InvariantCulture) +
                     ", " +
                     width.ToString(CultureInfo.InvariantCulture) +
                     ", " +
                     height.ToString(CultureInfo.InvariantCulture) +
                     ")";
        return true;
    }

    private static bool TryConvertGridLengthLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseGridLength(value, out var unit, out var numericValue))
        {
            return false;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (unit)
        {
            case AvaloniaGridLengthLiteralUnit.Auto when HasPublicStaticProperty(
                type,
                "Auto",
                typeName):
                expression = typeName + ".Auto";
                return true;

            case AvaloniaGridLengthLiteralUnit.Auto:
                expression = "new " +
                             typeName +
                             "(" +
                             FormatDoubleLiteral(numericValue) +
                             ", global::Avalonia.Controls.GridUnitType.Auto)";
                return true;

            case AvaloniaGridLengthLiteralUnit.Star:
                expression = "new " +
                             typeName +
                             "(" +
                             FormatDoubleLiteral(numericValue) +
                             ", global::Avalonia.Controls.GridUnitType.Star)";
                return true;

            default:
                expression = "new " +
                             typeName +
                             "(" +
                             FormatDoubleLiteral(numericValue) +
                             ", global::Avalonia.Controls.GridUnitType.Pixel)";
                return true;
        }
    }

    private static bool TryConvertRelativePointLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativePoint(value, out var x, out var y, out var unit))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(x) +
                     ", " +
                     FormatDoubleLiteral(y) +
                     ", " +
                     GetRelativeUnitExpression(unit) +
                     ")";
        return true;
    }

    private static bool TryConvertRelativeScalarLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativeScalar(value, out var scalar, out var unit))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(scalar) +
                     ", " +
                     GetRelativeUnitExpression(unit) +
                     ")";
        return true;
    }

    private static bool TryConvertRelativeRectLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativeRect(value, out var x, out var y, out var width, out var height, out var unit))
        {
            return false;
        }

        expression = "new " +
                     type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     FormatDoubleLiteral(x) +
                     ", " +
                     FormatDoubleLiteral(y) +
                     ", " +
                     FormatDoubleLiteral(width) +
                     ", " +
                     FormatDoubleLiteral(height) +
                     ", " +
                     GetRelativeUnitExpression(unit) +
                     ")";
        return true;
    }

    private static string GetRelativeUnitExpression(AvaloniaRelativeUnitLiteral unit)
    {
        return unit == AvaloniaRelativeUnitLiteral.Relative
            ? "global::Avalonia.RelativeUnit.Relative"
            : "global::Avalonia.RelativeUnit.Absolute";
    }

    private static bool TryResolveAvaloniaColorExpression(
        INamedTypeSymbol colorType,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var colorTypeName = colorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (XamlAvaloniaValueLiteralSemantics.TryParseHexColor(value, out var argb) &&
            HasPublicStaticMethod(
                colorType,
                "FromUInt32",
                colorTypeName,
                ["global::System.UInt32"]))
        {
            expression = colorTypeName + ".FromUInt32(" + FormatHexUInt32Literal(argb) + ")";
            return true;
        }

        return TryResolveAvaloniaNamedColorExpression(colorType, value, out expression);
    }

    private static bool TryConvertDeterministicSolidColorBrushExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var solidColorBrushType = ResolveContractType(compilation, TypeContractId.AvaloniaSolidColorBrush);
        var brushType = ResolveContractType(compilation, TypeContractId.AvaloniaBrush);
        var brushInterfaceType = ResolveContractType(compilation, TypeContractId.AvaloniaIBrush);
        var colorType = ResolveContractType(compilation, TypeContractId.AvaloniaColor);
        if (solidColorBrushType is null &&
            brushType is null &&
            brushInterfaceType is null)
        {
            return false;
        }

        var isSolidColorBrushTarget = solidColorBrushType is not null &&
                                      (SymbolEqualityComparer.Default.Equals(type, solidColorBrushType) ||
                                       TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, solidColorBrushType));
        var isBrushTarget = brushType is not null &&
                            (SymbolEqualityComparer.Default.Equals(type, brushType) ||
                             TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, brushType));
        var isBrushInterfaceTarget = brushInterfaceType is not null &&
                                     (SymbolEqualityComparer.Default.Equals(type, brushInterfaceType) ||
                                      TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, brushInterfaceType));
        if (!isSolidColorBrushTarget && !isBrushTarget && !isBrushInterfaceTarget)
        {
            return false;
        }

        if (solidColorBrushType is not null &&
            colorType is not null &&
            TryResolveAvaloniaColorExpression(colorType, value, out var colorExpression) &&
            CanConstructSolidColorBrushFromColor(solidColorBrushType, colorType))
        {
            expression = "new " +
                         solidColorBrushType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "(" +
                         colorExpression +
                         ")";
            return true;
        }

        if (isSolidColorBrushTarget &&
            TryBuildCompatibleParseExpression(
                solidColorBrushType,
                solidColorBrushType,
                value,
                out var solidColorBrushParseExpression))
        {
            expression = solidColorBrushParseExpression;
            return true;
        }

        if (isBrushTarget &&
            brushType is not null &&
            TryBuildCompatibleParseExpression(brushType, brushType, value, out var brushParseExpression))
        {
            expression = brushParseExpression;
            return true;
        }

        if (isBrushInterfaceTarget &&
            brushType is not null &&
            TryBuildCompatibleParseExpression(brushType, type, value, out var parseExpression))
        {
            expression = parseExpression;
            return true;
        }

        return false;
    }

    private static bool CanConstructSolidColorBrushFromColor(INamedTypeSymbol solidColorBrushType, INamedTypeSymbol colorType)
    {
        var colorTypeName = colorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var constructor in solidColorBrushType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.Parameters.Length == 0)
            {
                continue;
            }

            if (!TypeNameMatches(
                    constructor.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    colorTypeName))
            {
                continue;
            }

            var remainingParametersAreOptional = true;
            for (var index = 1; index < constructor.Parameters.Length; index++)
            {
                if (!constructor.Parameters[index].IsOptional)
                {
                    remainingParametersAreOptional = false;
                    break;
                }
            }

            if (remainingParametersAreOptional)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertDeterministicTransformOperationsExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var transformOperationsType = ResolveContractType(compilation, TypeContractId.AvaloniaTransformOperations);
        if (transformOperationsType is null ||
            !SymbolEqualityComparer.Default.Equals(type, transformOperationsType) &&
            !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, transformOperationsType))
        {
            return false;
        }

        if (!XamlAvaloniaTransformLiteralSemantics.TryParse(value, out var isIdentity, out var operations))
        {
            return false;
        }

        var transformOperationsTypeName = transformOperationsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (isIdentity &&
            HasPublicStaticProperty(
                transformOperationsType,
                "Identity",
                transformOperationsTypeName))
        {
            expression = transformOperationsTypeName + ".Identity";
            return true;
        }

        if (!TryFindCompatiblePublicStaticMethod(
                transformOperationsType,
                "CreateBuilder",
                ["global::System.Int32"],
                out var createBuilderMethod))
        {
            return false;
        }

        if (!TryFindCompatiblePublicInstanceMethod(
                createBuilderMethod.ReturnType as INamedTypeSymbol,
                "Build",
                [],
                transformOperationsTypeName,
                out _))
        {
            return false;
        }

        var operationStatements = new List<string>(operations.Length);
        foreach (var operation in operations)
        {
            if (!TryBuildTransformBuilderAppendStatement(
                    createBuilderMethod.ReturnType as INamedTypeSymbol,
                    operation,
                    compilation,
                    out var statement))
            {
                return false;
            }

            operationStatements.Add(statement);
        }

        expression =
            "new global::System.Func<" +
            transformOperationsTypeName +
            ">(() => { var __builder = " +
            transformOperationsTypeName +
            ".CreateBuilder(" +
            operations.Length.ToString(CultureInfo.InvariantCulture) +
            "); " +
            string.Join(" ", operationStatements) +
            " return __builder.Build(); })()";
        return true;
    }

    private static bool TryBuildTransformBuilderAppendStatement(
        INamedTypeSymbol? builderType,
        AvaloniaTransformOperationLiteral operation,
        Compilation compilation,
        out string statement)
    {
        statement = string.Empty;
        if (builderType is null)
        {
            return false;
        }

        switch (operation.Kind)
        {
            case AvaloniaTransformOperationLiteralKind.Translate:
                if (!TryFindCompatiblePublicInstanceMethod(
                        builderType,
                        "AppendTranslate",
                        ["global::System.Double", "global::System.Double"],
                        "global::System.Void",
                        out _))
                {
                    return false;
                }

                statement =
                    "__builder.AppendTranslate(" +
                    FormatDoubleLiteral(operation.Value1) +
                    ", " +
                    FormatDoubleLiteral(operation.Value2) +
                    ");";
                return true;

            case AvaloniaTransformOperationLiteralKind.Scale:
                if (!TryFindCompatiblePublicInstanceMethod(
                        builderType,
                        "AppendScale",
                        ["global::System.Double", "global::System.Double"],
                        "global::System.Void",
                        out _))
                {
                    return false;
                }

                statement =
                    "__builder.AppendScale(" +
                    FormatDoubleLiteral(operation.Value1) +
                    ", " +
                    FormatDoubleLiteral(operation.Value2) +
                    ");";
                return true;

            case AvaloniaTransformOperationLiteralKind.Skew:
                if (!TryFindCompatiblePublicInstanceMethod(
                        builderType,
                        "AppendSkew",
                        ["global::System.Double", "global::System.Double"],
                        "global::System.Void",
                        out _))
                {
                    return false;
                }

                statement =
                    "__builder.AppendSkew(" +
                    FormatDoubleLiteral(operation.Value1) +
                    ", " +
                    FormatDoubleLiteral(operation.Value2) +
                    ");";
                return true;

            case AvaloniaTransformOperationLiteralKind.Rotate:
                if (!TryFindCompatiblePublicInstanceMethod(
                        builderType,
                        "AppendRotate",
                        ["global::System.Double"],
                        "global::System.Void",
                        out _))
                {
                    return false;
                }

                statement =
                    "__builder.AppendRotate(" +
                    FormatDoubleLiteral(operation.Value1) +
                    ");";
                return true;

            case AvaloniaTransformOperationLiteralKind.Matrix:
                var matrixType = ResolveContractType(compilation, TypeContractId.AvaloniaMatrix);
                if (matrixType is null ||
                    !TryFindCompatiblePublicInstanceMethod(
                        builderType,
                        "AppendMatrix",
                        [matrixType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)],
                        "global::System.Void",
                        out _))
                {
                    return false;
                }

                statement =
                    "__builder.AppendMatrix(new " +
                    matrixType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    "(" +
                    FormatDoubleLiteral(operation.Value1) +
                    ", " +
                    FormatDoubleLiteral(operation.Value2) +
                    ", " +
                    FormatDoubleLiteral(operation.Value3) +
                    ", " +
                    FormatDoubleLiteral(operation.Value4) +
                    ", " +
                    FormatDoubleLiteral(operation.Value5) +
                    ", " +
                    FormatDoubleLiteral(operation.Value6) +
                    "));";
                return true;

            default:
                return false;
        }
    }

    private static bool TryConvertAvaloniaCursorExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var cursorType = ResolveContractType(compilation, TypeContractId.AvaloniaCursor);
        var standardCursorType = ResolveContractType(compilation, TypeContractId.AvaloniaStandardCursorType);
        if (cursorType is null ||
            standardCursorType is null ||
            !SymbolEqualityComparer.Default.Equals(type, cursorType) &&
            !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, cursorType))
        {
            return false;
        }

        if (!XamlAvaloniaCursorLiteralSemantics.TryParseStandardCursorTypeMember(value, out var memberName) ||
            standardCursorType.GetMembers(memberName).OfType<IFieldSymbol>().FirstOrDefault(static field => field.HasConstantValue) is null)
        {
            return false;
        }

        if (!TryFindCompatiblePublicConstructor(
                cursorType,
                [standardCursorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]))
        {
            return false;
        }

        expression =
            "new " +
            cursorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" +
            standardCursorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." +
            memberName +
            ")";
        return true;
    }

    private static bool TryConvertAvaloniaKeyGestureExpression(
        ITypeSymbol type,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var keyGestureType = ResolveContractType(compilation, TypeContractId.AvaloniaKeyGesture);
        var keyType = ResolveContractType(compilation, TypeContractId.AvaloniaKey);
        var keyModifiersType = ResolveContractType(compilation, TypeContractId.AvaloniaKeyModifiers);
        if (keyGestureType is null ||
            keyType is null ||
            keyModifiersType is null ||
            !SymbolEqualityComparer.Default.Equals(type, keyGestureType) &&
            !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(type, keyGestureType))
        {
            return false;
        }

        if (!XamlAvaloniaKeyGestureLiteralSemantics.TryParse(value, out var keyToken, out var modifierTokens) ||
            string.IsNullOrWhiteSpace(keyToken) ||
            keyType.GetMembers(keyToken!).OfType<IFieldSymbol>().FirstOrDefault(static field => field.HasConstantValue) is null)
        {
            return false;
        }

        if (!TryFindCompatiblePublicConstructor(
                keyGestureType,
                [
                    keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    keyModifiersType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                ]))
        {
            return false;
        }

        var modifiersExpression = keyModifiersType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".None";
        if (modifierTokens.Length > 0)
        {
            var modifierMembers = new List<string>(modifierTokens.Length);
            foreach (var modifierToken in modifierTokens)
            {
                if (keyModifiersType.GetMembers(modifierToken).OfType<IFieldSymbol>().FirstOrDefault(static field => field.HasConstantValue) is null)
                {
                    return false;
                }

                modifierMembers.Add(keyModifiersType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + modifierToken);
            }

            modifiersExpression = string.Join(" | ", modifierMembers);
        }

        expression =
            "new " +
            keyGestureType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" +
            keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "." +
            keyToken +
            ", " +
            modifiersExpression +
            ")";
        return true;
    }

    private static bool TryConvertCueLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var scale = 1d;
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 1);
            scale = 0.01d;
        }

        if (!double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsedValue) ||
            !TryFindCompatiblePublicConstructor(type, ["global::System.Double"]))
        {
            return false;
        }

        expression =
            "new " +
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" +
            FormatDoubleLiteral(parsedValue * scale) +
            ")";
        return true;
    }

    private static bool TryConvertKeySplineLiteralExpression(INamedTypeSymbol type, string value, out string expression)
    {
        expression = string.Empty;
        if (!TryParseFourDoubles(value, out var values) ||
            !TryFindCompatiblePublicConstructor(
                type,
                [
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double"
                ]))
        {
            return false;
        }

        expression =
            "new " +
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            "(" +
            FormatDoubleLiteral(values[0]) +
            ", " +
            FormatDoubleLiteral(values[1]) +
            ", " +
            FormatDoubleLiteral(values[2]) +
            ", " +
            FormatDoubleLiteral(values[3]) +
            ")";
        return true;
    }

    private static bool TryParseFourDoubles(string value, out double[] values)
    {
        values = Array.Empty<double>();
        var tokens = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 4)
        {
            return false;
        }

        var parsed = new double[4];
        for (var index = 0; index < tokens.Length; index++)
        {
            if (!double.TryParse(tokens[index].Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed[index]))
            {
                return false;
            }
        }

        values = parsed;
        return true;
    }

    private static bool TryBuildCompatibleParseExpression(
        INamedTypeSymbol parseOwnerType,
        ITypeSymbol targetType,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var parseOwnerTypeName = parseOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        foreach (var member in parseOwnerType.GetMembers("Parse"))
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

            if (!SymbolEqualityComparer.Default.Equals(method.ReturnType, targetType) &&
                !TypeSymbolLookupSemanticsService.IsTypeAssignableTo(method.ReturnType, targetType))
            {
                continue;
            }

            expression = method.Parameters.Length == 1
                ? parseOwnerTypeName + ".Parse(\"" + Escape(value) + "\")"
                : parseOwnerTypeName + ".Parse(\"" + Escape(value) + "\", global::System.Globalization.CultureInfo.InvariantCulture)";
            return true;
        }

        return false;
    }

    private static bool TryFindCompatiblePublicStaticMethod(
        INamedTypeSymbol type,
        string methodName,
        IReadOnlyList<string> parameterTypeNames,
        out IMethodSymbol method)
    {
        method = null!;
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol candidate ||
                !candidate.IsStatic ||
                candidate.DeclaredAccessibility != Accessibility.Public ||
                !ParameterTypesMatch(candidate.Parameters, parameterTypeNames))
            {
                continue;
            }

            method = candidate;
            return true;
        }

        return false;
    }

    private static bool TryFindCompatiblePublicInstanceMethod(
        INamedTypeSymbol? type,
        string methodName,
        IReadOnlyList<string> parameterTypeNames,
        string returnTypeName,
        out IMethodSymbol method)
    {
        method = null!;
        if (type is null)
        {
            return false;
        }

        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol candidate ||
                candidate.IsStatic ||
                candidate.DeclaredAccessibility != Accessibility.Public ||
                !TypeNameMatches(candidate.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), returnTypeName) ||
                !ParameterTypesMatch(candidate.Parameters, parameterTypeNames))
            {
                continue;
            }

            method = candidate;
            return true;
        }

        return false;
    }

    private static bool TryFindCompatiblePublicConstructor(
        INamedTypeSymbol type,
        IReadOnlyList<string> parameterTypeNames)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic &&
                ParameterTypesMatch(constructor.Parameters, parameterTypeNames))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ParameterTypesMatch(
        ImmutableArray<IParameterSymbol> parameters,
        IReadOnlyList<string> parameterTypeNames)
    {
        if (parameters.Length != parameterTypeNames.Count)
        {
            return false;
        }

        for (var index = 0; index < parameters.Length; index++)
        {
            if (!TypeNameMatches(
                    parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    parameterTypeNames[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSupportedCultureParameter(ITypeSymbol parameterType)
    {
        var typeName = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return typeName == "global::System.Globalization.CultureInfo" ||
               typeName == "global::System.IFormatProvider";
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
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        ITypeSymbol? fallbackValueType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool allowCompiledBindingRegistration = true,
        string? compiledBindingAccessorPlaceholderToken = null,
        INamedTypeSymbol? explicitOwnerType = null,
        string? explicitFrameworkPropertyFieldName = null,
        bool isInsideDataTemplate = false,
        string xBindDefaultMode = "OneTime",
        XamlObjectNode? currentNode = null)
    {
        _ = targetTypeName;
        _ = compiledBindings;
        _ = unsafeAccessors;
        _ = compileBindingsEnabled;
        _ = allowCompiledBindingRegistration;
        _ = compiledBindingAccessorPlaceholderToken;

        resolvedAssignment = null;
        var ownerType = explicitOwnerType ?? targetType;
        if (!TryFindAvaloniaPropertyField(
                ownerType,
                propertyName,
                out var resolvedOwnerType,
                out var propertyField,
                explicitFrameworkPropertyFieldName))
        {
            return false;
        }

        var propertyValueType = fallbackValueType ?? TryGetAvaloniaPropertyValueType(propertyField.Type) ?? compilation.ObjectType;
        var preserveBindingValue = HasAssignBindingAttribute(
            TypeSymbolLookupSemanticsService.FindProperty(explicitOwnerType ?? resolvedOwnerType ?? targetType, propertyName));

        if (TryParseXBindMarkup(assignment.Value, out var xBindMarkup))
        {
            if (!TryBuildXBindBindingExpression(
                    compilation,
                    document,
                    currentNode ?? document.RootObject,
                    xBindMarkup,
                    nodeDataType,
                    rootTypeSymbol,
                    setterTargetType ?? targetType,
                    propertyValueType,
                    bindingPriorityScope,
                    isInsideDataTemplate,
                    xBindDefaultMode,
                    out var xBindExpression,
                    out _,
                    out var xBindErrorCode,
                    out var xBindErrorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    string.IsNullOrWhiteSpace(xBindErrorCode) ? "AXSG0110" : xBindErrorCode,
                    xBindErrorMessage,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: xBindExpression,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue,
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    resolvedOwnerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
            return true;
        }

        if (TryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var inlineCode))
        {
            if (!TryBuildInlineCodeBindingExpression(
                    compilation,
                    nodeDataType,
                    rootTypeSymbol,
                    setterTargetType ?? targetType,
                    inlineCode,
                    out var inlineBindingExpression,
                    out _,
                    out _,
                    out var inlineErrorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0112",
                    $"Inline C# for '{propertyName}' is invalid: {inlineErrorMessage}",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: inlineBindingExpression,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue,
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    resolvedOwnerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
            return true;
        }

        if (TryResolveImplicitCSharpShorthandExpression(
                assignment.Value,
                compilation,
                document,
                options,
                nodeDataType,
                rootTypeSymbol,
                setterTargetType ?? targetType,
                unsafeAccessors,
                out var isShorthandExpression,
                out var shorthandResolution))
        {
            if (isShorthandExpression &&
                !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticId) &&
                !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    shorthandResolution.DiagnosticId!,
                    shorthandResolution.DiagnosticMessage!,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (shorthandResolution.Kind == CSharpShorthandResolutionKind.BindingPath &&
                shorthandResolution.Path is not null &&
                TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    new BindingMarkup(
                        isCompiledBinding: false,
                        path: shorthandResolution.Path,
                        mode: null,
                        elementName: null,
                        relativeSource: null,
                        source: null,
                        dataType: null,
                        converter: null,
                        converterCulture: null,
                        converterParameter: null,
                        stringFormat: null,
                        fallbackValue: null,
                        targetNullValue: null,
                        delay: null,
                        priority: null,
                        updateSourceTrigger: null,
                        hasSourceConflict: false,
                        sourceConflictMessage: null),
                    setterTargetType ?? targetType,
                    bindingPriorityScope,
                    out var shorthandBindingExpression))
            {
                if (allowCompiledBindingRegistration &&
                    shorthandResolution.SourceTypeName is not null &&
                    shorthandResolution.AccessorExpression is not null)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: shorthandResolution.Path,
                        SourceTypeName: shorthandResolution.SourceTypeName,
                        ResultTypeName: shorthandResolution.ResultTypeName,
                        AccessorExpression: shorthandResolution.AccessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column));
                }

                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: shorthandBindingExpression,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue,
                    FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                        resolvedOwnerType,
                        propertyField,
                        GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
                return true;
            }

            if (shorthandResolution.Kind == CSharpShorthandResolutionKind.RootExpression &&
                shorthandResolution.ValueExpression is not null)
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: shorthandResolution.ValueExpression,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue,
                    FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                        resolvedOwnerType,
                        propertyField,
                        GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
                return true;
            }
        }

        var expressionBindingAccessorPlaceholderToken = compiledBindingAccessorPlaceholderToken;
        if (expressionBindingAccessorPlaceholderToken is null &&
            allowCompiledBindingRegistration)
        {
            expressionBindingAccessorPlaceholderToken = BuildCompiledBindingAccessorPlaceholderToken(
                assignment.Line,
                assignment.Column);
        }

        if (TryConvertCSharpExpressionMarkupToBindingExpression(
                assignment.Value,
                compilation,
                document,
                options,
                nodeDataType,
                expressionBindingAccessorPlaceholderToken,
                out var isExpressionMarkup,
                out var expressionBindingValueExpression,
                out var expressionAccessorExpression,
                out var normalizedExpression,
                out var expressionResultTypeName,
                out var expressionErrorCode,
                out var expressionErrorMessage))
        {
            if (string.IsNullOrWhiteSpace(expressionBindingValueExpression) ||
                string.IsNullOrWhiteSpace(expressionAccessorExpression) ||
                nodeDataType is null)
            {
                isExpressionMarkup = true;
            }
            else
            {
                if (allowCompiledBindingRegistration)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: "{= " + normalizedExpression + " }",
                        SourceTypeName: nodeDataType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResultTypeName: expressionResultTypeName,
                        AccessorExpression: expressionAccessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column,
                        AccessorPlaceholderToken: expressionBindingAccessorPlaceholderToken));
                }

                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: expressionBindingValueExpression,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue,
                    FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                        resolvedOwnerType,
                        propertyField,
                        GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
                return true;
            }
        }

        if (isExpressionMarkup)
        {
            var message = expressionErrorCode == "AXSG0110"
                ? $"Expression binding for '{propertyName}' requires x:DataType in scope."
                : $"Expression binding '{assignment.Value}' is invalid for source type '{nodeDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
            diagnostics.Add(new DiagnosticInfo(
                expressionErrorCode,
                message,
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
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

            var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || compileBindingsEnabled;
            INamedTypeSymbol? compiledBindingSourceType = null;
            var compiledBindingResolution = default(CompiledBindingAccessorResolution);
            var directCompiledBindingPlaceholderToken = compiledBindingAccessorPlaceholderToken;
            var requiresAmbientDataType = false;
            var hasInvalidLocalDataType = false;
            var shouldCompileBinding = wantsCompiledBinding &&
                                       TryResolveCompiledBindingSourceType(
                                           compilation,
                                           document,
                                           bindingMarkup,
                                           nodeDataType,
                                           setterTargetType ?? targetType,
                                           out compiledBindingSourceType,
                                           out requiresAmbientDataType,
                                           out hasInvalidLocalDataType);
            if (shouldCompileBinding)
            {
                if (!TryBuildCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        compiledBindingSourceType!,
                        bindingMarkup.Path,
                        propertyValueType,
                        unsafeAccessors,
                        out compiledBindingResolution,
                        out var compiledBindingErrorMessage))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0111",
                        $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{compiledBindingSourceType!.ToDisplayString()}': {compiledBindingErrorMessage}",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                if (CanUseCompiledBinding(bindingMarkup) &&
                    string.IsNullOrWhiteSpace(directCompiledBindingPlaceholderToken) &&
                    allowCompiledBindingRegistration)
                {
                    directCompiledBindingPlaceholderToken = BuildCompiledBindingAccessorPlaceholderToken(
                        assignment.Line,
                        assignment.Column);
                }

                if (allowCompiledBindingRegistration)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: compiledBindingResolution.NormalizedPath,
                        SourceTypeName: compiledBindingSourceType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResultTypeName: compiledBindingResolution.ResultTypeName,
                        AccessorExpression: compiledBindingResolution.AccessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column,
                        AccessorPlaceholderToken: directCompiledBindingPlaceholderToken));
                }
            }
            else if (wantsCompiledBinding && hasInvalidLocalDataType)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    $"Compiled binding for '{propertyName}' specifies invalid DataType '{bindingMarkup.DataType}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }
            else if (wantsCompiledBinding && requiresAmbientDataType)
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

            if (shouldCompileBinding &&
                CanUseCompiledBinding(bindingMarkup) &&
                TryBuildCompiledBindingRuntimeExpression(
                    compiledBindingSourceType!,
                    compiledBindingResolution,
                    directCompiledBindingPlaceholderToken,
                    out var compiledBindingValueExpression))
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: compiledBindingValueExpression,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue,
                    FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                        resolvedOwnerType,
                        propertyField,
                        GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
                return true;
            }

            if (!TryBuildBindingValueExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    propertyValueType,
                    setterTargetType,
                    bindingPriorityScope,
                    out var bindingExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0102",
                    $"Could not convert binding '{assignment.Value}' for '{propertyName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: bindingExpression,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue,
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    resolvedOwnerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
            return true;
        }

        if (ResolveByNameBindingService.HasSemantics(explicitOwnerType ?? targetType, propertyName) &&
            ResolveByNameBindingService.TryBuildLiteralExpression(
                assignment.Value,
                propertyValueType,
                out var resolveByNameExpression))
        {
            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: resolveByNameExpression,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.MarkupExtension,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue,
                FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                    resolvedOwnerType,
                    propertyField,
                    GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
            return true;
        }

        if (!TryResolveSetterValueWithSharedPolicy(
                assignment.Value,
                propertyValueType,
                compilation,
                document,
                setterTargetType,
                (int)bindingPriorityScope,
                preferTypedStaticResourceCoercion: false,
                allowObjectStringLiteralFallbackDuringConversion: !options.StrictMode && propertyValueType.SpecialType == SpecialType.System_Object,
                allowCompatibilityStringLiteralFallback: !options.StrictMode && propertyValueType.SpecialType == SpecialType.System_Object,
                converterAttributes: default,
                out var conversion))
        {
            return false;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: propertyName,
            ValueExpression: conversion.Expression,
            ClrPropertyOwnerTypeName: null,
            ClrPropertyTypeName: null,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: conversion.ValueKind,
            RequiresStaticResourceResolver: conversion.RequiresStaticResourceResolver,
            ValueRequirements: conversion.EffectiveRequirements,
            PreserveBindingValue: preserveBindingValue,
            FrameworkPropertyOperation: CreateAvaloniaFrameworkPropertyOperation(
                resolvedOwnerType,
                propertyField,
                GetSetValueBindingPriorityExpression(targetType, propertyField, compilation, bindingPriorityScope)));
        return true;
    }
}
