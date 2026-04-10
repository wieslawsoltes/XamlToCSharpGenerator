using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryBindInlineObjectNodePropertyElementCodeSubscription(
        INamedTypeSymbol? objectType,
        XamlPropertyElement propertyElement,
        string rawCode,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        if (objectType is null)
        {
            return false;
        }

        return TryBindInlineEventCodeSubscription(
            objectType,
            propertyElement.PropertyName,
            rawCode,
            propertyElement.Line,
            propertyElement.Column,
            propertyElement.Condition,
            compilation,
            nodeDataType,
            rootTypeSymbol,
            diagnostics,
            document,
            options,
            out subscription);
    }
}
