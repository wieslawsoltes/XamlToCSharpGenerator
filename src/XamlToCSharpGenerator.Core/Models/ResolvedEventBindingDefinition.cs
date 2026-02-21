using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public enum ResolvedEventBindingTargetKind
{
    Command = 0,
    Method = 1
}

public enum ResolvedEventBindingSourceMode
{
    DataContextThenRoot = 0,
    DataContext = 1,
    Root = 2
}

public sealed record ResolvedEventBindingParameter(
    string Name,
    string TypeName);

public sealed record ResolvedEventBindingDefinition(
    string GeneratedMethodName,
    string DelegateTypeName,
    ImmutableArray<ResolvedEventBindingParameter> Parameters,
    ResolvedEventBindingTargetKind TargetKind,
    ResolvedEventBindingSourceMode SourceMode,
    string TargetPath,
    string? ParameterPath,
    string? ParameterValueExpression,
    bool HasParameterValueExpression,
    bool PassEventArgs,
    string? DataContextTypeName,
    string? RootTypeName,
    string? CompiledDataContextTargetPath,
    string? CompiledRootTargetPath,
    string? CompiledDataContextParameterPath,
    string? CompiledRootParameterPath,
    int Line,
    int Column);
