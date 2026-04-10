using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using XamlToCSharpGenerator.Core.Models;

namespace XamlToCSharpGenerator.Framework.Shared.Emission;

public sealed class CompiledBindingEmissionService
{
    public CompiledBindingAccessorEmissionPlan BuildEmissionPlan(
        ImmutableArray<ResolvedCompiledBindingDefinition> compiledBindings)
    {
        if (compiledBindings.IsDefaultOrEmpty)
        {
            return new CompiledBindingAccessorEmissionPlan(
                ImmutableArray<CompiledBindingAccessorEmissionMethod>.Empty,
                new Dictionary<int, string>(),
                new Dictionary<string, string>(StringComparer.Ordinal));
        }

        var methods = ImmutableArray.CreateBuilder<CompiledBindingAccessorEmissionMethod>();
        var methodBySignature = new Dictionary<string, CompiledBindingAccessorEmissionMethod>(StringComparer.Ordinal);
        var objectMethodNamesByIndex = new Dictionary<int, string>();
        var methodNamesByPlaceholder = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var index = 0; index < compiledBindings.Length; index++)
        {
            var compiledBinding = compiledBindings[index];
            var signatureKey = BuildSignatureKey(compiledBinding);
            if (!methodBySignature.TryGetValue(signatureKey, out var method))
            {
                var methodName = "__AXSG_CompiledBindingAccessorMethod_" + methods.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var bindingMethodName = "__AXSG_CompiledBinding_" + methods.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var objectMethodName = "__AXSG_CompiledBindingObjectAccessorMethod_" + methods.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                method = new CompiledBindingAccessorEmissionMethod(
                    index,
                    methodName,
                    bindingMethodName,
                    objectMethodName,
                    compiledBinding.SourceTypeName,
                    signatureKey);
                methodBySignature.Add(signatureKey, method);
                methods.Add(method);
            }

            objectMethodNamesByIndex[index] = method.ObjectMethodName;
            if (!string.IsNullOrWhiteSpace(compiledBinding.AccessorPlaceholderToken))
            {
                methodNamesByPlaceholder[compiledBinding.AccessorPlaceholderToken!] = method.BindingMethodName;
            }
        }

        return new CompiledBindingAccessorEmissionPlan(
            methods.ToImmutable(),
            objectMethodNamesByIndex,
            methodNamesByPlaceholder);
    }

    public string ResolveObjectAccessorMethodName(
        int compiledBindingIndex,
        CompiledBindingAccessorEmissionPlan emissionPlan)
    {
        if (emissionPlan.ObjectMethodNamesByCompiledBindingIndex.TryGetValue(compiledBindingIndex, out var methodName))
        {
            return methodName;
        }

        return "__CompiledBindingAccessor";
    }

    public string RewriteAccessorPlaceholders(
        string source,
        CompiledBindingAccessorEmissionPlan emissionPlan)
    {
        if (string.IsNullOrEmpty(source) || emissionPlan.MethodNamesByPlaceholderToken.Count == 0)
        {
            return source;
        }

        foreach (var replacement in emissionPlan.MethodNamesByPlaceholderToken
                     .OrderByDescending(static pair => pair.Key.Length)
                     .ThenBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            source = ReplaceOrdinal(source, replacement.Key, replacement.Value);
        }

        return source;
    }

    public string RewriteSourceReceiver(
        string source,
        string oldReceiver,
        string newReceiver)
    {
        if (string.IsNullOrEmpty(source) ||
            string.IsNullOrEmpty(oldReceiver) ||
            string.Equals(oldReceiver, newReceiver, StringComparison.Ordinal))
        {
            return source;
        }

        return ReplaceOrdinal(source, oldReceiver, newReceiver);
    }

    private static string BuildSignatureKey(ResolvedCompiledBindingDefinition compiledBinding)
    {
        var builder = new StringBuilder(compiledBinding.SourceTypeName.Length + compiledBinding.AccessorExpression.Length + 64);
        builder.Append(compiledBinding.SourceTypeName);
        builder.Append('|');
        builder.Append(compiledBinding.ResultTypeName);
        builder.Append('|');
        builder.Append(compiledBinding.AccessorExpression);
        builder.Append('|');
        builder.Append(compiledBinding.IsSetterBinding ? '1' : '0');
        return builder.ToString();
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

        var builder = new StringBuilder(source.Length);
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
}
