using System;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Framework.Abstractions;

namespace XamlToCSharpGenerator.Avalonia.Emission;

public sealed class AvaloniaFrameworkEventBindingEmitterAdapter : IXamlFrameworkEventBindingEmitterAdapter
{
    public static AvaloniaFrameworkEventBindingEmitterAdapter Instance { get; } = new();

    private AvaloniaFrameworkEventBindingEmitterAdapter()
    {
    }

    public void EmitMethod(
        StringBuilder sourceBuilder,
        ResolvedEventBindingDefinition definition,
        string emittedMethodName)
    {
        var senderExpression = definition.Parameters.Length > 0
            ? BuildWrapperParameterName(0)
            : "null";
        var eventArgsExpression = definition.Parameters.Length > 1
            ? BuildWrapperParameterName(1)
            : "null";

        sourceBuilder.AppendLine(
            $"        private void {emittedMethodName}({BuildEventBindingParameterList(definition.Parameters)})");
        sourceBuilder.AppendLine("        {");

        if (definition.TargetKind is ResolvedEventBindingTargetKind.Command or ResolvedEventBindingTargetKind.Method)
        {
            sourceBuilder.AppendLine("            static object? __TryGetEventBindingDataContext(object? value)");
            sourceBuilder.AppendLine("            {");
            sourceBuilder.AppendLine("                return value is global::Avalonia.Controls.Control control");
            sourceBuilder.AppendLine("                    ? control.DataContext");
            sourceBuilder.AppendLine("                    : null;");
            sourceBuilder.AppendLine("            }");
        }

        if (definition.TargetKind == ResolvedEventBindingTargetKind.Command)
        {
            EmitCommandInvocation(sourceBuilder, definition, senderExpression, eventArgsExpression);
        }
        else if (definition.TargetKind == ResolvedEventBindingTargetKind.Method)
        {
            EmitMethodInvocation(sourceBuilder, definition, senderExpression, eventArgsExpression);
        }
        else
        {
            EmitLambdaInvocation(sourceBuilder, definition, senderExpression);
        }

        sourceBuilder.AppendLine("        }");
        sourceBuilder.AppendLine();
    }

    private static void EmitCommandInvocation(
        StringBuilder sourceBuilder,
        ResolvedEventBindingDefinition definition,
        string senderExpression,
        string eventArgsExpression)
    {
        var dataContextCommandExpression = string.Empty;
        var dataContextParameterExpression = "null";
        var canEmitDataContextCommand = definition.SourceMode != ResolvedEventBindingSourceMode.Root &&
                                        !string.IsNullOrWhiteSpace(definition.DataContextTypeName) &&
                                        !string.IsNullOrWhiteSpace(definition.CompiledDataContextTargetPath) &&
                                        TryBuildEventBindingMemberAccessExpression(
                                            "__axsgDataContextTyped",
                                            definition.CompiledDataContextTargetPath!,
                                            out dataContextCommandExpression) &&
                                        TryBuildEventBindingParameterExpression(
                                            definition,
                                            "__axsgDataContextTyped",
                                            definition.CompiledDataContextParameterPath,
                                            eventArgsExpression,
                                            out dataContextParameterExpression);

        var rootCommandExpression = string.Empty;
        var rootParameterExpression = "null";
        var canEmitRootCommand = definition.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                                 !string.IsNullOrWhiteSpace(definition.RootTypeName) &&
                                 !string.IsNullOrWhiteSpace(definition.CompiledRootTargetPath) &&
                                 TryBuildEventBindingMemberAccessExpression(
                                     "__axsgRootTyped",
                                     definition.CompiledRootTargetPath!,
                                     out rootCommandExpression) &&
                                 TryBuildEventBindingParameterExpression(
                                     definition,
                                     "__axsgRootTyped",
                                     definition.CompiledRootParameterPath,
                                     eventArgsExpression,
                                     out rootParameterExpression);

        sourceBuilder.AppendLine("            try");
        sourceBuilder.AppendLine("            {");
        if (canEmitDataContextCommand)
        {
            sourceBuilder.AppendLine(
                $"                var __axsgDataContext = __TryGetEventBindingDataContext({senderExpression}) ?? __TryGetEventBindingDataContext(this);");
            sourceBuilder.AppendLine(
                $"                if (__axsgDataContext is {definition.DataContextTypeName} __axsgDataContextTyped)");
            sourceBuilder.AppendLine("                {");
            sourceBuilder.AppendLine(
                $"                    var __axsgCommandCandidate = (object?)({dataContextCommandExpression});");
            sourceBuilder.AppendLine("                    if (__axsgCommandCandidate is global::System.Windows.Input.ICommand __axsgCommand)");
            sourceBuilder.AppendLine("                    {");
            sourceBuilder.AppendLine(
                $"                        var __axsgParameter = {dataContextParameterExpression};");
            sourceBuilder.AppendLine("                        if (__axsgCommand.CanExecute(__axsgParameter))");
            sourceBuilder.AppendLine("                        {");
            sourceBuilder.AppendLine("                            __axsgCommand.Execute(__axsgParameter);");
            sourceBuilder.AppendLine("                        }");
            sourceBuilder.AppendLine("                        return;");
            sourceBuilder.AppendLine("                    }");
            sourceBuilder.AppendLine("                }");
        }

        if (canEmitRootCommand)
        {
            sourceBuilder.AppendLine(
                $"                if (this is {definition.RootTypeName} __axsgRootTyped)");
            sourceBuilder.AppendLine("                {");
            sourceBuilder.AppendLine(
                $"                    var __axsgCommandCandidate = (object?)({rootCommandExpression});");
            sourceBuilder.AppendLine("                    if (__axsgCommandCandidate is global::System.Windows.Input.ICommand __axsgCommand)");
            sourceBuilder.AppendLine("                    {");
            sourceBuilder.AppendLine(
                $"                        var __axsgParameter = {rootParameterExpression};");
            sourceBuilder.AppendLine("                        if (__axsgCommand.CanExecute(__axsgParameter))");
            sourceBuilder.AppendLine("                        {");
            sourceBuilder.AppendLine("                            __axsgCommand.Execute(__axsgParameter);");
            sourceBuilder.AppendLine("                        }");
            sourceBuilder.AppendLine("                        return;");
            sourceBuilder.AppendLine("                    }");
            sourceBuilder.AppendLine("                }");
        }

        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("            catch");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("            }");
    }

    private static void EmitMethodInvocation(
        StringBuilder sourceBuilder,
        ResolvedEventBindingDefinition definition,
        string senderExpression,
        string eventArgsExpression)
    {
        var dataContextMethodInvocationExpression = string.Empty;
        var dataContextMethodParameterExpression = "null";
        var dataContextMethodNeedsParameter = false;
        var canBuildDataContextMethodInvocation = false;
        if (definition.SourceMode != ResolvedEventBindingSourceMode.Root &&
            definition.CompiledDataContextMethodCall is not null)
        {
            canBuildDataContextMethodInvocation = TryBuildEventBindingMethodInvocationExpression(
                "__axsgDataContextTyped",
                definition.CompiledDataContextMethodCall,
                senderExpression,
                eventArgsExpression,
                out dataContextMethodInvocationExpression,
                out dataContextMethodNeedsParameter);
        }

        var canEmitDataContextMethod = canBuildDataContextMethodInvocation &&
                                       !string.IsNullOrWhiteSpace(definition.DataContextTypeName) &&
                                       (!dataContextMethodNeedsParameter ||
                                        TryBuildEventBindingParameterExpression(
                                            definition,
                                            "__axsgDataContextTyped",
                                            definition.CompiledDataContextParameterPath,
                                            eventArgsExpression,
                                            out dataContextMethodParameterExpression));

        var rootMethodInvocationExpression = string.Empty;
        var rootMethodParameterExpression = "null";
        var rootMethodNeedsParameter = false;
        var canBuildRootMethodInvocation = false;
        if (definition.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
            definition.CompiledRootMethodCall is not null)
        {
            canBuildRootMethodInvocation = TryBuildEventBindingMethodInvocationExpression(
                "__axsgRootTyped",
                definition.CompiledRootMethodCall,
                senderExpression,
                eventArgsExpression,
                out rootMethodInvocationExpression,
                out rootMethodNeedsParameter);
        }

        var canEmitRootMethod = canBuildRootMethodInvocation &&
                                !string.IsNullOrWhiteSpace(definition.RootTypeName) &&
                                (!rootMethodNeedsParameter ||
                                 TryBuildEventBindingParameterExpression(
                                     definition,
                                     "__axsgRootTyped",
                                     definition.CompiledRootParameterPath,
                                     eventArgsExpression,
                                     out rootMethodParameterExpression));

        sourceBuilder.AppendLine("            try");
        sourceBuilder.AppendLine("            {");
        if (canEmitDataContextMethod)
        {
            sourceBuilder.AppendLine(
                $"                var __axsgDataContext = __TryGetEventBindingDataContext({senderExpression}) ?? __TryGetEventBindingDataContext(this);");
            sourceBuilder.AppendLine(
                $"                if (__axsgDataContext is {definition.DataContextTypeName} __axsgDataContextTyped)");
            sourceBuilder.AppendLine("                {");
            if (dataContextMethodNeedsParameter)
            {
                sourceBuilder.AppendLine(
                    $"                    var __axsgParameter = {dataContextMethodParameterExpression};");
            }

            sourceBuilder.AppendLine($"                    {dataContextMethodInvocationExpression};");
            sourceBuilder.AppendLine("                    return;");
            sourceBuilder.AppendLine("                }");
        }

        if (canEmitRootMethod)
        {
            sourceBuilder.AppendLine(
                $"                if (this is {definition.RootTypeName} __axsgRootTyped)");
            sourceBuilder.AppendLine("                {");
            if (rootMethodNeedsParameter)
            {
                sourceBuilder.AppendLine(
                    $"                    var __axsgParameter = {rootMethodParameterExpression};");
            }

            sourceBuilder.AppendLine($"                    {rootMethodInvocationExpression};");
            sourceBuilder.AppendLine("                    return;");
            sourceBuilder.AppendLine("                }");
        }

        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("            catch");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("            }");
    }

    private static void EmitLambdaInvocation(
        StringBuilder sourceBuilder,
        ResolvedEventBindingDefinition definition,
        string senderExpression)
    {
        var canEmitDataContextLambda = definition.SourceMode != ResolvedEventBindingSourceMode.Root &&
                                       !string.IsNullOrWhiteSpace(definition.DataContextTypeName) &&
                                       !string.IsNullOrWhiteSpace(definition.CompiledDataContextLambdaExpression);
        var canEmitRootLambda = definition.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                                !string.IsNullOrWhiteSpace(definition.RootTypeName) &&
                                !string.IsNullOrWhiteSpace(definition.CompiledRootLambdaExpression);

        sourceBuilder.AppendLine("            try");
        sourceBuilder.AppendLine("            {");
        if (canEmitDataContextLambda)
        {
            sourceBuilder.AppendLine(
                $"                var __axsgDataContext = __TryGetEventBindingDataContext({senderExpression}) ?? __TryGetEventBindingDataContext(this);");
            sourceBuilder.AppendLine(
                $"                if (__axsgDataContext is {definition.DataContextTypeName} __axsgDataContextTyped)");
            sourceBuilder.AppendLine("                {");
            EmitCompiledEventLambdaInvocation(
                sourceBuilder,
                "                    ",
                definition,
                definition.CompiledDataContextLambdaExpression!,
                definition.LambdaSourceTypeName ?? definition.DataContextTypeName!,
                "__axsgDataContextTyped",
                "this",
                senderExpression);
            sourceBuilder.AppendLine("                }");
        }

        if (canEmitRootLambda)
        {
            sourceBuilder.AppendLine(
                $"                if (this is {definition.RootTypeName} __axsgRootTyped)");
            sourceBuilder.AppendLine("                {");
            EmitCompiledEventLambdaInvocation(
                sourceBuilder,
                "                    ",
                definition,
                definition.CompiledRootLambdaExpression!,
                definition.LambdaSourceTypeName ?? definition.RootTypeName ?? "global::System.Object",
                "__axsgRootTyped",
                "__axsgRootTyped",
                senderExpression);
            sourceBuilder.AppendLine("                }");
        }

        sourceBuilder.AppendLine("            }");
        sourceBuilder.AppendLine("            catch");
        sourceBuilder.AppendLine("            {");
        sourceBuilder.AppendLine("            }");
    }

    private static string BuildEventBindingParameterList(ImmutableArray<ResolvedEventBindingParameter> parameters)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            var parameter = parameters[index];
            var typeName = string.IsNullOrWhiteSpace(parameter.TypeName)
                ? "object?"
                : parameter.TypeName;
            var name = BuildWrapperParameterName(index);
            builder.Append(typeName).Append(' ').Append(name);
        }

        return builder.ToString();
    }

    private static string BuildEventBindingInvocationArgumentList(ImmutableArray<ResolvedEventBindingParameter> parameters)
    {
        if (parameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var index = 0; index < parameters.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            builder.Append(BuildWrapperParameterName(index));
        }

        return builder.ToString();
    }

    private static void EmitCompiledEventLambdaInvocation(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string lambdaExpression,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string senderExpression)
    {
        if (TryParseCompiledEventLambda(
                lambdaExpression,
                out var lambdaParameterNames,
                out var lambdaBodyCode,
                out var lambdaBodyIsBlock))
        {
            EmitCompiledEventLambdaBodyInvocation(
                sourceBuilder,
                indent,
                definition,
                sourceTypeName,
                sourceExpression,
                rootExpression,
                senderExpression,
                lambdaParameterNames,
                lambdaBodyCode,
                lambdaBodyIsBlock);
            return;
        }

        if (!definition.UsesInlineCodeContext)
        {
            sourceBuilder.AppendLine(indent + "var __axsgLambdaSource = " + sourceExpression + ";");
            sourceBuilder.AppendLine(indent + definition.DelegateTypeName + " __axsgHandler = " + lambdaExpression + ";");
            sourceBuilder.AppendLine(indent + "__axsgHandler(" + BuildEventBindingInvocationArgumentList(definition.Parameters) + ");");
            sourceBuilder.AppendLine(indent + "return;");
            return;
        }

        var targetTypeName = string.IsNullOrWhiteSpace(definition.LambdaContextTargetTypeName)
            ? "global::System.Object"
            : definition.LambdaContextTargetTypeName!;

        EmitInlineEventLambdaInvocationBlock(
            sourceBuilder,
            indent,
            definition,
            lambdaExpression,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            senderExpression,
            "__axsgTargetTyped");

        sourceBuilder.AppendLine(indent + "if ((object)this is " + targetTypeName + " __axsgSelfTargetTyped)");
        sourceBuilder.AppendLine(indent + "{");
        EmitInlineEventLambdaContextAndInvoke(
            sourceBuilder,
            indent + "    ",
            definition,
            lambdaExpression,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            "__axsgSelfTargetTyped");
        sourceBuilder.AppendLine(indent + "    return;");
        sourceBuilder.AppendLine(indent + "}");
    }

    private static void EmitCompiledEventLambdaBodyInvocation(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string senderExpression,
        ImmutableArray<string> lambdaParameterNames,
        string lambdaBodyCode,
        bool lambdaBodyIsBlock)
    {
        if (!definition.UsesInlineCodeContext)
        {
            sourceBuilder.AppendLine(indent + sourceTypeName + " source = " + sourceExpression + ";");
            EmitLambdaParameterAliases(sourceBuilder, indent, definition, lambdaParameterNames);
            EmitLambdaBody(sourceBuilder, indent, lambdaBodyCode, lambdaBodyIsBlock);
            sourceBuilder.AppendLine(indent + "return;");
            return;
        }

        var targetTypeName = string.IsNullOrWhiteSpace(definition.LambdaContextTargetTypeName)
            ? "global::System.Object"
            : definition.LambdaContextTargetTypeName!;

        EmitInlineEventLambdaBodyInvocationBlock(
            sourceBuilder,
            indent,
            definition,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            senderExpression,
            "__axsgTargetTyped",
            lambdaParameterNames,
            lambdaBodyCode,
            lambdaBodyIsBlock);

        sourceBuilder.AppendLine(indent + "if ((object)this is " + targetTypeName + " __axsgSelfTargetTyped)");
        sourceBuilder.AppendLine(indent + "{");
        EmitInlineEventLambdaContextAndBody(
            sourceBuilder,
            indent + "    ",
            definition,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            "__axsgSelfTargetTyped",
            lambdaParameterNames,
            lambdaBodyCode,
            lambdaBodyIsBlock);
        sourceBuilder.AppendLine(indent + "    return;");
        sourceBuilder.AppendLine(indent + "}");
    }

    private static void EmitInlineEventLambdaInvocationBlock(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string lambdaExpression,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string targetTypeName,
        string senderExpression,
        string targetVariableName)
    {
        sourceBuilder.AppendLine(indent + "if (" + senderExpression + " is " + targetTypeName + " " + targetVariableName + ")");
        sourceBuilder.AppendLine(indent + "{");
        EmitInlineEventLambdaContextAndInvoke(
            sourceBuilder,
            indent + "    ",
            definition,
            lambdaExpression,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            targetVariableName);
        sourceBuilder.AppendLine(indent + "    return;");
        sourceBuilder.AppendLine(indent + "}");
    }

    private static void EmitInlineEventLambdaBodyInvocationBlock(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string targetTypeName,
        string senderExpression,
        string targetVariableName,
        ImmutableArray<string> lambdaParameterNames,
        string lambdaBodyCode,
        bool lambdaBodyIsBlock)
    {
        sourceBuilder.AppendLine(indent + "if (" + senderExpression + " is " + targetTypeName + " " + targetVariableName + ")");
        sourceBuilder.AppendLine(indent + "{");
        EmitInlineEventLambdaContextAndBody(
            sourceBuilder,
            indent + "    ",
            definition,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetTypeName,
            targetVariableName,
            lambdaParameterNames,
            lambdaBodyCode,
            lambdaBodyIsBlock);
        sourceBuilder.AppendLine(indent + "    return;");
        sourceBuilder.AppendLine(indent + "}");
    }

    private static void EmitInlineEventLambdaContextAndInvoke(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string lambdaExpression,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string targetTypeName,
        string targetExpression)
    {
        EmitResolvedEventLambdaSource(
            sourceBuilder,
            indent,
            definition,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetExpression);
        sourceBuilder.AppendLine(indent + definition.RootTypeName + " root = " + rootExpression + ";");
        sourceBuilder.AppendLine(indent + targetTypeName + " target = " + targetExpression + ";");
        sourceBuilder.AppendLine(indent + definition.DelegateTypeName + " __axsgHandler = " + lambdaExpression + ";");
        sourceBuilder.AppendLine(indent + "__axsgHandler(" + BuildEventBindingInvocationArgumentList(definition.Parameters) + ");");
    }

    private static void EmitInlineEventLambdaContextAndBody(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string targetTypeName,
        string targetExpression,
        ImmutableArray<string> lambdaParameterNames,
        string lambdaBodyCode,
        bool lambdaBodyIsBlock)
    {
        EmitResolvedEventLambdaSource(
            sourceBuilder,
            indent,
            definition,
            sourceTypeName,
            sourceExpression,
            rootExpression,
            targetExpression);
        sourceBuilder.AppendLine(indent + definition.RootTypeName + " root = " + rootExpression + ";");
        sourceBuilder.AppendLine(indent + targetTypeName + " target = " + targetExpression + ";");
        EmitLambdaParameterAliases(sourceBuilder, indent, definition, lambdaParameterNames);
        EmitLambdaBody(sourceBuilder, indent, lambdaBodyCode, lambdaBodyIsBlock);
    }

    private static void EmitResolvedEventLambdaSource(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        string sourceTypeName,
        string sourceExpression,
        string rootExpression,
        string targetExpression)
    {
        if (string.IsNullOrWhiteSpace(definition.LambdaSourceDependencyExpression))
        {
            sourceBuilder.AppendLine(indent + sourceTypeName + " source = " + sourceExpression + ";");
            return;
        }

        sourceBuilder.AppendLine(
            indent +
            "if (!global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.TryResolveXBindDependency<" +
            sourceTypeName +
            ">(" +
            definition.LambdaSourceDependencyExpression +
            ", " +
            targetExpression +
            ", " +
            targetExpression +
            ", " +
            rootExpression +
            ", out var source))");
        sourceBuilder.AppendLine(indent + "{");
        sourceBuilder.AppendLine(indent + "    return;");
        sourceBuilder.AppendLine(indent + "}");
    }

    private static void EmitLambdaParameterAliases(
        StringBuilder sourceBuilder,
        string indent,
        ResolvedEventBindingDefinition definition,
        ImmutableArray<string> lambdaParameterNames)
    {
        if (lambdaParameterNames.IsDefaultOrEmpty || definition.Parameters.IsDefaultOrEmpty)
        {
            return;
        }

        var count = Math.Min(lambdaParameterNames.Length, definition.Parameters.Length);
        for (var index = 0; index < count; index++)
        {
            var aliasName = lambdaParameterNames[index];
            if (string.IsNullOrWhiteSpace(aliasName))
            {
                continue;
            }

            var wrapperParameter = definition.Parameters[index];
            var wrapperParameterName = BuildWrapperParameterName(index);
            if (string.Equals(aliasName, wrapperParameterName, StringComparison.Ordinal))
            {
                continue;
            }

            var wrapperTypeName = string.IsNullOrWhiteSpace(wrapperParameter.TypeName)
                ? "object?"
                : wrapperParameter.TypeName;
            sourceBuilder.AppendLine(indent + wrapperTypeName + " " + aliasName + " = " + wrapperParameterName + ";");
        }
    }

    private static string BuildWrapperParameterName(int index)
    {
        return "__arg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void EmitLambdaBody(
        StringBuilder sourceBuilder,
        string indent,
        string lambdaBodyCode,
        bool lambdaBodyIsBlock)
    {
        if (string.IsNullOrWhiteSpace(lambdaBodyCode))
        {
            return;
        }

        if (lambdaBodyIsBlock)
        {
            AppendIndentedCodeBlock(sourceBuilder, indent, lambdaBodyCode);
        }
        else
        {
            sourceBuilder.AppendLine(indent + lambdaBodyCode.Trim() + ";");
        }
    }

    private static void AppendIndentedCodeBlock(
        StringBuilder sourceBuilder,
        string indent,
        string code)
    {
        var normalized = code.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                sourceBuilder.AppendLine();
                continue;
            }

            sourceBuilder.Append(indent);
            sourceBuilder.AppendLine(line);
        }
    }

    private static bool TryParseCompiledEventLambda(
        string lambdaExpression,
        out ImmutableArray<string> parameterNames,
        out string lambdaBodyCode,
        out bool lambdaBodyIsBlock)
    {
        parameterNames = ImmutableArray<string>.Empty;
        lambdaBodyCode = string.Empty;
        lambdaBodyIsBlock = false;

        if (string.IsNullOrWhiteSpace(lambdaExpression))
        {
            return false;
        }

        var parsedExpression = SyntaxFactory.ParseExpression(lambdaExpression);
        var parseDiagnostic = parsedExpression.GetDiagnostics()
            .FirstOrDefault(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        if (parseDiagnostic is not null || parsedExpression is not AnonymousFunctionExpressionSyntax anonymousFunction)
        {
            return false;
        }

        parameterNames = GetLambdaParameterNames(anonymousFunction);
        switch (anonymousFunction.Body)
        {
            case BlockSyntax blockSyntax:
                lambdaBodyCode = blockSyntax.Statements.ToFullString().Trim();
                lambdaBodyIsBlock = true;
                return lambdaBodyCode.Length > 0;
            case ExpressionSyntax expressionSyntax:
                lambdaBodyCode = expressionSyntax.ToFullString().Trim();
                lambdaBodyIsBlock = false;
                return lambdaBodyCode.Length > 0;
            default:
                return false;
        }
    }

    private static ImmutableArray<string> GetLambdaParameterNames(AnonymousFunctionExpressionSyntax anonymousFunction)
    {
        switch (anonymousFunction)
        {
            case SimpleLambdaExpressionSyntax simpleLambdaExpression:
                return ImmutableArray.Create(simpleLambdaExpression.Parameter.Identifier.ValueText);
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpression:
            {
                if (parenthesizedLambdaExpression.ParameterList.Parameters.Count == 0)
                {
                    return ImmutableArray<string>.Empty;
                }

                var builder = ImmutableArray.CreateBuilder<string>(parenthesizedLambdaExpression.ParameterList.Parameters.Count);
                foreach (var parameter in parenthesizedLambdaExpression.ParameterList.Parameters)
                {
                    builder.Add(parameter.Identifier.ValueText);
                }

                return builder.MoveToImmutable();
            }
            case AnonymousMethodExpressionSyntax anonymousMethodExpression when anonymousMethodExpression.ParameterList is not null:
            {
                if (anonymousMethodExpression.ParameterList.Parameters.Count == 0)
                {
                    return ImmutableArray<string>.Empty;
                }

                var builder = ImmutableArray.CreateBuilder<string>(anonymousMethodExpression.ParameterList.Parameters.Count);
                foreach (var parameter in anonymousMethodExpression.ParameterList.Parameters)
                {
                    builder.Add(parameter.Identifier.ValueText);
                }

                return builder.MoveToImmutable();
            }
            default:
                return ImmutableArray<string>.Empty;
        }
    }

    private static bool TryBuildEventBindingMemberAccessExpression(
        string sourceExpression,
        string path,
        out string memberAccessExpression)
    {
        memberAccessExpression = string.Empty;
        if (string.IsNullOrWhiteSpace(sourceExpression))
        {
            return false;
        }

        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0)
        {
            return false;
        }

        if (normalizedPath == ".")
        {
            memberAccessExpression = sourceExpression;
            return true;
        }

        if (!IsSimpleEventBindingMemberPath(normalizedPath))
        {
            return false;
        }

        memberAccessExpression = sourceExpression + "." + normalizedPath;
        return true;
    }

    private static bool TryBuildEventBindingParameterExpression(
        ResolvedEventBindingDefinition definition,
        string sourceExpression,
        string? compiledParameterPath,
        string eventArgsExpression,
        out string parameterExpression)
    {
        parameterExpression = "(object?)null";

        if (!string.IsNullOrWhiteSpace(definition.ParameterPath))
        {
            if (compiledParameterPath is null)
            {
                return false;
            }

            var compiledParameterPathValue = compiledParameterPath.Trim();
            if (compiledParameterPathValue.Length == 0 ||
                !TryBuildEventBindingMemberAccessExpression(sourceExpression, compiledParameterPathValue, out var parameterAccessExpression))
            {
                return false;
            }

            parameterExpression = "(object?)(" + parameterAccessExpression + ")";
            return true;
        }

        if (definition.HasParameterValueExpression && !string.IsNullOrWhiteSpace(definition.ParameterValueExpression))
        {
            parameterExpression = "(object?)(" + definition.ParameterValueExpression! + ")";
            return true;
        }

        if (definition.PassEventArgs)
        {
            parameterExpression = "(object?)(" + eventArgsExpression + ")";
            return true;
        }

        parameterExpression = "(object?)null";
        return true;
    }

    private static bool TryBuildEventBindingMethodInvocationExpression(
        string sourceExpression,
        ResolvedEventBindingMethodCallPlan methodCallPlan,
        string senderExpression,
        string eventArgsExpression,
        out string invocationExpression,
        out bool requiresParameter)
    {
        invocationExpression = string.Empty;
        requiresParameter = false;

        if (string.IsNullOrWhiteSpace(sourceExpression) ||
            string.IsNullOrWhiteSpace(methodCallPlan.MethodName) ||
            !IsSimpleEventBindingIdentifier(methodCallPlan.MethodName))
        {
            return false;
        }

        var targetExpression = sourceExpression;
        if (!string.IsNullOrWhiteSpace(methodCallPlan.TargetPath) &&
            !methodCallPlan.TargetPath.Equals(".", StringComparison.Ordinal))
        {
            if (!TryBuildEventBindingMemberAccessExpression(sourceExpression, methodCallPlan.TargetPath, out targetExpression))
            {
                return false;
            }
        }

        var argumentCount = methodCallPlan.Arguments.IsDefaultOrEmpty
            ? 0
            : methodCallPlan.Arguments.Length;
        var builder = new StringBuilder(
            targetExpression.Length +
            methodCallPlan.MethodName.Length +
            3 +
            (argumentCount * 24));
        builder.Append(targetExpression);
        builder.Append('.');
        builder.Append(methodCallPlan.MethodName);
        builder.Append('(');
        if (!methodCallPlan.Arguments.IsDefaultOrEmpty)
        {
            for (var index = 0; index < methodCallPlan.Arguments.Length; index++)
            {
                var argument = methodCallPlan.Arguments[index];
                var sourceArgumentExpression = argument.Kind switch
                {
                    ResolvedEventBindingMethodArgumentKind.Sender => senderExpression,
                    ResolvedEventBindingMethodArgumentKind.EventArgs => eventArgsExpression,
                    ResolvedEventBindingMethodArgumentKind.Parameter => "__axsgParameter",
                    _ => "null"
                };
                requiresParameter |= argument.Kind == ResolvedEventBindingMethodArgumentKind.Parameter;

                var targetTypeName = string.IsNullOrWhiteSpace(argument.TypeName)
                    ? "object?"
                    : argument.TypeName;
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("((");
                builder.Append(targetTypeName);
                builder.Append(")(");
                builder.Append(sourceArgumentExpression);
                builder.Append("))");
            }
        }

        builder.Append(')');
        invocationExpression = builder.ToString();
        return true;
    }

    private static bool IsSimpleEventBindingIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var first = value[0];
        if (!(first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleEventBindingMemberPath(string path)
    {
        var segmentStart = -1;
        for (var index = 0; index < path.Length; index++)
        {
            var current = path[index];
            if (current == '.')
            {
                if (!TryValidateSimpleEventBindingPathSegment(path, segmentStart, index))
                {
                    return false;
                }

                segmentStart = -1;
                continue;
            }

            if (segmentStart < 0)
            {
                segmentStart = index;
            }
        }

        return TryValidateSimpleEventBindingPathSegment(path, segmentStart, path.Length);
    }

    private static bool TryValidateSimpleEventBindingPathSegment(string path, int segmentStart, int segmentEndExclusive)
    {
        if (segmentStart < 0)
        {
            return false;
        }

        while (segmentStart < segmentEndExclusive && char.IsWhiteSpace(path[segmentStart]))
        {
            segmentStart++;
        }

        while (segmentEndExclusive > segmentStart && char.IsWhiteSpace(path[segmentEndExclusive - 1]))
        {
            segmentEndExclusive--;
        }

        if (segmentStart >= segmentEndExclusive)
        {
            return false;
        }

        var first = path[segmentStart];
        if (!(first == '_' || char.IsLetter(first)))
        {
            return false;
        }

        for (var index = segmentStart + 1; index < segmentEndExclusive; index++)
        {
            var current = path[index];
            if (!(current == '_' || char.IsLetterOrDigit(current)))
            {
                return false;
            }
        }

        return true;
    }

    private static string MapSourceMode(ResolvedEventBindingSourceMode sourceMode)
    {
        return sourceMode switch
        {
            ResolvedEventBindingSourceMode.DataContext => "DataContext",
            ResolvedEventBindingSourceMode.Root => "Root",
            _ => "DataContextThenRoot"
        };
    }

    private static string SanitizeIdentifier(string? value, int index)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "__arg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var builder = new StringBuilder(value!.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');
        }

        if (builder.Length == 0 || (!char.IsLetter(builder[0]) && builder[0] != '_'))
        {
            builder.Insert(0, "__arg");
            builder.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string QuoteOrNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "null"
            : "\"" + Escape(value!) + "\"";
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
