using System;
using System.Collections.Generic;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class FrameworkValueOperationEmissionService
{
    private readonly IXamlFrameworkValueOperationEmitterAdapter _adapter;
    private readonly Func<string, string> _escape;
    private readonly Func<string, string?> _extractMemberName;
    private readonly Func<string, bool> _isValidIdentifierForGeneratedMemberAccess;

    public FrameworkValueOperationEmissionService(
        IXamlFrameworkValueOperationEmitterAdapter adapter,
        Func<string, string> escape,
        Func<string, string?> extractMemberName,
        Func<string, bool> isValidIdentifierForGeneratedMemberAccess)
    {
        _adapter = adapter;
        _escape = escape;
        _extractMemberName = extractMemberName;
        _isValidIdentifierForGeneratedMemberAccess = isValidIdentifierForGeneratedMemberAccess;
    }

    public string FrameworkObjectTypeName => _adapter.FrameworkObjectTypeName;

    public bool HasFrameworkPropertyOperation(ResolvedPropertyAssignment assignment)
    {
        return assignment.HasFrameworkPropertyOperation(_adapter.FrameworkId);
    }

    public bool HasFrameworkPropertyOperation(ResolvedPropertyElementAssignment assignment)
    {
        return assignment.HasFrameworkPropertyOperation(_adapter.FrameworkId);
    }

    public bool HasFrameworkPropertyOperation(ResolvedSetterDefinition setter)
    {
        return setter.HasFrameworkPropertyOperation(_adapter.FrameworkId);
    }

    public string? BuildFrameworkPropertyExpression(ResolvedPropertyAssignment assignment)
    {
        return _adapter.BuildFrameworkPropertyExpression(
            assignment.GetFrameworkPropertyOperation(_adapter.FrameworkId));
    }

    public string? BuildFrameworkPropertyExpression(ResolvedPropertyElementAssignment assignment)
    {
        return _adapter.BuildFrameworkPropertyExpression(
            assignment.GetFrameworkPropertyOperation(_adapter.FrameworkId));
    }

    public string? BuildFrameworkPropertyExpression(ResolvedSetterDefinition setter)
    {
        return _adapter.BuildFrameworkPropertyExpression(
            setter.GetFrameworkPropertyOperation(_adapter.FrameworkId));
    }

    public string BuildMarkupContextTargetPropertyExpression(string? frameworkPropertyExpression)
    {
        return string.IsNullOrWhiteSpace(frameworkPropertyExpression)
            ? "null"
            : frameworkPropertyExpression!;
    }

    public string BuildBindingMetadataAttachmentExpression(
        string valueExpression,
        string? nameScopeReference,
        string? xmlNamespacesReference)
    {
        return _adapter.BuildBindingMetadataAttachmentExpression(
            valueExpression,
            nameScopeReference,
            xmlNamespacesReference);
    }

    public string? BuildFrameworkBindingAssignmentStatement(
        string targetObjectReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment,
        string bindingAnchorExpression)
    {
        var propertyExpression = BuildFrameworkPropertyExpression(assignment);
        if (propertyExpression is null)
        {
            return null;
        }

        return _adapter.BuildFrameworkBindingAssignmentStatement(
            targetObjectReference,
            propertyExpression,
            valueExpression,
            bindingAnchorExpression);
    }

    public string? BuildFrameworkSetValueStatement(
        string targetObjectReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment)
    {
        var operation = assignment.GetFrameworkPropertyOperation(_adapter.FrameworkId);
        var propertyExpression = _adapter.BuildFrameworkPropertyExpression(operation);
        if (propertyExpression is null)
        {
            return null;
        }

        return _adapter.BuildFrameworkSetValueStatement(
            targetObjectReference,
            propertyExpression,
            valueExpression,
            operation?.ValuePriorityExpression);
    }

    public bool TryGetClrHotReloadMemberName(
        ResolvedPropertyAssignment assignment,
        IReadOnlyDictionary<string, string> namedFieldMap,
        out string memberName)
    {
        memberName = string.Empty;
        if (string.IsNullOrWhiteSpace(assignment.PropertyName))
        {
            return false;
        }

        if (namedFieldMap.TryGetValue(assignment.PropertyName, out var namedField))
        {
            memberName = namedField;
            return true;
        }

        return false;
    }

    public bool TryBuildFrameworkHotReloadCleanup(
        ResolvedPropertyAssignment assignment,
        out FrameworkHotReloadPropertyCleanupPlan cleanupPlan)
    {
        var operation = assignment.GetFrameworkPropertyOperation(_adapter.FrameworkId);
        if (operation is null ||
            string.IsNullOrWhiteSpace(operation.PropertyOwnerTypeName) ||
            string.IsNullOrWhiteSpace(operation.PropertyFieldName))
        {
            cleanupPlan = null!;
            return false;
        }

        cleanupPlan = new FrameworkHotReloadPropertyCleanupPlan(
            operation.PropertyOwnerTypeName!,
            operation.PropertyFieldName!,
            operation.ValuePriorityExpression);
        return true;
    }
}
