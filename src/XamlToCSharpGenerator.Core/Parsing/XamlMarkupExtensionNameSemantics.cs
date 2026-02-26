using System;

namespace XamlToCSharpGenerator.Core.Parsing;

public enum XamlMarkupExtensionKind
{
    Unknown = 0,
    Binding,
    CompiledBinding,
    ReflectionBinding,
    EventBinding,
    StaticResource,
    DynamicResource,
    TemplateBinding,
    RelativeSource,
    OnPlatform,
    OnFormFactor,
    Reference,
    ResolveByName,
    Static,
    Type,
    Null,
    String,
    Char,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    DateTime,
    TimeSpan,
    Uri,
    True,
    False,
    Array
}

public static class XamlMarkupExtensionNameSemantics
{
    private const string ExtensionSuffix = "Extension";
    private const string XamlDirectivePrefix = "x:";

    public static XamlMarkupExtensionKind Classify(string? extensionName)
    {
        if (!TryNormalizeToken(extensionName, out var token))
        {
            return XamlMarkupExtensionKind.Unknown;
        }

        return token switch
        {
            "binding" => XamlMarkupExtensionKind.Binding,
            "compiledbinding" => XamlMarkupExtensionKind.CompiledBinding,
            "reflectionbinding" => XamlMarkupExtensionKind.ReflectionBinding,
            "eventbinding" => XamlMarkupExtensionKind.EventBinding,
            "staticresource" => XamlMarkupExtensionKind.StaticResource,
            "dynamicresource" => XamlMarkupExtensionKind.DynamicResource,
            "templatebinding" => XamlMarkupExtensionKind.TemplateBinding,
            "relativesource" => XamlMarkupExtensionKind.RelativeSource,
            "onplatform" => XamlMarkupExtensionKind.OnPlatform,
            "onformfactor" => XamlMarkupExtensionKind.OnFormFactor,
            "reference" => XamlMarkupExtensionKind.Reference,
            "resolvebyname" => XamlMarkupExtensionKind.ResolveByName,
            "static" => XamlMarkupExtensionKind.Static,
            "type" => XamlMarkupExtensionKind.Type,
            "null" => XamlMarkupExtensionKind.Null,
            "string" => XamlMarkupExtensionKind.String,
            "char" => XamlMarkupExtensionKind.Char,
            "byte" => XamlMarkupExtensionKind.Byte,
            "sbyte" => XamlMarkupExtensionKind.SByte,
            "int16" => XamlMarkupExtensionKind.Int16,
            "uint16" => XamlMarkupExtensionKind.UInt16,
            "int32" => XamlMarkupExtensionKind.Int32,
            "uint32" => XamlMarkupExtensionKind.UInt32,
            "int64" => XamlMarkupExtensionKind.Int64,
            "uint64" => XamlMarkupExtensionKind.UInt64,
            "single" => XamlMarkupExtensionKind.Single,
            "double" => XamlMarkupExtensionKind.Double,
            "decimal" => XamlMarkupExtensionKind.Decimal,
            "datetime" => XamlMarkupExtensionKind.DateTime,
            "timespan" => XamlMarkupExtensionKind.TimeSpan,
            "uri" => XamlMarkupExtensionKind.Uri,
            "true" => XamlMarkupExtensionKind.True,
            "false" => XamlMarkupExtensionKind.False,
            "array" => XamlMarkupExtensionKind.Array,
            _ => XamlMarkupExtensionKind.Unknown
        };
    }

    public static bool Matches(string? extensionName, string? knownName)
    {
        return TryNormalizeToken(extensionName, out var normalizedExtensionName) &&
               TryNormalizeToken(knownName, out var normalizedKnownName) &&
               string.Equals(normalizedExtensionName, normalizedKnownName, StringComparison.Ordinal);
    }

    public static string ToClrExtensionTypeToken(string? extensionName)
    {
        if (string.IsNullOrWhiteSpace(extensionName))
        {
            return string.Empty;
        }

        var token = extensionName.Trim();
        if (token.StartsWith(XamlDirectivePrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(XamlDirectivePrefix.Length);
        }

        token = token.Trim();
        if (token.Length == 0)
        {
            return string.Empty;
        }

        return token.EndsWith(ExtensionSuffix, StringComparison.OrdinalIgnoreCase)
            ? token
            : token + ExtensionSuffix;
    }

    private static bool TryNormalizeToken(string? extensionName, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(extensionName))
        {
            return false;
        }

        var token = extensionName.Trim();
        if (token.StartsWith(XamlDirectivePrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(XamlDirectivePrefix.Length);
        }

        if (token.EndsWith(ExtensionSuffix, StringComparison.OrdinalIgnoreCase))
        {
            token = token.Substring(0, token.Length - ExtensionSuffix.Length);
        }

        token = token.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        normalized = token.ToLowerInvariant();
        return true;
    }
}
