using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public sealed record ResolvedUnsafeAccessorDefinition(
    string MethodName,
    string UnsafeAccessorTargetName,
    string DeclaringTypeName,
    string ReturnTypeName,
    ImmutableArray<string> ParameterTypeNames);
