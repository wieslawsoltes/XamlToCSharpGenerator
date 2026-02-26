using System;
using System.Collections.Immutable;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Core.Parsing;

public enum XamlMarkupNamedArgumentParseStatus
{
    None = 0,
    Parsed,
    LeadingEquals,
    EmptyName
}

public static class XamlMarkupArgumentSemantics
{
    public static bool TryParseHead(string innerContent, out string name, out string argumentsText)
    {
        name = string.Empty;
        argumentsText = string.Empty;
        if (string.IsNullOrWhiteSpace(innerContent))
        {
            return false;
        }

        var headLength = 0;
        while (headLength < innerContent.Length &&
               !char.IsWhiteSpace(innerContent[headLength]) &&
               innerContent[headLength] != ',')
        {
            headLength++;
        }

        name = innerContent[..headLength].Trim();
        if (name.Length == 0)
        {
            return false;
        }

        argumentsText = headLength < innerContent.Length ? innerContent[headLength..].Trim() : string.Empty;
        if (argumentsText.StartsWith(",", StringComparison.Ordinal))
        {
            argumentsText = argumentsText[1..].TrimStart();
        }

        return true;
    }

    public static ImmutableArray<string> SplitArguments(string argumentsText)
    {
        if (string.IsNullOrWhiteSpace(argumentsText))
        {
            return ImmutableArray<string>.Empty;
        }

        return TopLevelTextParser.SplitTopLevel(
            argumentsText,
            ',',
            trimTokens: true,
            removeEmpty: true);
    }

    public static XamlMarkupNamedArgumentParseStatus TryParseNamedArgument(
        string argument,
        out string key,
        out string value)
    {
        key = string.Empty;
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return XamlMarkupNamedArgumentParseStatus.None;
        }

        var trimmedArgument = argument.Trim();
        if (trimmedArgument.Length == 0)
        {
            return XamlMarkupNamedArgumentParseStatus.None;
        }

        var equalsIndex = TopLevelTextParser.IndexOfTopLevel(trimmedArgument, '=');
        if (equalsIndex < 0)
        {
            return XamlMarkupNamedArgumentParseStatus.None;
        }

        if (equalsIndex == 0)
        {
            value = trimmedArgument;
            return XamlMarkupNamedArgumentParseStatus.LeadingEquals;
        }

        key = trimmedArgument[..equalsIndex].Trim();
        value = trimmedArgument[(equalsIndex + 1)..].Trim();
        return key.Length == 0
            ? XamlMarkupNamedArgumentParseStatus.EmptyName
            : XamlMarkupNamedArgumentParseStatus.Parsed;
    }
}
