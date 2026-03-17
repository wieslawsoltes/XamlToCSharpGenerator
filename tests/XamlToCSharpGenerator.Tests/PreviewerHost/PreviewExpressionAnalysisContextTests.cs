using System.Reflection;
using XamlToCSharpGenerator.Previewer.DesignerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public sealed class PreviewExpressionAnalysisContextTests
{
    private static readonly Assembly TestAssembly = typeof(PreviewTestRoot).Assembly;

    [Fact]
    public void TryRewritePreviewExpression_DoesNotLeak_NestedLambdaParameter_Names_Into_OuterScope()
    {
        var context = PreviewExpressionAnalysisContext.ForAssembly(TestAssembly);

        var rewritten = context.TryRewritePreviewExpression(
            typeof(PreviewTestViewModel),
            rootType: null,
            targetType: null,
            "Name + new[] { 1 }.Select(Name => Name.ToString()).First()",
            out var rewrittenExpression,
            out var dependencyNames,
            out var errorMessage);

        Assert.True(rewritten, errorMessage);
        Assert.Equal(
            "source.Name + new[] { 1 }.Select(Name => Name.ToString()).First()",
            rewrittenExpression);
        Assert.Equal(["Name"], dependencyNames);
    }

    [Fact]
    public void TryRewritePreviewExpression_DoesNotRewrite_QueryRangeVariables()
    {
        var context = PreviewExpressionAnalysisContext.ForAssembly(TestAssembly);

        var rewritten = context.TryRewritePreviewExpression(
            typeof(PreviewQueryTestViewModel),
            rootType: null,
            targetType: null,
            "from Name in Items select Name",
            out var rewrittenExpression,
            out var dependencyNames,
            out var errorMessage);

        Assert.True(rewritten, errorMessage);
        Assert.Equal("from Name in source.Items select Name", rewrittenExpression);
        Assert.Equal(["Items"], dependencyNames);
    }

    [Fact]
    public void TryRewritePreviewExpression_DoesNotRewrite_JoinInto_ContinuationVariables()
    {
        var context = PreviewExpressionAnalysisContext.ForAssembly(TestAssembly);

        var rewritten = context.TryRewritePreviewExpression(
            typeof(PreviewQueryTestViewModel),
            rootType: null,
            targetType: null,
            "from Name in Items join Alias in OtherItems on Name equals Alias into Matches select Matches",
            out var rewrittenExpression,
            out var dependencyNames,
            out var errorMessage);

        Assert.True(rewritten, errorMessage);
        Assert.Equal(
            "from Name in source.Items join Alias in source.OtherItems on Name equals Alias into Matches select Matches",
            rewrittenExpression);
        Assert.Equal(["Items", "OtherItems"], dependencyNames);
    }
}

internal sealed class PreviewQueryTestViewModel
{
    public string Name { get; set; } = string.Empty;

    public IEnumerable<string> Items { get; set; } = Array.Empty<string>();

    public IEnumerable<string> OtherItems { get; set; } = Array.Empty<string>();
}
