using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public sealed class XamlFrameworkTransformRuleAggregateResult
{
    public XamlFrameworkTransformRuleAggregateResult(
        XamlTransformConfiguration configuration,
        ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Configuration = configuration;
        Diagnostics = diagnostics;
    }

    public XamlTransformConfiguration Configuration { get; }

    public ImmutableArray<DiagnosticInfo> Diagnostics { get; }
}
