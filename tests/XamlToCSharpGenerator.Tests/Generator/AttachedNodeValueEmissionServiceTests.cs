using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class AttachedNodeValueEmissionServiceTests
{
    [Fact]
    public void BuildAttachedNodeValueExpression_Returns_Node_Reference_For_Non_Markup_Object()
    {
        var service = new AttachedNodeValueEmissionService();
        var node = CreateNode(ResolvedObjectNodeSemanticFlags.None);

        var expression = service.BuildAttachedNodeValueExpression(
            node,
            "__n0",
            "__sp",
            "__root",
            "__intermediate",
            "__baseUri",
            "__parents");

        Assert.Equal("__n0", expression);
    }

    [Fact]
    public void BuildAttachedNodeValueExpression_Wraps_Markup_Extension_Object()
    {
        var service = new AttachedNodeValueEmissionService();
        var node = CreateNode(ResolvedObjectNodeSemanticFlags.MarkupExtensionObject);

        var expression = service.BuildAttachedNodeValueExpression(
            node,
            "__n0",
            "__sp",
            "__root",
            "__intermediate",
            "__baseUri",
            "__parents");

        Assert.Contains("SourceGenMarkupExtensionRuntime.ProvideMarkupExtension", expression, StringComparison.Ordinal);
        Assert.Contains("(global::Demo.MarkupType)(__n0)", expression, StringComparison.Ordinal);
        Assert.Contains("__sp", expression, StringComparison.Ordinal);
        Assert.Contains("__parents", expression, StringComparison.Ordinal);
    }

    private static ResolvedObjectNode CreateNode(ResolvedObjectNodeSemanticFlags semanticFlags)
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: "global::Demo.MarkupType",
            IsBindingObjectNode: false,
            FactoryExpression: "new global::Demo.MarkupType()",
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: 1,
            Column: 1,
            SemanticFlags: semanticFlags);
    }
}
