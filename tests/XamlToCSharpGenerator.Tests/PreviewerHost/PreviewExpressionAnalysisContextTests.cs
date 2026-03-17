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
}
