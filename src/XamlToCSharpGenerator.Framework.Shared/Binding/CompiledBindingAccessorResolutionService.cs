using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public readonly record struct CompiledBindingAccessorResolutionResult(
    string AccessorExpression,
    string NormalizedPath,
    string? ResultTypeName,
    ITypeSymbol? ResultTypeSymbol,
    ImmutableArray<string> DependencyNames);

public sealed class CompiledBindingAccessorResolutionService
{
    private readonly Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> _resolveTypeToken;
    private readonly Func<Compilation, TypeContractId, INamedTypeSymbol?> _resolveContractType;
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;

    public CompiledBindingAccessorResolutionService(
        Func<Compilation, XamlDocumentModel, string, string, INamedTypeSymbol?> resolveTypeToken,
        Func<Compilation, TypeContractId, INamedTypeSymbol?> resolveContractType,
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo)
    {
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _resolveContractType = resolveContractType ?? throw new ArgumentNullException(nameof(resolveContractType));
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
    }

    public bool TryBuildAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolutionResult resolution,
        out string errorMessage)
    {
        resolution = default;
        errorMessage = string.Empty;

        var normalizedPath = string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath.Trim();
        if (normalizedPath == ".")
        {
            resolution = new CompiledBindingAccessorResolutionResult(
                "__source",
                normalizedPath,
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                sourceType,
                ImmutableArray<string>.Empty);
            return true;
        }

        if (CompiledBindingPathParser.TryParse(normalizedPath, out _, out _, out _))
        {
            if (TryLowerCompiledBindingPathExpression(
                    compilation,
                    document,
                    sourceType,
                    normalizedPath,
                    targetPropertyType,
                    unsafeAccessors,
                    out var loweredResultType,
                    out var loweredExpression,
                    out var resolvedPath,
                    out errorMessage))
            {
                resolution = new CompiledBindingAccessorResolutionResult(
                    loweredExpression,
                    resolvedPath,
                    loweredResultType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    loweredResultType,
                    ImmutableArray<string>.Empty);
                return true;
            }

            return false;
        }

        if (!CSharpSourceContextExpressionAnalysisService.TryAnalyze(
                compilation,
                sourceType,
                normalizedPath,
                "__source",
                out var analysis,
                out errorMessage))
        {
            return false;
        }

        resolution = new CompiledBindingAccessorResolutionResult(
            analysis.AccessorExpression,
            normalizedPath,
            analysis.ResultTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            analysis.ResultTypeSymbol,
            analysis.DependencyNames);
        return true;
    }

    public bool SupportsUnsafeAccessor(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute") is not null;
    }

    public string RegisterUnsafeAccessorDefinition(
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        IMethodSymbol method)
    {
        var parameterTypeNames = method.Parameters
            .Select(static parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .ToImmutableArray();
        var stableKey = string.Join(
            "|",
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            method.Name,
            method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            string.Join(",", parameterTypeNames));
        var generatedMethodName = "__AXSG_UnsafeAccessor_" + StableHashSemantics.ComputeFnv1aHex(stableKey);

        if (unsafeAccessors is not null &&
            !unsafeAccessors.Any(definition => definition.MethodName.Equals(generatedMethodName, StringComparison.Ordinal)))
        {
            unsafeAccessors.Add(new ResolvedUnsafeAccessorDefinition(
                MethodName: generatedMethodName,
                UnsafeAccessorTargetName: method.Name,
                DeclaringTypeName: method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ReturnTypeName: method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                ParameterTypeNames: parameterTypeNames));
        }

        return generatedMethodName;
    }

    private bool TryLowerCompiledBindingPathExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string normalizedPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out ITypeSymbol? resultType,
        out string loweredExpression,
        out string resolvedPath,
        out string errorMessage)
    {
        resultType = null;
        loweredExpression = string.Empty;
        resolvedPath = normalizedPath;
        errorMessage = string.Empty;

        if (!CompiledBindingPathParser.TryParse(normalizedPath, out var segments, out var leadingNotCount, out errorMessage))
        {
            return false;
        }

        var currentExpression = "__source";
        ITypeSymbol currentType = sourceType;
        var containsUnsafeAccessor = false;
        var accessibilityWithin = TypeSymbolLookupSemanticsService.GetGeneratedCodeAccessibilityWithinSymbol(compilation, document);
        var fallbackClrNamespace = document.ClassNamespace ?? string.Empty;
        var hasTopLevelConditionalGuard = false;
        var topLevelConditionalGuardExpression = string.Empty;
        var topLevelConditionalTempName = string.Empty;

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            var isTerminalSegment = segmentIndex == segments.Length - 1;
            var receiverExpression = currentExpression;
            var receiverType = currentType;

            if (segment.AcceptsNull &&
                receiverType.IsValueType &&
                !IsNullableValueType(receiverType))
            {
                errorMessage =
                    "Null-conditional access is not valid for non-nullable value type '" +
                    receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    "'.";
                return false;
            }

            if (segment.IsAttachedProperty)
            {
                if (receiverType is not INamedTypeSymbol currentNamedType)
                {
                    errorMessage = $"Attached property '{segment.MemberName}' requires a named target type.";
                    return false;
                }

                var ownerType = _resolveTypeToken(
                    compilation,
                    document,
                    segment.AttachedOwnerTypeToken ?? string.Empty,
                    fallbackClrNamespace);
                if (ownerType is null)
                {
                    errorMessage = $"Unable to resolve attached property owner '{segment.AttachedOwnerTypeToken}'.";
                    return false;
                }

                var getterMethod = TypeSymbolLookupSemanticsService.FindAttachedPropertyGetterMethod(
                    ownerType,
                    segment.MemberName,
                    currentNamedType);
                if (getterMethod is null)
                {
                    errorMessage = $"Attached property getter '{ownerType.Name}.Get{segment.MemberName}' was not found.";
                    return false;
                }

                var candidateExpression = BuildMethodInvocationExpression(
                    ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + getterMethod.Name,
                    receiverExpression,
                    ImmutableArray<string>.Empty);
                if (segment.CastTypeToken is { Length: > 0 })
                {
                    if (!TryResolveCastTypeExpression(
                            compilation,
                            document,
                            fallbackClrNamespace,
                            segment.CastTypeToken,
                            candidateExpression,
                            out candidateExpression,
                            out errorMessage))
                    {
                        return false;
                    }
                }

                if (segment.AcceptsNull)
                {
                    if (!TryApplyConditionalGuard(
                            compilation,
                            sourceType,
                            receiverExpression,
                            candidateExpression,
                            getterMethod.ReturnType,
                            ref hasTopLevelConditionalGuard,
                            ref topLevelConditionalGuardExpression,
                            ref topLevelConditionalTempName,
                            ref currentExpression,
                            ref currentType,
                            out errorMessage))
                    {
                        return false;
                    }
                }
                else if (!TryAdvanceExpression(
                             compilation,
                             sourceType,
                             candidateExpression,
                             getterMethod.ReturnType,
                             containsUnsafeAccessor,
                             ref currentExpression,
                             ref currentType,
                             out errorMessage))
                {
                    return false;
                }
            }
            else if (segment.IsMethodCall)
            {
                if (receiverType is not INamedTypeSymbol currentNamedType)
                {
                    errorMessage = $"The name '{segment.MemberName}' does not exist in the current context";
                    return false;
                }

                if (!TryResolveMethodInvocation(
                        compilation,
                        document,
                        sourceType,
                        currentNamedType,
                        receiverExpression,
                        segment,
                        accessibilityWithin,
                        unsafeAccessors,
                        fallbackClrNamespace,
                        out var candidateExpression,
                        out var candidateType,
                        out var usesUnsafeAccessor,
                        out errorMessage))
                {
                    return false;
                }

                containsUnsafeAccessor |= usesUnsafeAccessor;
                if (segment.AcceptsNull && usesUnsafeAccessor)
                {
                    if (!TryApplyConditionalGuard(
                            compilation,
                            sourceType,
                            receiverExpression,
                            candidateExpression,
                            candidateType,
                            ref hasTopLevelConditionalGuard,
                            ref topLevelConditionalGuardExpression,
                            ref topLevelConditionalTempName,
                            ref currentExpression,
                            ref currentType,
                            out errorMessage))
                    {
                        return false;
                    }
                }
                else if (!TryAdvanceExpression(
                             compilation,
                             sourceType,
                             candidateExpression,
                             candidateType,
                             containsUnsafeAccessor,
                             ref currentExpression,
                             ref currentType,
                             out errorMessage))
                {
                    return false;
                }
            }
            else
            {
                if (receiverType is not INamedTypeSymbol currentNamedType)
                {
                    errorMessage = $"The name '{segment.MemberName}' does not exist in the current context";
                    return false;
                }

                if (IsCommandTargetType(compilation, targetPropertyType) &&
                    isTerminalSegment &&
                    segment.CastTypeToken is null &&
                    segment.Indexers.IsDefaultOrEmpty &&
                    segment.StreamCount == 0 &&
                    TryResolveCommandMethodAccess(
                        compilation,
                        currentNamedType,
                        receiverExpression,
                        segment.MemberName,
                        accessibilityWithin,
                        unsafeAccessors,
                        normalizedPath,
                        out var commandExpression,
                        out var commandResultType,
                        out var commandPath,
                        out errorMessage))
                {
                    currentExpression = commandExpression;
                    currentType = commandResultType;
                    resolvedPath = commandPath;
                    continue;
                }

                if (!TryResolveMemberAccess(
                        compilation,
                        document,
                        sourceType,
                        currentNamedType,
                        receiverExpression,
                        segment,
                        accessibilityWithin,
                        unsafeAccessors,
                        fallbackClrNamespace,
                        out var candidateExpression,
                        out var candidateType,
                        out var usesUnsafeAccessor,
                        out errorMessage))
                {
                    return false;
                }

                containsUnsafeAccessor |= usesUnsafeAccessor;
                if (segment.AcceptsNull && usesUnsafeAccessor)
                {
                    if (!TryApplyConditionalGuard(
                            compilation,
                            sourceType,
                            receiverExpression,
                            candidateExpression,
                            candidateType,
                            ref hasTopLevelConditionalGuard,
                            ref topLevelConditionalGuardExpression,
                            ref topLevelConditionalTempName,
                            ref currentExpression,
                            ref currentType,
                            out errorMessage))
                    {
                        return false;
                    }
                }
                else if (!TryAdvanceExpression(
                             compilation,
                             sourceType,
                             candidateExpression,
                             candidateType,
                             containsUnsafeAccessor,
                             ref currentExpression,
                             ref currentType,
                             out errorMessage))
                {
                    return false;
                }
            }

            foreach (var indexer in segment.Indexers)
            {
                var indexedExpression = currentExpression + "[" + NormalizeCompiledBindingArgument(indexer) + "]";
                if (!TryAdvanceExpression(
                        compilation,
                        sourceType,
                        indexedExpression,
                        currentType,
                        containsUnsafeAccessor,
                        ref currentExpression,
                        ref currentType,
                        out errorMessage))
                {
                    return false;
                }
            }

            for (var streamIndex = 0; streamIndex < segment.StreamCount; streamIndex++)
            {
                if (!TryBuildStreamUnwrapExpression(
                        compilation,
                        currentExpression,
                        currentType,
                        out var unwrappedExpression,
                        out var unwrappedType,
                        out errorMessage))
                {
                    return false;
                }

                currentExpression = unwrappedExpression;
                currentType = unwrappedType;
                var usesRuntimeStreamHelper = currentExpression.Contains(
                    "SourceGenCompiledBindingStreamHelper",
                    StringComparison.Ordinal);
                if (!TryAdvanceExpression(
                        compilation,
                        sourceType,
                        currentExpression,
                        currentType,
                        containsUnsafeAccessor || usesRuntimeStreamHelper,
                        ref currentExpression,
                        ref currentType,
                        out errorMessage))
                {
                    return false;
                }
            }
        }

        if (hasTopLevelConditionalGuard)
        {
            currentExpression = BuildConditionalExpression(
                compilation,
                topLevelConditionalGuardExpression,
                topLevelConditionalTempName,
                currentExpression,
                currentType,
                out currentType);
        }

        for (var index = 0; index < leadingNotCount; index++)
        {
            currentExpression = "!global::System.Convert.ToBoolean(" + currentExpression + ")";
            currentType = compilation.GetSpecialType(SpecialType.System_Boolean);
        }

        ITypeSymbol? finalResultType = null;
        var containsRuntimeStreamHelper = currentExpression.Contains(
            "SourceGenCompiledBindingStreamHelper",
            StringComparison.Ordinal);
        var containsMethodCommandRuntime = currentExpression.Contains(
            "SourceGenMethodCommandRuntime",
            StringComparison.Ordinal);
        if (!containsUnsafeAccessor &&
            !containsRuntimeStreamHelper &&
            !containsMethodCommandRuntime &&
            !TryAnalyzeLoweredExpression(
                compilation,
                sourceType,
                currentExpression,
                out finalResultType,
                out errorMessage))
        {
            return false;
        }

        if (!containsUnsafeAccessor &&
            !containsRuntimeStreamHelper &&
            !containsMethodCommandRuntime &&
            finalResultType is not null)
        {
            currentType = NormalizeTypeToCompilation(compilation, finalResultType, currentType);
        }

        resultType = currentType;
        loweredExpression = currentExpression.Replace("__source", "source");
        return true;
    }

    private bool IsCommandTargetType(Compilation compilation, ITypeSymbol? targetPropertyType)
    {
        if (targetPropertyType is null)
        {
            return false;
        }

        var commandType = _resolveContractType(compilation, TypeContractId.SystemICommand);
        return commandType is not null &&
               (SymbolEqualityComparer.Default.Equals(targetPropertyType, commandType) ||
                _isTypeAssignableTo(targetPropertyType, commandType));
    }

    private bool TryResolveCommandMethodAccess(
        Compilation compilation,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        string memberName,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        string normalizedPath,
        out string expression,
        out ITypeSymbol resultType,
        out string resolvedPath,
        out string errorMessage)
    {
        expression = string.Empty;
        resolvedPath = normalizedPath;
        errorMessage = string.Empty;
        resultType = receiverType;

        var commandType = _resolveContractType(compilation, TypeContractId.SystemICommand);
        if (commandType is null)
        {
            return false;
        }

        if (TryResolvePropertyOrFieldAccess(
                compilation,
                receiverType,
                receiverExpression,
                memberName,
                acceptsNull: false,
                accessibilityWithin,
                unsafeAccessors,
                out var memberExpression,
                out var memberType,
                out _,
                out _))
        {
            if (IsCommandLikeType(memberType, commandType))
            {
                expression = memberExpression;
                resultType = memberType;
                return true;
            }

            return false;
        }

        if (TryResolveCommandValueMethodAccess(
                compilation,
                receiverType,
                receiverExpression,
                memberName,
                accessibilityWithin,
                unsafeAccessors,
                out var methodExpression,
                out var methodResultType,
                out _))
        {
            if (IsCommandLikeType(methodResultType, commandType))
            {
                expression = methodExpression;
                resultType = methodResultType;
                return true;
            }
        }

        if (!TryResolveCommandExecuteMethod(
                compilation,
                receiverType,
                memberName,
                accessibilityWithin,
                unsafeAccessors,
                out var executeMethod,
                out var useUnsafeExecute))
        {
            return false;
        }

        var canExecuteMethodName = "Can" + memberName;
        TryResolveCanExecuteMethod(
            compilation,
            receiverType,
            canExecuteMethodName,
            accessibilityWithin,
            unsafeAccessors,
            out var canExecuteMethod,
            out var useUnsafeCanExecute);

        var targetCastTypeName = executeMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var executeTargetExpression = "((" + targetCastTypeName + ")target)";
        var executeInvocation = BuildCommandInvocationExpression(
            executeMethod,
            executeTargetExpression,
            useUnsafeExecute,
            unsafeAccessors);

        string canExecuteExpression;
        if (canExecuteMethod is null)
        {
            canExecuteExpression = "null";
        }
        else
        {
            var canExecuteTargetTypeName = canExecuteMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var canExecuteTargetExpression = "((" + canExecuteTargetTypeName + ")target)";
            var canExecuteInvocation = BuildCommandInvocationExpression(
                canExecuteMethod,
                canExecuteTargetExpression,
                useUnsafeCanExecute,
                unsafeAccessors);
            canExecuteExpression = "static (target, parameter) => " + canExecuteInvocation;
        }

        expression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMethodCommandRuntime.Create((object?)(" +
            receiverExpression +
            "), static (target, parameter) => " +
            executeInvocation +
            ", " +
            canExecuteExpression +
            ", " +
            BuildDependencyArrayLiteral(BuildCommandDependencyNames(normalizedPath, memberName, canExecuteMethod)) +
            ")";
        resultType = commandType;
        resolvedPath = normalizedPath.EndsWith(")", StringComparison.Ordinal)
            ? normalizedPath
            : normalizedPath + "()";
        return true;
    }

    private bool TryResolveCommandExecuteMethod(
        Compilation compilation,
        INamedTypeSymbol receiverType,
        string methodName,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out IMethodSymbol executeMethod,
        out bool usesUnsafeAccessor)
    {
        executeMethod = null!;
        usesUnsafeAccessor = false;

        IMethodSymbol? bestAccessible = null;
        IMethodSymbol? bestInaccessible = null;

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!IsSupportedCommandExecuteMethod(method))
                {
                    continue;
                }

                if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, receiverType))
                {
                    bestAccessible ??= method;
                    if (GetCommandMethodArityScore(method) > GetCommandMethodArityScore(bestAccessible))
                    {
                        bestAccessible = method;
                    }

                    continue;
                }

                bestInaccessible ??= method;
                if (GetCommandMethodArityScore(method) > GetCommandMethodArityScore(bestInaccessible))
                {
                    bestInaccessible = method;
                }
            }
        }

        if (bestAccessible is not null)
        {
            executeMethod = bestAccessible;
            return true;
        }

        if (bestInaccessible is not null && CanEmitUnsafeAccessor(unsafeAccessors))
        {
            executeMethod = bestInaccessible;
            usesUnsafeAccessor = true;
            return true;
        }

        return false;
    }

    private bool TryResolveCanExecuteMethod(
        Compilation compilation,
        INamedTypeSymbol receiverType,
        string methodName,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out IMethodSymbol? canExecuteMethod,
        out bool usesUnsafeAccessor)
    {
        canExecuteMethod = null;
        usesUnsafeAccessor = false;

        IMethodSymbol? bestAccessible = null;
        IMethodSymbol? bestInaccessible = null;

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!IsSupportedCanExecuteMethod(method))
                {
                    continue;
                }

                if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, receiverType))
                {
                    bestAccessible ??= method;
                    if (GetCanExecuteMethodScore(method) > GetCanExecuteMethodScore(bestAccessible))
                    {
                        bestAccessible = method;
                    }

                    continue;
                }

                bestInaccessible ??= method;
                if (GetCanExecuteMethodScore(method) > GetCanExecuteMethodScore(bestInaccessible))
                {
                    bestInaccessible = method;
                }
            }
        }

        if (bestAccessible is not null)
        {
            canExecuteMethod = bestAccessible;
            return true;
        }

        if (bestInaccessible is not null && CanEmitUnsafeAccessor(unsafeAccessors))
        {
            canExecuteMethod = bestInaccessible;
            usesUnsafeAccessor = true;
            return true;
        }

        return false;
    }

    private static bool IsSupportedCommandExecuteMethod(IMethodSymbol method)
    {
        return !method.IsStatic &&
               method.MethodKind == MethodKind.Ordinary &&
               !method.IsImplicitlyDeclared &&
               method.ReturnsVoid &&
               method.Parameters.Length <= 1;
    }

    private static bool IsSupportedCanExecuteMethod(IMethodSymbol method)
    {
        return !method.IsStatic &&
               method.MethodKind == MethodKind.Ordinary &&
               !method.IsImplicitlyDeclared &&
               method.ReturnType.SpecialType == SpecialType.System_Boolean &&
               method.Parameters.Length <= 1;
    }

    private static int GetCommandMethodArityScore(IMethodSymbol method)
    {
        return method.Parameters.Length == 0 ? 2 : 1;
    }

    private static int GetCanExecuteMethodScore(IMethodSymbol method)
    {
        return method.Parameters.Length == 1 ? 2 : 1;
    }

    private bool TryResolveCommandValueMethodAccess(
        Compilation compilation,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        string memberName,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string expression,
        out ITypeSymbol resultType,
        out bool usesUnsafeAccessor)
    {
        expression = string.Empty;
        resultType = receiverType;
        usesUnsafeAccessor = false;

        IMethodSymbol? inaccessibleMethod = null;
        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(memberName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.Parameters.Length != 0 ||
                    method.ReturnsVoid)
                {
                    continue;
                }

                if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, receiverType))
                {
                    expression = BuildMethodInvocationExpression(
                        method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        method.Name,
                        method.IsStatic,
                        receiverExpression,
                        ImmutableArray<string>.Empty);
                    resultType = method.ReturnType;
                    return true;
                }

                inaccessibleMethod ??= method;
            }
        }

        if (inaccessibleMethod is not null && CanEmitUnsafeAccessor(unsafeAccessors))
        {
            var accessorMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, inaccessibleMethod);
            expression = BuildUnsafeAccessorInvocationExpression(
                accessorMethodName,
                receiverExpression,
                ImmutableArray<string>.Empty);
            resultType = inaccessibleMethod.ReturnType;
            usesUnsafeAccessor = true;
            return true;
        }

        return false;
    }

    private string BuildCommandInvocationExpression(
        IMethodSymbol method,
        string targetExpression,
        bool usesUnsafeAccessor,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors)
    {
        var arguments = method.Parameters.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(BuildCommandParameterExpression(method.Parameters[0].Type));

        if (usesUnsafeAccessor)
        {
            var accessorMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, method);
            return BuildUnsafeAccessorInvocationExpression(accessorMethodName, targetExpression, arguments);
        }

        return targetExpression + "." + method.Name + "(" + string.Join(", ", arguments) + ")";
    }

    private static string BuildCommandParameterExpression(ITypeSymbol parameterType)
    {
        if (parameterType.SpecialType == SpecialType.System_Object)
        {
            return "parameter";
        }

        return "global::XamlToCSharpGenerator.Runtime.SourceGenMethodCommandRuntime.ConvertParameter<" +
               parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               ">(parameter)";
    }

    private static bool IsCommandLikeType(ITypeSymbol type, ITypeSymbol commandType)
    {
        return SymbolEqualityComparer.Default.Equals(type, commandType) ||
               SymbolEqualityComparer.Default.Equals(
                   type.WithNullableAnnotation(NullableAnnotation.None),
                   commandType.WithNullableAnnotation(NullableAnnotation.None)) ||
               type is INamedTypeSymbol namedType && namedType.AllInterfaces.Any(interfaceType =>
                   SymbolEqualityComparer.Default.Equals(
                       interfaceType.WithNullableAnnotation(NullableAnnotation.None),
                       commandType.WithNullableAnnotation(NullableAnnotation.None)));
    }

    private static ImmutableArray<string> GetDependsOnPropertyNames(IMethodSymbol? method)
    {
        if (method is null)
        {
            return ImmutableArray<string>.Empty;
        }

        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var attribute in method.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.Name, "DependsOnAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string constructorName &&
                !string.IsNullOrWhiteSpace(constructorName))
            {
                names.Add(constructorName);
                continue;
            }

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (!string.Equals(namedArgument.Key, "Name", StringComparison.Ordinal) ||
                    namedArgument.Value.Value is not string namedName ||
                    string.IsNullOrWhiteSpace(namedName))
                {
                    continue;
                }

                names.Add(namedName);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<string> BuildCommandDependencyNames(
        string normalizedPath,
        string terminalMemberName,
        IMethodSymbol? canExecuteMethod)
    {
        var dependsOnNames = GetDependsOnPropertyNames(canExecuteMethod);
        if (!dependsOnNames.IsDefaultOrEmpty)
        {
            return dependsOnNames;
        }

        var names = ImmutableArray.CreateBuilder<string>();
        var currentPrefix = string.Empty;
        var terminalSuffix = "." + terminalMemberName;
        var receiverPath = normalizedPath.EndsWith(terminalSuffix, StringComparison.Ordinal)
            ? normalizedPath.Substring(0, normalizedPath.Length - terminalSuffix.Length)
            : normalizedPath;

        foreach (var rawToken in receiverPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length == 0 ||
                token.IndexOfAny(['(', ')', '[', ']']) >= 0)
            {
                break;
            }

            currentPrefix = currentPrefix.Length == 0
                ? token
                : currentPrefix + "." + token;
            names.Add(currentPrefix);
        }

        return names.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static string BuildDependencyArrayLiteral(ImmutableArray<string> dependencyNames)
    {
        if (dependencyNames.IsDefaultOrEmpty)
        {
            return "global::System.Array.Empty<string>()";
        }

        return "new string[] { " +
               string.Join(", ", dependencyNames.Select(static name => "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"")) +
               " }";
    }

    private bool TryResolveMethodInvocation(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        CompiledBindingPathSegment segment,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        string fallbackClrNamespace,
        out string expression,
        out ITypeSymbol resultType,
        out bool usesUnsafeAccessor,
        out string errorMessage)
    {
        expression = string.Empty;
        resultType = receiverType;
        usesUnsafeAccessor = false;
        errorMessage = string.Empty;

        var normalizedArguments = segment.MethodArguments
            .Select(static argument => NormalizeCompiledBindingArgument(argument))
            .ToImmutableArray();
        var accessibleCandidateExists = false;
        string? accessibleFailure = null;

        IMethodSymbol? bestInaccessibleMethod = null;
        var bestInaccessibleScore = int.MinValue;

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(segment.MemberName).OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary ||
                    method.Parameters.Length != normalizedArguments.Length)
                {
                    continue;
                }

                if (!compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, receiverType))
                {
                    continue;
                }

                accessibleCandidateExists = true;
                var candidateExpression = BuildMethodInvocationExpression(
                    method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Name,
                    method.IsStatic,
                    receiverExpression,
                    normalizedArguments);
                if (segment.CastTypeToken is { Length: > 0 })
                {
                    if (!TryResolveCastTypeExpression(
                            compilation,
                            document,
                            fallbackClrNamespace,
                            segment.CastTypeToken,
                            candidateExpression,
                            out candidateExpression,
                            out errorMessage))
                    {
                        return false;
                    }
                }

                if (TryAnalyzeLoweredExpression(
                        compilation,
                        sourceType,
                        candidateExpression,
                        out var analyzedType,
                        out var candidateError))
                {
                    expression = candidateExpression;
                    resultType = analyzedType ?? method.ReturnType;
                    return true;
                }

                accessibleFailure = candidateError;
            }
        }

        if (!CanEmitUnsafeAccessor(unsafeAccessors))
        {
            errorMessage = accessibleFailure ?? $"The name '{segment.MemberName}' does not exist in the current context";
            return false;
        }

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(segment.MemberName).OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary ||
                    method.IsStatic ||
                    method.Parameters.Length != normalizedArguments.Length ||
                    compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, receiverType))
                {
                    continue;
                }

                var compatibilityScore = GetMethodCompatibilityScore(method, normalizedArguments);
                if (compatibilityScore < 0 || compatibilityScore <= bestInaccessibleScore)
                {
                    continue;
                }

                bestInaccessibleMethod = method;
                bestInaccessibleScore = compatibilityScore;
            }
        }

        if (bestInaccessibleMethod is not null)
        {
            var unsafeAccessorMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, bestInaccessibleMethod);
            expression = BuildUnsafeAccessorInvocationExpression(
                unsafeAccessorMethodName,
                receiverExpression,
                normalizedArguments);
            if (segment.CastTypeToken is { Length: > 0 })
            {
                if (!TryResolveCastTypeExpression(
                        compilation,
                        document,
                        fallbackClrNamespace,
                        segment.CastTypeToken,
                        expression,
                        out expression,
                        out errorMessage))
                {
                    return false;
                }
            }

            resultType = bestInaccessibleMethod.ReturnType;
            usesUnsafeAccessor = true;
            return true;
        }

        errorMessage = accessibleFailure ?? $"The name '{segment.MemberName}' does not exist in the current context";
        return false;
    }

    private bool TryResolveMemberAccess(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        CompiledBindingPathSegment segment,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        string fallbackClrNamespace,
        out string expression,
        out ITypeSymbol resultType,
        out bool usesUnsafeAccessor,
        out string errorMessage)
    {
        expression = string.Empty;
        resultType = receiverType;
        usesUnsafeAccessor = false;
        errorMessage = string.Empty;

        if (TryResolvePropertyOrFieldAccess(
                compilation,
                receiverType,
                receiverExpression,
                segment.MemberName,
                segment.AcceptsNull,
                accessibilityWithin,
                unsafeAccessors,
                out expression,
                out resultType,
                out usesUnsafeAccessor,
                out errorMessage))
        {
            if (segment.CastTypeToken is { Length: > 0 })
            {
                return TryResolveCastTypeExpression(
                    compilation,
                    document,
                    fallbackClrNamespace,
                    segment.CastTypeToken,
                    expression,
                    out expression,
                    out errorMessage);
            }

            return true;
        }

        if (!TryResolveParameterlessMethodAccess(
                compilation,
                document,
                sourceType,
                receiverType,
                receiverExpression,
                segment,
                accessibilityWithin,
                unsafeAccessors,
                fallbackClrNamespace,
                out expression,
                out resultType,
                out usesUnsafeAccessor,
                out errorMessage))
        {
            return false;
        }

        return true;
    }

    private bool TryResolvePropertyOrFieldAccess(
        Compilation compilation,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        string memberName,
        bool acceptsNull,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string expression,
        out ITypeSymbol resultType,
        out bool usesUnsafeAccessor,
        out string errorMessage)
    {
        expression = string.Empty;
        resultType = receiverType;
        usesUnsafeAccessor = false;
        errorMessage = string.Empty;

        IMethodSymbol? inaccessibleGetter = null;

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var property in current.GetMembers(memberName).OfType<IPropertySymbol>())
            {
                var getter = property.GetMethod;
                if (getter is null)
                {
                    continue;
                }

                if (compilation.IsSymbolAccessibleWithin(getter, accessibilityWithin, receiverType))
                {
                    expression = property.IsStatic
                        ? property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + property.Name
                        : receiverExpression + (acceptsNull ? "?." : ".") + property.Name;
                    resultType = property.Type;
                    return true;
                }

                if (!property.IsStatic && inaccessibleGetter is null)
                {
                    inaccessibleGetter = getter;
                }
            }

            foreach (var field in current.GetMembers(memberName).OfType<IFieldSymbol>())
            {
                if (!compilation.IsSymbolAccessibleWithin(field, accessibilityWithin, receiverType))
                {
                    continue;
                }

                expression = field.IsStatic
                    ? field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + field.Name
                    : receiverExpression + (acceptsNull ? "?." : ".") + field.Name;
                resultType = field.Type;
                return true;
            }
        }

        if (inaccessibleGetter is not null && CanEmitUnsafeAccessor(unsafeAccessors))
        {
            var unsafeAccessorMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, inaccessibleGetter);
            expression = BuildUnsafeAccessorInvocationExpression(
                unsafeAccessorMethodName,
                receiverExpression,
                ImmutableArray<string>.Empty);
            resultType = inaccessibleGetter.ReturnType;
            usesUnsafeAccessor = true;
            return true;
        }

        errorMessage = $"The name '{memberName}' does not exist in the current context";
        return false;
    }

    private bool TryResolveParameterlessMethodAccess(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        INamedTypeSymbol receiverType,
        string receiverExpression,
        CompiledBindingPathSegment segment,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        string fallbackClrNamespace,
        out string expression,
        out ITypeSymbol resultType,
        out bool usesUnsafeAccessor,
        out string errorMessage)
    {
        expression = string.Empty;
        resultType = receiverType;
        usesUnsafeAccessor = false;
        errorMessage = string.Empty;

        var parameterlessMethod = TypeSymbolLookupSemanticsService.FindAccessibleParameterlessMethod(
            compilation,
            accessibilityWithin,
            receiverType,
            segment.MemberName,
            out _);
        string? accessibleFailure = null;
        if (parameterlessMethod is not null)
        {
            if (parameterlessMethod.ReturnsVoid)
            {
                errorMessage = $"'{segment.MemberName}' is not a supported parameterless method with a return value";
                return false;
            }

            expression = BuildMethodInvocationExpression(
                parameterlessMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                parameterlessMethod.Name,
                parameterlessMethod.IsStatic,
                receiverExpression,
                ImmutableArray<string>.Empty);
            if (segment.CastTypeToken is { Length: > 0 })
            {
                if (!TryResolveCastTypeExpression(
                        compilation,
                        document,
                        fallbackClrNamespace,
                        segment.CastTypeToken,
                        expression,
                        out expression,
                        out errorMessage))
                {
                    return false;
                }
            }

            if (!TryAnalyzeLoweredExpression(
                    compilation,
                    sourceType,
                    expression,
                    out var analyzedType,
                    out var accessibleError))
            {
                accessibleFailure = accessibleError;
            }
            else
            {
                resultType = analyzedType ?? parameterlessMethod.ReturnType;
                return true;
            }
        }

        if (!CanEmitUnsafeAccessor(unsafeAccessors))
        {
            errorMessage = accessibleFailure ?? $"The name '{segment.MemberName}' does not exist in the current context";
            return false;
        }

        foreach (var current in TypeSymbolLookupSemanticsService.EnumerateInstanceMemberLookupTypes(receiverType))
        {
            foreach (var method in current.GetMembers(segment.MemberName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.Parameters.Length != 0)
                {
                    continue;
                }

                if (method.ReturnsVoid)
                {
                    errorMessage = $"'{segment.MemberName}' is not a supported parameterless method with a return value";
                    return false;
                }

                if (!TryAnalyzeUnsafeAccessorInvocation(
                        compilation,
                        sourceType,
                        method,
                        receiverExpression,
                        ImmutableArray<string>.Empty,
                        out var unsafeAccessorError))
                {
                    accessibleFailure ??= unsafeAccessorError;
                    continue;
                }

                var unsafeAccessorMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, method);
                expression = BuildUnsafeAccessorInvocationExpression(
                    unsafeAccessorMethodName,
                    receiverExpression,
                    ImmutableArray<string>.Empty);
                if (segment.CastTypeToken is { Length: > 0 })
                {
                    if (!TryResolveCastTypeExpression(
                            compilation,
                            document,
                            fallbackClrNamespace,
                            segment.CastTypeToken,
                            expression,
                            out expression,
                            out errorMessage))
                    {
                        return false;
                    }
                }

                resultType = method.ReturnType;
                usesUnsafeAccessor = true;
                return true;
            }
        }

        errorMessage = accessibleFailure ?? $"The name '{segment.MemberName}' does not exist in the current context";
        return false;
    }

    private bool TryResolveCastTypeExpression(
        Compilation compilation,
        XamlDocumentModel document,
        string fallbackClrNamespace,
        string castTypeToken,
        string expression,
        out string castExpression,
        out string errorMessage)
    {
        castExpression = string.Empty;
        errorMessage = string.Empty;

        var castType = _resolveTypeToken(compilation, document, castTypeToken, fallbackClrNamespace);
        if (castType is null)
        {
            errorMessage = $"Unable to resolve cast type '{castTypeToken}'.";
            return false;
        }

        castExpression = "((" +
                         castType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         ")" +
                         expression +
                         ")";
        return true;
    }

    private bool TryAdvanceExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string candidateExpression,
        ITypeSymbol candidateType,
        bool skipValidation,
        ref string currentExpression,
        ref ITypeSymbol currentType,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (skipValidation ||
            candidateExpression.Contains("SourceGenCompiledBindingStreamHelper", StringComparison.Ordinal) ||
            candidateExpression.Contains("SourceGenMethodCommandRuntime", StringComparison.Ordinal))
        {
            currentExpression = candidateExpression;
            currentType = candidateType;
            return true;
        }

        if (!TryAnalyzeLoweredExpression(
                compilation,
                sourceType,
                candidateExpression,
                out var resultType,
                out errorMessage))
        {
            return false;
        }

        currentExpression = candidateExpression;
        currentType = resultType is null
            ? candidateType
            : NormalizeTypeToCompilation(compilation, resultType, candidateType);
        return true;
    }

    private bool TryApplyConditionalGuard(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string guardExpression,
        string branchExpression,
        ITypeSymbol branchType,
        ref bool hasTopLevelConditionalGuard,
        ref string topLevelConditionalGuardExpression,
        ref string topLevelConditionalTempName,
        ref string currentExpression,
        ref ITypeSymbol currentType,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!hasTopLevelConditionalGuard)
        {
            hasTopLevelConditionalGuard = true;
            topLevelConditionalGuardExpression = guardExpression;
            topLevelConditionalTempName = BuildConditionalTempName(guardExpression + "|" + branchExpression);
            currentExpression = ReplaceReceiverExpression(guardExpression, topLevelConditionalTempName, branchExpression);
            currentType = branchType;
            return true;
        }

        var nestedTempName = BuildConditionalTempName(guardExpression + "|" + branchExpression);
        var rewrittenBranchExpression = ReplaceReceiverExpression(guardExpression, nestedTempName, branchExpression);
        var conditionalExpression = BuildConditionalExpression(
            compilation,
            guardExpression,
            nestedTempName,
            rewrittenBranchExpression,
            branchType,
            out var conditionalType);
        if (!TryAdvanceExpression(
                compilation,
                sourceType,
                conditionalExpression,
                conditionalType,
                skipValidation: true,
                ref currentExpression,
                ref currentType,
                out errorMessage))
        {
            return false;
        }

        return true;
    }

    private static string ReplaceReceiverExpression(string receiverExpression, string replacement, string expression)
    {
        return expression.Equals(receiverExpression, StringComparison.Ordinal)
            ? replacement
            : ReplaceOrdinal(expression, receiverExpression, replacement);
    }

    private static string BuildConditionalTempName(string seed)
    {
        return "__axsg_target_" + StableHashSemantics.ComputeFnv1aHex(seed);
    }

    private bool TryBuildStreamUnwrapExpression(
        Compilation compilation,
        string currentExpression,
        ITypeSymbol currentType,
        out string unwrappedExpression,
        out ITypeSymbol unwrappedType,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        if (TryGetTaskResultType(compilation, currentType, out var taskResultType))
        {
            unwrappedType = taskResultType;
            unwrappedExpression =
                "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask<" +
                taskResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">(" +
                currentExpression +
                ")";
            return true;
        }

        if (TryGetObservableResultType(compilation, currentType, out var observableResultType))
        {
            unwrappedType = observableResultType;
            unwrappedExpression =
                "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapObservable<" +
                observableResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">(" +
                currentExpression +
                ")";
            return true;
        }

        unwrappedType = currentType;
        unwrappedExpression = string.Empty;
        errorMessage =
            "Stream operator '^' is only valid for values assignable to Task<T> or IObservable<T>.";
        return false;
    }

    private bool TryGetTaskResultType(
        Compilation compilation,
        ITypeSymbol type,
        out ITypeSymbol resultType)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1 &&
            MatchesGenericContract(compilation, namedType, TypeContractId.SystemTaskOfT))
        {
            resultType = namedType.TypeArguments[0];
            return true;
        }

        resultType = type;
        return false;
    }

    private bool TryGetObservableResultType(
        Compilation compilation,
        ITypeSymbol type,
        out ITypeSymbol resultType)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.TypeArguments.Length == 1 &&
            MatchesGenericContract(compilation, namedType, TypeContractId.SystemObservableOfT))
        {
            resultType = namedType.TypeArguments[0];
            return true;
        }

        if (type is INamedTypeSymbol named &&
            named.AllInterfaces.FirstOrDefault(interfaceType =>
                interfaceType.IsGenericType &&
                interfaceType.TypeArguments.Length == 1 &&
                MatchesGenericContract(compilation, interfaceType, TypeContractId.SystemObservableOfT)) is { } observableInterface)
        {
            resultType = observableInterface.TypeArguments[0];
            return true;
        }

        resultType = type;
        return false;
    }

    private bool MatchesGenericContract(
        Compilation compilation,
        INamedTypeSymbol candidate,
        TypeContractId contractId)
    {
        var contractType = _resolveContractType(compilation, contractId);
        if (contractType is not INamedTypeSymbol namedContractType)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, namedContractType.OriginalDefinition) ||
               _isTypeAssignableTo(candidate, namedContractType);
    }

    private static string NormalizeCompiledBindingArgument(string rawValue)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length >= 2 &&
            trimmed[0] == '\'' &&
            trimmed[trimmed.Length - 1] == '\'')
        {
            var content = trimmed.Substring(1, trimmed.Length - 2)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"");
            return "\"" + content + "\"";
        }

        return CSharpExpressionTextSemantics.NormalizeExpressionCode(trimmed);
    }

    private static bool CanEmitUnsafeAccessor(
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors)
    {
        return unsafeAccessors is not null;
    }

    private static string BuildMethodInvocationExpression(
        string containingTypeName,
        string methodName,
        bool isStatic,
        string receiverExpression,
        ImmutableArray<string> arguments)
    {
        var targetExpression = isStatic
            ? containingTypeName + "." + methodName
            : receiverExpression + "." + methodName;
        return targetExpression + "(" + string.Join(", ", arguments) + ")";
    }

    private static string BuildMethodInvocationExpression(
        string methodExpression,
        string firstArgument,
        ImmutableArray<string> remainingArguments)
    {
        if (remainingArguments.IsDefaultOrEmpty)
        {
            return methodExpression + "(" + firstArgument + ")";
        }

        return methodExpression + "(" + firstArgument + ", " + string.Join(", ", remainingArguments) + ")";
    }

    private static string BuildUnsafeAccessorInvocationExpression(
        string methodName,
        string receiverExpression,
        ImmutableArray<string> arguments)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return methodName + "(" + receiverExpression + ")";
        }

        return methodName + "(" + receiverExpression + ", " + string.Join(", ", arguments) + ")";
    }

    private static string BuildConditionalExpression(
        Compilation compilation,
        string guardExpression,
        string tempName,
        string trueExpression,
        ITypeSymbol branchType,
        out ITypeSymbol resultType)
    {
        if (branchType.IsValueType && !IsNullableValueType(branchType))
        {
            var nullableType = BuildNullableType(compilation, branchType);
            var nullableTypeDisplay = BuildExplicitNullableTypeDisplay(branchType);
            resultType = nullableType;
            return guardExpression +
                   " is { } " +
                   tempName +
                   " ? (" +
                   nullableTypeDisplay +
                   ")" +
                   trueExpression +
                   " : default(" +
                   nullableTypeDisplay +
                   ")";
        }

        resultType = branchType;
        return guardExpression +
               " is { } " +
               tempName +
               " ? " +
               trueExpression +
               " : null";
    }

    private static ITypeSymbol BuildNullableType(Compilation compilation, ITypeSymbol valueType)
    {
        var nullableType = compilation.GetSpecialType(SpecialType.System_Nullable_T);
        if (nullableType is INamedTypeSymbol namedNullableType)
        {
            return namedNullableType.Construct(valueType);
        }

        return valueType;
    }

    private static string BuildExplicitNullableTypeDisplay(ITypeSymbol valueType)
    {
        var innerTypeDisplay = valueType.SpecialType == SpecialType.None
            ? valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : valueType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return "global::System.Nullable<" + innerTypeDisplay + ">";
    }

    private static string ReplaceOrdinal(string source, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(oldValue))
        {
            return source;
        }

        var firstMatch = source.IndexOf(oldValue, StringComparison.Ordinal);
        if (firstMatch < 0)
        {
            return source;
        }

        var builder = new System.Text.StringBuilder(source.Length);
        var copyIndex = 0;
        var matchIndex = firstMatch;
        while (matchIndex >= 0)
        {
            builder.Append(source, copyIndex, matchIndex - copyIndex);
            builder.Append(newValue);
            copyIndex = matchIndex + oldValue.Length;
            matchIndex = source.IndexOf(oldValue, copyIndex, StringComparison.Ordinal);
        }

        builder.Append(source, copyIndex, source.Length - copyIndex);
        return builder.ToString();
    }

    private static bool IsNullableValueType(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static int GetMethodCompatibilityScore(
        IMethodSymbol method,
        ImmutableArray<string> normalizedArguments)
    {
        if (method.Parameters.Length != normalizedArguments.Length)
        {
            return -1;
        }

        var score = 0;
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameterScore = GetArgumentCompatibilityScore(method.Parameters[index].Type, normalizedArguments[index]);
            if (parameterScore < 0)
            {
                return -1;
            }

            score += parameterScore;
        }

        return score;
    }

    private static int GetArgumentCompatibilityScore(ITypeSymbol parameterType, string argument)
    {
        if (argument.Length >= 2 &&
            argument[0] == '"' &&
            argument[argument.Length - 1] == '"')
        {
            if (parameterType.SpecialType == SpecialType.System_String)
            {
                return 4;
            }

            if (parameterType.SpecialType == SpecialType.System_Object ||
                parameterType.IsReferenceType)
            {
                return 2;
            }

            return -1;
        }

        if (int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return parameterType.SpecialType switch
            {
                SpecialType.System_Int32 => 4,
                SpecialType.System_Int64 or
                SpecialType.System_Single or
                SpecialType.System_Double or
                SpecialType.System_Decimal or
                SpecialType.System_Object => 2,
                _ => -1
            };
        }

        if (string.Equals(argument, "true", StringComparison.Ordinal) ||
            string.Equals(argument, "false", StringComparison.Ordinal))
        {
            return parameterType.SpecialType switch
            {
                SpecialType.System_Boolean => 4,
                SpecialType.System_Object => 2,
                _ => -1
            };
        }

        return 1;
    }

    private static ITypeSymbol NormalizeTypeToCompilation(
        Compilation compilation,
        ITypeSymbol analyzedType,
        ITypeSymbol fallbackType)
    {
        if (analyzedType.TypeKind == TypeKind.Error)
        {
            return fallbackType;
        }

        if (analyzedType.SpecialType != SpecialType.None)
        {
            return compilation.GetSpecialType(analyzedType.SpecialType);
        }

        if (analyzedType is IArrayTypeSymbol arrayType)
        {
            var elementType = NormalizeTypeToCompilation(compilation, arrayType.ElementType, arrayType.ElementType);
            return compilation.CreateArrayTypeSymbol(elementType, arrayType.Rank);
        }

        if (analyzedType is INamedTypeSymbol namedType)
        {
            var metadataName = namedType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            var resolvedNamedType = compilation.GetTypeByMetadataName(metadataName);
            if (resolvedNamedType is null)
            {
                return fallbackType;
            }

            if (!namedType.IsGenericType || namedType.TypeArguments.Length == 0)
            {
                return resolvedNamedType.WithNullableAnnotation(analyzedType.NullableAnnotation);
            }

            var normalizedTypeArguments = namedType.TypeArguments
                .Select(typeArgument => NormalizeTypeToCompilation(compilation, typeArgument, typeArgument))
                .ToArray();
            return resolvedNamedType.Construct(normalizedTypeArguments)
                .WithNullableAnnotation(analyzedType.NullableAnnotation);
        }

        return fallbackType;
    }

    private static bool TryAnalyzeLoweredExpression(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        string candidateExpression,
        out ITypeSymbol? resultType,
        out string errorMessage)
    {
        resultType = null;
        errorMessage = string.Empty;

        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_CompiledBindingAnalysis",
            "{",
            "    internal static class __Context",
            "    {",
            "        internal static object? __Evaluate(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " __source) => default;",
            "    }",
            "}");
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationRoot = validationTree.GetRoot();
        var placeholderExpression = validationRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText == "__Evaluate")
            .Select(static method => method.ExpressionBody?.Expression)
            .FirstOrDefault();
        if (placeholderExpression is null)
        {
            errorMessage = "compiled binding analysis tree did not contain the evaluation expression";
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(candidateExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var updatedRoot = validationRoot.ReplaceNode(
            placeholderExpression,
            parsedExpression.WithoutTrivia());
        validationTree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var validationCompilation = compilation.AddSyntaxTrees(validationTree);
        var validationDiagnostic = validationCompilation.GetDiagnostics()
            .FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == validationTree);
        if (validationDiagnostic is not null)
        {
            errorMessage = validationDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var expressionSyntax = validationTree.GetRoot().DescendantNodes()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static clause => clause.Expression)
            .FirstOrDefault();
        if (expressionSyntax is null)
        {
            errorMessage = "compiled binding analysis tree did not contain the evaluation expression";
            return false;
        }

        var semanticModel = validationCompilation.GetSemanticModel(validationTree, ignoreAccessibility: true);
        var typeInfo = semanticModel.GetTypeInfo(expressionSyntax);
        resultType = typeInfo.Type ?? typeInfo.ConvertedType;
        return true;
    }

    private static bool TryAnalyzeUnsafeAccessorInvocation(
        Compilation compilation,
        INamedTypeSymbol sourceType,
        IMethodSymbol targetMethod,
        string receiverExpression,
        ImmutableArray<string> arguments,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        var helperName = "__AXSG_UnsafeAccessorValidation";
        var parameterList = new List<string>(capacity: targetMethod.Parameters.Length + 1)
        {
            targetMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " __instance"
        };

        for (var index = 0; index < targetMethod.Parameters.Length; index++)
        {
            parameterList.Add(
                targetMethod.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                " __arg" +
                index.ToString(CultureInfo.InvariantCulture));
        }

        var invocationExpression = BuildUnsafeAccessorInvocationExpression(helperName, receiverExpression, arguments);
        var parseOptions = compilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions;
        var validationSource = string.Join(
            Environment.NewLine,
            "namespace __AXSG_CompiledBindingAnalysis",
            "{",
            "    internal static class __Context",
            "    {",
            "        private static " +
            targetMethod.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " " +
            helperName +
            "(" +
            string.Join(", ", parameterList) +
            ") => default!;",
            "        internal static object? __Evaluate(" +
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " __source) => default;",
            "    }",
            "}");
        var validationTree = CSharpSyntaxTree.ParseText(validationSource, parseOptions);
        var validationRoot = validationTree.GetRoot();
        var placeholderExpression = validationRoot.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.Identifier.ValueText == "__Evaluate")
            .Select(static method => method.ExpressionBody?.Expression)
            .FirstOrDefault();
        if (placeholderExpression is null)
        {
            errorMessage = "compiled binding analysis tree did not contain the evaluation expression";
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(invocationExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null)
        {
            errorMessage = parseDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        var updatedRoot = validationRoot.ReplaceNode(
            placeholderExpression,
            parsedExpression.WithoutTrivia());
        validationTree = CSharpSyntaxTree.Create((CSharpSyntaxNode)updatedRoot, parseOptions);
        var validationCompilation = compilation.AddSyntaxTrees(validationTree);
        var validationDiagnostic = validationCompilation.GetDiagnostics()
            .FirstOrDefault(diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.SourceTree == validationTree);
        if (validationDiagnostic is not null)
        {
            errorMessage = validationDiagnostic.GetMessage(CultureInfo.InvariantCulture);
            return false;
        }

        return true;
    }
}
