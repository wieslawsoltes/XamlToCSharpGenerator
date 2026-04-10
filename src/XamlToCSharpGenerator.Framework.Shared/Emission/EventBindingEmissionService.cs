using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class EventBindingEmissionService
{
    public ImmutableArray<ResolvedEventBindingDefinition> CollectDefinitions(ResolvedObjectNode rootObject)
    {
        var results = ImmutableArray.CreateBuilder<ResolvedEventBindingDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectDefinitions(rootObject, results, seen);
        return results.ToImmutable();
    }

    public IReadOnlyDictionary<string, string> BuildStableMethodNameMap(
        ImmutableArray<ResolvedEventBindingDefinition> definitions)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < definitions.Length; index++)
        {
            var generatedMethodName = definitions[index].GeneratedMethodName;
            if (map.ContainsKey(generatedMethodName))
            {
                continue;
            }

            if (!counts.TryGetValue(generatedMethodName, out var count))
            {
                counts[generatedMethodName] = 1;
                map[generatedMethodName] = generatedMethodName;
                continue;
            }

            count++;
            counts[generatedMethodName] = count;
            map[generatedMethodName] = generatedMethodName + "_" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return map;
    }

    public string ResolveEmittedMethodName(
        ResolvedEventSubscription subscription,
        IReadOnlyDictionary<string, string> emittedMethodNames)
    {
        if (subscription.EventBindingDefinition is not null &&
            emittedMethodNames.TryGetValue(subscription.EventBindingDefinition.GeneratedMethodName, out var emittedMethodName))
        {
            return emittedMethodName;
        }

        if (emittedMethodNames.TryGetValue(subscription.HandlerMethodName, out var handlerMethodName))
        {
            return handlerMethodName;
        }

        return subscription.HandlerMethodName;
    }

    private static void CollectDefinitions(
        ResolvedObjectNode node,
        ImmutableArray<ResolvedEventBindingDefinition>.Builder results,
        ISet<string> seen)
    {
        for (var subscriptionIndex = 0; subscriptionIndex < node.EventSubscriptions.Length; subscriptionIndex++)
        {
            var definition = node.EventSubscriptions[subscriptionIndex].EventBindingDefinition;
            if (definition is not null && seen.Add(definition.GeneratedMethodName))
            {
                results.Add(definition);
            }
        }

        for (var assignmentIndex = 0; assignmentIndex < node.PropertyElementAssignments.Length; assignmentIndex++)
        {
            var propertyAssignment = node.PropertyElementAssignments[assignmentIndex];
            for (var objectIndex = 0; objectIndex < propertyAssignment.ObjectValues.Length; objectIndex++)
            {
                CollectDefinitions(propertyAssignment.ObjectValues[objectIndex], results, seen);
            }
        }

        for (var childIndex = 0; childIndex < node.Children.Length; childIndex++)
        {
            CollectDefinitions(node.Children[childIndex], results, seen);
        }
    }
}
