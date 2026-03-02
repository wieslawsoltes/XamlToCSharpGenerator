using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using XamlToCSharpGenerator.Core.Configuration;

namespace XamlToCSharpGenerator.Tests.Generator.Configuration;

public class SemanticContractMapTests
{
    [Fact]
    public void TypeContracts_Are_Deterministically_Ordered_And_Reused()
    {
        var map = new SemanticContractMap(
            mapId: "Test.Ordering",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.SystemObject,
                    ImmutableArray.Create("System.Object"),
                    IsRequired: true,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.ControlTheme,
                    ImmutableArray.Create("Demo.ControlTheme"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var first = map.TypeContracts;
        var second = map.TypeContracts;

        Assert.Equal(
            [TypeContractId.SystemObject, TypeContractId.Style, TypeContractId.ControlTheme],
            first.Select(static contract => contract.Id).ToArray());

        var firstArray = ImmutableCollectionsMarshal.AsArray(first);
        var secondArray = ImmutableCollectionsMarshal.AsArray(second);
        Assert.NotNull(firstArray);
        Assert.Same(firstArray, secondArray);
    }

    [Fact]
    public void CatalogCacheKey_Is_Stable_For_Equivalent_Maps_Regardless_Of_Input_Order()
    {
        var first = new SemanticContractMap(
            mapId: "Test.CacheKey",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.SystemObject,
                    ImmutableArray.Create("System.Object"),
                    IsRequired: true,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var second = new SemanticContractMap(
            mapId: "Test.CacheKey",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Style"),
                    IsRequired: false,
                    FeatureTag: "tests"),
                new SemanticTypeContract(
                    TypeContractId.SystemObject,
                    ImmutableArray.Create("System.Object"),
                    IsRequired: true,
                    FeatureTag: "tests")
            ]);

        Assert.Equal(first.CatalogCacheKey, second.CatalogCacheKey);
    }

    [Fact]
    public void CatalogCacheKey_Changes_When_Metadata_Fallback_Order_Changes()
    {
        var first = new SemanticContractMap(
            mapId: "Test.CacheKey.FallbackOrder",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Primary", "Demo.Fallback"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        var second = new SemanticContractMap(
            mapId: "Test.CacheKey.FallbackOrder",
            frameworkId: "Test",
            typeContracts:
            [
                new SemanticTypeContract(
                    TypeContractId.Style,
                    ImmutableArray.Create("Demo.Fallback", "Demo.Primary"),
                    IsRequired: false,
                    FeatureTag: "tests")
            ]);

        Assert.NotEqual(first.CatalogCacheKey, second.CatalogCacheKey);
    }
}
