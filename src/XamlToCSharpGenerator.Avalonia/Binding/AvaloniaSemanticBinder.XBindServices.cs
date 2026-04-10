using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryBuildRelativeSourceExpression(
        Compilation compilation,
        XamlDocumentModel document,
        RelativeSourceMarkup relativeSource,
        out string expression,
        out string errorMessage)
    {
        expression = string.Empty;
        errorMessage = string.Empty;

        var modeToken = string.IsNullOrWhiteSpace(relativeSource.Mode) &&
                        !string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken)
            ? "FindAncestor"
            : relativeSource.Mode;
        if (string.IsNullOrWhiteSpace(modeToken) ||
            !AvaloniaBindingEnumSemantics.TryMapRelativeSourceModeToken(modeToken!, out var modeExpression))
        {
            errorMessage = "Unsupported RelativeSource mode '" + (relativeSource.Mode ?? string.Empty) + "'.";
            return false;
        }

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
        {
            var ancestorType = ResolveTypeToken(
                compilation,
                document,
                relativeSource.AncestorTypeToken!,
                document.ClassNamespace);
            if (ancestorType is null)
            {
                errorMessage = "Could not resolve RelativeSource AncestorType '" + relativeSource.AncestorTypeToken + "'.";
                return false;
            }

            initializerParts.Add("AncestorType = typeof(" + ancestorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")");
        }

        if (relativeSource.AncestorLevel is int ancestorLevel)
        {
            initializerParts.Add("AncestorLevel = " + ancestorLevel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(relativeSource.Tree))
        {
            if (!AvaloniaBindingEnumSemantics.TryMapTreeTypeToken(relativeSource.Tree!, out var treeExpression))
            {
                errorMessage = "Unsupported RelativeSource Tree '" + relativeSource.Tree + "'.";
                return false;
            }

            initializerParts.Add("Tree = " + treeExpression);
        }

        expression = "new global::Avalonia.Data.RelativeSource(" + modeExpression + ")";
        if (initializerParts.Count > 0)
        {
            expression += " { " + string.Join(", ", initializerParts) + " }";
        }

        return true;
    }

    private static bool TryBuildXBindExplicitSourceExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string rawSource,
        out string expression,
        out string errorMessage)
    {
        if (TryConvertValueExpression(
                rawSource,
                compilation.ObjectType,
                compilation,
                document,
                setterTargetType: null,
                BindingPriorityScope.None,
                out expression))
        {
            errorMessage = string.Empty;
            return true;
        }

        expression = rawSource.Trim();
        errorMessage = string.Empty;
        return expression.Length > 0;
    }

    private static bool TryResolveExplicitXBindSourceType(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawDataType,
        out INamedTypeSymbol? explicitSourceType,
        out string errorMessage)
    {
        explicitSourceType = null;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(rawDataType))
        {
            return true;
        }

        explicitSourceType = ResolveTypeToken(compilation, document, Unquote(rawDataType!), document.ClassNamespace);
        if (explicitSourceType is not null)
        {
            return true;
        }

        errorMessage = $"x:Bind specifies invalid DataType '{rawDataType}'.";
        return false;
    }

    private static bool TryResolveXBindSourceConfiguration(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup xBindMarkup,
        INamedTypeSymbol baseSourceType,
        INamedTypeSymbol? ambientDataContextType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        XBindPathReference baseSourceReference,
        out ResolvedXBindSourceConfiguration sourceConfiguration,
        out string errorMessage)
    {
        return XBindSourceConfigurationService.TryResolveSourceConfiguration(
            compilation,
            document,
            currentNode,
            xBindMarkup,
            baseSourceType,
            ambientDataContextType,
            rootType,
            targetType,
            baseSourceReference,
            out sourceConfiguration,
            out errorMessage);
    }

    private static bool TryBuildXBindBindBackExpression(
        Compilation compilation,
        XamlDocumentModel document,
        XBindExpressionNode? xBindExpression,
        string? rawBindBack,
        XBindLoweringContext loweringContext,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        ITypeSymbol? bindingValueType,
        ITypeSymbol? resultTypeSymbol,
        out string bindBackExpression,
        out string bindBackValueTypeExpression,
        out string errorMessage)
    {
        return XBindBindBackExpressionService.TryBuildBindBackExpression(
            compilation,
            document,
            xBindExpression,
            rawBindBack,
            loweringContext,
            sourceType,
            rootType,
            targetType,
            bindingValueType,
            resultTypeSymbol,
            out bindBackExpression,
            out bindBackValueTypeExpression,
            out errorMessage);
    }

    private static bool TryBuildXBindOptionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string propertyName,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        return XBindOptionExpressionService.TryBuildOptionExpression(
            compilation,
            document,
            propertyName,
            rawValue,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindDelayExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        return XBindOptionExpressionService.TryBuildDelayExpression(
            compilation,
            document,
            rawValue,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindUpdateSourceTriggerExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        return XBindOptionExpressionService.TryBuildUpdateSourceTriggerExpression(
            compilation,
            document,
            rawValue,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindPriorityExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string? rawValue,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        out string expression,
        out string errorMessage)
    {
        return XBindOptionExpressionService.TryBuildPriorityExpression(
            compilation,
            document,
            rawValue,
            (int)bindingPriorityScope,
            setterTargetType,
            out expression,
            out errorMessage);
    }

    private static bool TryBuildXBindEventBindingDefinition(
        Compilation compilation,
        XamlDocumentModel document,
        XamlObjectNode currentNode,
        XBindMarkup xBindMarkup,
        string eventName,
        INamedTypeSymbol? ambientDataContextType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol targetType,
        ITypeSymbol eventHandlerType,
        bool isInsideDataTemplate,
        int line,
        int column,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out string errorMessage)
    {
        return XBindEventBindingDefinitionService.TryBuildDefinition(
            compilation,
            document,
            currentNode,
            xBindMarkup,
            eventName,
            ambientDataContextType,
            rootType,
            targetType,
            eventHandlerType,
            isInsideDataTemplate,
            line,
            column,
            out eventBindingDefinition,
            out errorMessage);
    }
}
