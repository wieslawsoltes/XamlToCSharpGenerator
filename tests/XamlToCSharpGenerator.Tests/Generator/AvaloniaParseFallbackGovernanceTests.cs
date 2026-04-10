using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaParseFallbackGovernanceTests
{
    private static readonly Regex ExplicitGlobalParseRegex = new(
        @"global::(?<type>[A-Za-z0-9_.]+)\.Parse\(",
        RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedExplicitParseTypes = new(StringComparer.Ordinal)
    {
        "Avalonia.Media.Brush",
        "Avalonia.Media.Transformation.TransformOperations",
        "Avalonia.Media.FontFeature"
    };

    [Fact]
    public void Binder_Explicit_Parse_Emission_Is_Constrained_To_Allowlist()
    {
        var source = ReadMarkupTypeConversionSource();
        var explicitParseTypes = CollectExplicitGlobalParseTypes(source);

        var unexpected = explicitParseTypes
            .Where(static typeName => !AllowedExplicitParseTypes.Contains(typeName))
            .OrderBy(static typeName => typeName, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Unexpected explicit Parse emission types in binder: " + string.Join(", ", unexpected));
    }

    [Fact]
    public void Binder_Preserves_Generic_Static_Parse_Fallback_Hook()
    {
        var typedLiteralConversionSource = ReadTypedLiteralConversionSource();
        var markupTypeConversionSource = ReadMarkupTypeConversionSource();

        Assert.Contains("_tryConvertByStaticParseMethod(type, rawValue, out var parseExpression)", typedLiteralConversionSource, StringComparison.Ordinal);
        Assert.Contains("public bool TryConvertByStaticParseMethod(", markupTypeConversionSource, StringComparison.Ordinal);
    }

    private static HashSet<string> CollectExplicitGlobalParseTypes(string source)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in ExplicitGlobalParseRegex.Matches(source))
        {
            if (!match.Success)
            {
                continue;
            }

            var typeGroup = match.Groups["type"];
            if (!typeGroup.Success || string.IsNullOrWhiteSpace(typeGroup.Value))
            {
                continue;
            }

            result.Add(typeGroup.Value);
        }

        return result;
    }

    private static string ReadTypedLiteralConversionSource()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "src", "XamlToCSharpGenerator.Framework.Shared", "Binding", "TypedLiteralValueConversionService.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMarkupTypeConversionSource()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "src", "XamlToCSharpGenerator.Framework.Shared", "Binding", "MarkupTypeConversionService.cs");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
