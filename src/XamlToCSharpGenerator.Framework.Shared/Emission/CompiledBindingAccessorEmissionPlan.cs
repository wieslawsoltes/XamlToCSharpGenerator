using System.Collections.Generic;
using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed record CompiledBindingAccessorEmissionMethod(
    int CompiledBindingIndex,
    string MethodName,
    string BindingMethodName,
    string ObjectMethodName,
    string SourceTypeName,
    string SignatureKey);

public sealed record CompiledBindingAccessorEmissionPlan(
    ImmutableArray<CompiledBindingAccessorEmissionMethod> Methods,
    IReadOnlyDictionary<int, string> ObjectMethodNamesByCompiledBindingIndex,
    IReadOnlyDictionary<string, string> MethodNamesByPlaceholderToken);
