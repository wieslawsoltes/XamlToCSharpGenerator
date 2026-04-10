using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct EventSubscriptionBindingTarget(
    ResolvedEventSubscriptionKind Kind,
    ITypeSymbol HandlerType,
    string? RoutedEventOwnerTypeName,
    string? RoutedEventFieldName,
    string? RoutedEventHandlerTypeName);

public sealed class EventSubscriptionBindingService
{
    public delegate bool TryParseInlineCSharpMarkupExtensionCodeDelegate(string value, out string rawCode);
    public delegate bool TryParseXBindMarkupDelegate(string value, out XBindMarkup xBindMarkup);

    public delegate bool TryBuildXBindEventBindingDefinitionDelegate(
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
        out string errorMessage);

    public delegate bool TryBindInlineEventLambdaDelegate(
        XamlPropertyAssignment assignment,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out bool handled);

    private readonly Func<INamedTypeSymbol, string, IEventSymbol?> _findEvent;
    private readonly Func<string, string> _normalizePropertyName;
    private readonly FrameworkRoutedEventResolutionService _routedEventResolutionService;
    private readonly EventBindingDefinitionService _eventBindingDefinitionService;
    private readonly EventHandlerBindingService _eventHandlerBindingService;
    private readonly TryParseInlineCSharpMarkupExtensionCodeDelegate _tryParseInlineCSharpMarkupExtensionCode;
    private readonly TryParseXBindMarkupDelegate _tryParseXBindMarkup;
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly TryBuildXBindEventBindingDefinitionDelegate _tryBuildXBindEventBindingDefinition;
    private readonly TryBindInlineEventLambdaDelegate _tryBindInlineEventLambda;

    public EventSubscriptionBindingService(
        Func<INamedTypeSymbol, string, IEventSymbol?> findEvent,
        Func<string, string> normalizePropertyName,
        FrameworkRoutedEventResolutionService routedEventResolutionService,
        EventBindingDefinitionService eventBindingDefinitionService,
        EventHandlerBindingService eventHandlerBindingService,
        TryParseInlineCSharpMarkupExtensionCodeDelegate tryParseInlineCSharpMarkupExtensionCode,
        TryParseXBindMarkupDelegate tryParseXBindMarkup,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        TryBuildXBindEventBindingDefinitionDelegate tryBuildXBindEventBindingDefinition,
        TryBindInlineEventLambdaDelegate tryBindInlineEventLambda)
    {
        _findEvent = findEvent ?? throw new ArgumentNullException(nameof(findEvent));
        _normalizePropertyName = normalizePropertyName ?? throw new ArgumentNullException(nameof(normalizePropertyName));
        _routedEventResolutionService = routedEventResolutionService ?? throw new ArgumentNullException(nameof(routedEventResolutionService));
        _eventBindingDefinitionService = eventBindingDefinitionService ?? throw new ArgumentNullException(nameof(eventBindingDefinitionService));
        _eventHandlerBindingService = eventHandlerBindingService ?? throw new ArgumentNullException(nameof(eventHandlerBindingService));
        _tryParseInlineCSharpMarkupExtensionCode = tryParseInlineCSharpMarkupExtensionCode ?? throw new ArgumentNullException(nameof(tryParseInlineCSharpMarkupExtensionCode));
        _tryParseXBindMarkup = tryParseXBindMarkup ?? throw new ArgumentNullException(nameof(tryParseXBindMarkup));
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
        _tryBuildXBindEventBindingDefinition = tryBuildXBindEventBindingDefinition ?? throw new ArgumentNullException(nameof(tryBuildXBindEventBindingDefinition));
        _tryBindInlineEventLambda = tryBindInlineEventLambda ?? throw new ArgumentNullException(nameof(tryBindInlineEventLambda));
    }

    public bool TryBindAssignment(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        XamlObjectNode? currentNode,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = _normalizePropertyName(assignment.PropertyName);
        if (!TryResolveTarget(targetType, eventName, compilation, out var bindingTarget, out var errorMessage))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                errorMessage,
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        if (_tryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var inlineEventCode))
        {
            if (!TryBuildInlineCodeSubscription(
                    inlineEventCode,
                    CSharpMarkupExpressionSemantics.IsLambdaExpression(inlineEventCode),
                    bindingTarget,
                    eventName,
                    targetType,
                    assignment.Line,
                    assignment.Column,
                    assignment.Condition,
                    compilation,
                    nodeDataType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out subscription))
            {
                return true;
            }

            return true;
        }

        if (_tryParseXBindMarkup(assignment.Value, out var xBindEventMarkup))
        {
            if (!_tryBuildXBindEventBindingDefinition(
                    compilation,
                    document,
                    currentNode ?? document.RootObject,
                    xBindEventMarkup,
                    eventName,
                    nodeDataType,
                    rootTypeSymbol,
                    targetType,
                    bindingTarget.HandlerType,
                    isInsideDataTemplate,
                    assignment.Line,
                    assignment.Column,
                    out var xBindEventBindingDefinition,
                    out var xBindEventError))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    xBindEventError,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = CreateSubscription(
                bindingTarget,
                eventName,
                xBindEventBindingDefinition!.GeneratedMethodName,
                assignment.Line,
                assignment.Column,
                assignment.Condition,
                xBindEventBindingDefinition);
            return true;
        }

        if (_tryParseMarkupExtension(assignment.Value, out var eventMarkupExtension) &&
            EventBindingDefinitionService.IsEventBindingMarkupExtension(eventMarkupExtension))
        {
            if (!_eventBindingDefinitionService.TryBuildParsedDefinition(
                    assignment.Value,
                    eventName,
                    compilation,
                    bindingTarget.HandlerType,
                    nodeDataType,
                    rootTypeSymbol,
                    assignment.Line,
                    assignment.Column,
                    out var eventBindingResult,
                    out var eventBindingError))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    eventBindingError,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            foreach (var warningMessage in eventBindingResult.WarningMessages)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    warningMessage,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
            }

            var eventBindingDefinition = eventBindingResult.Definition;
            subscription = CreateSubscription(
                bindingTarget,
                eventName,
                eventBindingDefinition.GeneratedMethodName,
                assignment.Line,
                assignment.Column,
                assignment.Condition,
                eventBindingDefinition);
            return true;
        }

        if (_tryBindInlineEventLambda(
                assignment,
                eventName,
                compilation,
                bindingTarget.HandlerType,
                nodeDataType,
                targetType,
                rootTypeSymbol,
                diagnostics,
                document,
                options,
                out var inlineLambdaDefinition,
                out var inlineLambdaHandled))
        {
            subscription = CreateSubscription(
                bindingTarget,
                eventName,
                inlineLambdaDefinition!.GeneratedMethodName,
                assignment.Line,
                assignment.Column,
                assignment.Condition,
                inlineLambdaDefinition);
            return true;
        }

        if (inlineLambdaHandled)
        {
            return true;
        }

        if (!_eventHandlerBindingService.TryParseHandlerName(assignment.Value, out var handlerMethodName))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Event '{eventName}' expects a CLR handler method name.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        if (rootTypeSymbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        if (!_eventHandlerBindingService.HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, bindingTarget.HandlerType))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{bindingTarget.HandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        subscription = CreateSubscription(
            bindingTarget,
            eventName,
            handlerMethodName!,
            assignment.Line,
            assignment.Column,
            assignment.Condition);
        return true;
    }

    public bool TryBindInlineCode(
        INamedTypeSymbol targetType,
        string propertyName,
        string rawCode,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = _normalizePropertyName(propertyName);
        if (!TryResolveTarget(targetType, eventName, compilation, out var bindingTarget, out var errorMessage))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                errorMessage,
                document.FilePath,
                line,
                column,
                options.StrictMode));
            return true;
        }

        if (!TryBuildInlineCodeSubscription(
                rawCode,
                CSharpMarkupExpressionSemantics.IsLambdaExpression(rawCode),
                bindingTarget,
                eventName,
                targetType,
                line,
                column,
                condition,
                compilation,
                nodeDataType,
                rootTypeSymbol,
                diagnostics,
                document,
                options,
                out subscription))
        {
            return true;
        }

        return true;
    }

    private bool TryResolveTarget(
        INamedTypeSymbol targetType,
        string eventName,
        Compilation compilation,
        out EventSubscriptionBindingTarget target,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (_findEvent(targetType, eventName) is { } eventSymbol)
        {
            target = new EventSubscriptionBindingTarget(
                ResolvedEventSubscriptionKind.ClrEvent,
                eventSymbol.Type,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null);
            return true;
        }

        var routedEventResolution = _routedEventResolutionService.ResolveTarget(targetType, eventName, compilation);
        if (!routedEventResolution.FoundStaticEventField)
        {
            target = default;
            return false;
        }

        var routedEventOwnerType = routedEventResolution.OwnerType!;
        var routedEventField = routedEventResolution.EventField!;
        if (routedEventResolution.HandlerType is null)
        {
            target = default;
            errorMessage =
                $"Event definition '{routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{routedEventField.Name}' is not compatible with Avalonia routed events.";
            return true;
        }

        target = new EventSubscriptionBindingTarget(
            ResolvedEventSubscriptionKind.RoutedEvent,
            routedEventResolution.HandlerType,
            routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            routedEventField.Name,
            routedEventResolution.HandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        return true;
    }

    private bool TryBuildInlineCodeSubscription(
        string rawCode,
        bool isLambdaExpression,
        EventSubscriptionBindingTarget target,
        string eventName,
        INamedTypeSymbol targetType,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        if (!_eventBindingDefinitionService.TryBuildInlineCodeDefinition(
                rawCode: rawCode,
                isLambdaExpression: isLambdaExpression,
                eventName: eventName,
                eventHandlerType: target.HandlerType,
                compilation: compilation,
                nodeDataType: nodeDataType,
                targetType: targetType,
                rootTypeSymbol: rootTypeSymbol,
                documentClassFullName: document.IsClassBacked ? document.ClassFullName : null,
                line: line,
                column: column,
                out var eventBindingDefinition,
                out var errorMessage))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                errorMessage,
                document.FilePath,
                line,
                column,
                options.StrictMode));
            return false;
        }

        subscription = CreateSubscription(
            target,
            eventName,
            eventBindingDefinition!.GeneratedMethodName,
            line,
            column,
            condition,
            eventBindingDefinition);
        return true;
    }

    private static ResolvedEventSubscription CreateSubscription(
        EventSubscriptionBindingTarget target,
        string eventName,
        string handlerMethodName,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        ResolvedEventBindingDefinition? eventBindingDefinition = null)
    {
        return new ResolvedEventSubscription(
            EventName: eventName,
            HandlerMethodName: handlerMethodName,
            Kind: target.Kind,
            RoutedEventOwnerTypeName: target.RoutedEventOwnerTypeName,
            RoutedEventFieldName: target.RoutedEventFieldName,
            RoutedEventHandlerTypeName: target.RoutedEventHandlerTypeName,
            Line: line,
            Column: column,
            Condition: condition,
            EventBindingDefinition: eventBindingDefinition);
    }
}
