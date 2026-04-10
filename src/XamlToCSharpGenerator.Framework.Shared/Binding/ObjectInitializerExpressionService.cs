using System.Collections.Generic;
using System.Text;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ObjectInitializerExpressionService
{
    public string BuildObjectCreationExpression(
        string typeName,
        string constructorExpression,
        IReadOnlyDictionary<string, string> assignments)
    {
        if (assignments is null || assignments.Count == 0)
        {
            return constructorExpression;
        }

        return constructorExpression + " { " + BuildAssignmentsText(assignments) + " }";
    }

    public string BuildAssignmentsText(IReadOnlyDictionary<string, string> assignments)
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var pair in assignments)
        {
            if (!first)
            {
                builder.Append(", ");
            }

            builder.Append(pair.Key);
            builder.Append(" = ");
            builder.Append(pair.Value);
            first = false;
        }

        return builder.ToString();
    }
}
