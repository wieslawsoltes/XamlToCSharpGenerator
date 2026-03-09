using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.LanguageService.Definitions;
using XamlToCSharpGenerator.LanguageService.Models;

namespace XamlToCSharpGenerator.LanguageService.Completion;

internal static class XamlInlineCSharpCompletionService
{
    public static bool TryGetCompletions(
        XamlAnalysisResult analysis,
        SourcePosition position,
        out ImmutableArray<XamlCompletionItem> completions)
    {
        completions = ImmutableArray<XamlCompletionItem>.Empty;
        if (!XamlInlineCSharpNavigationService.TryFindContextAtPosition(
                analysis,
                position,
                allowIncompleteExpressions: true,
                out var context,
                out var caretOffsetInCode))
        {
            return false;
        }

        if (!TryResolveCompletionTarget(
                analysis,
                context,
                caretOffsetInCode,
                out var receiverType,
                out var memberPrefix,
                out var allowContextVariableCompletions))
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<XamlCompletionItem>();
        if (allowContextVariableCompletions)
        {
            AddContextVariableCompletions(builder, context, memberPrefix);
            AddLambdaParameterCompletions(builder, context, memberPrefix);
            AddScopedSymbolCompletions(builder, context, caretOffsetInCode, memberPrefix);
            AddTopLevelMemberCompletions(builder, context, memberPrefix);
        }
        else if (receiverType is not null)
        {
            builder.AddRange(XamlClrMemberCompletionFactory.CreateMemberCompletions(
                receiverType,
                memberPrefix,
                XamlMemberCompletionMode.Expression));
        }

        completions = builder
            .ToImmutable()
            .DistinctBy(static item => item.Label, StringComparer.Ordinal)
            .ToImmutableArray();
        return completions.Length > 0;
    }

    private static bool TryResolveCompletionTarget(
        XamlAnalysisResult analysis,
        XamlInlineCSharpContext context,
        int caretOffsetInCode,
        out INamedTypeSymbol? receiverType,
        out string memberPrefix,
        out bool allowContextVariableCompletions)
    {
        receiverType = null;
        memberPrefix = string.Empty;
        allowContextVariableCompletions = true;

        if (caretOffsetInCode < 0 || caretOffsetInCode > context.RawCode.Length)
        {
            return false;
        }

        var prefixStart = caretOffsetInCode;
        while (prefixStart > 0 && IsIdentifierPart(context.RawCode[prefixStart - 1]))
        {
            prefixStart--;
        }

        memberPrefix = context.RawCode.Substring(prefixStart, caretOffsetInCode - prefixStart);
        if (memberPrefix.Length > 0 && !IsIdentifierPrefix(memberPrefix))
        {
            return false;
        }

        if (prefixStart == 0 || context.RawCode[prefixStart - 1] != '.')
        {
            return true;
        }

        var receiverStart = FindReceiverExpressionStart(context.RawCode, prefixStart - 1);
        if (receiverStart < 0 || receiverStart >= prefixStart - 1)
        {
            return false;
        }

        var receiverExpression = context.RawCode.Substring(receiverStart, prefixStart - receiverStart - 1).Trim();
        if (receiverExpression.Length == 0)
        {
            return false;
        }

        receiverType = ResolveReceiverExpressionType(analysis, context, receiverExpression, receiverStart);
        allowContextVariableCompletions = false;
        return receiverType is not null;
    }

    private static INamedTypeSymbol? ResolveReceiverExpressionType(
        XamlAnalysisResult analysis,
        XamlInlineCSharpContext context,
        string receiverExpression,
        int beforeOffset)
    {
        if (string.Equals(receiverExpression, "source", StringComparison.Ordinal))
        {
            return context.SourceType;
        }

        if (string.Equals(receiverExpression, "root", StringComparison.Ordinal))
        {
            return context.RootType;
        }

        if (string.Equals(receiverExpression, "target", StringComparison.Ordinal))
        {
            return context.TargetType;
        }

        if (string.Equals(receiverExpression, "sender", StringComparison.Ordinal))
        {
            return context.SenderType;
        }

        if (string.Equals(receiverExpression, "e", StringComparison.Ordinal))
        {
            return context.EventArgsType;
        }

        if (TryResolveLambdaParameterType(context, receiverExpression, out var lambdaParameterType))
        {
            return lambdaParameterType;
        }

        if (TryResolveScopedSymbolType(context, receiverExpression, beforeOffset, out var scopedSymbolType))
        {
            return scopedSymbolType;
        }

        if (analysis.Compilation is null)
        {
            return null;
        }

        return CSharpInlineCodeAnalysisService.TryAnalyzeExpression(
            analysis.Compilation,
            context.SourceType,
            context.RootType,
            context.TargetType,
            receiverExpression,
            out var expressionAnalysis,
            out _)
            ? expressionAnalysis.ResultTypeSymbol as INamedTypeSymbol
            : null;
    }

    private static void AddTopLevelMemberCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        XamlInlineCSharpContext context,
        string memberPrefix)
    {
        AddMembers(builder, context.SourceType, memberPrefix);
        AddMembers(builder, context.RootType, memberPrefix);
        AddMembers(builder, context.TargetType, memberPrefix);
    }

    private static void AddMembers(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        INamedTypeSymbol? typeSymbol,
        string memberPrefix)
    {
        if (typeSymbol is null)
        {
            return;
        }

        builder.AddRange(XamlClrMemberCompletionFactory.CreateMemberCompletions(
            typeSymbol,
            memberPrefix,
            XamlMemberCompletionMode.Expression));
    }

    private static void AddContextVariableCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        XamlInlineCSharpContext context,
        string memberPrefix)
    {
        TryAddContextVariable(builder, "source", context.SourceType, memberPrefix);
        TryAddContextVariable(builder, "root", context.RootType, memberPrefix);
        TryAddContextVariable(builder, "target", context.TargetType, memberPrefix);
        TryAddContextVariable(builder, "sender", context.SenderType, memberPrefix);
        TryAddContextVariable(builder, "e", context.EventArgsType, memberPrefix);
    }

    private static void AddScopedSymbolCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        XamlInlineCSharpContext context,
        int caretOffsetInCode,
        string memberPrefix)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var occurrence in context.SymbolOccurrences)
        {
            if (!occurrence.IsDeclaration || occurrence.Start > caretOffsetInCode)
            {
                continue;
            }

            switch (occurrence.Symbol)
            {
                case IParameterSymbol parameterSymbol:
                    TryAddScopedSymbolCompletion(
                        builder,
                        seen,
                        parameterSymbol.Name,
                        parameterSymbol.Type,
                        XamlCompletionItemKind.Snippet,
                        memberPrefix);
                    break;
                case ILocalSymbol localSymbol:
                    TryAddScopedSymbolCompletion(
                        builder,
                        seen,
                        localSymbol.Name,
                        localSymbol.Type,
                        XamlCompletionItemKind.Snippet,
                        memberPrefix);
                    break;
                case IMethodSymbol methodSymbol when methodSymbol.MethodKind == MethodKind.LocalFunction:
                    if (methodSymbol.Parameters.Length == 0)
                    {
                        TryAddScopedSymbolCompletion(
                            builder,
                            seen,
                            methodSymbol.Name,
                            methodSymbol.ReturnType,
                            XamlCompletionItemKind.Method,
                            memberPrefix,
                            methodSymbol.Name + "()");
                    }
                    else
                    {
                        TryAddScopedSymbolCompletion(
                            builder,
                            seen,
                            methodSymbol.Name,
                            methodSymbol.ReturnType,
                            XamlCompletionItemKind.Method,
                            memberPrefix,
                            methodSymbol.Name);
                    }

                    break;
            }
        }
    }

    private static void AddLambdaParameterCompletions(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        XamlInlineCSharpContext context,
        string memberPrefix)
    {
        if (!TryResolveLambdaParameterNames(context.RawCode, out var senderParameterName, out var eventArgsParameterName))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(senderParameterName) && context.SenderType is not null)
        {
            TryAddScopedSymbolCompletion(
                builder,
                seen,
                senderParameterName,
                context.SenderType,
                XamlCompletionItemKind.Snippet,
                memberPrefix);
        }

        if (!string.IsNullOrWhiteSpace(eventArgsParameterName) && context.EventArgsType is not null)
        {
            TryAddScopedSymbolCompletion(
                builder,
                seen,
                eventArgsParameterName,
                context.EventArgsType,
                XamlCompletionItemKind.Snippet,
                memberPrefix);
        }
    }

    private static void TryAddContextVariable(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        string name,
        ITypeSymbol? typeSymbol,
        string memberPrefix)
    {
        if (typeSymbol is null ||
            (memberPrefix.Length > 0 && !name.StartsWith(memberPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        builder.Add(new XamlCompletionItem(
            name,
            name,
            XamlCompletionItemKind.Snippet,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static void TryAddScopedSymbolCompletion(
        ImmutableArray<XamlCompletionItem>.Builder builder,
        HashSet<string> seen,
        string name,
        ITypeSymbol typeSymbol,
        XamlCompletionItemKind kind,
        string memberPrefix,
        string? insertText = null)
    {
        if (!seen.Add(name) ||
            (memberPrefix.Length > 0 && !name.StartsWith(memberPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        builder.Add(new XamlCompletionItem(
            name,
            insertText ?? name,
            kind,
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
    }

    private static bool TryResolveScopedSymbolType(
        XamlInlineCSharpContext context,
        string symbolName,
        int beforeOffset,
        out INamedTypeSymbol? typeSymbol)
    {
        typeSymbol = null;
        for (var index = context.SymbolOccurrences.Length - 1; index >= 0; index--)
        {
            var occurrence = context.SymbolOccurrences[index];
            if (!occurrence.IsDeclaration ||
                occurrence.Start > beforeOffset ||
                !string.Equals(occurrence.Symbol.Name, symbolName, StringComparison.Ordinal))
            {
                continue;
            }

            switch (occurrence.Symbol)
            {
                case IParameterSymbol parameterSymbol:
                    typeSymbol = parameterSymbol.Type as INamedTypeSymbol;
                    return typeSymbol is not null;
                case ILocalSymbol localSymbol:
                    typeSymbol = localSymbol.Type as INamedTypeSymbol;
                    return typeSymbol is not null;
            }
        }

        return false;
    }

    private static bool TryResolveLambdaParameterType(
        XamlInlineCSharpContext context,
        string symbolName,
        out INamedTypeSymbol? typeSymbol)
    {
        typeSymbol = null;
        if (!TryResolveLambdaParameterNames(context.RawCode, out var senderParameterName, out var eventArgsParameterName))
        {
            return false;
        }

        if (context.SenderType is not null &&
            string.Equals(symbolName, senderParameterName, StringComparison.Ordinal))
        {
            typeSymbol = context.SenderType;
            return true;
        }

        if (context.EventArgsType is not null &&
            string.Equals(symbolName, eventArgsParameterName, StringComparison.Ordinal))
        {
            typeSymbol = context.EventArgsType;
            return true;
        }

        return false;
    }

    private static bool TryResolveLambdaParameterNames(
        string rawCode,
        out string senderParameterName,
        out string eventArgsParameterName)
    {
        senderParameterName = string.Empty;
        eventArgsParameterName = string.Empty;

        var arrowIndex = rawCode.IndexOf("=>", StringComparison.Ordinal);
        if (arrowIndex <= 0)
        {
            return false;
        }

        var header = rawCode.Substring(0, arrowIndex).Trim();
        if (header.Length == 0)
        {
            return false;
        }

        if (header[0] == '(')
        {
            if (header[^1] == ')')
            {
                header = header.Substring(1, header.Length - 2);
            }
            else
            {
                header = header.Substring(1);
            }
        }

        var segments = header.Split(',');
        if (segments.Length == 0)
        {
            return false;
        }

        senderParameterName = ExtractLambdaParameterName(segments[0]);
        if (segments.Length > 1)
        {
            eventArgsParameterName = ExtractLambdaParameterName(segments[1]);
        }

        return !string.IsNullOrWhiteSpace(senderParameterName) || !string.IsNullOrWhiteSpace(eventArgsParameterName);
    }

    private static string ExtractLambdaParameterName(string segment)
    {
        var trimmed = segment.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var lastSpace = trimmed.LastIndexOf(' ');
        return lastSpace >= 0 && lastSpace < trimmed.Length - 1
            ? trimmed.Substring(lastSpace + 1)
            : trimmed;
    }

    private static int FindReceiverExpressionStart(string text, int dotIndex)
    {
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var index = dotIndex - 1; index >= 0; index--)
        {
            var current = text[index];
            switch (current)
            {
                case ')':
                    parenthesisDepth++;
                    continue;
                case ']':
                    bracketDepth++;
                    continue;
                case '}':
                    braceDepth++;
                    continue;
                case '(':
                    if (parenthesisDepth == 0)
                    {
                        return index + 1;
                    }

                    parenthesisDepth--;
                    continue;
                case '[':
                    if (bracketDepth == 0)
                    {
                        return index + 1;
                    }

                    bracketDepth--;
                    continue;
                case '{':
                    if (braceDepth == 0)
                    {
                        return index + 1;
                    }

                    braceDepth--;
                    continue;
            }

            if (parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0 && IsReceiverBoundary(current))
            {
                return index + 1;
            }
        }

        return 0;
    }

    private static bool IsReceiverBoundary(char value)
    {
        return char.IsWhiteSpace(value) ||
               value is ',' or ';' or '=' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '!' or '?' or ':' or '<' or '>';
    }

    private static bool IsIdentifierPrefix(string prefix)
    {
        return prefix.Length == 0 ||
               (SyntaxFacts.IsIdentifierStartCharacter(prefix[0]) && prefix.Skip(1).All(IsIdentifierPart));
    }

    private static bool IsIdentifierPart(char value)
    {
        return SyntaxFacts.IsIdentifierPartCharacter(value);
    }
}
