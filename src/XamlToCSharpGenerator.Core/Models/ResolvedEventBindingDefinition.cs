using System.Collections.Immutable;

namespace XamlToCSharpGenerator.Core.Models;

public enum ResolvedEventBindingTargetKind
{
    Command = 0,
    Method = 1,
    Lambda = 2
}

public enum ResolvedEventBindingSourceMode
{
    DataContextThenRoot = 0,
    DataContext = 1,
    Root = 2
}

public enum ResolvedEventBindingMethodArgumentKind
{
    Sender = 0,
    EventArgs = 1,
    Parameter = 2
}

public sealed record ResolvedEventBindingParameter(
    string Name,
    string TypeName);

public sealed record ResolvedEventBindingMethodArgument(
    ResolvedEventBindingMethodArgumentKind Kind,
    string TypeName);

public sealed record ResolvedEventBindingMethodCallPlan(
    string TargetPath,
    string MethodName,
    ImmutableArray<ResolvedEventBindingMethodArgument> Arguments);

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
    ResolvedEventBindingMethodCallPlan? CompiledDataContextMethodCall,
    ResolvedEventBindingMethodCallPlan? CompiledRootMethodCall,
    string? CompiledDataContextLambdaExpression,
    string? CompiledRootLambdaExpression,
    string? CompiledDataContextParameterPath,
    string? CompiledRootParameterPath,
    int Line,
    int Column);
