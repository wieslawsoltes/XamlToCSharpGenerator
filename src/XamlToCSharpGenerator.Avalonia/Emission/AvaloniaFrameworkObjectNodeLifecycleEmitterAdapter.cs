using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter : IXamlFrameworkObjectNodeLifecycleEmitterAdapter
{
    public static AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkObjectNodeLifecycleEmitterAdapter()
    {
    }

    public string BuildAttachNameScopeStatement(string nodeReference, string nameScopeReference, int scopedIndex)
    {
        return "if ((object)" + nodeReference + " is global::Avalonia.StyledElement __scopedStyledElement" + scopedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") __AXSGObjectGraph.TrySetNameScope(__scopedStyledElement" + scopedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + nameScopeReference + ");";
    }

    public string BuildAssignObjectNameStatement(string nodeReference, string objectName)
    {
        return "if ((object)" + nodeReference + " is global::Avalonia.StyledElement) ((global::Avalonia.StyledElement)(object)" + nodeReference + ").Name = \"" + Escape(objectName) + "\";";
    }

    public string BuildRegisterNameScopeEntryStatement(string nameScopeReference, string objectName, string nodeReference)
    {
        return nameScopeReference + ".Register(\"" + Escape(objectName) + "\", " + nodeReference + ");";
    }

    public string BuildBeginInitStatement(string nodeReference)
    {
        return "__AXSGObjectGraph.BeginInit(" + nodeReference + ");";
    }

    public string BuildEndInitStatement(string nodeReference)
    {
        return "__AXSGObjectGraph.EndInit(" + nodeReference + ");";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
