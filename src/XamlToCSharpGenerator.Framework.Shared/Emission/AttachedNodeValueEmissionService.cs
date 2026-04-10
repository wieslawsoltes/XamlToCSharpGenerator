using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class AttachedNodeValueEmissionService
{
    public string BuildAttachedNodeValueExpression(
        ResolvedObjectNode node,
        string nodeReference,
        string serviceProviderReference,
        string rootReference,
        string intermediateRootReference,
        string baseUriExpression,
        string parentStackExpression)
    {
        if (!ShouldProvideValueForAttachedMarkupExtension(node))
        {
            return nodeReference;
        }

        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideMarkupExtension((" +
               node.TypeName +
               ")(" +
               nodeReference +
               "), " +
               serviceProviderReference +
               ", " +
               rootReference +
               ", " +
               intermediateRootReference +
               ", " +
               intermediateRootReference +
               ", null, " +
               baseUriExpression +
               ", " +
               parentStackExpression +
               ")";
    }

    public bool ShouldProvideValueForAttachedMarkupExtension(ResolvedObjectNode node)
    {
        if (node.IsBindingObjectNode)
        {
            return false;
        }

        return node.HasSemantic(ResolvedObjectNodeSemanticFlags.StaticResourceMarkupExtension) ||
               node.HasSemantic(ResolvedObjectNodeSemanticFlags.MarkupExtensionObject);
    }
}
