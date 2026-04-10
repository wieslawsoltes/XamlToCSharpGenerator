using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Shared.Binding;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ClrObjectNodeEmissionService
{
    private readonly Func<string, string> _escape;
    private readonly Func<ResolvedPropertyAssignment, bool> _hasFrameworkPropertyOperation;
    private readonly MarkupContextTokenSet _markupContextTokens;

    public ClrObjectNodeEmissionService(
        Func<string, string> escape,
        Func<ResolvedPropertyAssignment, bool> hasFrameworkPropertyOperation,
        MarkupContextTokenSet markupContextTokens)
    {
        _escape = escape;
        _hasFrameworkPropertyOperation = hasFrameworkPropertyOperation;
        _markupContextTokens = markupContextTokens;
    }

    public string BuildObjectCreationExpression(
        ResolvedObjectNode node,
        string serviceProviderReference,
        string baseUriExpression,
        string? rootReference = null,
        string? intermediateRootReference = null,
        string? parentStackExpression = null)
    {
        if (!string.IsNullOrWhiteSpace(node.FactoryExpression))
        {
            return ExpandMarkupContextExpression(
                node.FactoryExpression!,
                serviceProviderReference,
                rootReference ?? "null",
                intermediateRootReference ?? "null",
                intermediateRootReference ?? "null",
                "null",
                baseUriExpression,
                parentStackExpression ?? "null");
        }

        string constructorExpression;
        if (node.UseServiceProviderConstructor)
        {
            constructorExpression = "new " +
                                    node.TypeName +
                                    "(global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CreateObjectConstructionServiceProvider(" +
                                    serviceProviderReference + ", " +
                                    (rootReference ?? "null") + ", " +
                                    (intermediateRootReference ?? "null") + ", " +
                                    baseUriExpression + ", " +
                                    (parentStackExpression ?? "null") +
                                    "))";
        }
        else if (node.HasSemantic(ResolvedObjectNodeSemanticFlags.RequiresBaseUriConstructor))
        {
            constructorExpression = "new " + node.TypeName + "(" + baseUriExpression + ")";
        }
        else
        {
            constructorExpression = "new " + node.TypeName + "()";
        }

        return AppendObjectInitializer(node, constructorExpression);
    }

    public string ExpandMarkupContextExpression(
        string expression,
        string serviceProviderReference,
        string rootReference,
        string intermediateRootReference,
        string targetObjectReference,
        string? targetPropertyExpression,
        string baseUriExpression,
        string parentStackExpression)
    {
        var expanded = expression;
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.ServiceProviderToken, serviceProviderReference);
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.RootObjectToken, rootReference);
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.IntermediateRootObjectToken, intermediateRootReference);
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.TargetObjectToken, targetObjectReference);
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.TargetPropertyToken, targetPropertyExpression ?? "null");
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.BaseUriToken, baseUriExpression);
        expanded = ReplaceOrdinal(expanded, _markupContextTokens.ParentStackToken, parentStackExpression);
        return expanded;
    }

    public bool CanEmitInClrObjectInitializer(ResolvedPropertyAssignment assignment)
    {
        return assignment.RequiresObjectInitializer &&
               !_hasFrameworkPropertyOperation(assignment) &&
               assignment.ValueKind != ResolvedValueKind.Binding &&
               assignment.ValueKind != ResolvedValueKind.TemplateBinding &&
               assignment.ValueKind != ResolvedValueKind.DynamicResourceBinding;
    }

    public bool CanEmitResolvedValueInClrObjectInitializer(ResolvedPropertyAssignment assignment)
    {
        return CanEmitInClrObjectInitializer(assignment) &&
               !RequiresMarkupRuntimeContext(assignment);
    }

    public bool RequiresPlaceholderClrObjectInitializerValue(ResolvedPropertyAssignment assignment)
    {
        return assignment.IsRequiredClrProperty &&
               CanEmitInClrObjectInitializer(assignment) &&
               !CanEmitResolvedValueInClrObjectInitializer(assignment);
    }

    public bool TryBuildInitOnlyClrSetterAccessorInvocation(
        string nodeReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment,
        out string statement)
    {
        if (!assignment.IsInitOnlyClrProperty ||
            string.IsNullOrWhiteSpace(assignment.ClrSetterUnsafeAccessorMethodName))
        {
            statement = string.Empty;
            return false;
        }

        statement = assignment.ClrSetterUnsafeAccessorMethodName + "(" + nodeReference + ", " + valueExpression + ");";
        return true;
    }

    public bool TryBuildSpecialClrSetterInvocation(
        string nodeReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment,
        out string statement)
    {
        if (TryBuildInitOnlyClrSetterAccessorInvocation(nodeReference, valueExpression, assignment, out statement))
        {
            return true;
        }

        if (TryBuildAttachedClrSetterInvocation(nodeReference, valueExpression, assignment, out statement))
        {
            return true;
        }

        statement = string.Empty;
        return false;
    }

    public string BuildRootInitializerGuardedStatement(
        string nodeReference,
        string propertyName,
        string valueExpression)
    {
        return nodeReference + "." + propertyName + " = " + valueExpression + ";";
    }

    public bool TryBuildDirectClrPropertyAssignment(
        string nodeReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment,
        out string statement)
    {
        if (_hasFrameworkPropertyOperation(assignment))
        {
            statement = string.Empty;
            return false;
        }

        if (TryBuildSpecialClrSetterInvocation(nodeReference, valueExpression, assignment, out statement))
        {
            return true;
        }

        statement = nodeReference +
                    "." +
                    assignment.PropertyName +
                    " = " +
                    BuildClrTypedValueExpression(assignment.ClrPropertyTypeName, valueExpression) +
                    ";";
        return true;
    }

    private bool TryBuildAttachedClrSetterInvocation(
        string nodeReference,
        string valueExpression,
        ResolvedPropertyAssignment assignment,
        out string statement)
    {
        if (string.IsNullOrWhiteSpace(assignment.ClrPropertyOwnerTypeName) ||
            assignment.ClrPropertyTypeName is not null)
        {
            statement = string.Empty;
            return false;
        }

        if (assignment.PropertyName.StartsWith("SetClass:", StringComparison.Ordinal))
        {
            var className = assignment.PropertyName.Substring("SetClass:".Length);
            statement = assignment.ClrPropertyOwnerTypeName +
                        ".ApplyClassValue(" +
                        nodeReference +
                        ", \"" +
                        _escape(className) +
                        "\", " +
                        valueExpression +
                        ");";
            return true;
        }

        statement = assignment.ClrPropertyOwnerTypeName +
                    "." +
                    assignment.PropertyName +
                    "(" +
                    nodeReference +
                    ", " +
                    valueExpression +
                    ");";
        return true;
    }

    private string AppendObjectInitializer(
        ResolvedObjectNode node,
        string constructorExpression)
    {
        if (!constructorExpression.StartsWith("new ", StringComparison.Ordinal) ||
            node.PropertyAssignments.IsDefaultOrEmpty)
        {
            return constructorExpression;
        }

        var assignmentsBuilder = new StringBuilder();
        var hasAssignments = false;
        foreach (var assignment in node.PropertyAssignments)
        {
            if (_hasFrameworkPropertyOperation(assignment) ||
                string.IsNullOrWhiteSpace(assignment.PropertyName))
            {
                continue;
            }

            string? initializerValueExpression = null;
            if (CanEmitResolvedValueInClrObjectInitializer(assignment))
            {
                initializerValueExpression = BuildClrTypedValueExpression(
                    assignment.ClrPropertyTypeName,
                    assignment.ValueExpression);
            }
            else if (RequiresPlaceholderClrObjectInitializerValue(assignment))
            {
                initializerValueExpression = "default!";
            }

            if (string.IsNullOrWhiteSpace(initializerValueExpression))
            {
                continue;
            }

            if (hasAssignments)
            {
                assignmentsBuilder.Append(", ");
            }

            assignmentsBuilder.Append(assignment.PropertyName);
            assignmentsBuilder.Append(" = ");
            assignmentsBuilder.Append(initializerValueExpression);
            hasAssignments = true;
        }

        return hasAssignments
            ? constructorExpression + " { " + assignmentsBuilder + " }"
            : constructorExpression;
    }

    private static bool RequiresMarkupRuntimeContext(ResolvedPropertyAssignment assignment)
    {
        var requirements = assignment.ValueRequirements;
        return requirements.NeedsServiceProvider ||
               requirements.NeedsProvideValueTarget ||
               requirements.NeedsRootObject ||
               requirements.NeedsBaseUri ||
               requirements.NeedsParentStack;
    }

    private static string ReplaceOrdinal(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
        {
            return source;
        }

        var firstIndex = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            return source;
        }

        var builder = new System.Text.StringBuilder(source.Length);
        var copyIndex = 0;
        var matchIndex = firstIndex;
        while (matchIndex >= 0)
        {
            builder.Append(source, copyIndex, matchIndex - copyIndex);
            builder.Append(newValue);
            copyIndex = matchIndex + oldValue.Length;
            matchIndex = source.IndexOf(oldValue, copyIndex, StringComparison.Ordinal);
        }

        builder.Append(source, copyIndex, source.Length - copyIndex);
        return builder.ToString();
    }

    private static string BuildClrTypedValueExpression(string? clrPropertyTypeName, string valueExpression)
    {
        if (string.IsNullOrWhiteSpace(clrPropertyTypeName))
        {
            return valueExpression;
        }

        var normalizedTypeName = clrPropertyTypeName.Trim();
        return normalizedTypeName switch
        {
            "global::System.Object" or
            "global::System.Object?" or
            "object" or
            "object?" => valueExpression,
            _ => "(" + normalizedTypeName + ")(" + valueExpression + ")"
        };
    }
}
