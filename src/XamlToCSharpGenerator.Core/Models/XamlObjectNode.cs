using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record XamlObjectNode(
    string XmlNamespace,
    string XmlTypeName,
    string? Key,
    string? Name,
    string? FieldModifier,
    string? DataType,
    bool? CompileBindings,
    string? FactoryMethod,
    ImmutableArray<string> TypeArguments,
    string? ArrayItemType,
    ImmutableArray<XamlObjectNode> ConstructorArguments,
    string? TextContent,
    ImmutableArray<XamlPropertyAssignment> PropertyAssignments,
    ImmutableArray<XamlObjectNode> ChildObjects,
    ImmutableArray<XamlPropertyElement> PropertyElements,
    int Line,
    int Column,
    ConditionalXamlExpression? Condition = null);
