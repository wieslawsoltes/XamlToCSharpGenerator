using System.Collections.Immutable;
using System.Text;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class ParentStackEmissionService
{
    public ImmutableArray<string> ExtendParentStack(
        ImmutableArray<string> parentStackReferences,
        string currentReference)
    {
        if (string.IsNullOrWhiteSpace(currentReference))
        {
            return parentStackReferences.IsDefault ? ImmutableArray<string>.Empty : parentStackReferences;
        }

        var builder = parentStackReferences.IsDefaultOrEmpty
            ? ImmutableArray.CreateBuilder<string>(1)
            : parentStackReferences.ToBuilder();
        builder.Add(currentReference);
        return builder.ToImmutable();
    }

    public string BuildParentStackExpression(ImmutableArray<string> parentStackReferences)
    {
        if (parentStackReferences.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<object>()";
        }

        var builder = new StringBuilder(24 + EstimateDelimitedListCapacity(parentStackReferences));
        builder.Append("new object[] { ");
        AppendDelimitedList(builder, parentStackReferences);
        builder.Append(" }");
        return builder.ToString();
    }

    public string BuildTypeofArgumentListExpression(ImmutableArray<string> typeNames)
    {
        if (typeNames.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(EstimateDelimitedListCapacity(typeNames, itemWrapperLength: 8));
        for (var index = 0; index < typeNames.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append("typeof(");
            builder.Append(typeNames[index]);
            builder.Append(')');
        }

        return builder.ToString();
    }

    private static int EstimateDelimitedListCapacity(ImmutableArray<string> values, int itemWrapperLength = 0)
    {
        var capacity = 0;
        for (var index = 0; index < values.Length; index++)
        {
            capacity += values[index]?.Length ?? 0;
            capacity += itemWrapperLength;
            if (index > 0)
            {
                capacity += 2;
            }
        }

        return capacity;
    }

    private static void AppendDelimitedList(StringBuilder builder, ImmutableArray<string> values)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(values[index]);
        }
    }
}
