using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Avalonia.Emission;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AvaloniaCodeEmitterTests
{
    private static readonly CompiledBindingEmissionService CompiledBindingEmissionService = new();

    [Fact]
    public void RewriteCompiledBindingExpressionInvocations_Replaces_Prefix_Colliding_Tokens_Deterministically()
    {
        var emissionPlan = new CompiledBindingAccessorEmissionPlan(
            ImmutableArray<CompiledBindingAccessorEmissionMethod>.Empty,
            new Dictionary<int, string>(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["__AXSG_CompiledBindingAccessor_10_2"] = "__Accessor2",
                ["__AXSG_CompiledBindingAccessor_10_20"] = "__Accessor20"
            });

        const string source =
            "return __AXSG_CompiledBindingAccessor_10_2(source) + __AXSG_CompiledBindingAccessor_10_20(source);";
        var rewritten = CompiledBindingEmissionService.RewriteAccessorPlaceholders(source, emissionPlan);

        Assert.Contains("__Accessor2(source)", rewritten, StringComparison.Ordinal);
        Assert.Contains("__Accessor20(source)", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("__AXSG_CompiledBindingAccessor_", rewritten, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildEmissionPlan_Deduplicates_Equivalent_Compiled_Binding_Signatures()
    {
        var compiledBindings = ImmutableArray.Create(
            new ResolvedCompiledBindingDefinition(
                "global::Demo.Controls.Button",
                "Content",
                "Title",
                "global::Demo.ViewModels.MainVm",
                "string",
                "source.Title",
                false,
                1,
                1,
                "__placeholder_a"),
            new ResolvedCompiledBindingDefinition(
                "global::Demo.Controls.Button",
                "Content",
                "Title",
                "global::Demo.ViewModels.MainVm",
                "string",
                "source.Title",
                false,
                2,
                1,
                "__placeholder_b"));

        var emissionPlan = CompiledBindingEmissionService.BuildEmissionPlan(compiledBindings);

        Assert.Single(emissionPlan.Methods);
        Assert.Equal(
            emissionPlan.MethodNamesByPlaceholderToken["__placeholder_a"],
            emissionPlan.MethodNamesByPlaceholderToken["__placeholder_b"]);
    }

    [Theory]
    [InlineData("ThemeDictionaries", "\"Default\"", "global::Avalonia.Styling.ThemeVariant.Default")]
    [InlineData("ThemeDictionaries", "\"Dark\"", "global::Avalonia.Styling.ThemeVariant.Dark")]
    [InlineData("ThemeDictionaries", "\"Light\"", "global::Avalonia.Styling.ThemeVariant.Light")]
    [InlineData("Resources", "\"Dark\"", "\"Dark\"")]
    public void AvaloniaDeferredDictionaryAdapter_Normalizes_ThemeDictionary_Keys(
        string propertyName,
        string keyExpression,
        string expected)
    {
        var adapter = AvaloniaFrameworkDeferredDictionaryEmitterAdapter.Instance;

        var normalized = adapter.NormalizeDictionaryKeyExpression(propertyName, keyExpression);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void AvaloniaLifecycleAdapter_Emits_NameScope_And_Name_Assignment_Statements()
    {
        var adapter = AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter.Instance;

        var attachNameScope = adapter.BuildAttachNameScopeStatement("__node", "__scope", 4);
        var assignName = adapter.BuildAssignObjectNameStatement("__node", "Header");

        Assert.Equal(
            "if ((object)__node is global::Avalonia.StyledElement __scopedStyledElement4) __AXSGObjectGraph.TrySetNameScope(__scopedStyledElement4, __scope);",
            attachNameScope);
        Assert.Equal(
            "if ((object)__node is global::Avalonia.StyledElement) ((global::Avalonia.StyledElement)(object)__node).Name = \"Header\";",
            assignName);
        Assert.Equal("__AXSGObjectGraph.BeginInit(__node);", adapter.BuildBeginInitStatement("__node"));
        Assert.Equal("__AXSGObjectGraph.EndInit(__node);", adapter.BuildEndInitStatement("__node"));
    }

    [Fact]
    public void AvaloniaEventSubscriptionAdapter_Emits_Routed_Event_Remove_And_Add()
    {
        var adapter = AvaloniaFrameworkEventSubscriptionEmitterAdapter.Instance;

        var statements = adapter.BuildSubscriptionStatements(
            "__node",
            "__root",
            "__OnTapped_Generated",
            new ResolvedEventSubscription(
                EventName: "Tapped",
                HandlerMethodName: "OnTapped",
                Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                RoutedEventOwnerTypeName: "global::Demo.InputElement",
                RoutedEventFieldName: "TappedEvent",
                RoutedEventHandlerTypeName: "global::Demo.TappedHandler",
                Line: 3,
                Column: 4));

        Assert.Equal(2, statements.Length);
        Assert.Equal(
            "__node.RemoveHandler(global::Demo.InputElement.TappedEvent, (global::Demo.TappedHandler)__root.__OnTapped_Generated);",
            statements[0]);
        Assert.Equal(
            "__node.AddHandler(global::Demo.InputElement.TappedEvent, (global::Demo.TappedHandler)__root.__OnTapped_Generated);",
            statements[1]);
    }
}
