using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal sealed class PreviewExpressionAnalysisContext
{
    private static readonly ConcurrentDictionary<Assembly, PreviewExpressionAnalysisContext> Cache = new();

    private readonly CSharpCompilation _compilation;

    private PreviewExpressionAnalysisContext(Assembly localAssembly)
    {
        var references = new List<MetadataReference>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            var assembly = loadedAssemblies[index];
            if (assembly.IsDynamic ||
                string.IsNullOrWhiteSpace(assembly.Location) ||
                !File.Exists(assembly.Location) ||
                !seenPaths.Add(assembly.Location))
            {
                continue;
            }

            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        if (!string.IsNullOrWhiteSpace(localAssembly.Location) &&
            File.Exists(localAssembly.Location) &&
            seenPaths.Add(localAssembly.Location))
        {
            references.Add(MetadataReference.CreateFromFile(localAssembly.Location));
        }

        _compilation = CSharpCompilation.Create(
            assemblyName: "__AXSG_PreviewExpressionAnalysis",
            syntaxTrees: [CSharpSyntaxTree.ParseText("internal static class __AXSGPreviewExpressionAnalysisRoot { }")],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    public static PreviewExpressionAnalysisContext ForAssembly(Assembly localAssembly)
    {
        ArgumentNullException.ThrowIfNull(localAssembly);
        return Cache.GetOrAdd(localAssembly, static assembly => new PreviewExpressionAnalysisContext(assembly));
    }

    public bool TryRewriteSourceContextExpression(
        Type sourceType,
        string rawExpression,
        out string rewrittenExpression,
        out IReadOnlyList<string> dependencyNames,
        out string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentException.ThrowIfNullOrEmpty(rawExpression);

        rewrittenExpression = string.Empty;
        dependencyNames = Array.Empty<string>();
        errorMessage = string.Empty;

        var sourceTypeSymbol = _compilation.GetTypeByMetadataName(sourceType.FullName ?? string.Empty);
        if (sourceTypeSymbol is not INamedTypeSymbol namedSourceType)
        {
            errorMessage = "Could not resolve preview source-context type '" + sourceType.FullName + "'.";
            return false;
        }

        if (!CSharpSourceContextExpressionAnalysisService.TryAnalyze(
                _compilation,
                namedSourceType,
                rawExpression,
                "source",
                out var result,
                out errorMessage))
        {
            return false;
        }

        rewrittenExpression = result.AccessorExpression;
        dependencyNames = result.DependencyNames;
        return true;
    }
}
