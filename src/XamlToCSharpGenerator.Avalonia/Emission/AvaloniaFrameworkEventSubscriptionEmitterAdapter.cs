using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkEventSubscriptionEmitterAdapter : IXamlFrameworkEventSubscriptionEmitterAdapter
{
    public static AvaloniaFrameworkEventSubscriptionEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkEventSubscriptionEmitterAdapter()
    {
    }

    public ImmutableArray<string> BuildSubscriptionStatements(
        string nodeReference,
        string rootReference,
        string emittedMethodName,
        ResolvedEventSubscription eventSubscription)
    {
        if (eventSubscription.Kind == ResolvedEventSubscriptionKind.RoutedEvent &&
            !string.IsNullOrWhiteSpace(eventSubscription.RoutedEventOwnerTypeName) &&
            !string.IsNullOrWhiteSpace(eventSubscription.RoutedEventFieldName) &&
            !string.IsNullOrWhiteSpace(eventSubscription.RoutedEventHandlerTypeName))
        {
            var routedEventExpression = eventSubscription.RoutedEventOwnerTypeName + "." + eventSubscription.RoutedEventFieldName;
            var handlerExpression = "(" + eventSubscription.RoutedEventHandlerTypeName + ")" + rootReference + "." + emittedMethodName;
            return ImmutableArray.Create(
                nodeReference + ".RemoveHandler(" + routedEventExpression + ", " + handlerExpression + ");",
                nodeReference + ".AddHandler(" + routedEventExpression + ", " + handlerExpression + ");");
        }

        return ImmutableArray.Create(
            nodeReference + "." + eventSubscription.EventName + " -= " + rootReference + "." + emittedMethodName + ";",
            nodeReference + "." + eventSubscription.EventName + " += " + rootReference + "." + emittedMethodName + ";");
    }
}
