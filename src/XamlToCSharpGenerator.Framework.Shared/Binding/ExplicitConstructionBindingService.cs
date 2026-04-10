using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ExplicitConstructionBindingService
{
    private readonly ConditionalXamlEvaluationService _conditionalXamlEvaluationService;

    public ExplicitConstructionBindingService(ConditionalXamlEvaluationService conditionalXamlEvaluationService)
    {
        _conditionalXamlEvaluationService = conditionalXamlEvaluationService;
    }

    public bool TryBuildInlineResolvedObjectExpression(
        ResolvedObjectNode node,
        out string expression)
    {
        _ = _conditionalXamlEvaluationService;
        expression = string.Empty;

        if (!string.IsNullOrWhiteSpace(node.FactoryExpression))
        {
            expression = node.FactoryExpression!;
            return true;
        }

        if (node.UseTopDownInitialization ||
            node.PropertyAssignments.Length > 0 ||
            node.PropertyElementAssignments.Length > 0 ||
            node.EventSubscriptions.Length > 0 ||
            node.Children.Length > 0 ||
            node.ChildAddInstructions.Length > 0)
        {
            return false;
        }

        expression = "new " + node.TypeName + "()";
        return true;
    }
}
