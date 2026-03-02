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
        var source = ReadBindingSemanticsSource();
        var explicitParseTypes = CollectExplicitGlobalParseTypes(source);

        var unexpected = explicitParseTypes
            .Where(static typeName => !AllowedExplicitParseTypes.Contains(typeName))
            .OrderBy(static typeName => typeName, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "Unexpected explicit Parse emission types in binder: " + string.Join(", ", unexpected));

        foreach (var expectedType in AllowedExplicitParseTypes)
        {
            Assert.Contains(expectedType, explicitParseTypes);
        }
    }

    [Fact]
    public void Binder_Preserves_Generic_Static_Parse_Fallback_Hook()
    {
        var source = ReadBindingSemanticsSource();
        var markupHelpersSource = ReadMarkupHelpersSource();

        Assert.Contains("TryConvertByStaticParseMethod(type, value, out var parsedExpression)", source, StringComparison.Ordinal);
        Assert.Contains("private static bool TryConvertByStaticParseMethod(ITypeSymbol type, string value, out string expression)", markupHelpersSource, StringComparison.Ordinal);
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

    private static string ReadBindingSemanticsSource()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "src", "XamlToCSharpGenerator.Avalonia", "Binding", "AvaloniaSemanticBinder.BindingSemantics.cs");
        return File.ReadAllText(path);
    }

    private static string ReadMarkupHelpersSource()
    {
        var root = GetRepositoryRoot();
        var path = Path.Combine(root, "src", "XamlToCSharpGenerator.Avalonia", "Binding", "AvaloniaSemanticBinder.MarkupHelpers.cs");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }
}
