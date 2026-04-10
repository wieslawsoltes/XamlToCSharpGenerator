using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Emission;

namespace XamlToCSharpGenerator.Tests.Generator;

public class RecursiveObjectGraphEmissionServiceTests
{
    [Fact]
    public void EmitNode_Creates_Context_And_Recurses_Through_Body_Callback()
    {
        var sourceBuilder = new StringBuilder();
        var parentStackService = new ParentStackEmissionService();
        var rootNode = CreateNode(
            "Root",
            useTopDownInitialization: false,
            children: ImmutableArray.Create(CreateNode("Child", useTopDownInitialization: true)));
        var rootObjectCreationExpression = string.Empty;
        var childPendingAttachmentStatement = string.Empty;
        var childVariableName = string.Empty;
        var service = new RecursiveObjectGraphEmissionService(
            parentStackService,
            (
                ResolvedObjectNode node,
                string variableName,
                string? existingVariableName,
                ref int nodeCounter,
                int scopedIndex,
                string _,
                string _,
                string _,
                string? _,
                bool _,
                string? objectCreationExpression,
                bool _,
                string? pendingTopDownAttachmentStatement,
                FrameworkObjectGraphEmissionContext emissionContext,
                EmitObjectNodeFromSharedContextDelegate emitNode,
                BuildAttachedNodeValueExpressionFromContextDelegate _) =>
            {
                Assert.Null(existingVariableName);
                Assert.Equal("    ", emissionContext.Indent);
                if (node.TypeName == "Root")
                {
                    Assert.Equal("__n0", variableName);
                    Assert.Equal(1, scopedIndex);
                    Assert.Collection(
                        emissionContext.ParentStackReferences,
                        item => Assert.Equal("__n0", item));
                    rootObjectCreationExpression = objectCreationExpression ?? string.Empty;
                    childVariableName = emitNode(
                        node.Children[0],
                        emissionContext,
                        ref nodeCounter,
                        existingVariableName: null,
                        topDownAttachmentTemplate: "Attach(__AXSG_VALUE__)",
                        completeNameScopeOnNodeCompletion: false);
                    return;
                }

                Assert.Equal("Child", node.TypeName);
                Assert.Equal("__n1", variableName);
                Assert.Equal(2, scopedIndex);
                Assert.Collection(
                    emissionContext.ParentStackReferences,
                    first => Assert.Equal("__n0", first),
                    second => Assert.Equal("__n1", second));
                Assert.Equal("create(Child,new object[] { __n0 })", objectCreationExpression);
                childPendingAttachmentStatement = pendingTopDownAttachmentStatement ?? string.Empty;
            },
            static (
                ResolvedObjectNode node,
                string _,
                string _,
                string? _,
                string? _,
                string? parentStackExpression) =>
                "create(" + node.TypeName + "," + parentStackExpression + ")",
            static (
                ResolvedObjectNode _,
                string nodeReference,
                string _,
                string _,
                string _,
                string _,
                string parentStackExpression) =>
                "value(" + nodeReference + "," + parentStackExpression + ")");

        var nodeCounter = 0;
        var variableName = service.EmitNode(
            rootNode,
            sourceBuilder,
            ref nodeCounter,
            "    ",
            "__root",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            emitNameScopeRegistration: false,
            nameScopeReference: null,
            topDownAttachValueToken: "__AXSG_VALUE__",
            bindingXmlNamespaceMapReference: "__BindingXmlNamespaces");

        Assert.Equal("__n0", variableName);
        Assert.Equal("__n1", childVariableName);
        Assert.Equal(2, nodeCounter);
        Assert.Equal("create(Root,global::System.Array.Empty<object>())", rootObjectCreationExpression);
        Assert.Equal("Attach(value(__n1,new object[] { __n0, __n1 }))", childPendingAttachmentStatement);
    }

    [Fact]
    public void EmitNode_Reuses_Existing_Variable_Name_Without_Object_Creation()
    {
        var service = new RecursiveObjectGraphEmissionService(
            new ParentStackEmissionService(),
            static (
                ResolvedObjectNode _,
                string variableName,
                string? existingVariableName,
                ref int _,
                int scopedIndex,
                string _,
                string _,
                string _,
                string? _,
                bool _,
                string? objectCreationExpression,
                bool _,
                string? _,
                FrameworkObjectGraphEmissionContext _,
                EmitObjectNodeFromSharedContextDelegate _,
                BuildAttachedNodeValueExpressionFromContextDelegate _) =>
            {
                Assert.Equal("__existing", variableName);
                Assert.Equal("__existing", existingVariableName);
                Assert.Equal(5, scopedIndex);
                Assert.Null(objectCreationExpression);
            },
            static (_, _, _, _, _, _) => "unused",
            static (_, _, _, _, _, _, _) => "unused");

        var nodeCounter = 5;
        var variableName = service.EmitNode(
            CreateNode("Existing"),
            new StringBuilder(),
            ref nodeCounter,
            "    ",
            "__root",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            emitNameScopeRegistration: false,
            nameScopeReference: null,
            topDownAttachValueToken: "__AXSG_VALUE__",
            bindingXmlNamespaceMapReference: "__BindingXmlNamespaces",
            existingVariableName: "__existing");

        Assert.Equal("__existing", variableName);
        Assert.Equal(5, nodeCounter);
    }

    private static ResolvedObjectNode CreateNode(
        string typeName,
        bool useTopDownInitialization = false,
        ImmutableArray<ResolvedObjectNode> children = default)
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: typeName,
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: useTopDownInitialization,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: children.IsDefault ? ImmutableArray<ResolvedObjectNode>.Empty : children,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: 1,
            Column: 1);
    }
}
