using System;
using System.Globalization;
using XamlToCSharpGenerator.Core.Models;
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

        var extensionName = markup.Name;
        if (!extensionName.Equals("Binding", StringComparison.OrdinalIgnoreCase) &&
            !extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, tryParseMarkupExtension, out bindingMarkup);
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

        var extensionName = markup.Name;
        if (!extensionName.Equals("ReflectionBinding", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseBindingMarkupCore(markup, extensionName, tryParseMarkupExtension, out bindingMarkup);
    }

    public static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        string extensionName,
        TryParseMarkupExtensionDelegate tryParseMarkupExtension,
        out BindingMarkup bindingMarkup)
    {
        var path = string.Empty;
        if (markup.NamedArguments.TryGetValue("Path", out var explicitPath))
        {
            path = Unquote(explicitPath);
        }
        else if (markup.PositionalArguments.Length > 0)
        {
            path = Unquote(markup.PositionalArguments[0]);
        }

        var parsedMarkup = new BindingMarkup(
            isCompiledBinding: extensionName.Equals("CompiledBinding", StringComparison.OrdinalIgnoreCase),
            path: path,
            mode: TryGetNamedMarkupArgument(markup, "Mode"),
            elementName: TryGetNamedMarkupArgument(markup, "ElementName"),
            relativeSource: markup.NamedArguments.TryGetValue("RelativeSource", out var relativeSourceValue) &&
                            TryParseRelativeSourceMarkup(relativeSourceValue, tryParseMarkupExtension, out var relativeSourceMarkup)
                ? relativeSourceMarkup
                : null,
            source: TryGetNamedMarkupArgument(markup, "Source"),
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
                return Unquote(value);
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

        if (TryParseElementNameQuery(trimmedPath, out var elementName, out var normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '#name' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: elementName,
                relativeSource: normalizedBindingMarkup.RelativeSource,
                source: normalizedBindingMarkup.Source,
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

        if (TryParseSelfQuery(trimmedPath, out var selfRelativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$self' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: selfRelativeSource,
                source: normalizedBindingMarkup.Source,
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

        if (TryParseParentQuery(trimmedPath, out var relativeSource, out normalizedPath))
        {
            if (HasExplicitBindingSource(normalizedBindingMarkup))
            {
                return CreateBindingSourceConflict(
                    normalizedBindingMarkup,
                    "Binding path source query '$parent' cannot be combined with ElementName, RelativeSource, or Source.");
            }

            return new BindingMarkup(
                isCompiledBinding: normalizedBindingMarkup.IsCompiledBinding,
                path: normalizedPath,
                mode: normalizedBindingMarkup.Mode,
                elementName: normalizedBindingMarkup.ElementName,
                relativeSource: relativeSource,
                source: normalizedBindingMarkup.Source,
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
        var name = markupExtension.Name.Trim();
        return name.Equals("EventBinding", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("x:EventBinding", StringComparison.OrdinalIgnoreCase);
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
            if (!bool.TryParse(Unquote(passEventArgsToken), out passEventArgs))
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

        var methodPath = Unquote(methodToken!).Trim();
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

        var normalized = Unquote(sourceToken).Trim();
        if (normalized.Equals("DataContextThenRoot", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContextThenRoot;
            return true;
        }

        if (normalized.Equals("DataContext", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.DataContext;
            return true;
        }

        if (normalized.Equals("Root", StringComparison.OrdinalIgnoreCase))
        {
            sourceMode = ResolvedEventBindingSourceMode.Root;
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

        path = Unquote(token).Trim();
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

        if (string.IsNullOrWhiteSpace(parameterToken))
        {
            return true;
        }

        if (TryParseBindingMarkup(parameterToken, tryParseMarkupExtension, out var bindingMarkup))
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

        if (tryParseMarkupExtension(parameterToken, out var markupExtension))
        {
            var extensionName = markupExtension.Name.Trim();
            if (extensionName.Equals("x:Null", StringComparison.OrdinalIgnoreCase) ||
                extensionName.Equals("Null", StringComparison.OrdinalIgnoreCase))
            {
                parameterValueExpression = "null";
                hasParameterValueExpression = true;
                return true;
            }

            errorMessage = "EventBinding parameter supports literals, x:Null, or Binding/CompiledBinding paths.";
            return false;
        }

        if (!tryConvertLiteralValueExpression(Unquote(parameterToken), out var literalExpression))
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
            var markupName = markup.Name.Trim();
            if (!markupName.Equals("x:Reference", StringComparison.OrdinalIgnoreCase) &&
                !markupName.Equals("Reference", StringComparison.OrdinalIgnoreCase) &&
                !markupName.Equals("ResolveByName", StringComparison.OrdinalIgnoreCase))
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
        normalizedName = string.Empty;
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return false;
        }

        var unquoted = Unquote(rawName!).Trim();
        if (unquoted.Length == 0 ||
            unquoted.IndexOfAny([' ', '\t', '\r', '\n']) >= 0)
        {
            return false;
        }

        normalizedName = unquoted;
        return true;
    }

    public static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        elementName = string.Empty;
        normalizedPath = string.Empty;

        if (!path.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var index = 1;
        while (index < path.Length && MiniLanguageSyntaxFacts.IsIdentifierPart(path[index]))
        {
            index++;
        }

        if (index == 1)
        {
            return false;
        }

        elementName = path.Substring(1, index - 1);
        if (index == path.Length)
        {
            normalizedPath = ".";
            return true;
        }

        if (path[index] != '.')
        {
            return false;
        }

        normalizedPath = path.Substring(index + 1).Trim();
        if (normalizedPath.Length == 0)
        {
            normalizedPath = ".";
        }

        return true;
    }

    public static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        relativeSource = default;
        normalizedPath = string.Empty;

        if (!path.StartsWith("$self", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$self".Length;
        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

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

        if (!path.StartsWith("$parent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$parent".Length;
        string? ancestorTypeToken = null;
        int? ancestorLevel = 1;

        if (index < path.Length && path[index] == '[')
        {
            var closingBracket = path.IndexOf(']', index + 1);
            if (closingBracket <= index + 1)
            {
                return false;
            }

            var inside = path.Substring(index + 1, closingBracket - index - 1).Trim();
            if (inside.Length > 0)
            {
                var separators = inside.Split(new[] { ',', ';' }, 2, StringSplitOptions.None);
                var firstPart = separators[0].Trim();
                if (separators.Length == 1)
                {
                    if (int.TryParse(firstPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSingleLevel) &&
                        parsedSingleLevel > 0)
                    {
                        ancestorLevel = parsedSingleLevel;
                        ancestorTypeToken = null;
                    }
                    else
                    {
                        ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    }
                }
                else
                {
                    ancestorTypeToken = firstPart.Length > 0 ? firstPart : null;
                    if (int.TryParse(separators[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLevel) &&
                        parsedLevel > 0)
                    {
                        ancestorLevel = parsedLevel;
                    }
                }
            }

            index = closingBracket + 1;
        }

        if (index == path.Length)
        {
            normalizedPath = ".";
        }
        else
        {
            if (path[index] != '.')
            {
                return false;
            }

            normalizedPath = path.Substring(index + 1).Trim();
            if (normalizedPath.Length == 0)
            {
                normalizedPath = ".";
            }
        }

        relativeSource = new RelativeSourceMarkup(
            mode: "FindAncestor",
            ancestorTypeToken: ancestorTypeToken,
            ancestorLevel: ancestorLevel,
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
            if (!markupExtension.Name.Equals("RelativeSource", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var mode = markupExtension.NamedArguments.TryGetValue("Mode", out var explicitMode)
                ? Unquote(explicitMode)
                : markupExtension.PositionalArguments.Length > 0
                    ? Unquote(markupExtension.PositionalArguments[0])
                    : null;

            int? ancestorLevel = null;
            if ((markupExtension.NamedArguments.TryGetValue("AncestorLevel", out var rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("FindAncestor", out rawAncestorLevel) ||
                 markupExtension.NamedArguments.TryGetValue("Level", out rawAncestorLevel)) &&
                int.TryParse(Unquote(rawAncestorLevel), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAncestorLevel))
            {
                ancestorLevel = parsedAncestorLevel;
            }

            var ancestorType = markupExtension.NamedArguments.TryGetValue("AncestorType", out var rawAncestorType)
                ? Unquote(rawAncestorType)
                : null;
            var tree = markupExtension.NamedArguments.TryGetValue("Tree", out var rawTree)
                ? Unquote(rawTree)
                : markupExtension.NamedArguments.TryGetValue("TreeType", out var rawTreeType)
                    ? Unquote(rawTreeType)
                    : null;

            relativeSourceMarkup = new RelativeSourceMarkup(mode, ancestorType, ancestorLevel, tree);
            return true;
        }

        relativeSourceMarkup = new RelativeSourceMarkup(
            mode: Unquote(value.Trim()),
            ancestorTypeToken: null,
            ancestorLevel: null,
            tree: null);
        return true;
    }

    public static string Unquote(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"') ||
             (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
        {
            return trimmed.Substring(1, trimmed.Length - 2);
        }

        return trimmed;
    }
}
