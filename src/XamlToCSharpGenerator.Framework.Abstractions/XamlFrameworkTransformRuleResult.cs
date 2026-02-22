using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public sealed record XamlFrameworkTransformRuleResult(
    string FilePath,
    XamlTransformConfiguration Configuration,
    ImmutableArray<DiagnosticInfo> Diagnostics);
