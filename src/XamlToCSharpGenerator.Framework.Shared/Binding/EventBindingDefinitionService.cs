using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct EventBindingDefinitionBuildResult(
    ResolvedEventBindingDefinition Definition,
    ImmutableArray<string> WarningMessages);

public sealed class EventBindingDefinitionService
{
    private readonly EventBindingSemanticBindingService _semanticBindingService;
    private readonly TryParseMarkupExtensionDelegate _tryParseMarkupExtension;
    private readonly TryConvertLiteralValueExpressionDelegate _tryConvertLiteralValueExpression;

    public EventBindingDefinitionService(
        EventBindingSemanticBindingService semanticBindingService,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        TryConvertLiteralValueExpressionDelegate tryConvertLiteralValueExpression)
    {
        _semanticBindingService = semanticBindingService ?? throw new ArgumentNullException(nameof(semanticBindingService));
        _tryParseMarkupExtension = tryParseMarkupExtension ?? throw new ArgumentNullException(nameof(tryParseMarkupExtension));
        _tryConvertLiteralValueExpression = tryConvertLiteralValueExpression ?? throw new ArgumentNullException(nameof(tryConvertLiteralValueExpression));
    }

    public static bool IsEventBindingMarkupExtension(MarkupExtensionInfo markupExtension)
    {
        return BindingEventMarkupParser.IsEventBindingMarkupExtension(markupExtension);
    }

    public bool TryBuildInlineCodeDefinition(
        string rawCode,
        bool isLambdaExpression,
        string eventName,
        ITypeSymbol eventHandlerType,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        string? documentClassFullName,
        int line,
        int column,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out string errorMessage)
    {
        if (_semanticBindingService.TryBuildInlineCodeDefinition(
                rawCode,
                isLambdaExpression,
                eventName,
                eventHandlerType,
                compilation,
                nodeDataType,
                targetType,
                rootTypeSymbol,
                documentClassFullName,
                out eventBindingDefinition,
                out errorMessage))
        {
            eventBindingDefinition = eventBindingDefinition! with { Line = line, Column = column };
            return true;
        }

        eventBindingDefinition = null;
        return false;
    }

    public bool TryBuildParsedDefinition(
        string rawValue,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        int line,
        int column,
        out EventBindingDefinitionBuildResult result,
        out string errorMessage)
    {
        result = default;
        errorMessage = string.Empty;

        if (rootTypeSymbol is null)
        {
            errorMessage = $"EventBinding on '{eventName}' requires x:Class-backed root type.";
            return false;
        }

        if (!_tryParseMarkupExtension(rawValue, out var markupExtension) ||
            !IsEventBindingMarkupExtension(markupExtension))
        {
            errorMessage = $"Event '{eventName}' uses unsupported EventBinding syntax.";
            return false;
        }

        if (!BindingEventMarkupParser.TryParseEventBindingMarkup(
                markupExtension,
                _tryParseMarkupExtension,
                _tryConvertLiteralValueExpression,
                out var parsedBinding,
                out var parseError))
        {
            errorMessage = parseError ?? $"EventBinding on '{eventName}' is invalid.";
            return false;
        }

        if (!_semanticBindingService.TryBuildParsedDefinition(
                parsedBinding,
                eventName,
                eventHandlerType,
                compilation,
                nodeDataType,
                rootTypeSymbol,
                line,
                column,
                out var eventBindingResult,
                out errorMessage))
        {
            return false;
        }

        var warningMessages = ImmutableArray.CreateBuilder<string>(2);
        if (!eventBindingResult.TargetPathValidated)
        {
            warningMessages.Add(
                $"EventBinding {(parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command ? "command" : "method")} path '{parsedBinding.TargetPath}' could not be validated against available source types.");
        }

        if (!eventBindingResult.HasCompiledCoverage)
        {
            warningMessages.Add(
                $"EventBinding {(parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command ? "command" : "method")} path '{parsedBinding.TargetPath}' requires compile-time resolvable members for source mode '{parsedBinding.SourceMode}'.");
        }

        result = new EventBindingDefinitionBuildResult(
            eventBindingResult.Definition,
            warningMessages.ToImmutable());
        return true;
    }
}
