using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using XamlToCSharpGenerator.Avalonia.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaCodeEmitterTests
{
    [Fact]
    public void RewriteCompiledBindingExpressionInvocations_Replaces_Prefix_Colliding_Tokens_Deterministically()
    {
        var emitterType = typeof(AvaloniaCodeEmitter);
        var compiledBindingAccessorMethodType =
            emitterType.GetNestedType("CompiledBindingAccessorMethod", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing compiled binding accessor method type.");
        var compiledBindingAccessorEmissionPlanType =
            emitterType.GetNestedType("CompiledBindingAccessorEmissionPlan", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing compiled binding accessor emission plan type.");
        var rewriteMethod =
            emitterType.GetMethod(
                "RewriteCompiledBindingExpressionInvocations",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing rewrite method.");

        var emptyMethods = typeof(ImmutableArray<>)
            .MakeGenericType(compiledBindingAccessorMethodType)
            .GetField("Empty", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null)
            ?? throw new InvalidOperationException("Missing immutable array empty value.");
        var emissionPlanConstructor = compiledBindingAccessorEmissionPlanType
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(static constructor => constructor.GetParameters().Length == 3);
        var emissionPlan = emissionPlanConstructor.Invoke(
            [
                emptyMethods,
                new Dictionary<int, string>(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["__AXSG_CompiledBindingAccessor_10_2"] = "__Accessor2",
                    ["__AXSG_CompiledBindingAccessor_10_20"] = "__Accessor20"
                }
            ]);

        const string source =
            "return __AXSG_CompiledBindingAccessor_10_2(source) + __AXSG_CompiledBindingAccessor_10_20(source);";
        var rewritten = (string?)rewriteMethod.Invoke(null, [source, emissionPlan]);

        Assert.NotNull(rewritten);
        Assert.Contains("__Accessor2(source)", rewritten!, StringComparison.Ordinal);
        Assert.Contains("__Accessor20(source)", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("__AXSG_CompiledBindingAccessor_", rewritten, StringComparison.Ordinal);
    }
}
