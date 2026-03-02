using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace XamlToCSharpGenerator.Core.Configuration;

public sealed record XamlSourceGenConfigurationBuildResult(
    XamlSourceGenConfiguration Configuration,
    ImmutableArray<XamlSourceGenConfigurationSourceSnapshot> Sources,
    ImmutableArray<XamlSourceGenConfigurationIssue> Issues)
{
    public bool HasErrors =>
        Issues.Any(static issue => issue.Severity == XamlSourceGenConfigurationIssueSeverity.Error);
}

public sealed class XamlSourceGenConfigurationBuilder
{
    private readonly List<IXamlSourceGenConfigurationSource> _sources = new();
    private XamlSourceGenConfiguration _baseConfiguration = XamlSourceGenConfiguration.Default;
    private IXamlSourceGenConfigurationValidator _validator = new XamlSourceGenConfigurationValidator();

    public XamlSourceGenConfigurationBuilder SetBaseConfiguration(XamlSourceGenConfiguration baseConfiguration)
    {
        _baseConfiguration = baseConfiguration ?? throw new ArgumentNullException(nameof(baseConfiguration));
        return this;
    }

    public XamlSourceGenConfigurationBuilder SetValidator(IXamlSourceGenConfigurationValidator validator)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        return this;
    }

    public XamlSourceGenConfigurationBuilder AddSource(IXamlSourceGenConfigurationSource source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        _sources.Add(source);
        return this;
    }

    public XamlSourceGenConfigurationBuilder AddSources(IEnumerable<IXamlSourceGenConfigurationSource> sources)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        foreach (var source in sources)
        {
            AddSource(source);
        }

        return this;
    }

    public XamlSourceGenConfigurationBuildResult Build(XamlSourceGenConfigurationSourceContext? context = null)
    {
        var effectiveContext = context ?? XamlSourceGenConfigurationSourceContext.Empty;
        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();
        var snapshots = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationSourceSnapshot>();

        var orderedSources = _sources
            .Select(static (source, index) => new OrderedConfigurationSource(source, index))
            .OrderBy(static item => item.Source.Precedence)
            .ThenBy(static item => item.Order)
            .ToArray();

        var configuration = _baseConfiguration;
        foreach (var orderedSource in orderedSources)
        {
            XamlSourceGenConfigurationSourceResult sourceResult;
            try
            {
                sourceResult = orderedSource.Source.Load(effectiveContext) ?? XamlSourceGenConfigurationSourceResult.Empty;
            }
            catch (Exception ex)
            {
                var loadFailure = new XamlSourceGenConfigurationIssue(
                    "AXSG0900",
                    XamlSourceGenConfigurationIssueSeverity.Error,
                    "Configuration source '" + orderedSource.Source.Name + "' failed to load: " + ex.Message,
                    orderedSource.Source.Name);
                sourceResult = new XamlSourceGenConfigurationSourceResult
                {
                    Issues = ImmutableArray.Create(loadFailure)
                };
            }

            var patch = sourceResult.Patch ?? XamlSourceGenConfigurationPatch.Empty;
            configuration = configuration.ApplyPatch(patch);

            var normalizedSourceIssues = NormalizeSourceIssues(orderedSource.Source.Name, sourceResult.Issues);
            issues.AddRange(normalizedSourceIssues);
            snapshots.Add(new XamlSourceGenConfigurationSourceSnapshot(
                orderedSource.Source.Name,
                orderedSource.Source.Precedence,
                patch,
                normalizedSourceIssues));
        }

        var immutableSnapshots = snapshots.ToImmutable();
        issues.AddRange(_validator.Validate(configuration, immutableSnapshots));

        return new XamlSourceGenConfigurationBuildResult(
            configuration,
            immutableSnapshots,
            issues.ToImmutable());
    }

    private static ImmutableArray<XamlSourceGenConfigurationIssue> NormalizeSourceIssues(
        string sourceName,
        ImmutableArray<XamlSourceGenConfigurationIssue> sourceIssues)
    {
        if (sourceIssues.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlSourceGenConfigurationIssue>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>(sourceIssues.Length);
        foreach (var issue in sourceIssues)
        {
            if (issue.SourceName is null)
            {
                builder.Add(issue with { SourceName = sourceName });
            }
            else
            {
                builder.Add(issue);
            }
        }

        return builder.ToImmutable();
    }

    private readonly struct OrderedConfigurationSource
    {
        public OrderedConfigurationSource(IXamlSourceGenConfigurationSource source, int order)
        {
            Source = source;
            Order = order;
        }

        public IXamlSourceGenConfigurationSource Source { get; }

        public int Order { get; }
    }
}
