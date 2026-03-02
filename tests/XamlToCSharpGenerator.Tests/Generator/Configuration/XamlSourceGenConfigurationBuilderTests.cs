using System;
using System.Collections.Immutable;
using System.Linq;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class XamlSourceGenConfigurationBuilderTests
{
    [Fact]
    public void Build_Respects_Source_Precedence_And_Insertion_Order()
    {
        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new TestConfigurationSource("Defaults", 0, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Build = new XamlSourceGenBuildOptionsPatch
                    {
                        StrictMode = true
                    }
                })))
            .AddSource(new TestConfigurationSource("File", 100, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Build = new XamlSourceGenBuildOptionsPatch
                    {
                        Backend = "FileBackend"
                    }
                })))
            .AddSource(new TestConfigurationSource("MsBuild", 100, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Build = new XamlSourceGenBuildOptionsPatch
                    {
                        Backend = "MsBuildBackend"
                    }
                })))
            .AddSource(new TestConfigurationSource("Code", 200, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Build = new XamlSourceGenBuildOptionsPatch
                    {
                        Backend = "CodeBackend"
                    }
                })));

        var result = builder.Build();

        Assert.Equal("CodeBackend", result.Configuration.Build.Backend);
        Assert.True(result.Configuration.Build.StrictMode);
        Assert.Collection(
            result.Sources,
            snapshot => Assert.Equal("Defaults", snapshot.Name),
            snapshot => Assert.Equal("File", snapshot.Name),
            snapshot => Assert.Equal("MsBuild", snapshot.Name),
            snapshot => Assert.Equal("Code", snapshot.Name));
    }

    [Fact]
    public void Build_Merges_Dictionary_Patches_And_Supports_Key_Removal()
    {
        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new TestConfigurationSource("First", 10, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Parser = new XamlSourceGenParserOptionsPatch
                    {
                        GlobalXmlnsPrefixes = PatchMap(("x", "urn:first"), ("d", "urn:design")),
                        AdditionalProperties = PatchMap(("a", "1"))
                    }
                })))
            .AddSource(new TestConfigurationSource("Second", 20, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Parser = new XamlSourceGenParserOptionsPatch
                    {
                        GlobalXmlnsPrefixes = PatchMap(("x", "urn:second"), ("d", null)),
                        AdditionalProperties = PatchMap(("a", null), ("b", "2"))
                    }
                })));

        var result = builder.Build();

        Assert.Equal("urn:second", result.Configuration.Parser.GlobalXmlnsPrefixes["x"]);
        Assert.False(result.Configuration.Parser.GlobalXmlnsPrefixes.ContainsKey("d"));
        Assert.False(result.Configuration.Parser.AdditionalProperties.ContainsKey("a"));
        Assert.Equal("2", result.Configuration.Parser.AdditionalProperties["b"]);
    }

    [Fact]
    public void Build_Captures_Source_Load_Exceptions_As_Errors()
    {
        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new TestConfigurationSource("CrashingSource", 10, _ =>
                throw new InvalidOperationException("Boom")));

        var result = builder.Build();

        var loadError = Assert.Single(result.Issues.Where(issue => issue.Code == "AXSG0900"));
        Assert.Equal(XamlSourceGenConfigurationIssueSeverity.Error, loadError.Severity);
        Assert.Equal("CrashingSource", loadError.SourceName);
        Assert.True(result.HasErrors);
        Assert.Single(result.Sources);
        Assert.Equal("CrashingSource", result.Sources[0].Name);
    }

    [Fact]
    public void Build_Validator_Reports_Invalid_Configuration_Combinations()
    {
        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new TestConfigurationSource("InvalidConfig", 100, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    Build = new XamlSourceGenBuildOptionsPatch
                    {
                        Backend = string.Empty,
                        HotReloadEnabled = false,
                        IdeHotReloadEnabled = true
                    },
                    Parser = new XamlSourceGenParserOptionsPatch
                    {
                        AllowImplicitXmlnsDeclaration = true,
                        ImplicitDefaultXmlns = string.Empty
                    },
                    Diagnostics = new XamlSourceGenDiagnosticsOptionsPatch
                    {
                        SeverityOverrides = SeverityPatchMap(("bad-key", XamlSourceGenConfigurationIssueSeverity.Error))
                    },
                    SemanticContract = new XamlSourceGenSemanticContractOptionsPatch
                    {
                        TypeContracts = PatchMap(("StyledElement", string.Empty))
                    }
                })));

        var result = builder.Build();
        var issueCodes = result.Issues.Select(issue => issue.Code).ToImmutableHashSet(StringComparer.Ordinal);

        Assert.Contains("AXSG0901", issueCodes);
        Assert.Contains("AXSG0902", issueCodes);
        Assert.Contains("AXSG0903", issueCodes);
        Assert.Contains("AXSG0904", issueCodes);
        Assert.Contains("AXSG0906", issueCodes);
    }

    [Fact]
    public void Build_Merges_Framework_Extras_Sections()
    {
        var builder = new XamlSourceGenConfigurationBuilder()
            .AddSource(new TestConfigurationSource("First", 10, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    FrameworkExtras = new XamlSourceGenFrameworkExtrasPatch
                    {
                        Sections = SectionPatchMap((
                            "Canvas",
                            new (string Key, string? Value)[]
                            {
                                ("SelectionColor", "Blue"),
                                ("AdornerThickness", "2")
                            }))
                    }
                })))
            .AddSource(new TestConfigurationSource("Second", 20, _ =>
                XamlSourceGenConfigurationSourceResult.FromPatch(new XamlSourceGenConfigurationPatch
                {
                    FrameworkExtras = new XamlSourceGenFrameworkExtrasPatch
                    {
                        Sections = SectionPatchMap((
                            "Canvas",
                            new (string Key, string? Value)[]
                            {
                                ("AdornerThickness", null),
                                ("HandleSize", "8")
                            }))
                    }
                })));

        var result = builder.Build();
        var canvas = result.Configuration.FrameworkExtras.Sections["Canvas"];

        Assert.Equal("Blue", canvas["SelectionColor"]);
        Assert.False(canvas.ContainsKey("AdornerThickness"));
        Assert.Equal("8", canvas["HandleSize"]);
    }

    private static ImmutableDictionary<string, string?> PatchMap(params (string Key, string? Value)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            builder[pair.Key] = pair.Value;
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, XamlSourceGenConfigurationIssueSeverity?> SeverityPatchMap(
        params (string Key, XamlSourceGenConfigurationIssueSeverity? Severity)[] pairs)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, XamlSourceGenConfigurationIssueSeverity?>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            builder[pair.Key] = pair.Severity;
        }

        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableDictionary<string, string?>> SectionPatchMap(
        params (string Section, (string Key, string? Value)[] Values)[] sections)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableDictionary<string, string?>>(StringComparer.Ordinal);
        foreach (var section in sections)
        {
            builder[section.Section] = PatchMap(section.Values);
        }

        return builder.ToImmutable();
    }

    private sealed class TestConfigurationSource : IXamlSourceGenConfigurationSource
    {
        private readonly Func<XamlSourceGenConfigurationSourceContext, XamlSourceGenConfigurationSourceResult> _load;

        public TestConfigurationSource(
            string name,
            int precedence,
            Func<XamlSourceGenConfigurationSourceContext, XamlSourceGenConfigurationSourceResult> load)
        {
            Name = name;
            Precedence = precedence;
            _load = load;
        }

        public string Name { get; }

        public int Precedence { get; }

        public XamlSourceGenConfigurationSourceResult Load(XamlSourceGenConfigurationSourceContext context)
        {
            return _load(context);
        }
    }
}
