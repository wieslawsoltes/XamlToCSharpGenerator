using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder
{
    private static bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        return FrameworkBindingProjectionService.TryBuildRuntimeBindingExpression(
            compilation,
            document,
            bindingMarkup,
            setterTargetType,
            (int)bindingPriorityScope,
            out expression);
    }
}
