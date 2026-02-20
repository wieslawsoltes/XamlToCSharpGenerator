namespace XamlToCSharpGenerator.Runtime;

public sealed record SourceGenHotReloadEventDescriptor(
    string EventName,
    string HandlerMethodName,
    bool IsRoutedEvent,
    string? RoutedEventOwnerTypeName,
    string? RoutedEventFieldName,
    string? RoutedEventHandlerTypeName);
