using System;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Configuration;

public interface IXamlSourceGenConfigurationSource
{
    string Name { get; }

    int Precedence { get; }

    XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context);
}

public sealed record XamlSourceGenConfigurationSourceContext
{
    public static XamlSourceGenConfigurationSourceContext Empty { get; } = new();

    public string? ProjectDirectory { get; init; }

    public string? AssemblyName { get; init; }

    public ImmutableDictionary<string, string> Properties { get; init; } =
        XamlSourceGenConfigurationCollections.EmptyStringMap;
}

public sealed record XamlSourceGenConfigurationSourceResult
{
    public static XamlSourceGenConfigurationSourceResult Empty { get; } = new();

    public XamlSourceGenConfigurationPatch Patch { get; init; } = XamlSourceGenConfigurationPatch.Empty;

    public ImmutableArray<XamlSourceGenConfigurationIssue> Issues { get; init; } =
        ImmutableArray<XamlSourceGenConfigurationIssue>.Empty;

    public static XamlSourceGenConfigurationSourceResult FromPatch(XamlSourceGenConfigurationPatch patch)
    {
        if (patch is null)
        {
            throw new ArgumentNullException(nameof(patch));
        }

        return new XamlSourceGenConfigurationSourceResult
        {
            Patch = patch
        };
    }
}

public sealed record XamlSourceGenConfigurationSourceSnapshot(
    string Name,
    int Precedence,
    XamlSourceGenConfigurationPatch Patch,
    ImmutableArray<XamlSourceGenConfigurationIssue> Issues);
