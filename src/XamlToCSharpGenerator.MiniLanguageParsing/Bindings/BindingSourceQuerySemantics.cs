using System;
using System.Globalization;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

public enum BindingSourceQueryKind
{
    ElementName,
    Self,
    Parent
}

public readonly record struct BindingSourceQuery(
    BindingSourceQueryKind Kind,
    string NormalizedPath,
    string? ElementName = null,
    string? AncestorTypeToken = null,
    int? AncestorLevel = null);

public static class BindingSourceQuerySemantics
{
    public static bool TryParseElementName(string path, out BindingSourceQuery query)
    {
        query = default;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("#", StringComparison.Ordinal))
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

        var elementName = path.Substring(1, index - 1);
        if (!TryParseTrailingPath(path, index, out var normalizedPath))
        {
            return false;
        }

        query = new BindingSourceQuery(
            Kind: BindingSourceQueryKind.ElementName,
            NormalizedPath: normalizedPath,
            ElementName: elementName);
        return true;
    }

    public static bool TryParseSelf(string path, out BindingSourceQuery query)
    {
        query = default;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("$self", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$self".Length;
        if (!TryParseTrailingPath(path, index, out var normalizedPath))
        {
            return false;
        }

        query = new BindingSourceQuery(
            Kind: BindingSourceQueryKind.Self,
            NormalizedPath: normalizedPath);
        return true;
    }

    public static bool TryParseParent(string path, out BindingSourceQuery query)
    {
        query = default;
        if (string.IsNullOrWhiteSpace(path) ||
            !path.StartsWith("$parent", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = "$parent".Length;
        string? ancestorTypeToken = null;
        int? ancestorLevel = 1;

        if (index < path.Length && path[index] == '[')
        {
            var descriptorCursor = index;
            if (!TopLevelTextParser.TryReadBalancedContent(path, ref descriptorCursor, '[', ']', out var rawDescriptor))
            {
                return false;
            }

            if (rawDescriptor.Length == 0)
            {
                return false;
            }

            ParseParentDescriptor(rawDescriptor, ref ancestorTypeToken, ref ancestorLevel);
            index = descriptorCursor;
        }

        if (!TryParseTrailingPath(path, index, out var normalizedPath))
        {
            return false;
        }

        query = new BindingSourceQuery(
            Kind: BindingSourceQueryKind.Parent,
            NormalizedPath: normalizedPath,
            AncestorTypeToken: ancestorTypeToken,
            AncestorLevel: ancestorLevel);
        return true;
    }

    private static void ParseParentDescriptor(
        string descriptorText,
        ref string? ancestorTypeToken,
        ref int? ancestorLevel)
    {
        var descriptor = descriptorText.Trim();
        if (descriptor.Length == 0)
        {
            return;
        }

        if (!TrySplitParentDescriptor(descriptor, out var typeToken, out var levelToken))
        {
            if (TryParsePositiveInteger(descriptor, out var parsedLevel))
            {
                ancestorLevel = parsedLevel;
            }
            else
            {
                ancestorTypeToken = descriptor;
            }

            return;
        }

        ancestorTypeToken = typeToken.Length > 0 ? typeToken : null;
        if (TryParsePositiveInteger(levelToken, out var level))
        {
            ancestorLevel = level;
        }
    }

    private static bool TrySplitParentDescriptor(
        string descriptor,
        out string typeToken,
        out string levelToken)
    {
        typeToken = string.Empty;
        levelToken = string.Empty;

        var separatorIndex = FindParentDescriptorSeparator(descriptor);
        if (separatorIndex < 0)
        {
            return false;
        }

        typeToken = descriptor.Substring(0, separatorIndex).Trim();
        levelToken = descriptor.Substring(separatorIndex + 1).Trim();
        return true;
    }

    private static int FindParentDescriptorSeparator(string descriptor)
    {
        var commaIndex = TopLevelTextParser.IndexOfTopLevel(descriptor, ',');
        var semicolonIndex = TopLevelTextParser.IndexOfTopLevel(descriptor, ';');

        if (commaIndex < 0)
        {
            return semicolonIndex;
        }

        if (semicolonIndex < 0)
        {
            return commaIndex;
        }

        return Math.Min(commaIndex, semicolonIndex);
    }

    private static bool TryParsePositiveInteger(string token, out int value)
    {
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseTrailingPath(string path, int startIndex, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (startIndex == path.Length)
        {
            normalizedPath = ".";
            return true;
        }

        if (startIndex < 0 ||
            startIndex >= path.Length ||
            path[startIndex] != '.')
        {
            return false;
        }

        normalizedPath = path.Substring(startIndex + 1).Trim();
        if (normalizedPath.Length == 0)
        {
            normalizedPath = ".";
        }

        return true;
    }
}
