using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using XamlToCSharpGenerator.Core.Abstractions;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static bool TryBuildExplicitConstructionExpression(
        XamlObjectNode node,
        INamedTypeSymbol? symbol,
        string typeName,
        Compilation compilation,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        bool inheritedCompileBindingsEnabled,
        INamedTypeSymbol? inheritedDataType,
        INamedTypeSymbol? inheritedSetterTargetType,
        BindingPriorityScope inheritedBindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        out string? expression)
    {
        expression = null;
        var hasConstructionDirectives = node.ConstructorArguments.Length > 0 ||
                                        !string.IsNullOrWhiteSpace(node.FactoryMethod);
        if (!hasConstructionDirectives)
        {
            return false;
        }

        if (symbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0106",
                $"Could not resolve type '{node.XmlTypeName}' for construction directives.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        var arguments = new List<string>(node.ConstructorArguments.Length);
        foreach (var argumentNode in node.ConstructorArguments)
        {
            if (ShouldSkipConditionalBranch(
                    argumentNode.Condition,
                    compilation,
                    document,
                    diagnostics,
                    options))
            {
                continue;
            }

            var resolvedArgument = BindObjectNode(
                argumentNode,
                compilation,
                diagnostics,
                document,
                options,
                compiledBindings,
                inheritedCompileBindingsEnabled,
                inheritedDataType,
                inheritedSetterTargetType,
                inheritedBindingPriorityScope,
                rootTypeSymbol: rootTypeSymbol);

            if (!TryBuildInlineResolvedObjectExpression(resolvedArgument, out var argumentExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0106",
                    "x:Arguments value is not inline-constructable for source-generated construction.",
                    document.FilePath,
                    argumentNode.Line,
                    argumentNode.Column,
                    options.StrictMode));
                return true;
            }

            arguments.Add(argumentExpression);
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryMethod))
        {
            var factoryMethod = TryFindMatchingFactoryMethod(symbol, node.FactoryMethod!, arguments.Count);
            if (factoryMethod is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0107",
                    $"x:FactoryMethod '{node.FactoryMethod}' with {arguments.Count} argument(s) was not found on '{symbol.ToDisplayString()}'.",
                    document.FilePath,
                    node.Line,
                    node.Column,
                    options.StrictMode));
                return true;
            }

            expression = factoryMethod.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         factoryMethod.Name +
                         "(" +
                         string.Join(", ", arguments) +
                         ")";
            return true;
        }

        var constructor = TryFindMatchingConstructor(symbol, arguments.Count);
        if (constructor is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0107",
                $"No constructor with {arguments.Count} argument(s) was found on '{symbol.ToDisplayString()}'.",
                document.FilePath,
                node.Line,
                node.Column,
                options.StrictMode));
            return true;
        }

        expression = "new " + typeName + "(" + string.Join(", ", arguments) + ")";
        return true;
    }

    private static bool TryBuildInlineResolvedObjectExpression(
        ResolvedObjectNode node,
        out string expression)
    {
        expression = string.Empty;
        if (node.Condition is not null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(node.FactoryExpression))
        {
            if (node.FactoryValueRequirements.RequiresMarkupContext)
            {
                return false;
            }

            expression = node.FactoryExpression!;
            return true;
        }

        if (node.UseServiceProviderConstructor ||
            node.Children.Length > 0 ||
            node.PropertyElementAssignments.Length > 0 ||
            node.EventSubscriptions.Length > 0)
        {
            return false;
        }

        if (node.PropertyAssignments.Length == 0)
        {
            expression = "new " + node.TypeName + "()";
            return true;
        }

        var initializers = new List<string>(node.PropertyAssignments.Length);
        foreach (var assignment in node.PropertyAssignments)
        {
            if (!string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyOwnerTypeName) ||
                !string.IsNullOrWhiteSpace(assignment.AvaloniaPropertyFieldName) ||
                assignment.Condition is not null ||
                assignment.ValueRequirements.RequiresMarkupContext)
            {
                return false;
            }

            initializers.Add(assignment.PropertyName + " = " + assignment.ValueExpression);
        }

        expression = "new " + node.TypeName + "() { " + string.Join(", ", initializers) + " }";
        return true;
    }

    private static bool ShouldSkipConditionalBranch(
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

        if (TryEvaluateConditionalExpression(
                condition,
                compilation,
                out var result,
                out var errorMessage))
        {
            return !result;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0120",
            $"Invalid conditional XAML expression '{condition.RawExpression}': {errorMessage}",
            document.FilePath,
            condition.Line,
            condition.Column,
            options.StrictMode));
        return false;
    }

    private static bool TryEvaluateConditionalExpression(
        ConditionalXamlExpression condition,
        Compilation compilation,
        out bool result,
        out string errorMessage)
    {
        result = false;
        errorMessage = string.Empty;

        var args = condition.Arguments;
        switch (condition.MethodName)
        {
            case "IsTypePresent":
            case "IsTypeNotPresent":
            {
                if (args.Length != 1)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 1 argument.";
                    return false;
                }

                var isPresent = ResolveConditionalTypeSymbol(compilation, args[0]) is not null;
                result = condition.MethodName == "IsTypePresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsPropertyPresent":
            case "IsPropertyNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IPropertySymbol>().Any();
                result = condition.MethodName == "IsPropertyPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsMethodPresent":
            case "IsMethodNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IMethodSymbol>().Any(method =>
                                    method.MethodKind == MethodKind.Ordinary);
                result = condition.MethodName == "IsMethodPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsEventPresent":
            case "IsEventNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.GetMembers(args[1]).OfType<IEventSymbol>().Any();
                result = condition.MethodName == "IsEventPresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsEnumNamedValuePresent":
            case "IsEnumNamedValueNotPresent":
            {
                if (args.Length != 2)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects 2 arguments.";
                    return false;
                }

                var type = ResolveConditionalTypeSymbol(compilation, args[0]);
                var isPresent = type is not null &&
                                type.TypeKind == TypeKind.Enum &&
                                type.GetMembers(args[1]).OfType<IFieldSymbol>().Any(field => field.HasConstantValue);
                result = condition.MethodName == "IsEnumNamedValuePresent" ? isPresent : !isPresent;
                return true;
            }
            case "IsApiContractPresent":
            case "IsApiContractNotPresent":
            {
                if (args.Length < 1 || args.Length > 3)
                {
                    errorMessage = $"Method '{condition.MethodName}' expects between 1 and 3 arguments.";
                    return false;
                }

                var contractType = ResolveConditionalTypeSymbol(compilation, args[0]);
                var hasContract = contractType is not null;
                if (!hasContract)
                {
                    result = condition.MethodName == "IsApiContractNotPresent";
                    return true;
                }

                if (args.Length == 1)
                {
                    result = condition.MethodName == "IsApiContractPresent";
                    return true;
                }

                if (!TryParseNonNegativeInt(args[1], out var requiredMajor))
                {
                    errorMessage = $"Contract major version '{args[1]}' is not a valid non-negative integer.";
                    return false;
                }

                var requiredMinor = 0;
                if (args.Length > 2 && !TryParseNonNegativeInt(args[2], out requiredMinor))
                {
                    errorMessage = $"Contract minor version '{args[2]}' is not a valid non-negative integer.";
                    return false;
                }

                var actualMajor = 1;
                var actualMinor = 0;
                if (TryGetContractVersion(contractType!, out var parsedMajor, out var parsedMinor))
                {
                    actualMajor = parsedMajor;
                    actualMinor = parsedMinor;
                }

                var versionSatisfied = actualMajor > requiredMajor ||
                                       (actualMajor == requiredMajor && actualMinor >= requiredMinor);
                var contractPresent = hasContract && versionSatisfied;
                result = condition.MethodName == "IsApiContractPresent" ? contractPresent : !contractPresent;
                return true;
            }
            default:
                errorMessage = $"Unsupported conditional method '{condition.MethodName}'.";
                return false;
        }
    }

    private static INamedTypeSymbol? ResolveConditionalTypeSymbol(Compilation compilation, string rawTypeToken)
    {
        if (string.IsNullOrWhiteSpace(rawTypeToken))
        {
            return null;
        }

        var token = rawTypeToken.Trim();
        token = XamlTypeTokenSemantics.TrimGlobalQualifier(token);

        if (XamlTokenSplitSemantics.TrySplitAtFirstSeparator(
                token,
                ',',
                out var typeToken,
                out _))
        {
            token = typeToken;
        }

        if (token.Length == 0)
        {
            return null;
        }

        var resolved = compilation.GetTypeByMetadataName(token);
        if (resolved is not null)
        {
            return resolved;
        }

        return TryFindTypeByFullName(compilation.Assembly.GlobalNamespace, token);
    }

    private static INamedTypeSymbol? TryFindTypeByFullName(INamespaceSymbol namespaceSymbol, string fullTypeName)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            if (string.Equals(
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty, StringComparison.Ordinal),
                    fullTypeName,
                    StringComparison.Ordinal))
            {
                return type;
            }
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            var resolved = TryFindTypeByFullName(childNamespace, fullTypeName);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryGetContractVersion(INamedTypeSymbol contractType, out int major, out int minor)
    {
        major = 1;
        minor = 0;

        foreach (var attribute in contractType.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (attributeName is not ("Windows.Foundation.Metadata.ContractVersionAttribute" or "Windows.Foundation.Metadata.VersionAttribute") &&
                !string.Equals(attribute.AttributeClass?.Name, "ContractVersionAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 0)
            {
                continue;
            }

            if (TryDecodeContractVersion(attribute.ConstructorArguments[^1], out major, out minor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeContractVersion(
        TypedConstant argument,
        out int major,
        out int minor)
    {
        major = 1;
        minor = 0;

        if (argument.Value is null)
        {
            return false;
        }

        switch (argument.Value)
        {
            case ushort ushortVersion:
                major = ushortVersion;
                minor = 0;
                return true;
            case int intVersion:
                if (intVersion < 0)
                {
                    return false;
                }

                major = intVersion >> 16;
                minor = intVersion & 0xFFFF;
                return true;
            case uint uintVersion:
                major = (int)(uintVersion >> 16);
                minor = (int)(uintVersion & 0xFFFF);
                return true;
            case long longVersion:
                if (longVersion < 0)
                {
                    return false;
                }

                major = (int)(longVersion >> 16);
                minor = (int)(longVersion & 0xFFFF);
                return true;
            case ulong ulongVersion:
                major = (int)(ulongVersion >> 16);
                minor = (int)(ulongVersion & 0xFFFF);
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseNonNegativeInt(string token, out int value)
    {
        if (int.TryParse(token.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= 0)
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static IMethodSymbol? TryFindMatchingFactoryMethod(INamedTypeSymbol type, string methodName, int argumentCount)
    {
        return type.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.IsStatic &&
                method.DeclaredAccessibility == Accessibility.Public &&
                !method.IsGenericMethod &&
                !method.ReturnsVoid)
            .Where(method => method.Parameters.Length == argumentCount)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IMethodSymbol? TryFindMatchingConstructor(INamedTypeSymbol type, int argumentCount)
    {
        return type.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                !constructor.IsStatic)
            .Where(constructor => constructor.Parameters.Length == argumentCount)
            .OrderBy(static constructor => constructor.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
