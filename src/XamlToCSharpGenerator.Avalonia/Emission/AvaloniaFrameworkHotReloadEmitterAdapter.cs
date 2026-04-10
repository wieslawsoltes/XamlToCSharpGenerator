using System.Text;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkHotReloadEmitterAdapter : IXamlFrameworkHotReloadEmitterAdapter
{
    public static AvaloniaFrameworkHotReloadEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkHotReloadEmitterAdapter()
    {
    }

    public void EmitApplyInvalidationStatements(
        StringBuilder sourceBuilder,
        string indent,
        string instanceReference)
    {
        sourceBuilder.AppendLine(
            indent +
            "if ((object)" +
            instanceReference +
            " is global::Avalonia.Visual __visual) __visual.InvalidateVisual();");
        sourceBuilder.AppendLine(
            indent +
            "if ((object)" +
            instanceReference +
            " is global::Avalonia.Layout.Layoutable __layoutable) __layoutable.InvalidateMeasure();");
    }

    public void EmitRegistrationStateTransfer(
        StringBuilder sourceBuilder,
        string indent,
        string instanceReference,
        string stateReference)
    {
        sourceBuilder.AppendLine(indent + "_ = " + stateReference + ";");
        sourceBuilder.AppendLine(
            indent +
            "if ((object)" +
            instanceReference +
            " is global::Avalonia.StyledElement __styledElement && " +
            stateReference +
            " is global::Avalonia.Controls.NameScope __nameScope) __AXSGObjectGraph.TrySetNameScope(__styledElement, __nameScope);");
    }
}
