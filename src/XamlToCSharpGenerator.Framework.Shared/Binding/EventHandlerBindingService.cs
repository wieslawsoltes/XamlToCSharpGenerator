using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class EventHandlerBindingService
{
    private readonly Func<ITypeSymbol, ITypeSymbol, bool> _isTypeAssignableTo;
    private readonly string _rootObjectToken;

    public EventHandlerBindingService(
        Func<ITypeSymbol, ITypeSymbol, bool> isTypeAssignableTo,
        string rootObjectToken)
    {
        _isTypeAssignableTo = isTypeAssignableTo ?? throw new ArgumentNullException(nameof(isTypeAssignableTo));
        _rootObjectToken = rootObjectToken ?? throw new ArgumentNullException(nameof(rootObjectToken));
    }

    public bool TryParseHandlerName(string value, out string? handlerName)
    {
        handlerName = null;
        if (!XamlEventHandlerNameSemantics.TryParseHandlerName(value, out var parsedHandlerName))
        {
            return false;
        }

        handlerName = parsedHandlerName;
        return true;
    }

    public bool TryBuildDelegateMethodGroupValueExpression(
        string value,
        INamedTypeSymbol delegateType,
        INamedTypeSymbol? rootTypeSymbol,
        out string expression)
    {
        expression = string.Empty;
        if (rootTypeSymbol is null ||
            !TryParseHandlerName(value, out var handlerMethodName) ||
            string.IsNullOrWhiteSpace(handlerMethodName))
        {
            return false;
        }

        if (!HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, delegateType))
        {
            return false;
        }

        expression = "new " +
                     delegateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(((" +
                     rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     ")" +
                     _rootObjectToken +
                     ")." +
                     handlerMethodName +
                     ")";
        return true;
    }

    public bool HasCompatibleInstanceMethod(
        INamedTypeSymbol type,
        string methodName,
        ITypeSymbol delegateType)
    {
        if (delegateType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod)
        {
            return HasInstanceMethod(type, methodName);
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.MethodKind != MethodKind.Ordinary)
                {
                    continue;
                }

                if (IsMethodCompatibleWithDelegate(method, invokeMethod))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasInstanceMethod(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary);
            if (method is not null)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsMethodCompatibleWithDelegate(
        IMethodSymbol candidate,
        IMethodSymbol delegateInvoke)
    {
        if (candidate.Parameters.Length != delegateInvoke.Parameters.Length)
        {
            return false;
        }

        if (delegateInvoke.ReturnsVoid != candidate.ReturnsVoid)
        {
            return false;
        }

        if (!delegateInvoke.ReturnsVoid &&
            !_isTypeAssignableTo(candidate.ReturnType, delegateInvoke.ReturnType))
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < delegateInvoke.Parameters.Length; parameterIndex++)
        {
            var delegateParameter = delegateInvoke.Parameters[parameterIndex];
            var candidateParameter = candidate.Parameters[parameterIndex];
            if (!_isTypeAssignableTo(delegateParameter.Type, candidateParameter.Type))
            {
                return false;
            }
        }

        return true;
    }
}
