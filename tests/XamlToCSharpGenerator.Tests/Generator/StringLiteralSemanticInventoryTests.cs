using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XamlToCSharpGenerator.Tests.Generator;

public class StringLiteralSemanticInventoryTests
{
    private static readonly Regex MetadataNameLiteralRegex = new(
        "GetTypeByMetadataName\\(\\s*\"(?<metadataName>[^\"]+)\"\\s*\\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ApprovedFiles = new(StringComparer.Ordinal)
    {
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.NodeTypeResolution.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.SelectorPropertyReferences.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs",
        "src/XamlToCSharpGenerator.Avalonia/Binding/Services/NameScopeRegistrationSemanticsService.cs"
    };

    private static readonly IReadOnlyDictionary<string, int> BaselineHotspotCounts =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.BindingSemantics.cs"] = 31,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.NodeTypeResolution.cs"] = 7,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.MarkupHelpers.cs"] = 2,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.StylesTemplates.cs"] = 2,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.TransformExtensions.cs"] = 2,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/AvaloniaSemanticBinder.SelectorPropertyReferences.cs"] = 1,
            ["src/XamlToCSharpGenerator.Avalonia/Binding/Services/NameScopeRegistrationSemanticsService.cs"] = 1
        };

    [Fact]
    public void MetadataName_String_Literals_Are_Restricted_To_Approved_Semantic_Files()
    {
        var inventory = BuildInventory();

        var unexpected = inventory
            .Where(static occurrence => !ApprovedFiles.Contains(occurrence.FilePath))
            .ToArray();

        Assert.True(unexpected.Length == 0, BuildUnexpectedOccurrenceMessage(unexpected));
    }

    [Fact]
    public void MetadataName_String_Literal_Hotspot_Counts_Do_Not_Increase_From_Baseline()
    {
        var inventory = BuildInventory();
        var actualCounts = inventory
            .GroupBy(static occurrence => occurrence.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);

        var unexpectedFiles = actualCounts.Keys
            .Where(static file => !BaselineHotspotCounts.ContainsKey(file))
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();
        Assert.True(unexpectedFiles.Length == 0, BuildUnexpectedFileMessage(unexpectedFiles));

        var increasedFiles = actualCounts
            .Where(pair => pair.Value > BaselineHotspotCounts[pair.Key])
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        Assert.True(increasedFiles.Length == 0, BuildIncreasedCountMessage(increasedFiles));
    }

    [Fact]
    public void MetadataName_String_Literal_Inventory_Is_Explicitly_Classified()
    {
        var inventory = BuildInventory();
        var categoryCounts = new Dictionary<LiteralCategory, int>();
        foreach (var occurrence in inventory)
        {
            var category = Classify(occurrence.MetadataName);
            if (!categoryCounts.TryAdd(category, 1))
            {
                categoryCounts[category]++;
            }
        }

        var unknown = inventory
            .Where(static occurrence => Classify(occurrence.MetadataName) == LiteralCategory.Unknown)
            .ToArray();
        Assert.True(unknown.Length == 0, BuildUnknownCategoryMessage(unknown));

        var semanticContractCount = GetCategoryCount(categoryCounts, LiteralCategory.SemanticContractFramework) +
                                    GetCategoryCount(categoryCounts, LiteralCategory.SemanticContractBcl);
        var dataPayloadCount = GetCategoryCount(categoryCounts, LiteralCategory.DataPayload);

        Assert.Equal(inventory.Count, semanticContractCount);
        Assert.Equal(0, dataPayloadCount);

        Assert.Equal(46, inventory.Count);
        Assert.Equal(36, GetCategoryCount(categoryCounts, LiteralCategory.SemanticContractFramework));
        Assert.Equal(10, GetCategoryCount(categoryCounts, LiteralCategory.SemanticContractBcl));
    }

    private static int GetCategoryCount(
        IReadOnlyDictionary<LiteralCategory, int> categoryCounts,
        LiteralCategory category)
    {
        return categoryCounts.TryGetValue(category, out var count) ? count : 0;
    }

    private static string BuildUnexpectedOccurrenceMessage(IReadOnlyList<StringLiteralOccurrence> unexpected)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Found direct GetTypeByMetadataName(\"...\") usage outside approved files:");
        foreach (var occurrence in unexpected)
        {
            builder.Append(" - ")
                .Append(occurrence.FilePath)
                .Append(':')
                .Append(occurrence.LineNumber)
                .Append(" => ")
                .AppendLine(occurrence.MetadataName);
        }

        return builder.ToString();
    }

    private static string BuildUnexpectedFileMessage(IReadOnlyList<string> files)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Hotspot baseline is missing files:");
        foreach (var file in files)
        {
            builder.Append(" - ").AppendLine(file);
        }

        return builder.ToString();
    }

    private static string BuildIncreasedCountMessage(
        IReadOnlyList<KeyValuePair<string, int>> increasedFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("GetTypeByMetadataName(\"...\") string-literal counts increased above baseline:");
        foreach (var pair in increasedFiles)
        {
            builder.Append(" - ")
                .Append(pair.Key)
                .Append(": baseline=")
                .Append(BaselineHotspotCounts[pair.Key])
                .Append(", actual=")
                .AppendLine(pair.Value.ToString());
        }

        return builder.ToString();
    }

    private static string BuildUnknownCategoryMessage(IReadOnlyList<StringLiteralOccurrence> unknown)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Unclassified GetTypeByMetadataName(\"...\") metadata name literals:");
        foreach (var occurrence in unknown)
        {
            builder.Append(" - ")
                .Append(occurrence.FilePath)
                .Append(':')
                .Append(occurrence.LineNumber)
                .Append(" => ")
                .AppendLine(occurrence.MetadataName);
        }

        return builder.ToString();
    }

    private static LiteralCategory Classify(string metadataName)
    {
        if (metadataName.StartsWith("Avalonia.", StringComparison.Ordinal))
        {
            return LiteralCategory.SemanticContractFramework;
        }

        if (metadataName.StartsWith("System.", StringComparison.Ordinal))
        {
            return LiteralCategory.SemanticContractBcl;
        }

        return LiteralCategory.Unknown;
    }

    private static List<StringLiteralOccurrence> BuildInventory()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sourceRoot = Path.Combine(repositoryRoot, "src");
        var inventory = new List<StringLiteralOccurrence>();

        foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(filePath);
            var matches = MetadataNameLiteralRegex.Matches(text);
            if (matches.Count == 0)
            {
                continue;
            }

            var normalizedRelativePath = NormalizePath(Path.GetRelativePath(repositoryRoot, filePath));

            foreach (Match match in matches)
            {
                var metadataName = match.Groups["metadataName"].Value;
                if (string.IsNullOrWhiteSpace(metadataName))
                {
                    continue;
                }

                var lineNumber = CountLineNumber(text, match.Index);
                inventory.Add(new StringLiteralOccurrence(normalizedRelativePath, lineNumber, metadataName));
            }
        }

        inventory.Sort(static (left, right) =>
        {
            var pathComparison = string.CompareOrdinal(left.FilePath, right.FilePath);
            if (pathComparison != 0)
            {
                return pathComparison;
            }

            var lineComparison = left.LineNumber.CompareTo(right.LineNumber);
            if (lineComparison != 0)
            {
                return lineComparison;
            }

            return string.CompareOrdinal(left.MetadataName, right.MetadataName);
        });
        return inventory;
    }

    private static int CountLineNumber(string text, int charIndex)
    {
        var line = 1;
        for (var index = 0; index < charIndex && index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/');
    }

    private enum LiteralCategory
    {
        SemanticContractFramework,
        SemanticContractBcl,
        DataPayload,
        Unknown
    }

    private sealed record StringLiteralOccurrence(
        string FilePath,
        int LineNumber,
        string MetadataName);
}

