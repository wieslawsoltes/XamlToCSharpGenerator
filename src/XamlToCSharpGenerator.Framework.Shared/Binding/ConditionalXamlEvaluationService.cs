using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class ConditionalXamlEvaluationService
{
    public bool ShouldSkipBranch(
        ConditionalXamlExpression? condition,
        Compilation compilation,
        XamlDocumentModel document,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        GeneratorOptions options)
    {
        if (condition is null)
        {
            return false;
        }

        var methodName = NormalizeMethodName(condition.MethodName);
        if (methodName.Equals("IsTypePresent", StringComparison.Ordinal) ||
            methodName.Equals("IsTypeNotPresent", StringComparison.Ordinal))
        {
            if (condition.Arguments.Length != 1)
            {
                ReportInvalidCondition(
                    diagnostics,
                    document,
                    condition,
                    "ApiInformation.IsTypePresent expects a single type-name argument.",
                    options);
                return false;
            }

            var typeToken = Unquote(condition.Arguments[0]);
            if (string.IsNullOrWhiteSpace(typeToken))
            {
                return true;
            }

            var typeName = XamlTypeTokenSemantics.TrimGlobalQualifier(typeToken);
            var isTypePresent = compilation.GetTypeByMetadataName(typeName) is not null;
            return methodName.Equals("IsTypePresent", StringComparison.Ordinal)
                ? !isTypePresent
                : isTypePresent;
        }

        ReportInvalidCondition(
            diagnostics,
            document,
            condition,
            "Unsupported conditional XAML method '" + condition.MethodName + "'.",
            options);
        return false;
    }

    private static void ReportInvalidCondition(
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        ConditionalXamlExpression condition,
        string message,
        GeneratorOptions options)
    {
        diagnostics.Add(new DiagnosticInfo(
            "AXSG0120",
            message,
            document.FilePath,
            condition.Line,
            condition.Column,
            options.StrictMode));
    }

    private static string Unquote(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 2 &&
               ((trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'') ||
                (trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"'))
            ? trimmed.Substring(1, trimmed.Length - 2)
            : trimmed;
    }

    private static string NormalizeMethodName(string methodName)
    {
        if (string.IsNullOrWhiteSpace(methodName))
        {
            return string.Empty;
        }

        const string apiInformationPrefix = "ApiInformation.";
        return methodName.StartsWith(apiInformationPrefix, StringComparison.Ordinal)
            ? methodName.Substring(apiInformationPrefix.Length)
            : methodName;
    }
}
