using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct RoutedEventTargetResolution(
    bool FoundStaticEventField,
    INamedTypeSymbol? OwnerType,
    IFieldSymbol? EventField,
    ITypeSymbol? HandlerType);

public sealed class FrameworkRoutedEventResolutionService
{
    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly string _routedEventNamespace;
    private readonly string _routedEventTypeName;

    public FrameworkRoutedEventResolutionService(
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        string routedEventNamespace,
        string routedEventTypeName)
    {
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _routedEventNamespace = routedEventNamespace ?? throw new ArgumentNullException(nameof(routedEventNamespace));
        _routedEventTypeName = routedEventTypeName ?? throw new ArgumentNullException(nameof(routedEventTypeName));
    }

    public RoutedEventTargetResolution ResolveTarget(
        INamedTypeSymbol targetType,
        string eventName,
        Compilation compilation)
    {
        if (!TryFindStaticEventField(targetType, eventName, out var ownerType, out var eventField))
        {
            return default;
        }

        return new RoutedEventTargetResolution(
            FoundStaticEventField: true,
            OwnerType: ownerType,
            EventField: eventField,
            HandlerType: TryResolveRoutedEventHandlerType(eventField.Type, compilation, out var handlerType)
                ? handlerType
                : null);
    }

    private bool TryFindStaticEventField(
        INamedTypeSymbol targetType,
        string eventName,
        out INamedTypeSymbol ownerType,
        out IFieldSymbol eventField)
    {
        var fieldName = eventName + "Event";
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is null)
            {
                continue;
            }

            ownerType = current;
            eventField = field;
            return true;
        }

        ownerType = targetType;
        eventField = null!;
        return false;
    }

    private bool TryResolveRoutedEventHandlerType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol handlerType)
    {
        handlerType = _resolveContractType(compilation, TypeContractId.SystemDelegate) ?? compilation.ObjectType;
        if (!TryGetRoutedEventArgsType(routedEventType, compilation, out var routedEventArgsType, out var isGenericRoutedEvent))
        {
            return false;
        }

        var routedEventHandlerType = _resolveContractType(compilation, TypeContractId.AvaloniaRoutedEventHandler);
        if (!isGenericRoutedEvent && routedEventHandlerType is not null)
        {
            handlerType = routedEventHandlerType;
            return true;
        }

        var eventHandlerType = _resolveContractType(compilation, TypeContractId.SystemEventHandlerOfT);
        var eventArgsBaseType = _resolveContractType(compilation, TypeContractId.SystemEventArgs);
        if (eventHandlerType is INamedTypeSymbol eventHandlerNamed &&
            eventArgsBaseType is not null &&
            _isTypeAssignableTo(routedEventArgsType, eventArgsBaseType))
        {
            handlerType = eventHandlerNamed.Construct(routedEventArgsType);
            return true;
        }

        if (routedEventHandlerType is not null)
        {
            handlerType = routedEventHandlerType;
            return true;
        }

        return true;
    }

    private bool TryGetRoutedEventArgsType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol routedEventArgsType,
        out bool isGenericRoutedEventType)
    {
        isGenericRoutedEventType = false;
        routedEventArgsType = _resolveContractType(compilation, TypeContractId.AvaloniaRoutedEventArgs)
                              ?? _resolveContractType(compilation, TypeContractId.SystemEventArgs)
                              ?? compilation.ObjectType;

        if (routedEventType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var routedEventTypeSymbol = _resolveContractType(compilation, TypeContractId.AvaloniaRoutedEvent);
        var genericRoutedEventTypeSymbol = _resolveContractType(compilation, TypeContractId.AvaloniaGenericRoutedEvent);
        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            var isGenericRoutedEvent = genericRoutedEventTypeSymbol is not null &&
                                       SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, genericRoutedEventTypeSymbol);
            if (!isGenericRoutedEvent &&
                current.Name == _routedEventTypeName &&
                current.ContainingNamespace.ToDisplayString() == _routedEventNamespace &&
                current.IsGenericType &&
                current.TypeArguments.Length == 1)
            {
                isGenericRoutedEvent = true;
            }

            if (isGenericRoutedEvent && current.TypeArguments.Length == 1)
            {
                isGenericRoutedEventType = true;
                routedEventArgsType = current.TypeArguments[0];
                return true;
            }

            var isNonGenericRoutedEvent = routedEventTypeSymbol is not null &&
                                          SymbolEqualityComparer.Default.Equals(current, routedEventTypeSymbol);
            if (!isNonGenericRoutedEvent &&
                current.Name == _routedEventTypeName &&
                current.ContainingNamespace.ToDisplayString() == _routedEventNamespace &&
                !current.IsGenericType)
            {
                isNonGenericRoutedEvent = true;
            }

            if (isNonGenericRoutedEvent)
            {
                return true;
            }
        }

        return false;
    }
}
