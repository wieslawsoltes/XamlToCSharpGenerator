using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;
using XamlToCSharpGenerator.LanguageService.Text;

namespace XamlToCSharpGenerator.LanguageService.InlineCode;

public sealed class XamlInlineCSharpProjectionService
{
    public ImmutableArray<XamlInlineCSharpProjection> GetProjections(XamlAnalysisResult analysis)
    {
        if (analysis.XmlDocument?.Root is null || string.IsNullOrWhiteSpace(analysis.Document.Text))
        {
            return ImmutableArray<XamlInlineCSharpProjection>.Empty;
        }

        var contexts = XamlInlineCSharpNavigationService.EnumerateContexts(analysis, allowIncompleteExpressions: true);
        if (contexts.IsDefaultOrEmpty)
        {
            return ImmutableArray<XamlInlineCSharpProjection>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<XamlInlineCSharpProjection>(contexts.Length);
        for (var index = 0; index < contexts.Length; index++)
        {
            var context = contexts[index];
            if (!TryBuildProjection(context, out var projectionText, out var projectedCodeRange))
            {
                continue;
            }

            var projectionId = CreateProjectionId(context, index);
            builder.Add(new XamlInlineCSharpProjection(
                projectionId,
                GetProjectionKind(context),
                context.CodeRange,
                projectedCodeRange,
                projectionText));
        }

        return builder.ToImmutable();
    }

    private static bool TryBuildProjection(
        XamlInlineCSharpContext context,
        out string projectionText,
        out SourceRange projectedCodeRange)
    {
        projectionText = string.Empty;
        projectedCodeRange = default;

        var sourceCode = context.RawCode ?? string.Empty;
        if (context.IsLambda)
        {
            if (context.EventHandlerType is null)
            {
                return false;
            }

            projectionText = BuildLambdaProjection(context, sourceCode, out var codeStartOffset);
            projectedCodeRange = CreateRange(projectionText, codeStartOffset, sourceCode.Length);
            return true;
        }

        if (context.IsEventCode)
        {
            projectionText = BuildStatementProjection(context, sourceCode, out var codeStartOffset);
            projectedCodeRange = CreateRange(projectionText, codeStartOffset, sourceCode.Length);
            return true;
        }

        projectionText = BuildExpressionProjection(context, sourceCode, out var expressionStartOffset);
        projectedCodeRange = CreateRange(projectionText, expressionStartOffset, sourceCode.Length);
        return true;
    }

    private static string BuildExpressionProjection(
        XamlInlineCSharpContext context,
        string sourceCode,
        out int codeStartOffset)
    {
        var builder = new StringBuilder(256 + sourceCode.Length);
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace __AXSG_InlineProjection");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class __Context");
        builder.AppendLine("    {");
        builder.Append("        internal static object? __Evaluate(");
        AppendCommonContextParameters(builder, context);
        builder.Append(") => ");
        codeStartOffset = builder.Length;
        builder.Append(sourceCode);
        builder.AppendLine(";");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildLambdaProjection(
        XamlInlineCSharpContext context,
        string sourceCode,
        out int codeStartOffset)
    {
        var builder = new StringBuilder(256 + sourceCode.Length);
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace __AXSG_InlineProjection");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class __Context");
        builder.AppendLine("    {");
        builder.Append("        internal static ");
        builder.Append(GetTypeName(context.EventHandlerType));
        builder.Append(" __Bind(");
        AppendCommonContextParameters(builder, context);
        builder.Append(") => ");
        codeStartOffset = builder.Length;
        builder.Append(sourceCode);
        builder.AppendLine(";");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string BuildStatementProjection(
        XamlInlineCSharpContext context,
        string sourceCode,
        out int codeStartOffset)
    {
        var builder = new StringBuilder(320 + sourceCode.Length);
        builder.AppendLine("#nullable enable");
        builder.AppendLine("namespace __AXSG_InlineProjection");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class __Context");
        builder.AppendLine("    {");
        builder.Append("        internal static void __Execute(");
        AppendCommonContextParameters(builder, context);

        if (context.EventHandlerType?.DelegateInvokeMethod is IMethodSymbol invokeMethod)
        {
            for (var index = 0; index < invokeMethod.Parameters.Length; index++)
            {
                builder.Append(", ");
                builder.Append(invokeMethod.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                builder.Append(" arg");
                builder.Append(index.ToString(CultureInfo.InvariantCulture));
            }
        }

        builder.AppendLine(")");
        builder.AppendLine("        {");

        if (context.EventHandlerType?.DelegateInvokeMethod is IMethodSymbol aliasedInvokeMethod)
        {
            if (aliasedInvokeMethod.Parameters.Length > 0)
            {
                builder.AppendLine("            var sender = arg0;");
            }
            else
            {
                builder.AppendLine("            object? sender = null;");
            }

            if (aliasedInvokeMethod.Parameters.Length > 1)
            {
                builder.AppendLine("            var e = arg1;");
            }
            else
            {
                builder.AppendLine("            object? e = null;");
            }
        }

        codeStartOffset = builder.Length;
        builder.Append(sourceCode);
        if (sourceCode.Length > 0 && sourceCode[sourceCode.Length - 1] != '\n')
        {
            builder.AppendLine();
        }

        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendCommonContextParameters(StringBuilder builder, XamlInlineCSharpContext context)
    {
        builder.Append(GetTypeName(context.SourceType));
        builder.Append(" source, ");
        builder.Append(GetTypeName(context.RootType));
        builder.Append(" root, ");
        builder.Append(GetTypeName(context.TargetType));
        builder.Append(" target");
    }

    private static string GetTypeName(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Object?";
    }

    private static string CreateProjectionId(XamlInlineCSharpContext context, int index)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{index}:{context.CodeRange.Start.Line}:{context.CodeRange.Start.Character}:{context.CodeRange.End.Line}:{context.CodeRange.End.Character}:{GetProjectionKind(context)}");
    }

    private static string GetProjectionKind(XamlInlineCSharpContext context)
    {
        if (context.IsLambda)
        {
            return "lambda";
        }

        return context.IsEventCode ? "statements" : "expression";
    }

    private static SourceRange CreateRange(string text, int startOffset, int length)
    {
        return new SourceRange(
            TextCoordinateHelper.GetPosition(text, startOffset),
            TextCoordinateHelper.GetPosition(text, startOffset + length));
    }
}
