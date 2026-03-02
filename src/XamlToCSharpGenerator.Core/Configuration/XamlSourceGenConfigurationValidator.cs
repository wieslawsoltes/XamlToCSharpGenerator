using System;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace XamlToCSharpGenerator.Core.Configuration;

public enum XamlSourceGenConfigurationIssueSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}

public sealed record XamlSourceGenConfigurationIssue(
    string Code,
    XamlSourceGenConfigurationIssueSeverity Severity,
    string Message,
    string? SourceName = null);

public interface IXamlSourceGenConfigurationValidator
{
    ImmutableArray<XamlSourceGenConfigurationIssue> Validate(
        XamlSourceGenConfiguration configuration,
        ImmutableArray<XamlSourceGenConfigurationSourceSnapshot> sourceSnapshots);
}

public sealed class XamlSourceGenConfigurationValidator : IXamlSourceGenConfigurationValidator
{
    private static readonly Regex DiagnosticCodeRegex =
        new("^AXSG\\d{4}$", RegexOptions.CultureInvariant);

    public ImmutableArray<XamlSourceGenConfigurationIssue> Validate(
        XamlSourceGenConfiguration configuration,
        ImmutableArray<XamlSourceGenConfigurationSourceSnapshot> sourceSnapshots)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        var issues = ImmutableArray.CreateBuilder<XamlSourceGenConfigurationIssue>();

        if (string.IsNullOrWhiteSpace(configuration.Build.Backend))
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                "AXSG0901",
                XamlSourceGenConfigurationIssueSeverity.Error,
                "Build.Backend cannot be empty."));
        }

        if (configuration.Parser.AllowImplicitXmlnsDeclaration &&
            string.IsNullOrWhiteSpace(configuration.Parser.ImplicitDefaultXmlns))
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                "AXSG0902",
                XamlSourceGenConfigurationIssueSeverity.Error,
                "Parser.ImplicitDefaultXmlns must be set when implicit xmlns declaration is enabled."));
        }

        foreach (var severityOverride in configuration.Diagnostics.SeverityOverrides)
        {
            if (!DiagnosticCodeRegex.IsMatch(severityOverride.Key))
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    "AXSG0903",
                    XamlSourceGenConfigurationIssueSeverity.Warning,
                    "Diagnostics severity override key '" + severityOverride.Key +
                    "' is not in AXSG#### format."));
            }
        }

        if (configuration.Build.IdeHotReloadEnabled && !configuration.Build.HotReloadEnabled)
        {
            issues.Add(new XamlSourceGenConfigurationIssue(
                "AXSG0904",
                XamlSourceGenConfigurationIssueSeverity.Warning,
                "IDE hot reload is enabled while build hot reload is disabled."));
        }

        ValidateContractValues("TypeContracts", configuration.SemanticContract.TypeContracts, issues);
        ValidateContractValues("PropertyContracts", configuration.SemanticContract.PropertyContracts, issues);
        ValidateContractValues("EventContracts", configuration.SemanticContract.EventContracts, issues);

        return issues.ToImmutable();
    }

    private static void ValidateContractValues(
        string sectionName,
        ImmutableDictionary<string, string> contracts,
        ImmutableArray<XamlSourceGenConfigurationIssue>.Builder issues)
    {
        foreach (var pair in contracts)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    "AXSG0905",
                    XamlSourceGenConfigurationIssueSeverity.Error,
                    "Semantic contract section '" + sectionName + "' contains an empty key."));
            }

            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                issues.Add(new XamlSourceGenConfigurationIssue(
                    "AXSG0906",
                    XamlSourceGenConfigurationIssueSeverity.Error,
                    "Semantic contract entry '" + sectionName + ":" + pair.Key + "' has an empty metadata name."));
            }
        }
    }
}
