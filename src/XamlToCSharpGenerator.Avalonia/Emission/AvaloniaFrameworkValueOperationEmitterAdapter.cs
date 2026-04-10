using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkValueOperationEmitterAdapter : IXamlFrameworkValueOperationEmitterAdapter
{
    public static AvaloniaFrameworkValueOperationEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkValueOperationEmitterAdapter()
    {
    }

    public string FrameworkId => FrameworkProfileIds.Avalonia;

    public string FrameworkObjectTypeName => "global::Avalonia.AvaloniaObject";

    public string? BuildFrameworkPropertyExpression(ResolvedFrameworkPropertyOperation? operation)
    {
        if (operation is null ||
            string.IsNullOrWhiteSpace(operation.PropertyOwnerTypeName) ||
            string.IsNullOrWhiteSpace(operation.PropertyFieldName))
        {
            return null;
        }

        return operation.PropertyOwnerTypeName + "." + operation.PropertyFieldName;
    }

    public string BuildBindingMetadataAttachmentExpression(
        string valueExpression,
        string? nameScopeReference,
        string? xmlNamespacesReference)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.AttachBindingNameScope(" +
               valueExpression + ", " +
               (string.IsNullOrWhiteSpace(nameScopeReference) ? "null" : nameScopeReference) + ", " +
               (string.IsNullOrWhiteSpace(xmlNamespacesReference) ? "null" : xmlNamespacesReference) +
               ")";
    }

    public string BuildFrameworkBindingAssignmentStatement(
        string targetObjectReference,
        string frameworkPropertyExpression,
        string valueExpression,
        string bindingAnchorExpression)
    {
        return "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ApplyBinding(" +
               targetObjectReference + ", " +
               frameworkPropertyExpression + ", " +
               valueExpression + ", " +
               bindingAnchorExpression + ");";
    }

    public string BuildFrameworkSetValueStatement(
        string targetObjectReference,
        string frameworkPropertyExpression,
        string valueExpression,
        string? priorityExpression)
    {
        if (!string.IsNullOrWhiteSpace(priorityExpression))
        {
            return targetObjectReference + ".SetValue(" +
                   frameworkPropertyExpression + ", " +
                   valueExpression + ", " +
                   priorityExpression + ");";
        }

        return targetObjectReference + ".SetValue(" +
               frameworkPropertyExpression + ", " +
               valueExpression + ");";
    }
}
