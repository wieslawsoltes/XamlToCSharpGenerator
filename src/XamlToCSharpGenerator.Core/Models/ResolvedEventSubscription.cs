namespace XamlToCSharpGenerator.Core.Models;

public enum ResolvedEventSubscriptionKind
{
    ClrEvent = 0,
    RoutedEvent = 1
}

public sealed record ResolvedEventSubscription(
    string EventName,
    string HandlerMethodName,
    ResolvedEventSubscriptionKind Kind,
    string? RoutedEventOwnerTypeName,
    string? RoutedEventFieldName,
    string? RoutedEventHandlerTypeName,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null,
    ResolvedEventBindingDefinition? EventBindingDefinition = null);
