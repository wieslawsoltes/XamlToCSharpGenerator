using System.Collections.Generic;

namespace XamlToCSharpGenerator.NoUi;

public sealed class NoUiObjectNode
{
    public NoUiObjectNode(string typeName)
    {
        TypeName = typeName;
        Properties = [];
        Children = [];
    }

    public string TypeName { get; }

    public List<NoUiPropertyAssignment> Properties { get; }

    public List<NoUiObjectNode> Children { get; }
}

public sealed record NoUiPropertyAssignment(string PropertyName, string Value);
