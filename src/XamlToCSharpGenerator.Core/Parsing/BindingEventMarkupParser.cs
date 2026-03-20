using System;
using System.Globalization;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public delegate bool TryParseMarkupExtensionDelegate(string value, out MarkupExtensionInfo markupExtension);

public delegate bool TryConvertLiteralValueExpressionDelegate(string literalValue, out string expression);

public static class BindingEventMarkupParser
{
    public static bool TryParseBindingMarkup(
        string value,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!tryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionKind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
        if (extensionKind is not XamlMarkupExtensionKind.Binding &&
            extensionKind is not XamlMarkupExtensionKind.CompiledBinding)
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionKind, tryParseMarkupExtension, out bindingMarkup);
    }

    public static bool TryParseReflectionBindingMarkup(
        string value,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out BindingMarkup bindingMarkup)
    {
        bindingMarkup = default;
        if (!tryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        var extensionKind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
        if (extensionKind is not XamlMarkupExtensionKind.ReflectionBinding)
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionKind, tryParseMarkupExtension, out bindingMarkup);
    }

    public static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        XamlMarkupExtensionKind extensionKind,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out BindingMarkup bindingMarkup)
    {
        var path = string.Empty;
        if (markup.NamedArguments.TryGetValue("Path", out var explicitPath))
        {
            path = XamlQuotedValueSemantics.TrimAndUnquote(explicitPath);
        }
        else if (markup.PositionalArguments.Length > 0)
        {
            path = XamlQuotedValueSemantics.TrimAndUnquote(markup.PositionalArguments[0]);
        }

        var parsedMarkup = new BindingMarkup(
            isCompiledBinding: extensionKind is XamlMarkupExtensionKind.CompiledBinding,
            path: path,
            mode: TryGetNamedMarkupArgument(markup, "Mode"),
            elementName: TryGetNamedMarkupArgument(markup, "ElementName"),
            relativeSource: markup.NamedArguments.TryGetValue("RelativeSource", out var relativeSourceValue) &&
                            TryParseRelativeSourceMarkup(relativeSourceValue, tryParseMarkupExtension, out var relativeSourceMarkup)
                ? relativeSourceMarkup
                : null,
            source: TryGetNamedMarkupArgument(markup, "Source"),
            dataType: TryGetNamedMarkupArgument(markup, "DataType"),
            converter: TryGetNamedMarkupArgument(markup, "Converter"),
            converterCulture: TryGetNamedMarkupArgument(markup, "ConverterCulture"),
            converterParameter: TryGetNamedMarkupArgument(markup, "ConverterParameter"),
            stringFormat: TryGetNamedMarkupArgument(markup, "StringFormat", "Format"),
            fallbackValue: TryGetNamedMarkupArgument(markup, "FallbackValue", "Fallback"),
            targetNullValue: TryGetNamedMarkupArgument(markup, "TargetNullValue", "NullValue"),
            delay: TryGetNamedMarkupArgument(markup, "Delay"),
            priority: TryGetNamedMarkupArgument(markup, "Priority", "BindingPriority"),
            updateSourceTrigger: TryGetNamedMarkupArgument(markup, "UpdateSourceTrigger", "Trigger"),
            hasSourceConflict: false,
            sourceConflictMessage: null);

        bindingMarkup = NormalizeBindingQuerySyntax(parsedMarkup, tryParseMarkupExtension);
        return true;
    }

    public static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, params string[] argumentNames)
    {
        foreach (var argumentName in argumentNames)
        {
            if (markup.NamedArguments.TryGetValue(argumentName, out var value))
            {
                return XamlQuotedValueSemantics.TrimAndUnquote(value);
            }
        }

        return null;
    }

    public static BindingMarkup NormalizeBindingQuerySyntax(
        BindingMarkup bindingMarkup,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension)
    {
        if (CountExplicitBindingSources(bindingMarkup) > 1)
        {
            return CreateBindingSourceConflict(
                bindingMarkup,
                "Only one binding source may be specified. Use only one of ElementName, RelativeSource, or Source.");
        }

        var normalizedBindingMarkup = bindingMarkup;
        if (TryExtractReferenceElementName(normalizedBindingMarkup.Source, tryParseMarkupExtension, out var referenceElementName))
        {
            normalizedBindingMarkup = new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedBindingMarkup.Path,
                mode: normalizedBindingMarkup.Mode,
                elementName: referenceElementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: null,
                dataType: normalizedBindingMarkup.DataType,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        var trimmedPath = normalizedBindingMarkup.Path.Trim();
        if (trimmedPath.Length == 0)
        {
            return normalizedBindingMarkup;
        }

        var queryPath = trimmedPath;
        var leadingNotCount = CountLeadingNotOperators(queryPath);
        if (leadingNotCount > 0 && leadingNotCount < queryPath.Length)
        {
            queryPath = queryPath.Substring(leadingNotCount);
        }

        if (TryParseElementNameQuery(queryPath, out var elementName, out var normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '#name' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: ReapplyLeadingNotOperators(normalizedPath, leadingNotCount),
                mode: normalizedBindingMarkup.Mode,
                elementName: elementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: normalizedBindingMarkup.Source,
                dataType: normalizedBindingMarkup.DataType,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseSelfQuery(queryPath, out var selfRelativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$self' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: ReapplyLeadingNotOperators(normalizedPath, leadingNotCount),
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: selfRelativeSource,
                source: normalizedBindingMarkup.Source,
                dataType: normalizedBindingMarkup.DataType,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        if (TryParseParentQuery(queryPath, out var relativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$parent' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: ReapplyLeadingNotOperators(normalizedPath, leadingNotCount),
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: relativeSource,
                source: normalizedBindingMarkup.Source,
                dataType: normalizedBindingMarkup.DataType,
                converter: normalizedBindingMarkup.Converter,
                converterCulture: normalizedBindingMarkup.ConverterCulture,
                converterParameter: normalizedBindingMarkup.ConverterParameter,
                stringFormat: normalizedBindingMarkup.StringFormat,
                fallbackValue: normalizedBindingMarkup.FallbackValue,
                targetNullValue: normalizedBindingMarkup.TargetNullValue,
                delay: normalizedBindingMarkup.Delay,
                priority: normalizedBindingMarkup.Priority,
                updateSourceTrigger: normalizedBindingMarkup.UpdateSourceTrigger,
                hasSourceConflict: normalizedBindingMarkup.HasSourceConflict,
                sourceConflictMessage: normalizedBindingMarkup.SourceConflictMessage);
        }

        return normalizedBindingMarkup;
    }

    private static int CountLeadingNotOperators(string path)
    {
        var count = 0;
        while (count < path.Length && path[count] == '!')
        {
            count++;
        }

        return count;
    }

    private static string ReapplyLeadingNotOperators(string normalizedPath, int leadingNotCount)
    {
        if (leadingNotCount <= 0)
        {
            return normalizedPath;
        }

        return new string('!', leadingNotCount) + normalizedPath;
    }

    public static bool HasExplicitBindingSource(BindingMarkup bindingMarkup)
    {
        return CountExplicitBindingSources(bindingMarkup) > 0;
    }

    public static int CountExplicitBindingSources(BindingMarkup bindingMarkup)
    {
        var sourceCount = 0;
        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            sourceCount++;
        }

        if (bindingMarkup.RelativeSource is not null)
        {
            sourceCount++;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            sourceCount++;
        }

        return sourceCount;
    }

    public static BindingMarkup CreateBindingSourceConflict(BindingMarkup bindingMarkup, string message)
    {
        if (bindingMarkup.HasSourceConflict)
        {
            return bindingMarkup;
        }

        return new BindingMarkup(
            isCompiledBinding: bindingMarkup.IsCompiledBinding,
            path: bindingMarkup.Path,
            mode: bindingMarkup.Mode,
            elementName: bindingMarkup.ElementName,
            relativeSource: bindingMarkup.RelativeSource,
            source: bindingMarkup.Source,
            dataType: bindingMarkup.DataType,
            converter: bindingMarkup.Converter,
            converterCulture: bindingMarkup.ConverterCulture,
            converterParameter: bindingMarkup.ConverterParameter,
            stringFormat: bindingMarkup.StringFormat,
            fallbackValue: bindingMarkup.FallbackValue,
            targetNullValue: bindingMarkup.TargetNullValue,
            delay: bindingMarkup.Delay,
            priority: bindingMarkup.Priority,
            updateSourceTrigger: bindingMarkup.UpdateSourceTrigger,
            hasSourceConflict: true,
            sourceConflictMessage: message);
    }

    public static bool TryExtractReferenceElementName(
        string? sourceValue,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out string elementName)
    {
        elementName = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceValue))
        {
            return false;
        }

        if (!TryParseResolveByNameReferenceToken(sourceValue!, tryParseMarkupExtension, out var referenceToken) ||
            !referenceToken.FromMarkupExtension)
        {
            return false;
        }

        elementName = referenceToken.Name;
        return elementName.Length > 0;
    }

    public static bool IsEventBindingMarkupExtension(MarkupExtensionInfo markupExtension)
    {
        return XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) == XamlMarkupExtensionKind.EventBinding;
    }

    public static bool TryParseEventBindingMarkup(
        MarkupExtensionInfo markupExtension,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        TryConvertLiteralValueExpressionDelegate tryConvertLiteralValueExpression,
        out EventBindingMarkup eventBindingMarkup,
        out string? errorMessage)
    {
        eventBindingMarkup = default;
        errorMessage = null;

        var commandToken = markupExtension.NamedArguments.TryGetValue("Command", out var explicitCommand)
            ? explicitCommand
            : markupExtension.NamedArguments.TryGetValue("Path", out var explicitPath)
                ? explicitPath
                : markupExtension.PositionalArguments.Length > 0
                    ? markupExtension.PositionalArguments[0]
                    : null;
        var methodToken = markupExtension.NamedArguments.TryGetValue("Method", out var explicitMethod)
            ? explicitMethod
            : null;

        if (!string.IsNullOrWhiteSpace(commandToken) && !string.IsNullOrWhiteSpace(methodToken))
        {
            errorMessage = "EventBinding cannot define both Command and Method.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(commandToken) && string.IsNullOrWhiteSpace(methodToken))
        {
            errorMessage = "EventBinding requires either Command/Path or Method.";
            return false;
        }

        if (!TryParseEventBindingSourceMode(markupExtension, out var sourceMode, out errorMessage))
        {
            return false;
        }

        var passEventArgs = false;
        if (markupExtension.NamedArguments.TryGetValue("PassEventArgs", out var passEventArgsToken))
        {
            if (!bool.TryParse(XamlQuotedValueSemantics.TrimAndUnquote(passEventArgsToken), out passEventArgs))
            {
                errorMessage = "EventBinding PassEventArgs must be true or false.";
                return false;
            }
        }

        var parameterToken = markupExtension.NamedArguments.TryGetValue("Parameter", out var explicitParameter)
                             ? explicitParameter
                             : markupExtension.NamedArguments.TryGetValue("CommandParameter", out var explicitCommandParameter)
                                 ? explicitCommandParameter
                                 : null;

        string? parameterPath = null;
        string? parameterValueExpression = null;
        var hasParameterValueExpression = false;
        if (!TryParseEventBindingParameter(
                parameterToken,
                tryParseMarkupExtension,
                tryConvertLiteralValueExpression,
                out parameterPath,
                out parameterValueExpression,
                out hasParameterValueExpression,
                out var parameterError))
        {
            errorMessage = parameterError;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(commandToken))
        {
            if (!TryParseEventBindingPath(commandToken!, tryParseMarkupExtension, out var commandPath, out var pathError))
            {
                errorMessage = pathError;
                return false;
            }

            eventBindingMarkup = new EventBindingMarkup(
                ResolvedEventBindingTargetKind.Command,
                sourceMode,
                commandPath,
                parameterPath,
                parameterValueExpression,
                hasParameterValueExpression,
                passEventArgs);
            return true;
        }

        var methodPath = XamlQuotedValueSemantics.TrimAndUnquote(methodToken!);
        if (methodPath.Length == 0)
        {
            errorMessage = "EventBinding Method must not be empty.";
            return false;
        }

        eventBindingMarkup = new EventBindingMarkup(
            ResolvedEventBindingTargetKind.Method,
            sourceMode,
            methodPath,
            parameterPath,
            parameterValueExpression,
            hasParameterValueExpression,
            passEventArgs);
        return true;
    }

    public static bool TryParseEventBindingSourceMode(
        MarkupExtensionInfo markupExtension,
        out ResolvedEventBindingSourceMode sourceMode,
        out string? errorMessage)
    {
        errorMessage = null;
        sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
        if (!markupExtension.NamedArguments.TryGetValue("Source", out var sourceToken))
        {
            return true;
        }

        if (EventBindingSourceModeSemantics.TryParse(
                XamlQuotedValueSemantics.TrimAndUnquote(sourceToken),
                out sourceMode))
        {
            return true;
        }

        errorMessage = "EventBinding Source must be DataContext, Root, or DataContextThenRoot.";
        return false;
    }

    public static bool TryParseEventBindingPath(
        string token,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out string path,
        out string? errorMessage)
    {
        path = string.Empty;
        errorMessage = null;

        if (TryParseBindingMarkup(token, tryParseMarkupExtension, out var bindingMarkup))
        {
            if (!TryValidateEventBindingBindingSource(
                    bindingMarkup,
                    "EventBinding command path",
                    out errorMessage))
            {
                return false;
            }

            path = string.IsNullOrWhiteSpace(bindingMarkup.Path)
                ? "."
                : bindingMarkup.Path.Trim();
            return true;
        }

        if (tryParseMarkupExtension(token, out _))
        {
            errorMessage = "EventBinding command path supports only plain paths or Binding/CompiledBinding markup.";
            return false;
        }

        path = XamlQuotedValueSemantics.TrimAndUnquote(token);
        if (path.Length == 0)
        {
            errorMessage = "EventBinding command path must not be empty.";
            return false;
        }

        return true;
    }

    public static bool TryParseEventBindingParameter(
        string? parameterToken,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        TryConvertLiteralValueExpressionDelegate tryConvertLiteralValueExpression,
        out string? parameterPath,
        out string? parameterValueExpression,
        out bool hasParameterValueExpression,
        out string? errorMessage)
    {
        parameterPath = null;
        parameterValueExpression = null;
        hasParameterValueExpression = false;
        errorMessage = null;

        if (parameterToken is null)
        {
            return true;
        }

        var parameterTokenValue = parameterToken.Trim();
        if (parameterTokenValue.Length == 0)
        {
            return true;
        }

        if (TryParseBindingMarkup(parameterTokenValue, tryParseMarkupExtension, out var bindingMarkup))
        {
            if (!TryValidateEventBindingBindingSource(
                    bindingMarkup,
                    "EventBinding parameter path",
                    out errorMessage))
            {
                return false;
            }

            parameterPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
                ? "."
                : bindingMarkup.Path.Trim();
            return true;
        }

        if (tryParseMarkupExtension(parameterTokenValue, out var markupExtension))
        {
            if (XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) == XamlMarkupExtensionKind.Null)
            {
                parameterValueExpression = "null";
                hasParameterValueExpression = true;
                return true;
            }

            errorMessage = "EventBinding parameter supports literals, x:Null, or Binding/CompiledBinding paths.";
            return false;
        }

        if (!tryConvertLiteralValueExpression(XamlQuotedValueSemantics.TrimAndUnquote(parameterTokenValue), out var literalExpression))
        {
            errorMessage = "EventBinding parameter literal is invalid.";
            return false;
        }

        parameterValueExpression = literalExpression;
        hasParameterValueExpression = true;
        return true;
    }

    public static bool TryValidateEventBindingBindingSource(
        BindingMarkup bindingMarkup,
        string contextName,
        out string? errorMessage)
    {
        errorMessage = null;
        if (bindingMarkup.HasSourceConflict)
        {
            errorMessage = bindingMarkup.SourceConflictMessage ?? contextName + " binding source is invalid.";
            return false;
        }

        if (HasExplicitBindingSource(bindingMarkup))
        {
            errorMessage = contextName + " does not support explicit Binding Source/ElementName/RelativeSource.";
            return false;
        }

        return true;
    }

    public static bool TryParseResolveByNameReferenceToken(
        string rawValue,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out ResolveByNameReferenceToken referenceToken)
    {
        referenceToken = default;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (tryParseMarkupExtension(trimmed, out var markup))
        {
            var kind = XamlMarkupExtensionNameSemantics.Classify(markup.Name);
            if (kind is not XamlMarkupExtensionKind.Reference &&
                kind is not XamlMarkupExtensionKind.ResolveByName)
            {
                return false;
            }

            var rawName = TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                          (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
            if (!TryNormalizeReferenceName(rawName, out var normalizedName))
            {
                return false;
            }

            referenceToken = new ResolveByNameReferenceToken(normalizedName, fromMarkupExtension: true);
            return true;
        }

        if (!TryNormalizeReferenceName(trimmed, out var literalName))
        {
            return false;
        }

        referenceToken = new ResolveByNameReferenceToken(literalName, fromMarkupExtension: false);
        return true;
    }

    public static bool TryNormalizeReferenceName(string? rawName, out string normalizedName)
    {
        return XamlReferenceNameSemantics.TryNormalizeReferenceName(rawName, out normalizedName);
    }

    public static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        elementName = string.Empty;
        normalizedPath = string.Empty;

        if (!BindingSourceQuerySemantics.TryParseElementName(path, out var query))
        {
            return false;
        }

        elementName = query.ElementName ?? string.Empty;
        normalizedPath = query.NormalizedPath;
        return true;
    }

    public static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!BindingSourceQuerySemantics.TryParseSelf(path, out var query))
        {
            return false;
        }

        normalizedPath = query.NormalizedPath;
        relativeSource = new RelativeSourceMarkup(
            mode: "Self",
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    public static bool TryParseParentQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!BindingSourceQuerySemantics.TryParseParent(path, out var query))
        {
            return false;
        }

        normalizedPath = query.NormalizedPath;
        relativeSource = new RelativeSourceMarkup(
            mode: "FindAncestor",
            ancestorTypeToken: query.AncestorTypeToken,
            ancestorLevel: query.AncestorLevel,
            tree: null);
        return true;
    }

    public static bool TryParseRelativeSourceMarkup(
        string value,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out RelativeSourceMarkup relativeSourceMarkup)
    {
        relativeSourceMarkup = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (tryParseMarkupExtension(value, out var markupExtension))
        {
            if (XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) !=
                XamlMarkupExtensionKind.RelativeSource)
            {
                return false;
            }

            var mode = markupExtension.NamedArguments.TryGetValue("Mode", out var explicitMode)
                ? XamlQuotedValueSemantics.TrimAndUnquote(explicitMode)
                : markupExtension.PositionalArguments.Length > 0
                    ? XamlQuotedValueSemantics.TrimAndUnquote(markupExtension.PositionalArguments[0])
                    : null;

            int? ancestorLevel = null;
            if ((markupExtension.NamedArguments.TryGetValue("AncestorLevel", out var rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("FindAncestor", out rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("Level", out rawAncestorLevel)) &&
                int.TryParse(XamlQuotedValueSemantics.TrimAndUnquote(rawAncestorLevel), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAncestorLevel))
            {
                ancestorLevel = parsedAncestorLevel;
            }

            var ancestorType = markupExtension.NamedArguments.TryGetValue("AncestorType", out var rawAncestorType)
                ? XamlQuotedValueSemantics.TrimAndUnquote(rawAncestorType)
                : null;
            var tree = markupExtension.NamedArguments.TryGetValue("Tree", out var rawTree)
                ? XamlQuotedValueSemantics.TrimAndUnquote(rawTree)
                : markupExtension.NamedArguments.TryGetValue("TreeType", out var rawTreeType)
                    ? XamlQuotedValueSemantics.TrimAndUnquote(rawTreeType)
                    : null;

            relativeSourceMarkup = new RelativeSourceMarkup(mode, ancestorType, ancestorLevel, tree);
            return true;
        }

        relativeSourceMarkup = new RelativeSourceMarkup(
            mode: XamlQuotedValueSemantics.TrimAndUnquote(value),
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }
}
