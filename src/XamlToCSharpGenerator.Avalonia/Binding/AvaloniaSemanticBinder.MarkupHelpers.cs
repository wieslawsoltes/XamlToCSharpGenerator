using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryConvertXamlPrimitiveMarkupExtension(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        out string expression)
    {
        return PrimitiveMarkupExtensionConversionService.TryConvert(markup, targetType, out expression);
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
        return MarkupExtensionActivationService.TryConvertGenericExpression(
            markup,
            targetType,
            compilation,
            document,
            setterTargetType,
            (int)bindingPriorityScope,
            out expression);
    }

    private static bool TryResolveMarkupExtensionType(
        Compilation compilation,
        XamlDocumentModel document,
        string markupName,
        out INamedTypeSymbol? extensionType)
    {
        return MarkupExtensionActivationService.TryResolveExtensionType(
            compilation,
            document,
            markupName,
            out extensionType);
    }

    private static bool RequiresObjectInitializer(IPropertySymbol property, ResolvedValueRequirements valueRequirements)
    {
        return SymbolConstructionSemanticsService.RequiresObjectInitializer(property, valueRequirements);
    }
}
