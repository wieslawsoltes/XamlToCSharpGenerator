namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedCollectionAddInstruction(
    string ReceiverTypeName,
    string MethodName,
    string ParameterTypeName);
