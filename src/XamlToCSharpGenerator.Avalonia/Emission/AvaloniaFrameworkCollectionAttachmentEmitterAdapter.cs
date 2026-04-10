using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkCollectionAttachmentEmitterAdapter : IXamlFrameworkCollectionAttachmentEmitterAdapter
{
    private const string AvaloniaStyleTypeName = "global::Avalonia.Styling.Style";
    private const string AvaloniaControlThemeTypeName = "global::Avalonia.Styling.ControlTheme";
    private const string AvaloniaSetterTypeName = "global::Avalonia.Styling.Setter";

    public static AvaloniaFrameworkCollectionAttachmentEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkCollectionAttachmentEmitterAdapter()
    {
    }

    public bool ShouldApplyMergedResourceInclude(ResolvedObjectNode node)
    {
        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.IsResourceInclude) &&
               string.IsNullOrWhiteSpace(node.KeyExpression);
    }

    public bool ShouldApplyStyleInclude(ResolvedObjectNode node)
    {
        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.IsStyleInclude);
    }

    public bool TryBuildSpecialChildAttachmentStatement(
        ResolvedObjectNode parentNode,
        string parentReference,
        ResolvedObjectNode childNode,
        string valueExpression,
        ResolvedCollectionAddInstruction? instruction,
        out string statement)
    {
        _ = instruction;

        if (parentNode.TypeName == AvaloniaStyleTypeName)
        {
            if (childNode.TypeName == AvaloniaSetterTypeName)
            {
                statement = parentReference + ".Setters.Add(" + valueExpression + ");";
                return true;
            }

            if (childNode.TypeName == AvaloniaStyleTypeName)
            {
                statement = parentReference + ".Children.Add(" + valueExpression + ");";
                return true;
            }
        }

        if (parentNode.TypeName == AvaloniaControlThemeTypeName &&
            childNode.TypeName == AvaloniaSetterTypeName)
        {
            statement = parentReference + ".Setters.Add(" + valueExpression + ");";
            return true;
        }

        statement = string.Empty;
        return false;
    }

    public string BuildApplyMergedResourceIncludeStatement(
        string ownerDictionaryReference,
        string includeValueExpression,
        string documentUriExpression)
    {
        return "__AXSGObjectGraph.TryApplyMergedResourceInclude(" +
               ownerDictionaryReference + ", " +
               includeValueExpression + ", " +
               documentUriExpression + ");";
    }

    public string BuildApplyStyleIncludeStatement(
        string targetCollectionReference,
        string ownerContextReference,
        string includeValueExpression,
        string documentUriExpression)
    {
        return "__AXSGObjectGraph.TryApplyStyleInclude(" +
               targetCollectionReference + ", " +
               ownerContextReference + ", " +
               includeValueExpression + ", " +
               documentUriExpression + ");";
    }

    public string BuildCollectionAddStatement(
        string collectionReference,
        string valueExpression,
        ResolvedCollectionAddInstruction? instruction)
    {
        var methodName = instruction?.MethodName;
        if (string.IsNullOrWhiteSpace(methodName))
        {
            methodName = "Add";
        }

        if (!string.IsNullOrWhiteSpace(instruction?.ReceiverTypeName))
        {
            return "((" + instruction.ReceiverTypeName + ")" + collectionReference + ")." + methodName + "(" + valueExpression + ");";
        }

        return collectionReference + "." + methodName + "(" + valueExpression + ");";
    }
}
