using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Compiler;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class CompilerHostTransformConfigurationTests
{
    [Fact]
    public void ParseConfigurationTransformRuleInputs_Orders_By_Key_OrdinalIgnoreCase()
    {
        var provider = new RecordingTransformProvider();
        var rawDocuments = ImmutableDictionary<string, string>.Empty
            .Add("zeta", "{ }")
            .Add("Alpha", "{ }")
            .Add("beta", "{ }");

        var results = XamlSourceGeneratorCompilerHost.ParseConfigurationTransformRuleInputs(rawDocuments, provider);

        Assert.Collection(
            results,
            item => Assert.Equal("xaml-sourcegen.config.json::transform.rawTransformDocuments[Alpha]", item.FilePath),
            item => Assert.Equal("xaml-sourcegen.config.json::transform.rawTransformDocuments[beta]", item.FilePath),
            item => Assert.Equal("xaml-sourcegen.config.json::transform.rawTransformDocuments[zeta]", item.FilePath));
    }

    [Fact]
    public void MergeTransformConfigurations_UnifiedConfigurationOverridesLegacy_And_Sorts_Output()
    {
        var baseConfiguration = new XamlTransformConfiguration(
            ImmutableArray.Create(
                new XamlTypeAliasRule("urn:test", "Zeta", "Legacy.Zeta", "legacy.rules", 1, 1),
                new XamlTypeAliasRule("urn:test", "Alpha", "Legacy.Alpha", "legacy.rules", 2, 1)),
            ImmutableArray.Create(
                new XamlPropertyAliasRule("ZetaControl", "Caption", "LegacyCaption", "legacy.rules", 3, 1),
                new XamlPropertyAliasRule("AlphaControl", "Title", "LegacyTitle", "legacy.rules", 4, 1)));

        var overlayConfiguration = new XamlTransformConfiguration(
            ImmutableArray.Create(
                new XamlTypeAliasRule("urn:test", "Alpha", "Unified.Alpha", "config.json", 10, 1),
                new XamlTypeAliasRule("urn:test", "Beta", "Unified.Beta", "config.json", 11, 1)),
            ImmutableArray.Create(
                new XamlPropertyAliasRule("AlphaControl", "Title", "UnifiedTitle", "config.json", 12, 1),
                new XamlPropertyAliasRule("BetaControl", "Header", "UnifiedHeader", "config.json", 13, 1)));

        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();

        var merged = XamlSourceGeneratorCompilerHost.MergeTransformConfigurations(
            baseConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.LegacyRuleFiles,
            overlayConfiguration,
            XamlSourceGeneratorCompilerHost.TransformConfigurationSourceKind.UnifiedConfigurationTypedObject,
            diagnostics);

        Assert.Collection(
            merged.TypeAliases,
            alias =>
            {
                Assert.Equal("Alpha", alias.XamlTypeName);
                Assert.Equal("Unified.Alpha", alias.ClrTypeName);
            },
            alias =>
            {
                Assert.Equal("Beta", alias.XamlTypeName);
                Assert.Equal("Unified.Beta", alias.ClrTypeName);
            },
            alias =>
            {
                Assert.Equal("Zeta", alias.XamlTypeName);
                Assert.Equal("Legacy.Zeta", alias.ClrTypeName);
            });

        Assert.Collection(
            merged.PropertyAliases,
            alias =>
            {
                Assert.Equal("AlphaControl", alias.TargetTypeName);
                Assert.Equal("Title", alias.XamlPropertyName);
                Assert.Equal("UnifiedTitle", alias.ClrPropertyName);
            },
            alias =>
            {
                Assert.Equal("BetaControl", alias.TargetTypeName);
                Assert.Equal("Header", alias.XamlPropertyName);
                Assert.Equal("UnifiedHeader", alias.ClrPropertyName);
            },
            alias =>
            {
                Assert.Equal("ZetaControl", alias.TargetTypeName);
                Assert.Equal("Caption", alias.XamlPropertyName);
                Assert.Equal("LegacyCaption", alias.ClrPropertyName);
            });

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("AXSG0903", diagnostic.Id));
    }

    private sealed class RecordingTransformProvider : IXamlFrameworkTransformProvider
    {
        public XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input)
        {
            return new XamlFrameworkTransformRuleResult(
                input.FilePath,
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }

        public XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
            ImmutableArray<XamlFrameworkTransformRuleResult> files)
        {
            return new XamlFrameworkTransformRuleAggregateResult(
                XamlTransformConfiguration.Empty,
                ImmutableArray<DiagnosticInfo>.Empty);
        }
    }
}
