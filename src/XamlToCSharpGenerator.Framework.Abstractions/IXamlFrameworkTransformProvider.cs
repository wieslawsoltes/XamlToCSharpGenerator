using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkTransformProvider
{
    XamlFrameworkTransformRuleResult ParseTransformRule(XamlFrameworkTransformRuleInput input);

    XamlFrameworkTransformRuleAggregateResult MergeTransformRules(
        ImmutableArray<XamlFrameworkTransformRuleResult> files);
}
