using System;
using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct EventBindingSemanticBindingResult(
    ResolvedEventBindingDefinition Definition,
    bool TargetPathValidated,
    bool HasCompiledCoverage);

public sealed class EventBindingSemanticBindingService
{
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly Func<Compilation, INamedTypeSymbol?> _resolveCommandType;

    public EventBindingSemanticBindingService(
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        Func<Compilation, INamedTypeSymbol?> resolveCommandType)
    {
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _resolveCommandType = resolveCommandType ?? throw new ArgumentNullException(nameof(resolveCommandType));
    }

    public bool TryBuildDelegateSignature(
        INamedTypeSymbol delegateType,
        out string delegateTypeName,
        out ImmutableArray<ResolvedEventBindingParameter> delegateParameters)
    {
        delegateTypeName = delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        delegateParameters = ImmutableArray<ResolvedEventBindingParameter>.Empty;

        if (delegateType.DelegateInvokeMethod is not IMethodSymbol invokeMethod)
        {
            return false;
        }

        var parameters = ImmutableArray.CreateBuilder<ResolvedEventBindingParameter>(invokeMethod.Parameters.Length);
        foreach (var parameter in invokeMethod.Parameters)
        {
            parameters.Add(new ResolvedEventBindingParameter(
                Name: parameter.Name,
                TypeName: parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        delegateParameters = parameters.ToImmutable();
        return true;
    }

    public string BuildInlineStableKey(
        string normalizedExpression,
        INamedTypeSymbol delegateType,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol rootType,
        INamedTypeSymbol? targetType,
        bool isLambdaExpression)
    {
        var stableKey = string.Join(
            "|",
            normalizedExpression,
            delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            rootType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            isLambdaExpression ? "lambda" : "statements");
        return StableHashSemantics.ComputeFnv1aHex(stableKey);
    }

    public bool TryBuildInlineCodeDefinition(
        string rawCode,
        bool isLambdaExpression,
        string eventName,
        ITypeSymbol eventHandlerType,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        string? documentClassFullName,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out string errorMessage)
    {
        eventBindingDefinition = null;
        errorMessage = string.Empty;

        if (eventHandlerType is not INamedTypeSymbol delegateType ||
            !TryBuildDelegateSignature(delegateType, out var delegateTypeName, out var parameters))
        {
            errorMessage = "Event binding delegate type is not supported.";
            return false;
        }

        var sourceType = nodeDataType ?? rootTypeSymbol ?? targetType;
        if (sourceType is null)
        {
            errorMessage = "Inline event binding requires an available source type.";
            return false;
        }

        if (isLambdaExpression)
        {
            if (!CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                    compilation,
                    sourceType,
                    rootTypeSymbol,
                    targetType,
                    delegateType,
                    rawCode,
                    out var lambdaAnalysis,
                    out errorMessage))
            {
                return false;
            }

            eventBindingDefinition = new ResolvedEventBindingDefinition(
                GeneratedMethodName: BuildGeneratedMethodName(eventName, BuildInlineStableKey(lambdaAnalysis.RewrittenLambdaExpression, delegateType, sourceType, rootTypeSymbol ?? targetType, targetType, isLambdaExpression: true)),
                DelegateTypeName: delegateTypeName,
                Parameters: parameters,
                TargetKind: ResolvedEventBindingTargetKind.Lambda,
                SourceMode: ResolvedEventBindingSourceMode.DataContextThenRoot,
                TargetPath: rawCode,
                ParameterPath: null,
                ParameterValueExpression: null,
                HasParameterValueExpression: false,
                PassEventArgs: true,
                DataContextTypeName: sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RootTypeName: rootTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? documentClassFullName,
                CompiledDataContextTargetPath: null,
                CompiledRootTargetPath: null,
                CompiledDataContextMethodCall: null,
                CompiledRootMethodCall: null,
                CompiledDataContextLambdaExpression: lambdaAnalysis.RewrittenLambdaExpression,
                CompiledRootLambdaExpression: null,
                CompiledDataContextParameterPath: null,
                CompiledRootParameterPath: null,
                LambdaSourceTypeName: sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                LambdaSourceDependencyExpression: null,
                LambdaContextTargetTypeName: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                UsesInlineCodeContext: true,
                Line: 0,
                Column: 0);
            return true;
        }

        if (!CSharpInlineCodeAnalysisService.TryAnalyzeEventStatements(
                compilation,
                sourceType,
                rootTypeSymbol,
                targetType,
                delegateType,
                rawCode,
                out var statementsAnalysis,
                out errorMessage))
        {
            return false;
        }

        eventBindingDefinition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: BuildGeneratedMethodName(eventName, BuildInlineStableKey(statementsAnalysis.RewrittenLambdaExpression, delegateType, sourceType, rootTypeSymbol ?? targetType, targetType, isLambdaExpression: false)),
            DelegateTypeName: delegateTypeName,
            Parameters: parameters,
            TargetKind: ResolvedEventBindingTargetKind.Lambda,
            SourceMode: ResolvedEventBindingSourceMode.DataContextThenRoot,
            TargetPath: rawCode,
            ParameterPath: null,
            ParameterValueExpression: null,
            HasParameterValueExpression: false,
            PassEventArgs: true,
            DataContextTypeName: sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            RootTypeName: rootTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? documentClassFullName,
            CompiledDataContextTargetPath: null,
            CompiledRootTargetPath: null,
            CompiledDataContextMethodCall: null,
            CompiledRootMethodCall: null,
            CompiledDataContextLambdaExpression: statementsAnalysis.RewrittenLambdaExpression,
            CompiledRootLambdaExpression: null,
            CompiledDataContextParameterPath: null,
            CompiledRootParameterPath: null,
            LambdaSourceTypeName: sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            LambdaSourceDependencyExpression: null,
            LambdaContextTargetTypeName: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            UsesInlineCodeContext: true,
            Line: 0,
            Column: 0);
        return true;
    }

    public bool TryBuildParsedDefinition(
        EventBindingMarkup parsedBinding,
        string eventName,
        ITypeSymbol eventHandlerType,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol rootTypeSymbol,
        int line,
        int column,
        out EventBindingSemanticBindingResult result,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        var targetPathValidated = false;
        var hasCompiledCoverage = false;
        if (eventHandlerType is not INamedTypeSymbol delegateType ||
            !TryBuildDelegateSignature(delegateType, out var delegateTypeName, out var delegateParameters))
        {
            result = default;
            errorMessage = "Event binding delegate type is not supported.";
            return false;
        }

        INamedTypeSymbol? commandType = _resolveCommandType(compilation);
        var sourceMode = parsedBinding.SourceMode;
        var dataContextTypeName = nodeDataType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rootTypeName = rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var stableKey = StableHashSemantics.ComputeFnv1aHex(
            eventName + "|" + parsedBinding.TargetPath + "|" + sourceMode.ToString());
        var compiledDataContextTargetPath = default(string);
        var compiledRootTargetPath = default(string);
        var compiledDataContextMethodCall = default(ResolvedEventBindingMethodCallPlan);
        var compiledRootMethodCall = default(ResolvedEventBindingMethodCallPlan);
        var compiledDataContextParameterPath = default(string);
        var compiledRootParameterPath = default(string);

        if (sourceMode != ResolvedEventBindingSourceMode.Root &&
            nodeDataType is not null &&
            TryResolveCompiledBindingPlan(
                parsedBinding,
                eventHandlerType,
                nodeDataType,
                commandType,
                out var dataContextTargetValidated,
                out var dataContextHasCompiledCoverage,
                out compiledDataContextTargetPath,
                out compiledDataContextMethodCall,
                out compiledDataContextParameterPath))
        {
            targetPathValidated |= dataContextTargetValidated;
            hasCompiledCoverage |= dataContextHasCompiledCoverage;
        }

        if (sourceMode != ResolvedEventBindingSourceMode.DataContext &&
            TryResolveCompiledBindingPlan(
                parsedBinding,
                eventHandlerType,
                rootTypeSymbol,
                commandType,
                out var rootTargetValidated,
                out var rootHasCompiledCoverage,
                out compiledRootTargetPath,
                out compiledRootMethodCall,
                out compiledRootParameterPath))
        {
            targetPathValidated |= rootTargetValidated;
            hasCompiledCoverage |= rootHasCompiledCoverage;
        }

        var definition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: BuildGeneratedMethodName(eventName, stableKey),
            DelegateTypeName: delegateTypeName,
            Parameters: delegateParameters,
            TargetKind: parsedBinding.TargetKind,
            SourceMode: sourceMode,
            TargetPath: parsedBinding.TargetPath,
            ParameterPath: parsedBinding.ParameterPath,
            ParameterValueExpression: parsedBinding.ParameterValueExpression,
            HasParameterValueExpression: parsedBinding.HasParameterValueExpression,
            PassEventArgs: parsedBinding.PassEventArgs,
            DataContextTypeName: dataContextTypeName,
            RootTypeName: rootTypeName,
            CompiledDataContextTargetPath: compiledDataContextTargetPath,
            CompiledRootTargetPath: compiledRootTargetPath,
            CompiledDataContextMethodCall: compiledDataContextMethodCall,
            CompiledRootMethodCall: compiledRootMethodCall,
            CompiledDataContextLambdaExpression: null,
            CompiledRootLambdaExpression: null,
            CompiledDataContextParameterPath: compiledDataContextParameterPath,
            CompiledRootParameterPath: compiledRootParameterPath,
            LambdaSourceTypeName: null,
            LambdaSourceDependencyExpression: null,
            LambdaContextTargetTypeName: null,
            UsesInlineCodeContext: false,
            Line: line,
            Column: column);

        result = new EventBindingSemanticBindingResult(definition, targetPathValidated, hasCompiledCoverage);
        return true;
    }

    private bool TryResolveCompiledBindingPlan(
        EventBindingMarkup parsedBinding,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol? commandType,
        out bool targetPathValidated,
        out bool hasCompiledCoverage,
        out string? compiledTargetPath,
        out ResolvedEventBindingMethodCallPlan? compiledMethodCall,
        out string? compiledParameterPath)
    {
        targetPathValidated = false;
        hasCompiledCoverage = false;
        compiledTargetPath = null;
        compiledMethodCall = null;
        compiledParameterPath = null;

        switch (parsedBinding.TargetKind)
        {
            case ResolvedEventBindingTargetKind.Command:
                if (TryResolveCompiledPath(sourceType, parsedBinding.TargetPath, out compiledTargetPath))
                {
                    targetPathValidated = true;
                    hasCompiledCoverage = commandType is null ||
                                          TryResolveMemberPathType(sourceType, compiledTargetPath, out var resolvedCommandType) &&
                                          resolvedCommandType is not null &&
                                          TypeSymbolLookupSemanticsService.IsTypeAssignableTo(resolvedCommandType, commandType);
                }

                break;

            case ResolvedEventBindingTargetKind.Method:
                if (TryBuildCompiledMethodCallPlan(sourceType, parsedBinding, eventHandlerType, out compiledMethodCall))
                {
                    targetPathValidated = true;
                    hasCompiledCoverage = true;
                }

                break;
        }

        if (!string.IsNullOrWhiteSpace(parsedBinding.ParameterPath) &&
            TryResolveCompiledPath(sourceType, parsedBinding.ParameterPath!, out var resolvedParameterPath))
        {
            compiledParameterPath = resolvedParameterPath;
            hasCompiledCoverage = targetPathValidated && hasCompiledCoverage;
        }
        else if (string.IsNullOrWhiteSpace(parsedBinding.ParameterPath))
        {
            compiledParameterPath = null;
        }

        return true;
    }

    private bool TryBuildCompiledMethodCallPlan(
        INamedTypeSymbol sourceType,
        EventBindingMarkup parsedBinding,
        ITypeSymbol eventHandlerType,
        out ResolvedEventBindingMethodCallPlan? methodCallPlan)
    {
        methodCallPlan = null;
        if (eventHandlerType is not INamedTypeSymbol delegateType ||
            delegateType.DelegateInvokeMethod is not IMethodSymbol invokeMethod ||
            !EventBindingPathSemantics.TrySplitMethodPath(parsedBinding.TargetPath, out var targetPath, out var methodName) ||
            !TryResolveMethodTargetType(sourceType, targetPath, out var targetType))
        {
            return false;
        }

        var argumentSets = EventBindingPathSemantics.BuildMethodArgumentSets(
            hasParameterToken: !string.IsNullOrWhiteSpace(parsedBinding.ParameterPath) || parsedBinding.HasParameterValueExpression,
            passEventArgs: parsedBinding.PassEventArgs);
        for (var setIndex = 0; setIndex < argumentSets.Length; setIndex++)
        {
            var argumentKinds = argumentSets[setIndex];
            if (!TryFindCompatibleMethod(targetType, methodName, invokeMethod, parsedBinding, sourceType, argumentKinds, out var matchedMethod))
            {
                continue;
            }

            var arguments = ImmutableArray.CreateBuilder<ResolvedEventBindingMethodArgument>(matchedMethod.Parameters.Length);
            for (var index = 0; index < argumentKinds.Length; index++)
            {
                arguments.Add(new ResolvedEventBindingMethodArgument(
                    argumentKinds[index],
                    matchedMethod.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }

            methodCallPlan = new ResolvedEventBindingMethodCallPlan(targetPath, matchedMethod.Name, arguments.MoveToImmutable());
            return true;
        }

        return false;
    }

    private bool TryFindCompatibleMethod(
        INamedTypeSymbol targetType,
        string methodName,
        IMethodSymbol invokeMethod,
        EventBindingMarkup parsedBinding,
        INamedTypeSymbol sourceType,
        ImmutableArray<ResolvedEventBindingMethodArgumentKind> argumentKinds,
        out IMethodSymbol matchedMethod)
    {
        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(targetType))
        {
            foreach (var candidate in current.GetMembers(methodName))
            {
                if (candidate is not IMethodSymbol method ||
                    method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.Parameters.Length != argumentKinds.Length)
                {
                    continue;
                }

                var compatible = true;
                for (var index = 0; index < argumentKinds.Length; index++)
                {
                    if (!IsCompatibleEventBindingArgument(
                            method.Parameters[index].Type,
                            argumentKinds[index],
                            invokeMethod,
                            parsedBinding,
                            sourceType))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                {
                    matchedMethod = method;
                    return true;
                }
            }
        }

        matchedMethod = null!;
        return false;
    }

    private bool IsCompatibleEventBindingArgument(
        ITypeSymbol parameterType,
        ResolvedEventBindingMethodArgumentKind argumentKind,
        IMethodSymbol invokeMethod,
        EventBindingMarkup parsedBinding,
        INamedTypeSymbol sourceType)
    {
        return argumentKind switch
        {
            ResolvedEventBindingMethodArgumentKind.Sender =>
                invokeMethod.Parameters.Length > 0 &&
                _isTypeAssignableTo(invokeMethod.Parameters[0].Type, parameterType),
            ResolvedEventBindingMethodArgumentKind.EventArgs =>
                invokeMethod.Parameters.Length > 1 &&
                _isTypeAssignableTo(invokeMethod.Parameters[1].Type, parameterType),
            ResolvedEventBindingMethodArgumentKind.Parameter =>
                IsCompatibleParameterBindingArgument(parameterType, parsedBinding, sourceType),
            _ => false
        };
    }

    private bool IsCompatibleParameterBindingArgument(
        ITypeSymbol parameterType,
        EventBindingMarkup parsedBinding,
        INamedTypeSymbol sourceType)
    {
        if (!string.IsNullOrWhiteSpace(parsedBinding.ParameterPath))
        {
            return TryResolveMemberPathType(sourceType, parsedBinding.ParameterPath!, out var resolvedParameterType) &&
                   resolvedParameterType is not null &&
                   _isTypeAssignableTo(resolvedParameterType, parameterType);
        }

        return true;
    }

    private static bool TryResolveCompiledPath(
        INamedTypeSymbol sourceType,
        string rawPath,
        out string? compiledPath)
    {
        compiledPath = null;
        if (string.IsNullOrWhiteSpace(rawPath) ||
            !EventBindingPathSemantics.IsSimplePath(rawPath))
        {
            return false;
        }

        var normalizedPath = rawPath.Trim();
        if (!TryResolveMemberPathType(sourceType, normalizedPath, out _))
        {
            return false;
        }

        compiledPath = normalizedPath;
        return true;
    }

    private static bool TryResolveMethodTargetType(
        INamedTypeSymbol sourceType,
        string targetPath,
        out INamedTypeSymbol targetType)
    {
        if (string.Equals(targetPath, ".", StringComparison.Ordinal))
        {
            targetType = sourceType;
            return true;
        }

        return TryResolveMemberPathType(sourceType, targetPath, out targetType);
    }

    private static bool TryResolveMemberPathType(
        INamedTypeSymbol sourceType,
        string path,
        out INamedTypeSymbol targetType)
    {
        if (string.Equals(path, ".", StringComparison.Ordinal))
        {
            targetType = sourceType;
            return true;
        }

        targetType = null!;
        if (!EventBindingPathSemantics.IsSimplePath(path))
        {
            return false;
        }

        ITypeSymbol currentType = sourceType;
        var segments = XamlMemberPathSemantics.SplitPathSegments(path);
        for (var index = 0; index < segments.Length; index++)
        {
            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                return false;
            }

            if (TypeSymbolLookupSemanticsService.FindProperty(currentNamedType, segments[index]) is { } property)
            {
                currentType = property.Type;
                continue;
            }

            var field = FindField(currentNamedType, segments[index]);
            if (field is not null)
            {
                currentType = field.Type;
                continue;
            }

            return false;
        }

        if (currentType is INamedTypeSymbol currentResolvedType)
        {
            targetType = currentResolvedType;
            return true;
        }

        return false;
    }

    private static IFieldSymbol? FindField(INamedTypeSymbol type, string fieldName)
    {
        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var member in current.GetMembers(fieldName))
            {
                if (member is IFieldSymbol field && !field.IsStatic)
                {
                    return field;
                }
            }
        }

        return null;
    }

    private static string BuildGeneratedMethodName(string eventName, string stableKey)
    {
        return "__AXSG_EventBinding_" + eventName + "_" + stableKey;
    }
}
