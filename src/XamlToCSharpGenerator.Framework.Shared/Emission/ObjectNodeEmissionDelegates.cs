using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public delegate string EmitObjectNodeFromSharedContextDelegate(
    ResolvedObjectNode node,
    FrameworkObjectGraphEmissionContext context,
    ref int nodeCounter,
    string? existingVariableName,
    string? topDownAttachmentTemplate,
    bool completeNameScopeOnNodeCompletion);

public delegate string BuildAttachedNodeValueExpressionFromContextDelegate(
    ResolvedObjectNode node,
    string nodeReference,
    FrameworkObjectGraphEmissionContext context,
    string parentStackExpression);
