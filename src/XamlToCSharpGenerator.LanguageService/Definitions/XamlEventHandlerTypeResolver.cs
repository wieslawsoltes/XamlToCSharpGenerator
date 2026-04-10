using System;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.LanguageService.Completion;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Definitions;

internal static class XamlEventHandlerTypeResolver
{
    private const string AvaloniaRoutedEventNamespace = "Avalonia.Interactivity";
    private const string AvaloniaRoutedEventTypeName = "RoutedEvent";

    public static INamedTypeSymbol? ResolveHandlerType(
        XamlAnalysisResult analysis,
        XElement element,
        string eventName)
    {
        if (!XamlSemanticSourceTypeResolver.TryResolveElementTypeSymbol(analysis, element, out var elementType))
        {
            return null;
        }

        return ResolveHandlerType(analysis, elementType, eventName);
    }

    public static INamedTypeSymbol? ResolveHandlerType(
        XamlAnalysisResult analysis,
        INamedTypeSymbol ownerType,
        string eventName)
    {
        if (analysis.Compilation is null || string.IsNullOrWhiteSpace(eventName))
        {
            return null;
        }

        var eventSymbol = FindClrEvent(ownerType, eventName);
        if (eventSymbol?.Type is INamedTypeSymbol clrDelegateType)
        {
            return clrDelegateType;
        }

        if (!string.Equals(analysis.Framework.Id, FrameworkProfileIds.Avalonia, StringComparison.Ordinal))
        {
            return null;
        }

        return ResolveAvaloniaRoutedEventHandlerType(analysis.Compilation, ownerType, eventName);
    }

    private static IEventSymbol? FindClrEvent(INamedTypeSymbol ownerType, string eventName)
    {
        for (var current = ownerType; current is not null; current = current.BaseType)
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static INamedTypeSymbol? ResolveAvaloniaRoutedEventHandlerType(
        Compilation compilation,
        INamedTypeSymbol ownerType,
        string eventName)
    {
        if (!TryFindStaticEventField(ownerType, eventName, out var eventField))
        {
            return null;
        }

        var contracts = CompilationTypeSymbolCatalog.Create(compilation, SemanticContractMaps.AvaloniaDefault);
        return TryResolveRoutedEventHandlerType(contracts, eventField.Type, compilation);
    }

    private static bool TryFindStaticEventField(
        INamedTypeSymbol ownerType,
        string eventName,
        out IFieldSymbol eventField)
    {
        var fieldName = eventName + "Event";
        for (var current = ownerType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName)
                .OfType<IFieldSymbol>()
                .FirstOrDefault(static member => member.IsStatic);
            if (field is not null)
            {
                eventField = field;
                return true;
            }
        }

        eventField = null!;
        return false;
    }

    private static INamedTypeSymbol? TryResolveRoutedEventHandlerType(
        CompilationTypeSymbolCatalog contracts,
        ITypeSymbol routedEventType,
        Compilation compilation)
    {
        if (routedEventType is not INamedTypeSymbol namedRoutedEventType)
        {
            return null;
        }

        var routedEventHandlerType = contracts.GetOrDefault(TypeContractId.AvaloniaRoutedEventHandler);
        var genericEventHandlerType = contracts.GetOrDefault(TypeContractId.SystemEventHandlerOfT);
        var eventArgsBaseType = contracts.GetOrDefault(TypeContractId.SystemEventArgs);
        var genericRoutedEventType = contracts.GetOrDefault(TypeContractId.AvaloniaGenericRoutedEvent);
        var nonGenericRoutedEventType = contracts.GetOrDefault(TypeContractId.AvaloniaRoutedEvent);

        for (var current = namedRoutedEventType; current is not null; current = current.BaseType)
        {
            if (IsGenericAvaloniaRoutedEvent(current, genericRoutedEventType) &&
                current.TypeArguments.Length == 1)
            {
                var routedEventArgsType = current.TypeArguments[0];
                if (genericEventHandlerType is not null &&
                    eventArgsBaseType is not null &&
                    IsTypeAssignableTo(routedEventArgsType, eventArgsBaseType))
                {
                    return genericEventHandlerType.Construct(routedEventArgsType);
                }

                return routedEventHandlerType;
            }

            if (IsNonGenericAvaloniaRoutedEvent(current, nonGenericRoutedEventType))
            {
                return routedEventHandlerType;
            }
        }

        return null;
    }

    private static bool IsGenericAvaloniaRoutedEvent(
        INamedTypeSymbol candidate,
        INamedTypeSymbol? genericRoutedEventType)
    {
        return genericRoutedEventType is not null
            ? SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, genericRoutedEventType)
            : candidate.IsGenericType &&
              candidate.Name == AvaloniaRoutedEventTypeName &&
              candidate.ContainingNamespace.ToDisplayString() == AvaloniaRoutedEventNamespace &&
              candidate.TypeArguments.Length == 1;
    }

    private static bool IsNonGenericAvaloniaRoutedEvent(
        INamedTypeSymbol candidate,
        INamedTypeSymbol? nonGenericRoutedEventType)
    {
        return nonGenericRoutedEventType is not null
            ? SymbolEqualityComparer.Default.Equals(candidate, nonGenericRoutedEventType)
            : !candidate.IsGenericType &&
              candidate.Name == AvaloniaRoutedEventTypeName &&
              candidate.ContainingNamespace.ToDisplayString() == AvaloniaRoutedEventNamespace;
    }

    private static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (AreEquivalentIgnoringNullable(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        for (var current = namedSourceType; current is not null; current = current.BaseType)
        {
            if (AreEquivalentIgnoringNullable(current, targetType))
            {
                return true;
            }
        }

        foreach (var implementedInterface in namedSourceType.AllInterfaces)
        {
            if (AreEquivalentIgnoringNullable(implementedInterface, targetType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreEquivalentIgnoringNullable(ITypeSymbol left, ITypeSymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
               SymbolEqualityComparer.Default.Equals(
                   left.WithNullableAnnotation(NullableAnnotation.None),
                   right.WithNullableAnnotation(NullableAnnotation.None));
    }
}
