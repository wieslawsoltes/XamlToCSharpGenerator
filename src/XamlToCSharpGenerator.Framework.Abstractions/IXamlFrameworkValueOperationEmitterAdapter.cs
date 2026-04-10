using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Abstractions;

public interface IXamlFrameworkValueOperationEmitterAdapter
{
    string FrameworkId { get; }

    string FrameworkObjectTypeName { get; }

    string? BuildFrameworkPropertyExpression(ResolvedFrameworkPropertyOperation? operation);

    string BuildBindingMetadataAttachmentExpression(
        string valueExpression,
        string? nameScopeReference,
        string? xmlNamespacesReference);

    string BuildFrameworkBindingAssignmentStatement(
        string targetObjectReference,
        string frameworkPropertyExpression,
        string valueExpression,
        string bindingAnchorExpression);

    string BuildFrameworkSetValueStatement(
        string targetObjectReference,
        string frameworkPropertyExpression,
        string valueExpression,
        string? priorityExpression);
}
