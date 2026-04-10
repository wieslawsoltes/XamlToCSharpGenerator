using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class HotReloadRuntimeEmissionService
{
    private readonly FrameworkValueOperationEmissionService _valueOperationEmissionService;
    private readonly EventBindingEmissionService _eventBindingEmissionService;
    private readonly IXamlFrameworkHotReloadEmitterAdapter _adapter;
    private readonly Func<string, string> _escape;

    public HotReloadRuntimeEmissionService(
        FrameworkValueOperationEmissionService valueOperationEmissionService,
        EventBindingEmissionService eventBindingEmissionService,
        IXamlFrameworkHotReloadEmitterAdapter adapter,
        Func<string, string> escape)
    {
        _valueOperationEmissionService = valueOperationEmissionService;
        _eventBindingEmissionService = eventBindingEmissionService;
        _adapter = adapter;
        _escape = escape;
    }

    public static string? ExtractMemberName(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var trimmed = expression.Trim();
        var lastDot = trimmed.LastIndexOf('.');
        if (lastDot >= 0 && lastDot + 1 < trimmed.Length)
        {
            trimmed = trimmed.Substring(lastDot + 1);
        }

        return IsValidIdentifierForGeneratedMemberAccess(trimmed)
            ? trimmed
            : null;
    }

    public static bool IsValidIdentifierForGeneratedMemberAccess(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_'))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (!(char.IsLetterOrDigit(character) || character == '_'))
            {
                return false;
            }
        }

        return true;
    }

    public ImmutableArray<string> BuildRootHotReloadCollectionMembers(ResolvedObjectNode rootObject)
    {
        if (rootObject.PropertyElementAssignments.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        var members = ImmutableArray.CreateBuilder<string>(rootObject.PropertyElementAssignments.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assignment in rootObject.PropertyElementAssignments)
        {
            if (!RequiresCollectionCleanup(assignment) ||
                string.IsNullOrWhiteSpace(assignment.PropertyName) ||
                !seen.Add(assignment.PropertyName))
            {
                continue;
            }

            members.Add(assignment.PropertyName);
        }

        return members.ToImmutable();
    }

    public ImmutableArray<string> BuildRootHotReloadClrPropertyMembers(
        ResolvedObjectNode rootObject,
        IReadOnlyDictionary<string, string> namedFieldMap)
    {
        var members = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var assignment in rootObject.PropertyAssignments)
        {
            if (_valueOperationEmissionService.TryGetClrHotReloadMemberName(assignment, namedFieldMap, out var memberName) &&
                seen.Add(memberName))
            {
                members.Add(memberName);
            }
        }

        foreach (var namedField in namedFieldMap.Values)
        {
            if (string.IsNullOrWhiteSpace(namedField) ||
                !seen.Add(namedField))
            {
                continue;
            }

            members.Add(namedField);
        }

        return members.ToImmutable();
    }

    public ImmutableArray<FrameworkHotReloadPropertyCleanupPlan> BuildRootHotReloadFrameworkPropertyCleanupPlans(ResolvedObjectNode rootObject)
    {
        var plans = ImmutableArray.CreateBuilder<FrameworkHotReloadPropertyCleanupPlan>();
        foreach (var assignment in rootObject.PropertyAssignments)
        {
            if (_valueOperationEmissionService.TryBuildFrameworkHotReloadCleanup(assignment, out var cleanupPlan))
            {
                plans.Add(cleanupPlan);
            }
        }

        return plans.ToImmutable();
    }

    public ImmutableArray<ResolvedEventSubscription> BuildRootHotReloadEventSubscriptions(ResolvedObjectNode rootObject)
    {
        return rootObject.EventSubscriptions;
    }

    public bool ShouldClearRootSelfCollection(ResolvedObjectNode rootObject)
    {
        return rootObject.ChildAttachmentMode is
            ResolvedChildAttachmentMode.ChildrenCollection or
            ResolvedChildAttachmentMode.ItemsCollection or
            ResolvedChildAttachmentMode.DirectAdd or
            ResolvedChildAttachmentMode.DictionaryAdd;
    }

    public string BuildCollectionCleanupDescriptorArrayExpression(
        ImmutableArray<string> collectionMembers,
        string rootTypeName)
    {
        if (collectionMembers.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        var builder = new StringBuilder();
        var hasDescriptors = false;
        foreach (var member in collectionMembers)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccess(memberName))
            {
                continue;
            }

            if (!hasDescriptors)
            {
                builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
                hasDescriptors = true;
            }
            else
            {
                builder.Append(", ");
            }

            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(_escape(memberName));
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(rootTypeName);
            builder.Append(" __typed) { global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadStateTracker.TryClearCollection(__typed.");
            builder.Append(memberName);
            builder.Append("); } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    public string BuildClrPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<string> clrPropertyMembers,
        string rootTypeName)
    {
        if (clrPropertyMembers.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        var builder = new StringBuilder();
        var hasDescriptors = false;
        foreach (var member in clrPropertyMembers)
        {
            var memberName = member.Trim();
            if (!IsValidIdentifierForGeneratedMemberAccess(memberName))
            {
                continue;
            }

            if (!hasDescriptors)
            {
                builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
                hasDescriptors = true;
            }
            else
            {
                builder.Append(", ");
            }

            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(_escape(memberName));
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(rootTypeName);
            builder.Append(" __typed) { __typed.");
            builder.Append(memberName);
            builder.Append(" = default!; } })");
        }

        if (!hasDescriptors)
        {
            return "global::System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        builder.Append(" }");
        return builder.ToString();
    }

    public string BuildFrameworkPropertyCleanupDescriptorArrayExpression(
        ImmutableArray<FrameworkHotReloadPropertyCleanupPlan> cleanupPlans)
    {
        if (cleanupPlans.IsDefaultOrEmpty)
        {
            return "System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        var builder = new StringBuilder();
        builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
        for (var index = 0; index < cleanupPlans.Length; index++)
        {
            var cleanupPlan = cleanupPlans[index];
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(_escape(cleanupPlan.OwnerTypeName + "." + cleanupPlan.FieldName));
            builder.Append("\", static __instance => { if (__instance is ");
            builder.Append(_valueOperationEmissionService.FrameworkObjectTypeName);
            builder.Append(" __frameworkObject) __frameworkObject.ClearValue(");
            builder.Append(cleanupPlan.OwnerTypeName);
            builder.Append('.');
            builder.Append(cleanupPlan.FieldName);
            builder.Append("); })");
        }

        builder.Append(" }");
        return builder.ToString();
    }

    public string BuildEventCleanupDescriptorArrayExpression(
        ImmutableArray<ResolvedEventSubscription> eventSubscriptions,
        string rootTypeName,
        IReadOnlyDictionary<string, string> emittedMethodNames)
    {
        if (eventSubscriptions.IsDefaultOrEmpty)
        {
            return "System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        var builder = new StringBuilder();
        builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
        for (var index = 0; index < eventSubscriptions.Length; index++)
        {
            var eventSubscription = eventSubscriptions[index];
            if (index > 0)
            {
                builder.Append(", ");
            }

            var emittedMethodName = _eventBindingEmissionService.ResolveEmittedMethodName(
                eventSubscription,
                emittedMethodNames);
            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(_escape(BuildHotReloadEventToken(eventSubscription)));
            builder.Append("\", static __instance => _ = (( ");
            builder.Append(rootTypeName);
            builder.Append(")__instance).");
            builder.Append(emittedMethodName);
            builder.Append(" ) )");
        }

        builder.Append(" }");
        return builder.ToString();
    }

    public void EmitClassBackedHotReloadMembers(
        StringBuilder sourceBuilder,
        FrameworkHotReloadScaffoldContext scaffoldContext)
    {
        sourceBuilder.AppendLine("        private static void __RegisterSourceGenHotReload(object __instance)");
        sourceBuilder.AppendLine("        {");
        sourceBuilder.AppendLine(
            "            global::XamlToCSharpGenerator.Runtime.XamlSourceGenHotReloadManager.Register(__instance, static __target => { }, new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadRegistrationOptions");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("                BuildUri = \"" + scaffoldContext.EscapedUri + "\",");
        sourceBuilder.AppendLine("                SourcePath = \"" + scaffoldContext.EscapedSourcePath + "\"");
        sourceBuilder.AppendLine("            });");
        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }

    public void EmitStandaloneHotReloadMembers(
        StringBuilder sourceBuilder,
        FrameworkHotReloadScaffoldContext scaffoldContext)
    {
        EmitClassBackedHotReloadMembers(sourceBuilder, scaffoldContext);
    }

    public void EmitInitializeComponentHotReloadRegistrations(
        StringBuilder sourceBuilder,
        FrameworkHotReloadScaffoldContext scaffoldContext,
        string selfExpression)
    {
        _ = scaffoldContext;
        sourceBuilder.AppendLine("            __RegisterSourceGenHotReload(" + selfExpression + ");");
        _adapter.EmitApplyInvalidationStatements(sourceBuilder, "            ", selfExpression);
    }

    private string BuildDescriptorArrayExpression(
        ImmutableArray<string> members,
        string cleanupActionExpression)
    {
        if (members.IsDefaultOrEmpty)
        {
            return "System.Array.Empty<global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor>()";
        }

        var builder = new StringBuilder();
        builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor[] { ");
        for (var index = 0; index < members.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("new global::XamlToCSharpGenerator.Runtime.SourceGenHotReloadCleanupDescriptor(\"");
            builder.Append(_escape(members[index]));
            builder.Append("\", ");
            builder.Append(cleanupActionExpression);
            builder.Append(')');
        }

        builder.Append(" }");
        return builder.ToString();
    }

    private static bool RequiresCollectionCleanup(ResolvedPropertyElementAssignment assignment)
    {
        if (assignment.IsDictionaryMerge)
        {
            return true;
        }

        if (assignment.IsCollectionAdd)
        {
            return true;
        }

        if (assignment.CollectionAddInstructions.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var index = 0; index < assignment.CollectionAddInstructions.Length; index++)
        {
            var instruction = assignment.CollectionAddInstructions[index];
            if (!string.IsNullOrWhiteSpace(instruction.ParameterTypeName))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildHotReloadEventToken(ResolvedEventSubscription eventSubscription)
    {
        var kindToken = eventSubscription.Kind == ResolvedEventSubscriptionKind.RoutedEvent ? "R" : "C";
        return kindToken + "|" +
               (eventSubscription.EventName ?? string.Empty) + "|" +
               (eventSubscription.HandlerMethodName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventOwnerTypeName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventFieldName ?? string.Empty) + "|" +
               (eventSubscription.RoutedEventHandlerTypeName ?? string.Empty);
    }
}
