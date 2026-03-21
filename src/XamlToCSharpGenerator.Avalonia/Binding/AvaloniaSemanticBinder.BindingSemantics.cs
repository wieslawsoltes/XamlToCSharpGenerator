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
using XamlToCSharpGenerator.Core.Configuration;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.MiniLanguageParsing.Selectors;
using XamlToCSharpGenerator.MiniLanguageParsing.Text;

namespace XamlToCSharpGenerator.Avalonia.Binding;

public sealed partial class AvaloniaSemanticBinder : IXamlSemanticBinder
{


    private static bool TryParseBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseBindingMarkup(value, TryParseMarkupExtension, out bindingMarkup);
    }

    private static bool TryParseXBindMarkup(string value, out XBindMarkup xBindMarkup)
    {
        return BindingEventMarkupParser.TryParseXBindMarkup(value, TryParseMarkupExtension, out xBindMarkup);
    }

    private static bool TryParseReflectionBindingMarkup(string value, out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseReflectionBindingMarkup(value, TryParseMarkupExtension, out bindingMarkup);
    }

    private static bool TryParseBindingMarkupCore(
        MarkupExtensionInfo markup,
        XamlMarkupExtensionKind extensionKind,
        out BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.TryParseBindingMarkupCore(
            markup,
            extensionKind,
            TryParseMarkupExtension,
            out bindingMarkup);
    }

    private static string? TryGetNamedMarkupArgument(MarkupExtensionInfo markup, params string[] argumentNames)
    {
        return BindingEventMarkupParser.TryGetNamedMarkupArgument(markup, argumentNames);
    }

    private static BindingMarkup NormalizeBindingQuerySyntax(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.NormalizeBindingQuerySyntax(bindingMarkup, TryParseMarkupExtension);
    }

    private static bool HasExplicitBindingSource(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.HasExplicitBindingSource(bindingMarkup);
    }

    private static int CountExplicitBindingSources(BindingMarkup bindingMarkup)
    {
        return BindingEventMarkupParser.CountExplicitBindingSources(bindingMarkup);
    }

    private static BindingMarkup CreateBindingSourceConflict(BindingMarkup bindingMarkup, string message)
    {
        return BindingEventMarkupParser.CreateBindingSourceConflict(bindingMarkup, message);
    }

    private static bool TryExtractReferenceElementName(string? sourceValue, out string elementName)
    {
        return BindingEventMarkupParser.TryExtractReferenceElementName(
            sourceValue,
            TryParseMarkupExtension,
            out elementName);
    }

    private static bool HasResolveByNameSemantics(INamedTypeSymbol ownerType, string propertyName)
    {
        var property = FindProperty(ownerType, propertyName);
        if (property is not null)
        {
            if (HasResolveByNameAttribute(property) ||
                (property.GetMethod is not null && HasResolveByNameAttribute(property.GetMethod)) ||
                (property.SetMethod is not null && HasResolveByNameAttribute(property.SetMethod)))
            {
                return true;
            }
        }

        var setterName = "Set" + propertyName;
        var getterName = "Get" + propertyName;
        for (var current = ownerType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(setterName).OfType<IMethodSymbol>())
            {
                if (HasResolveByNameAttribute(method))
                {
                    return true;
                }
            }

            foreach (var method in current.GetMembers(getterName).OfType<IMethodSymbol>())
            {
                if (HasResolveByNameAttribute(method))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasResolveByNameAttribute(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            if (attributeType.Name.Equals("ResolveByNameAttribute", StringComparison.Ordinal))
            {
                return true;
            }

            if (attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Avalonia.Controls.ResolveByNameAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildResolveByNameLiteralExpression(
        string rawValue,
        ITypeSymbol? targetType,
        out string expression)
    {
        expression = string.Empty;
        if (!TryParseResolveByNameReferenceToken(rawValue, out var referenceToken))
        {
            return false;
        }

        var resolveExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
            Escape(referenceToken.Name) +
            "\", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", " +
            MarkupContextBaseUriToken +
            ", " +
            MarkupContextParentStackToken +
            ")";

        expression = targetType is null
            ? resolveExpression
            : WrapWithTargetTypeCast(targetType, resolveExpression);
        return true;
    }

    private static bool TryParseResolveByNameReferenceToken(
        string rawValue,
        out ResolveByNameReferenceToken referenceToken)
    {
        return BindingEventMarkupParser.TryParseResolveByNameReferenceToken(
            rawValue,
            TryParseMarkupExtension,
            out referenceToken);
    }

    private static bool TryNormalizeReferenceName(string? rawName, out string normalizedName)
    {
        return BindingEventMarkupParser.TryNormalizeReferenceName(rawName, out normalizedName);
    }

    private static bool TryParseElementNameQuery(string path, out string elementName, out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseElementNameQuery(path, out elementName, out normalizedPath);
    }

    private static bool TryParseSelfQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseSelfQuery(path, out relativeSource, out normalizedPath);
    }

    private static bool TryParseParentQuery(
        string path,
        out RelativeSourceMarkup relativeSource,
        out string normalizedPath)
    {
        return BindingEventMarkupParser.TryParseParentQuery(path, out relativeSource, out normalizedPath);
    }

    private static bool CanUseCompiledBinding(BindingMarkup bindingMarkup)
    {
        return !bindingMarkup.HasSourceConflict &&
               string.IsNullOrWhiteSpace(bindingMarkup.ElementName) &&
               bindingMarkup.RelativeSource is null &&
               string.IsNullOrWhiteSpace(bindingMarkup.Source);
    }

    private readonly record struct CompiledBindingAccessorResolution(
        string AccessorExpression,
        string NormalizedPath,
        string? ResultTypeName,
        ITypeSymbol? ResultTypeSymbol,
        ImmutableArray<string> DependencyNames);

    private static bool TryResolveCompiledBindingSourceType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? ambientDataType,
        INamedTypeSymbol? bindingTargetType,
        out INamedTypeSymbol? sourceType,
        out bool requiresAmbientDataType,
        out bool hasInvalidLocalDataType)
    {
        sourceType = null;
        requiresAmbientDataType = false;
        hasInvalidLocalDataType = false;

        if (bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName) ||
            !string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            return false;
        }

        if (bindingMarkup.RelativeSource is { } relativeSource)
        {
            if (string.Equals(relativeSource.Mode, "Self", StringComparison.OrdinalIgnoreCase))
            {
                sourceType = bindingTargetType;
                return sourceType is not null;
            }

            if (!string.IsNullOrWhiteSpace(relativeSource.AncestorTypeToken))
            {
                sourceType = ResolveTypeToken(compilation, document, relativeSource.AncestorTypeToken!, document.ClassNamespace);
                return sourceType is not null;
            }

            if (string.Equals(relativeSource.Mode, "DataContext", StringComparison.OrdinalIgnoreCase))
            {
                if (TryResolveBindingMarkupDataType(
                        compilation,
                        document,
                        bindingMarkup,
                        out sourceType,
                        out var hasExplicitLocalDataType))
                {
                    return true;
                }

                if (hasExplicitLocalDataType)
                {
                    hasInvalidLocalDataType = true;
                    return false;
                }

                sourceType = ambientDataType;
                requiresAmbientDataType = sourceType is null;
                return sourceType is not null;
            }

            return false;
        }

        if (TryResolveBindingMarkupDataType(
                compilation,
                document,
                bindingMarkup,
                out sourceType,
                out var hasExplicitBindingLocalDataType))
        {
            return true;
        }

        if (hasExplicitBindingLocalDataType)
        {
            hasInvalidLocalDataType = true;
            return false;
        }

        sourceType = ambientDataType;
        requiresAmbientDataType = sourceType is null;
        return sourceType is not null;
    }

    private static bool TryResolveBindingSourceTypeForScopeInference(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? ambientDataType,
        INamedTypeSymbol? bindingTargetType,
        out INamedTypeSymbol? sourceType,
        out bool requiresAmbientDataType)
    {
        sourceType = null;
        requiresAmbientDataType = false;

        if (bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            sourceType = ResolveNamedElementBindingSourceType(
                compilation,
                document,
                bindingMarkup.ElementName!);
            return sourceType is not null;
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.Source))
        {
            return false;
        }

        return TryResolveCompiledBindingSourceType(
            compilation,
            document,
            bindingMarkup,
            ambientDataType,
            bindingTargetType,
            out sourceType,
            out requiresAmbientDataType,
            out _);
    }

    private static bool TryResolveBindingMarkupDataType(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        out INamedTypeSymbol? sourceType,
        out bool hasExplicitDataType)
    {
        sourceType = null;
        hasExplicitDataType = !string.IsNullOrWhiteSpace(bindingMarkup.DataType);
        if (!hasExplicitDataType)
        {
            return false;
        }

        sourceType = ResolveTypeFromTypeExpression(
            compilation,
            document,
            bindingMarkup.DataType,
            document.ClassNamespace);
        return sourceType is not null;
    }

    private static INamedTypeSymbol? ResolveNamedElementBindingSourceType(
        Compilation compilation,
        XamlDocumentModel document,
        string elementName)
    {
        // Binding ElementName resolution is namescope-scoped at runtime.
        // The current document model only exposes a flat name list, so only the root
        // element can be inferred safely here without cross-template false positives.
        if (!string.Equals(document.RootObject.Name, elementName, StringComparison.Ordinal))
        {
            return null;
        }

        if (document.IsClassBacked)
        {
            var classSymbol = compilation.GetTypeByMetadataName(document.ClassFullName!);
            if (classSymbol is not null)
            {
                return classSymbol;
            }
        }

        return ResolveTypeSymbol(
            compilation,
            document.RootObject.XmlNamespace,
            document.RootObject.XmlTypeName);
    }

    private static bool TryBuildCompiledBindingAccessorExpression(
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol sourceType,
        string rawPath,
        ITypeSymbol? targetPropertyType,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out CompiledBindingAccessorResolution resolution,
        out string errorMessage)
    {
        var accessorExpression = "source";
        var normalizedPath = string.IsNullOrWhiteSpace(rawPath) ? "." : rawPath.Trim();
        var resultTypeName = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        resolution = new CompiledBindingAccessorResolution(
            accessorExpression,
            normalizedPath,
            resultTypeName,
            sourceType,
            BuildCompiledBindingDependencyNames(normalizedPath));
        errorMessage = string.Empty;

        if (normalizedPath == ".")
        {
            resolution = new CompiledBindingAccessorResolution(
                accessorExpression,
                normalizedPath,
                resultTypeName,
                sourceType,
                BuildCompiledBindingDependencyNames(normalizedPath));
            return true;
        }

        if (!CompiledBindingPathParser.TryParse(normalizedPath, out var segments, out var leadingNotCount, out errorMessage))
        {
            return false;
        }

        if (segments.Length == 0)
        {
            normalizedPath = ".";
            resolution = new CompiledBindingAccessorResolution(
                accessorExpression,
                normalizedPath,
                resultTypeName,
                sourceType,
                BuildCompiledBindingDependencyNames(normalizedPath));
            return true;
        }

        var expressionBuilder = "source";
        ITypeSymbol? currentType = sourceType;
        var normalizedSegments = new List<string>(segments.Length);
        var accessibilityWithin = GetGeneratedCodeAccessibilityWithinSymbol(compilation, document);
        var commandType = ResolveContractType(compilation, TypeContractId.SystemICommand);
        var treatLastMethodAsCommand = IsCommandTargetType(targetPropertyType, commandType);
        var pendingConditionalAccessScopes = new List<PendingConditionalAccessScope>();

        for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            var segment = segments[segmentIndex];
            if (currentType is not INamedTypeSymbol currentNamedType)
            {
                errorMessage = "intermediate segment is not a named CLR type";
                return false;
            }

            if (segment.AcceptsNull &&
                !CanUseNullConditionalAccess(currentType))
            {
                errorMessage = $"null-conditional access '?.' is not valid on '{currentType.ToDisplayString()}'";
                return false;
            }

            if (segment.IsAttachedProperty)
            {
                if (string.IsNullOrWhiteSpace(segment.AttachedOwnerTypeToken))
                {
                    errorMessage = "attached property owner type token is missing";
                    return false;
                }

                var ownerTypeToken = segment.AttachedOwnerTypeToken!;
                var ownerType = ResolveTypeToken(compilation, document, ownerTypeToken, document.ClassNamespace);
                if (ownerType is null)
                {
                    errorMessage = $"attached property owner type '{ownerTypeToken}' could not be resolved";
                    return false;
                }

                var getterMethod = FindAttachedPropertyGetterMethod(ownerType, segment.MemberName, currentType);
                if (getterMethod is null || getterMethod.ReturnsVoid)
                {
                    errorMessage = $"attached property getter 'Get{segment.MemberName}' was not found on '{ownerType.ToDisplayString()}'";
                    return false;
                }

                var ownerTypeName = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (segment.AcceptsNull)
                {
                    var scope = CreatePendingConditionalAccessScope(
                        expressionBuilder,
                        ownerTypeName + "." + getterMethod.Name,
                        out var targetVariableName);
                    expressionBuilder = ownerTypeName + "." + getterMethod.Name + "(" + targetVariableName + ")";
                    pendingConditionalAccessScopes.Add(scope);
                }
                else
                {
                    expressionBuilder = ownerTypeName + "." + getterMethod.Name + "(" + expressionBuilder + ")";
                }

                currentType = getterMethod.ReturnType;
                var attachedNormalizedSegment = "(" + ownerTypeToken + "." + segment.MemberName + ")";
                if (normalizedSegments.Count == 0)
                {
                    normalizedSegments.Add(attachedNormalizedSegment);
                }
                else
                {
                    var separator = segment.AcceptsNull ? "?." : ".";
                    normalizedSegments.Add(separator + attachedNormalizedSegment);
                }

                foreach (var indexerToken in segment.Indexers)
                {
                    if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                    {
                        return false;
                    }

                    expressionBuilder += "[" + indexerExpression + "]";
                    normalizedSegments[normalizedSegments.Count - 1] += "[" + normalizedIndexerToken + "]";
                    currentType = resultType;
                }

                var attachedSegmentIndex = normalizedSegments.Count - 1;
                var updatedAttachedSegment = normalizedSegments[attachedSegmentIndex];
                if (!TryApplyStreamOperators(
                        compilation,
                        segment.StreamCount,
                        ref currentType,
                        ref expressionBuilder,
                        ref updatedAttachedSegment,
                        out errorMessage))
                {
                    return false;
                }

                normalizedSegments[attachedSegmentIndex] = updatedAttachedSegment;
                continue;
            }

            var isLastSegment = segmentIndex == segments.Length - 1;
            var propertyAccessExpression = string.Empty;
            var propertyNormalizedSegment = string.Empty;
            ITypeSymbol propertyResultType = currentNamedType;
            var foundInaccessibleProperty = false;
            PendingConditionalAccessScope? propertyPendingConditionalAccessScope = null;
            var propertyResolved = !segment.IsMethodCall &&
                                   TryResolvePropertyPathAccessExpression(
                                       compilation,
                                       accessibilityWithin,
                                       currentNamedType,
                                       expressionBuilder,
                                       segment.MemberName,
                                       segment.AcceptsNull,
                                       unsafeAccessors,
                                       out propertyAccessExpression,
                                       out propertyNormalizedSegment,
                                       out propertyResultType,
                                       out foundInaccessibleProperty,
                                       out propertyPendingConditionalAccessScope);
            var commandErrorMessage = string.Empty;
            if (!segment.IsMethodCall &&
                treatLastMethodAsCommand &&
                isLastSegment &&
                leadingNotCount == 0 &&
                !propertyResolved &&
                !foundInaccessibleProperty &&
                TryBuildMethodCommandAccessorExpression(
                    compilation,
                    accessibilityWithin,
                    currentNamedType,
                    pendingConditionalAccessScopes.Count == 0
                        ? expressionBuilder
                        : ApplyPendingConditionalAccessScopes(expressionBuilder, currentNamedType, pendingConditionalAccessScopes),
                    segment.MemberName,
                    unsafeAccessors,
                    out var commandAccessorExpression,
                    out var commandResultTypeName,
                    out commandErrorMessage))
            {
                if (normalizedSegments.Count == 0)
                {
                    normalizedSegments.Add(segment.MemberName + "()");
                }
                else
                {
                    normalizedSegments.Add((segment.AcceptsNull ? "?." : ".") + segment.MemberName + "()");
                }

                normalizedPath = string.Join(string.Empty, normalizedSegments);
                resultTypeName = commandResultTypeName;
                resolution = new CompiledBindingAccessorResolution(
                    commandAccessorExpression,
                    normalizedPath,
                    resultTypeName,
                    commandType,
                    BuildCompiledBindingDependencyNames(normalizedPath));
                return true;
            }

            if (!segment.IsMethodCall &&
                treatLastMethodAsCommand &&
                isLastSegment &&
                !propertyResolved &&
                !foundInaccessibleProperty &&
                !string.IsNullOrWhiteSpace(commandErrorMessage))
            {
                errorMessage = commandErrorMessage;
                return false;
            }

            var methodAccessExpression = string.Empty;
            var methodNormalizedSegment = string.Empty;
            ITypeSymbol methodResultType = currentNamedType;
            var foundInaccessibleMethod = false;
            PendingConditionalAccessScope? methodPendingConditionalAccessScope = null;
            var methodResolved = !segment.IsMethodCall &&
                                 !propertyResolved &&
                                 !foundInaccessibleProperty &&
                                 TryResolveParameterlessMethodPathAccessExpression(
                                     compilation,
                                     accessibilityWithin,
                                     currentNamedType,
                                     expressionBuilder,
                                     segment.MemberName,
                                     segment.AcceptsNull,
                                     unsafeAccessors,
                                     out methodAccessExpression,
                                     out methodNormalizedSegment,
                                     out methodResultType,
                                     out foundInaccessibleMethod,
                                     out methodPendingConditionalAccessScope);
            if (!propertyResolved && !methodResolved)
            {
                if (!segment.IsMethodCall &&
                    !foundInaccessibleProperty &&
                    !foundInaccessibleMethod)
                {
                    var parameterlessMethod = FindParameterlessMethod(currentNamedType, segment.MemberName);
                    if (parameterlessMethod is not null && parameterlessMethod.ReturnsVoid)
                    {
                        errorMessage = $"method segment '{segment.MemberName}' is not a supported parameterless method with a return value";
                        return false;
                    }
                }

                if (segment.IsMethodCall &&
                    TryResolveMethodInvocation(
                        compilation,
                        accessibilityWithin,
                        currentNamedType,
                        expressionBuilder,
                        segment.MemberName,
                        segment.MethodArguments,
                        segment.AcceptsNull,
                        unsafeAccessors,
                        out var methodInvocationExpression,
                        out var methodInvocationNormalizedSegment,
                        out var methodReturnType,
                        out var methodInvocationPendingConditionalAccessScope,
                        out errorMessage))
                {
                    expressionBuilder = methodInvocationExpression;
                    currentType = methodReturnType;
                    if (methodInvocationPendingConditionalAccessScope is { } pendingConditionalAccessScope)
                    {
                        pendingConditionalAccessScopes.Add(pendingConditionalAccessScope);
                    }

                    var segmentSeparator = normalizedSegments.Count == 0
                        ? string.Empty
                        : segment.AcceptsNull ? "?." : ".";
                    var normalizedInvocationSegment = segmentSeparator + methodInvocationNormalizedSegment;
                    if (!TryApplyStreamOperators(
                            compilation,
                            segment.StreamCount,
                            ref currentType,
                            ref expressionBuilder,
                            ref normalizedInvocationSegment,
                            out errorMessage))
                    {
                        return false;
                    }

                    normalizedSegments.Add(normalizedInvocationSegment);
                    continue;
                }

                if (segment.IsMethodCall && !string.IsNullOrWhiteSpace(errorMessage))
                {
                    return false;
                }

                if (foundInaccessibleProperty || foundInaccessibleMethod)
                {
                    errorMessage = $"segment '{segment.MemberName}' is not accessible on '{currentNamedType.ToDisplayString()}'";
                    return false;
                }

                errorMessage = $"segment '{segment.MemberName}' was not found as a property or parameterless method on '{currentNamedType.ToDisplayString()}'";
                return false;
            }

            string normalizedSegment;
            if (propertyResolved)
            {
                expressionBuilder = propertyAccessExpression;
                currentType = propertyResultType;
                normalizedSegment = propertyNormalizedSegment;
                if (propertyPendingConditionalAccessScope is { } pendingConditionalAccessScope)
                {
                    pendingConditionalAccessScopes.Add(pendingConditionalAccessScope);
                }
            }
            else
            {
                expressionBuilder = methodAccessExpression;
                currentType = methodResultType;
                normalizedSegment = methodNormalizedSegment;
                if (methodPendingConditionalAccessScope is { } pendingConditionalAccessScope)
                {
                    pendingConditionalAccessScopes.Add(pendingConditionalAccessScope);
                }
            }

            foreach (var indexerToken in segment.Indexers)
            {
                if (!TryBuildIndexerExpression(currentType, indexerToken, out var indexerExpression, out var normalizedIndexerToken, out var resultType, out errorMessage))
                {
                    return false;
                }

                expressionBuilder += "[" + indexerExpression + "]";
                normalizedSegment += "[" + normalizedIndexerToken + "]";
                currentType = resultType;
            }

            if (!string.IsNullOrWhiteSpace(segment.CastTypeToken))
            {
                var castTypeToken = segment.CastTypeToken!;
                var castType = ResolveTypeToken(compilation, document, castTypeToken, document.ClassNamespace);
                if (castType is null)
                {
                    errorMessage = $"cast type '{castTypeToken}' could not be resolved";
                    return false;
                }

                expressionBuilder = "((" +
                                    castType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                    ")" +
                                    expressionBuilder +
                                    ")";
                normalizedSegment = "((" + castTypeToken + ")" + normalizedSegment + ")";
                currentType = castType;
            }

            if (!TryApplyStreamOperators(
                    compilation,
                    segment.StreamCount,
                    ref currentType,
                    ref expressionBuilder,
                    ref normalizedSegment,
                    out errorMessage))
            {
                return false;
            }

            if (normalizedSegments.Count == 0)
            {
                normalizedSegments.Add(normalizedSegment);
            }
            else
            {
                var separator = segment.AcceptsNull ? "?." : ".";
                normalizedSegments.Add(separator + normalizedSegment);
            }
        }

        if (pendingConditionalAccessScopes.Count > 0)
        {
            expressionBuilder = ApplyPendingConditionalAccessScopes(expressionBuilder, currentType!, pendingConditionalAccessScopes);
        }

        if (leadingNotCount > 0)
        {
            for (var i = 0; i < leadingNotCount; i++)
            {
                expressionBuilder = "(!global::System.Convert.ToBoolean(" + expressionBuilder + "))";
            }
        }

        accessorExpression = expressionBuilder;
        resultTypeName = currentType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? resultTypeName;
        if (normalizedSegments.Count > 0)
        {
            normalizedPath = string.Join(string.Empty, normalizedSegments);
            if (leadingNotCount > 0)
            {
                normalizedPath = new string('!', leadingNotCount) + normalizedPath;
            }
        }

        resolution = new CompiledBindingAccessorResolution(
            accessorExpression,
            normalizedPath,
            resultTypeName,
            currentType,
            BuildCompiledBindingDependencyNames(normalizedPath));
        return true;
    }

    private static ImmutableArray<string> BuildCompiledBindingDependencyNames(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            !CompiledBindingPathParser.TryParse(
                normalizedPath,
                out var segments,
                out _,
                out _))
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var pathBuilder = new System.Text.StringBuilder();

        foreach (var segment in segments)
        {
            if (segment.IsAttachedProperty ||
                !string.IsNullOrWhiteSpace(segment.CastTypeToken) ||
                segment.Indexers.Length > 0 ||
                segment.StreamCount > 0 ||
                segment.MethodArguments.Length > 0)
            {
                break;
            }

            if (segment.IsMethodCall)
            {
                break;
            }

            if (pathBuilder.Length > 0)
            {
                pathBuilder.Append('.');
            }

            pathBuilder.Append(segment.MemberName);
            builder.Add(pathBuilder.ToString());
        }

        return builder.Count == 0
            ? ImmutableArray<string>.Empty
            : builder.ToImmutable();
    }

    private static bool TryResolvePropertyPathAccessExpression(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string targetExpression,
        string propertyName,
        bool acceptsNull,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string accessExpression,
        out string normalizedSegment,
        out ITypeSymbol resultType,
        out bool foundInaccessibleProperty,
        out PendingConditionalAccessScope? pendingConditionalAccessScope)
    {
        accessExpression = string.Empty;
        normalizedSegment = string.Empty;
        resultType = targetType;
        foundInaccessibleProperty = false;
        pendingConditionalAccessScope = null;

        var property = FindAccessibleProperty(compilation, accessibilityWithin, targetType, propertyName, out foundInaccessibleProperty);
        if (property is not null)
        {
            if (property.IsStatic)
            {
                accessExpression = property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                   "." +
                                   property.Name;
                if (acceptsNull)
                {
                    pendingConditionalAccessScope = CreatePendingConditionalAccessScope(
                        targetExpression,
                        accessExpression,
                        out _);
                }
            }
            else
            {
                accessExpression = targetExpression + (acceptsNull ? "?." : ".") + property.Name;
            }

            normalizedSegment = property.Name;
            resultType = property.Type;
            return true;
        }

        if (!foundInaccessibleProperty ||
            unsafeAccessors is null ||
            !SupportsUnsafeAccessor(compilation))
        {
            return false;
        }

        var inaccessibleProperty = FindProperty(targetType, propertyName);
        if (inaccessibleProperty?.GetMethod is not IMethodSymbol getter ||
            getter.IsStatic)
        {
            return false;
        }

        var helperMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, getter);
        if (acceptsNull)
        {
            pendingConditionalAccessScope = CreateUnsafeAccessorPendingConditionalAccessScope(
                targetExpression,
                helperMethodName,
                Array.Empty<string>(),
                out accessExpression);
        }
        else
        {
            accessExpression = BuildUnsafeAccessorInvocationExpression(
                targetExpression,
                helperMethodName,
                acceptsNull,
                Array.Empty<string>(),
                inaccessibleProperty.Type);
        }

        normalizedSegment = inaccessibleProperty.Name;
        resultType = inaccessibleProperty.Type;
        return true;
    }

    private static bool TryResolveParameterlessMethodPathAccessExpression(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string targetExpression,
        string methodName,
        bool acceptsNull,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string accessExpression,
        out string normalizedSegment,
        out ITypeSymbol resultType,
        out bool foundInaccessibleMethod,
        out PendingConditionalAccessScope? pendingConditionalAccessScope)
    {
        accessExpression = string.Empty;
        normalizedSegment = string.Empty;
        resultType = targetType;
        foundInaccessibleMethod = false;
        pendingConditionalAccessScope = null;

        var method = FindAccessibleParameterlessMethod(
            compilation,
            accessibilityWithin,
            targetType,
            methodName,
            out foundInaccessibleMethod);
        if (method is not null)
        {
            if (method.ReturnsVoid)
            {
                return false;
            }

            accessExpression = targetExpression + (acceptsNull ? "?." : ".") + method.Name + "()";
            normalizedSegment = method.Name + "()";
            resultType = method.ReturnType;
            return true;
        }

        if (!foundInaccessibleMethod ||
            unsafeAccessors is null ||
            !SupportsUnsafeAccessor(compilation))
        {
            return false;
        }

        var inaccessibleMethod = FindParameterlessMethod(targetType, methodName);
        if (inaccessibleMethod is null || inaccessibleMethod.ReturnsVoid || inaccessibleMethod.IsStatic)
        {
            return false;
        }

        var helperMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, inaccessibleMethod);
        if (acceptsNull)
        {
            pendingConditionalAccessScope = CreateUnsafeAccessorPendingConditionalAccessScope(
                targetExpression,
                helperMethodName,
                Array.Empty<string>(),
                out accessExpression);
        }
        else
        {
            accessExpression = BuildUnsafeAccessorInvocationExpression(
                targetExpression,
                helperMethodName,
                acceptsNull,
                Array.Empty<string>(),
                inaccessibleMethod.ReturnType);
        }

        normalizedSegment = inaccessibleMethod.Name + "()";
        resultType = inaccessibleMethod.ReturnType;
        return true;
    }

    private static bool SupportsUnsafeAccessor(Compilation compilation)
    {
        var unsafeAccessorAttribute = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorAttribute");
        var unsafeAccessorKind = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.UnsafeAccessorKind");
        var coreLibrary = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;

        return unsafeAccessorAttribute is not null &&
               unsafeAccessorKind is not null &&
               SymbolEqualityComparer.Default.Equals(unsafeAccessorAttribute.ContainingAssembly, coreLibrary) &&
               SymbolEqualityComparer.Default.Equals(unsafeAccessorKind.ContainingAssembly, coreLibrary);
    }

    private static string RegisterUnsafeAccessorDefinition(
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        IMethodSymbol method)
    {
        var methodName = BuildUnsafeAccessorMethodName(method);
        unsafeAccessors?.Add(new ResolvedUnsafeAccessorDefinition(
            MethodName: methodName,
            UnsafeAccessorTargetName: method.Name,
            DeclaringTypeName: method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ReturnTypeName: method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ParameterTypeNames: method.Parameters
                .Select(static parameter => parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .ToImmutableArray()));
        return methodName;
    }

    private static string BuildUnsafeAccessorMethodName(IMethodSymbol method)
    {
        var signatureBuilder = new System.Text.StringBuilder();
        signatureBuilder.Append(method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        signatureBuilder.Append('|');
        signatureBuilder.Append(method.Name);
        signatureBuilder.Append('|');
        signatureBuilder.Append(method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        foreach (var parameter in method.Parameters)
        {
            signatureBuilder.Append('|');
            signatureBuilder.Append(parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return "__AXSG_UnsafeAccessor_" + ComputeStableHashHex(signatureBuilder.ToString());
    }

    private static string ComputeStableHashHex(string value)
    {
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    private static string BuildUnsafeAccessorInvocationExpression(
        string targetExpression,
        string helperMethodName,
        bool acceptsNull,
        IReadOnlyList<string> argumentExpressions,
        ITypeSymbol resultType)
    {
        if (!acceptsNull)
        {
            var invocationArguments = argumentExpressions.Count == 0
                ? targetExpression
                : targetExpression + ", " + string.Join(", ", argumentExpressions);
            return helperMethodName + "(" + invocationArguments + ")";
        }

        var targetVariableName = "__axsg_target_" +
                                 ComputeStableHashHex(
                                     targetExpression + "|" + helperMethodName + "|" + string.Join("|", argumentExpressions));
        var nullSafeInvocationArguments = argumentExpressions.Count == 0
            ? targetVariableName
            : targetVariableName + ", " + string.Join(", ", argumentExpressions);
        var nullSafeInvocation = helperMethodName + "(" + nullSafeInvocationArguments + ")";
        var requiresLiftedNullResult = resultType.IsValueType &&
                                       (resultType is not INamedTypeSymbol namedValueType ||
                                        !IsNullableValueType(namedValueType));
        if (requiresLiftedNullResult)
        {
            var nullableResultTypeName = "global::System.Nullable<" +
                                         resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                         ">";
            nullSafeInvocation = "(" + nullableResultTypeName + ")" + nullSafeInvocation;
            return "(" +
                   targetExpression +
                   " is { } " +
                   targetVariableName +
                   " ? " +
                   nullSafeInvocation +
                   " : default(" +
                   nullableResultTypeName +
                   "))";
        }

        return "(" +
               targetExpression +
               " is { } " +
               targetVariableName +
               " ? " +
               nullSafeInvocation +
               " : null)";
    }

    private static PendingConditionalAccessScope CreateUnsafeAccessorPendingConditionalAccessScope(
        string targetExpression,
        string helperMethodName,
        IReadOnlyList<string> argumentExpressions,
        out string accessExpression)
    {
        var pendingConditionalAccessScope = CreatePendingConditionalAccessScope(
            targetExpression,
            helperMethodName + "|" + string.Join("|", argumentExpressions),
            out var targetVariableName);
        var invocationArguments = argumentExpressions.Count == 0
            ? targetVariableName
            : targetVariableName + ", " + string.Join(", ", argumentExpressions);
        accessExpression = helperMethodName + "(" + invocationArguments + ")";
        return pendingConditionalAccessScope;
    }

    private static PendingConditionalAccessScope CreatePendingConditionalAccessScope(
        string targetExpression,
        string scopeIdentity,
        out string targetVariableName)
    {
        targetVariableName = "__axsg_target_" + ComputeStableHashHex(targetExpression + "|" + scopeIdentity);
        return new PendingConditionalAccessScope(targetExpression, targetVariableName);
    }

    private static string ApplyPendingConditionalAccessScopes(
        string expression,
        ITypeSymbol resultType,
        IReadOnlyList<PendingConditionalAccessScope> pendingConditionalAccessScopes)
    {
        for (var index = pendingConditionalAccessScopes.Count - 1; index >= 0; index--)
        {
            expression = BuildPendingConditionalAccessExpression(
                pendingConditionalAccessScopes[index],
                expression,
                resultType);
        }

        return expression;
    }

    private static string BuildPendingConditionalAccessExpression(
        PendingConditionalAccessScope pendingConditionalAccessScope,
        string trueBranchExpression,
        ITypeSymbol resultType)
    {
        var requiresLiftedNullResult = resultType.IsValueType &&
                                       (resultType is not INamedTypeSymbol namedValueType ||
                                        !IsNullableValueType(namedValueType));
        if (requiresLiftedNullResult)
        {
            var nullableResultTypeName = "global::System.Nullable<" +
                                         resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                         ">";
            return "(" +
                   pendingConditionalAccessScope.TargetExpression +
                   " is { } " +
                   pendingConditionalAccessScope.TargetVariableName +
                   " ? (" +
                   nullableResultTypeName +
                   ")" +
                   trueBranchExpression +
                   " : default(" +
                   nullableResultTypeName +
                   "))";
        }

        return "(" +
               pendingConditionalAccessScope.TargetExpression +
               " is { } " +
               pendingConditionalAccessScope.TargetVariableName +
               " ? " +
               trueBranchExpression +
               " : null)";
    }

    private readonly record struct PendingConditionalAccessScope(
        string TargetExpression,
        string TargetVariableName);

    private static bool TryBuildMethodCommandAccessorExpression(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string targetExpression,
        string methodName,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string accessorExpression,
        out string? resultTypeName,
        out string errorMessage)
    {
        accessorExpression = string.Empty;
        errorMessage = string.Empty;

        if (!TryResolveMethodCommandExecuteMethod(
                compilation,
                accessibilityWithin,
                targetType,
                methodName,
                unsafeAccessors,
                out var executeMethod,
                out errorMessage))
        {
            resultTypeName = null;
            return false;
        }

        if (!TryResolveMethodCommandCanExecuteMethod(
                compilation,
                accessibilityWithin,
                targetType,
                executeMethod,
                unsafeAccessors,
                out var canExecuteMethod,
                out var canExecuteErrorMessage))
        {
            resultTypeName = null;
            errorMessage = canExecuteErrorMessage;
            return false;
        }

        var dependsOnProperties = canExecuteMethod is null
            ? ImmutableArray<string>.Empty
            : GetDependsOnPropertyNames(canExecuteMethod);
        var commandType = ResolveContractType(compilation, TypeContractId.SystemICommand);
        resultTypeName = commandType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ??
                         "global::System.Windows.Input.ICommand";
        accessorExpression =
            "global::XamlToCSharpGenerator.Runtime.SourceGenMethodCommandRuntime.Create((object?)(" +
            targetExpression +
            "), " +
            BuildMethodCommandExecuteLambda(compilation, accessibilityWithin, unsafeAccessors, executeMethod) +
            ", " +
            BuildMethodCommandCanExecuteLambda(compilation, accessibilityWithin, unsafeAccessors, canExecuteMethod) +
            ", " +
            BuildStringArrayLiteral(dependsOnProperties) +
            ")";
        return true;
    }

    private static bool TryResolveMethodCommandExecuteMethod(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string methodName,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out IMethodSymbol executeMethod,
        out string errorMessage)
    {
        var candidates = ResolveMethodCommandCandidates(
            compilation,
            accessibilityWithin,
            targetType,
            methodName,
            unsafeAccessors is not null,
            static method => !method.IsStatic &&
                             method.MethodKind == MethodKind.Ordinary &&
                             !method.IsGenericMethod &&
                             !IsCommandLikeType(method.ReturnType) &&
                             method.Parameters.Length <= 1 &&
                             method.Parameters.All(static parameter => parameter.RefKind == RefKind.None));

        if (candidates.Length == 0)
        {
            executeMethod = null!;
            errorMessage = string.Empty;
            return false;
        }

        if (candidates.Length > 1)
        {
            executeMethod = null!;
            errorMessage =
                "method-to-command binding is ambiguous. Candidates: " +
                string.Join(
                    ", ",
                    candidates
                        .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                        .Select(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            return false;
        }

        executeMethod = candidates[0];
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryResolveMethodCommandCanExecuteMethod(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        IMethodSymbol executeMethod,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out IMethodSymbol? canExecuteMethod,
        out string errorMessage)
    {
        canExecuteMethod = null;
        errorMessage = string.Empty;

        var canExecuteCandidates = ResolveMethodCommandCandidates(
            compilation,
            accessibilityWithin,
            targetType,
            "Can" + executeMethod.Name,
            unsafeAccessors is not null,
            static method => !method.IsStatic &&
                             method.MethodKind == MethodKind.Ordinary &&
                             !method.IsGenericMethod &&
                             method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                             method.Parameters.Length == 1 &&
                             method.Parameters[0].Type.SpecialType == SpecialType.System_Object &&
                             method.Parameters[0].RefKind == RefKind.None);

        if (canExecuteCandidates.Length <= 1)
        {
            canExecuteMethod = canExecuteCandidates.Length == 0 ? null : canExecuteCandidates[0];
            return true;
        }

        errorMessage =
            "command can-execute binding is ambiguous. Candidates: " +
            string.Join(
                ", ",
                canExecuteCandidates
                    .OrderBy(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
                    .Select(static candidate => candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        return false;
    }

    private static ImmutableArray<IMethodSymbol> ResolveMethodCommandCandidates(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string methodName,
        bool allowUnsafeAccessors,
        Func<IMethodSymbol, bool> predicate)
    {
        var supportsUnsafeAccessor = allowUnsafeAccessors && SupportsUnsafeAccessor(compilation);
        if (targetType.TypeKind != TypeKind.Interface)
        {
            ImmutableArray<IMethodSymbol> fallbackCandidates = ImmutableArray<IMethodSymbol>.Empty;
            for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
            {
                var accessibleCandidates = ImmutableArray.CreateBuilder<IMethodSymbol>();
                ImmutableArray<IMethodSymbol>.Builder? inaccessibleCandidates = supportsUnsafeAccessor
                    ? ImmutableArray.CreateBuilder<IMethodSymbol>()
                    : null;

                foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
                {
                    if (!predicate(method))
                    {
                        continue;
                    }

                    if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, targetType))
                    {
                        accessibleCandidates.Add(method);
                        continue;
                    }

                    if (supportsUnsafeAccessor)
                    {
                        inaccessibleCandidates!.Add(method);
                    }
                }

                if (accessibleCandidates.Count > 0)
                {
                    return accessibleCandidates.ToImmutable();
                }

                if (fallbackCandidates.IsDefaultOrEmpty &&
                    inaccessibleCandidates is not null &&
                    inaccessibleCandidates.Count > 0)
                {
                    fallbackCandidates = inaccessibleCandidates.ToImmutable();
                }
            }

            return fallbackCandidates.IsDefault ? ImmutableArray<IMethodSymbol>.Empty : fallbackCandidates;
        }

        var accessibleInterfaceCandidates = ImmutableArray.CreateBuilder<IMethodSymbol>();
        ImmutableArray<IMethodSymbol>.Builder? inaccessibleInterfaceCandidates = supportsUnsafeAccessor
            ? ImmutableArray.CreateBuilder<IMethodSymbol>()
            : null;
        var seenMethods = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

        foreach (var current in EnumerateInstanceMemberLookupTypes(targetType))
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (!seenMethods.Add(method) ||
                    !predicate(method))
                {
                    continue;
                }

                if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, targetType))
                {
                    if (!ContainsEquivalentMethodCommandSignature(accessibleInterfaceCandidates, method))
                    {
                        accessibleInterfaceCandidates.Add(method);
                    }

                    continue;
                }

                if (supportsUnsafeAccessor &&
                    inaccessibleInterfaceCandidates is not null &&
                    !ContainsEquivalentMethodCommandSignature(inaccessibleInterfaceCandidates, method))
                {
                    inaccessibleInterfaceCandidates.Add(method);
                }
            }
        }

        if (accessibleInterfaceCandidates.Count > 0)
        {
            return accessibleInterfaceCandidates.ToImmutable();
        }

        if (inaccessibleInterfaceCandidates is not null && inaccessibleInterfaceCandidates.Count > 0)
        {
            return inaccessibleInterfaceCandidates.ToImmutable();
        }

        return ImmutableArray<IMethodSymbol>.Empty;
    }

    private static bool ContainsEquivalentMethodCommandSignature(
        ImmutableArray<IMethodSymbol>.Builder candidates,
        IMethodSymbol candidate)
    {
        foreach (var existing in candidates)
        {
            if (HasEquivalentMethodCommandSignature(existing, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEquivalentMethodCommandSignature(
        IMethodSymbol left,
        IMethodSymbol right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
            !SymbolEqualityComparer.Default.Equals(left.ReturnType, right.ReturnType) ||
            left.Parameters.Length != right.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Parameters.Length; index++)
        {
            var leftParameter = left.Parameters[index];
            var rightParameter = right.Parameters[index];
            if (leftParameter.RefKind != rightParameter.RefKind ||
                !SymbolEqualityComparer.Default.Equals(leftParameter.Type, rightParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<string> GetDependsOnPropertyNames(IMethodSymbol method)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in method.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            var isDependsOnAttribute =
                attributeType.Name.Equals("DependsOnAttribute", StringComparison.Ordinal) ||
                attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    .Equals("global::Avalonia.Metadata.DependsOnAttribute", StringComparison.Ordinal);
            if (!isDependsOnAttribute ||
                attribute.ConstructorArguments.Length != 1 ||
                attribute.ConstructorArguments[0].Value is not string propertyName ||
                string.IsNullOrWhiteSpace(propertyName) ||
                !seen.Add(propertyName))
            {
                continue;
            }

            builder.Add(propertyName);
        }

        return builder.Count == 0
            ? ImmutableArray<string>.Empty
            : builder.ToImmutable();
    }

    private static string BuildMethodCommandExecuteLambda(
        Compilation compilation,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        IMethodSymbol method)
    {
        var targetTypeName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var targetInstanceExpression = "((" + targetTypeName + ")target)";
        var canAccessDirectly = compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, method.ContainingType);
        var helperMethodName = canAccessDirectly ? null : RegisterUnsafeAccessorDefinition(unsafeAccessors, method);
        if (method.Parameters.Length == 0)
        {
            var invokeExpression = canAccessDirectly
                ? targetInstanceExpression + "." + method.Name + "()"
                : helperMethodName + "(" + targetInstanceExpression + ")";
            return "static (target, parameter) => { " + invokeExpression + "; }";
        }

        var parameterType = method.Parameters[0].Type;
        var parameterExpression = parameterType.SpecialType == SpecialType.System_Object
            ? "parameter"
            : "global::XamlToCSharpGenerator.Runtime.SourceGenMethodCommandRuntime.ConvertParameter<" +
              parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
              ">(parameter)";
        var callExpression = canAccessDirectly
            ? targetInstanceExpression + "." + method.Name + "(" + parameterExpression + ")"
            : helperMethodName + "(" + targetInstanceExpression + ", " + parameterExpression + ")";
        return "static (target, parameter) => { " + callExpression + "; }";
    }

    private static string BuildMethodCommandCanExecuteLambda(
        Compilation compilation,
        ISymbol accessibilityWithin,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        IMethodSymbol? method)
    {
        if (method is null)
        {
            return "null";
        }

        var targetTypeName = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var targetInstanceExpression = "((" + targetTypeName + ")target)";
        if (compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, method.ContainingType))
        {
            return "static (target, parameter) => " + targetInstanceExpression + "." + method.Name + "(parameter)";
        }

        var helperMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, method);
        return "static (target, parameter) => " + helperMethodName + "(" + targetInstanceExpression + ", parameter)";
    }

    private static bool CanUseNullConditionalAccess(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        if (type is INamedTypeSymbol namedType &&
            namedType.IsGenericType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        return false;
    }

    private static bool TryApplyStreamOperators(
        Compilation compilation,
        int streamCount,
        ref ITypeSymbol? currentType,
        ref string expressionBuilder,
        ref string normalizedSegment,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        if (streamCount <= 0)
        {
            return true;
        }

        for (var index = 0; index < streamCount; index++)
        {
            if (currentType is null)
            {
                errorMessage = "stream operator '^' cannot be applied because the current segment type is unknown";
                return false;
            }

            if (TryResolveTaskStreamType(compilation, currentType, out var taskResultType, out var useGenericTaskUnwrap))
            {
                if (useGenericTaskUnwrap)
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask<" +
                        taskResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                        ">(" +
                        expressionBuilder +
                        ")";
                }
                else
                {
                    expressionBuilder =
                        "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapTask(" +
                        expressionBuilder +
                        ")";
                }

                normalizedSegment += "^";
                currentType = taskResultType;
                continue;
            }

            if (TryResolveObservableStreamType(compilation, currentType, out var observableElementType))
            {
                expressionBuilder =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenCompiledBindingStreamHelper.UnwrapObservable<" +
                    observableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    ">(" +
                    expressionBuilder +
                    ")";
                normalizedSegment += "^";
                currentType = observableElementType;
                continue;
            }

            errorMessage =
                $"stream operator '^' is not supported for type '{currentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
            return false;
        }

        return true;
    }

    private static bool TryResolveTaskStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol resultType,
        out bool useGenericTaskUnwrap)
    {
        resultType = compilation.GetSpecialType(SpecialType.System_Object);
        useGenericTaskUnwrap = false;

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var taskOfType = ResolveContractType(compilation, TypeContractId.SystemTaskOfT);
        if (taskOfType is not null &&
            TryFindConstructedGenericType(namedSourceType, taskOfType, out var taskOfConstructedType) &&
            taskOfConstructedType.TypeArguments.Length == 1)
        {
            resultType = taskOfConstructedType.TypeArguments[0];
            useGenericTaskUnwrap = true;
            return true;
        }

        var taskType = ResolveContractType(compilation, TypeContractId.SystemTask);
        if (taskType is not null && IsTypeAssignableTo(sourceType, taskType))
        {
            resultType = compilation.GetSpecialType(SpecialType.System_Object);
            useGenericTaskUnwrap = false;
            return true;
        }

        return false;
    }

    private static bool TryResolveObservableStreamType(
        Compilation compilation,
        ITypeSymbol sourceType,
        out ITypeSymbol elementType)
    {
        elementType = compilation.GetSpecialType(SpecialType.System_Object);

        if (sourceType is not INamedTypeSymbol namedSourceType)
        {
            return false;
        }

        var observableType = ResolveContractType(compilation, TypeContractId.SystemObservableOfT);
        if (observableType is null)
        {
            return false;
        }

        if (!TryFindConstructedGenericType(namedSourceType, observableType, out var constructedObservableType) ||
            constructedObservableType.TypeArguments.Length != 1)
        {
            return false;
        }

        elementType = constructedObservableType.TypeArguments[0];
        return true;
    }

    private static bool TryFindConstructedGenericType(
        INamedTypeSymbol sourceType,
        INamedTypeSymbol genericTypeDefinition,
        out INamedTypeSymbol constructedType)
    {
        if (sourceType.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(sourceType.OriginalDefinition, genericTypeDefinition))
        {
            constructedType = sourceType;
            return true;
        }

        foreach (var interfaceType in sourceType.AllInterfaces)
        {
            if (interfaceType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = interfaceType;
                return true;
            }
        }

        for (var currentType = sourceType.BaseType; currentType is not null; currentType = currentType.BaseType)
        {
            if (currentType.IsGenericType &&
                SymbolEqualityComparer.Default.Equals(currentType.OriginalDefinition, genericTypeDefinition))
            {
                constructedType = currentType;
                return true;
            }
        }

        constructedType = null!;
        return false;
    }

    private static bool TryBuildIndexerExpression(
        ITypeSymbol collectionType,
        string rawIndexerToken,
        out string indexerExpression,
        out string normalizedIndexerToken,
        out ITypeSymbol resultType,
        out string errorMessage)
    {
        errorMessage = string.Empty;
        normalizedIndexerToken = rawIndexerToken;

        if (collectionType is IArrayTypeSymbol arrayType)
        {
            if (!XamlScalarLiteralSemantics.TryParseInt32(rawIndexerToken, out var arrayIndex))
            {
                indexerExpression = string.Empty;
                resultType = collectionType;
                errorMessage = $"array index '{rawIndexerToken}' is not a valid integer";
                return false;
            }

            indexerExpression = arrayIndex.ToString(CultureInfo.InvariantCulture);
            normalizedIndexerToken = indexerExpression;
            resultType = arrayType.ElementType;
            return true;
        }

        if (collectionType is not INamedTypeSymbol namedCollectionType)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not support indexers";
            return false;
        }

        IPropertySymbol? indexer = null;
        foreach (var current in EnumerateInstanceMemberLookupTypes(namedCollectionType))
        {
            indexer = current.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(property => property.IsIndexer &&
                                            property.Parameters.Length == 1 &&
                                            property.GetMethod is not null);
            if (indexer is not null)
            {
                break;
            }
        }

        if (indexer is null)
        {
            indexerExpression = string.Empty;
            resultType = collectionType;
            errorMessage = $"type '{collectionType.ToDisplayString()}' does not define a supported indexer";
            return false;
        }

        if (!TryConvertIndexerToken(rawIndexerToken, indexer.Parameters[0].Type, out indexerExpression, out normalizedIndexerToken))
        {
            resultType = collectionType;
            errorMessage = $"indexer token '{rawIndexerToken}' is incompatible with '{indexer.Parameters[0].Type.ToDisplayString()}'";
            return false;
        }

        resultType = indexer.Type;
        return true;
    }

    private static bool TryResolveMethodInvocation(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol targetType,
        string targetExpression,
        string methodName,
        ImmutableArray<string> methodArguments,
        bool acceptsNull,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        out string invocationExpression,
        out string normalizedSegment,
        out ITypeSymbol returnType,
        out PendingConditionalAccessScope? pendingConditionalAccessScope,
        out string errorMessage)
    {
        invocationExpression = string.Empty;
        normalizedSegment = string.Empty;
        returnType = targetType;
        pendingConditionalAccessScope = null;
        errorMessage = string.Empty;

        var supportsUnsafeAccessor = unsafeAccessors is not null && SupportsUnsafeAccessor(compilation);
        var accessibleCandidates = new List<IMethodSymbol>();
        var inaccessibleCandidates = new List<IMethodSymbol>();
        var foundInaccessibleCandidate = false;
        foreach (var current in EnumerateInstanceMemberLookupTypes(targetType))
        {
            foreach (var method in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    method.ReturnsVoid ||
                    method.IsGenericMethod ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
                    method.Parameters.Length != methodArguments.Length)
                {
                    continue;
                }

                if (!compilation.IsSymbolAccessibleWithin(method, accessibilityWithin, targetType))
                {
                    foundInaccessibleCandidate = true;
                    if (!supportsUnsafeAccessor)
                    {
                        continue;
                    }

                    inaccessibleCandidates.Add(method);
                    continue;
                }

                accessibleCandidates.Add(method);
            }
        }

        if (accessibleCandidates.Count == 0 && inaccessibleCandidates.Count == 0)
        {
            errorMessage = foundInaccessibleCandidate
                ? $"method segment '{methodName}' is not accessible on '{targetType.ToDisplayString()}'"
                : $"method segment '{methodName}' has no compatible overloads on '{targetType.ToDisplayString()}'";
            return false;
        }

        if (TrySelectBestMethodInvocationCandidate(
                accessibleCandidates,
                methodArguments,
                out var bestCandidate,
                out var bestArgumentExpressions,
                out var bestNormalizedArguments) ||
            TrySelectBestMethodInvocationCandidate(
                inaccessibleCandidates,
                methodArguments,
                out bestCandidate,
                out bestArgumentExpressions,
                out bestNormalizedArguments))
        {
            if (compilation.IsSymbolAccessibleWithin(bestCandidate!, accessibilityWithin, targetType))
            {
                invocationExpression = targetExpression +
                                       (acceptsNull ? "?." : ".") +
                                       bestCandidate.Name +
                                       "(" +
                                       string.Join(", ", bestArgumentExpressions!) +
                                       ")";
            }
            else
            {
                var helperMethodName = RegisterUnsafeAccessorDefinition(unsafeAccessors, bestCandidate);
                if (acceptsNull)
                {
                    pendingConditionalAccessScope = CreateUnsafeAccessorPendingConditionalAccessScope(
                        targetExpression,
                        helperMethodName,
                        bestArgumentExpressions!,
                        out invocationExpression);
                }
                else
                {
                    invocationExpression = BuildUnsafeAccessorInvocationExpression(
                        targetExpression,
                        helperMethodName,
                        acceptsNull,
                        bestArgumentExpressions!,
                        bestCandidate.ReturnType);
                }
            }

            normalizedSegment = bestCandidate.Name + "(" + string.Join(", ", bestNormalizedArguments!) + ")";
            returnType = bestCandidate.ReturnType;
            return true;
        }

        errorMessage = $"method segment '{methodName}' arguments do not match available overloads on '{targetType.ToDisplayString()}'";
        return false;
    }

    private static bool TrySelectBestMethodInvocationCandidate(
        List<IMethodSymbol> candidates,
        ImmutableArray<string> methodArguments,
        out IMethodSymbol? bestCandidate,
        out string[]? bestArgumentExpressions,
        out string[]? bestNormalizedArguments)
    {
        var bestScore = int.MaxValue;
        var bestObjectParameterCount = int.MaxValue;
        var bestSortKey = string.Empty;
        bestArgumentExpressions = null;
        bestNormalizedArguments = null;
        bestCandidate = null;

        foreach (var candidate in candidates.OrderBy(method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal))
        {
            var argumentExpressions = new string[methodArguments.Length];
            var normalizedArguments = new string[methodArguments.Length];
            var candidateScore = 0;
            var objectParameterCount = 0;
            var canUseCandidate = true;

            for (var i = 0; i < methodArguments.Length; i++)
            {
                var parameter = candidate.Parameters[i];
                if (parameter.Type.SpecialType == SpecialType.System_Object)
                {
                    objectParameterCount++;
                }

                if (!TryConvertMethodArgumentToken(
                        methodArguments[i],
                        parameter.Type,
                        out var argumentExpression,
                        out var normalizedArgument,
                        out var conversionCost))
                {
                    canUseCandidate = false;
                    break;
                }

                candidateScore += conversionCost;
                argumentExpressions[i] = BuildTypedInvocationArgument(parameter.Type, argumentExpression);
                normalizedArguments[i] = normalizedArgument;
            }

            if (!canUseCandidate)
            {
                continue;
            }

            var candidateSortKey = candidate.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (bestCandidate is null ||
                candidateScore < bestScore ||
                (candidateScore == bestScore && objectParameterCount < bestObjectParameterCount) ||
                (candidateScore == bestScore &&
                 objectParameterCount == bestObjectParameterCount &&
                 string.Compare(candidateSortKey, bestSortKey, StringComparison.Ordinal) < 0))
            {
                bestCandidate = candidate;
                bestScore = candidateScore;
                bestObjectParameterCount = objectParameterCount;
                bestSortKey = candidateSortKey;
                bestArgumentExpressions = argumentExpressions;
                bestNormalizedArguments = normalizedArguments;
            }
        }

        return bestCandidate is not null && bestArgumentExpressions is not null && bestNormalizedArguments is not null;
    }

    private static bool TryConvertMethodArgumentToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken,
        out int conversionCost)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        conversionCost = int.MaxValue;
        var tokenWasQuoted = IsQuotedLiteral(normalizedToken);

        if (parameterType is INamedTypeSymbol nullableType &&
            IsNullableValueType(nullableType))
        {
            if (XamlScalarLiteralSemantics.IsNullLiteral(normalizedToken))
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            if (TryConvertMethodArgumentToken(
                    rawToken,
                    nullableType.TypeArguments[0],
                    out expression,
                    out normalizedToken,
                    out var underlyingCost))
            {
                conversionCost = underlyingCost + 1;
                return true;
            }

            return false;
        }

        if (XamlScalarLiteralSemantics.IsNullLiteral(normalizedToken))
        {
            if (parameterType.IsReferenceType)
            {
                expression = "null";
                normalizedToken = "null";
                conversionCost = 2;
                return true;
            }

            return false;
        }

        var unquotedToken = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Char &&
            unquotedToken.Length == 1)
        {
            expression = "'" + EscapeChar(unquotedToken[0]) + "'";
            normalizedToken = unquotedToken;
            conversionCost = tokenWasQuoted ? 0 : 4;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Boolean &&
            XamlScalarLiteralSemantics.TryParseBoolean(unquotedToken, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            XamlScalarLiteralSemantics.TryParseInt32(unquotedToken, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Int64 &&
            XamlScalarLiteralSemantics.TryParseInt64(unquotedToken, out var longValue))
        {
            expression = longValue.ToString(CultureInfo.InvariantCulture) + "L";
            normalizedToken = longValue.ToString(CultureInfo.InvariantCulture);
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Double &&
            XamlScalarLiteralSemantics.TryParseDouble(unquotedToken, out var doubleValue))
        {
            expression = FormatDoubleLiteral(doubleValue);
            normalizedToken = unquotedToken;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Single &&
            XamlScalarLiteralSemantics.TryParseSingle(unquotedToken, out var floatValue))
        {
            expression = FormatSingleLiteral(floatValue);
            normalizedToken = unquotedToken;
            conversionCost = 0;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_Object)
        {
            if (XamlScalarLiteralSemantics.TryParseBoolean(unquotedToken, out boolValue))
            {
                expression = boolValue ? "true" : "false";
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (XamlScalarLiteralSemantics.TryParseInt32(unquotedToken, out intValue))
            {
                expression = intValue.ToString(CultureInfo.InvariantCulture);
                normalizedToken = expression;
                conversionCost = 50;
                return true;
            }

            if (XamlScalarLiteralSemantics.TryParseDouble(unquotedToken, out doubleValue))
            {
                expression = FormatDoubleLiteral(doubleValue);
                normalizedToken = unquotedToken;
                conversionCost = 50;
                return true;
            }

            expression = "\"" + Escape(unquotedToken) + "\"";
            normalizedToken = unquotedToken;
            conversionCost = 60;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum &&
            parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(unquotedToken, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                conversionCost = 0;
                return true;
            }
        }

        return false;
    }

    private static string BuildTypedInvocationArgument(ITypeSymbol parameterType, string expression)
    {
        if (parameterType.TypeKind == TypeKind.Dynamic)
        {
            return expression;
        }

        return "(" +
               parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
               ")(" +
               expression +
               ")";
    }

    private static bool IsNullableValueType(INamedTypeSymbol type)
    {
        return type.IsGenericType &&
               type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
               type.TypeArguments.Length == 1;
    }

    private static bool TryConvertIndexerToken(
        string rawToken,
        ITypeSymbol parameterType,
        out string expression,
        out string normalizedToken)
    {
        expression = string.Empty;
        normalizedToken = rawToken.Trim();
        var token = Unquote(normalizedToken);

        if (parameterType.SpecialType == SpecialType.System_Int32 &&
            XamlScalarLiteralSemantics.TryParseInt32(token, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            normalizedToken = expression;
            return true;
        }

        if (parameterType.SpecialType == SpecialType.System_String)
        {
            expression = "\"" + Escape(token) + "\"";
            normalizedToken = token;
            return true;
        }

        if (parameterType.TypeKind == TypeKind.Enum && parameterType is INamedTypeSymbol enumType)
        {
            var enumMember = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(member =>
                member.HasConstantValue &&
                member.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (enumMember is not null)
            {
                expression = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + enumMember.Name;
                normalizedToken = enumMember.Name;
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? TryResolveSetterValueType(
        INamedTypeSymbol? objectType,
        ImmutableArray<XamlPropertyAssignment> propertyAssignments,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType)
    {
        if (!IsSetterType(objectType))
        {
            return null;
        }

        foreach (var assignment in propertyAssignments)
        {
            if (assignment.IsAttached)
            {
                continue;
            }

            if (!NormalizePropertyName(assignment.PropertyName).Equals("Property", StringComparison.Ordinal))
            {
                continue;
            }

            if (TryResolveAvaloniaPropertyValueTypeFromToken(
                    assignment.Value,
                    compilation,
                    document,
                    defaultOwnerType,
                    out var valueType))
            {
                return valueType;
            }
        }

        return null;
    }

    private static bool IsSetterType(INamedTypeSymbol? objectType)
    {
        return objectType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               "global::Avalonia.Styling.Setter";
    }

    private static bool IsBindingObjectType(INamedTypeSymbol? typeSymbol, Compilation compilation)
    {
        if (typeSymbol is null)
        {
            return false;
        }

        if (IsTypeByMetadataName(typeSymbol, "Avalonia.Data.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.MultiBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Data.InstancedBinding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.Binding") ||
            IsTypeByMetadataName(typeSymbol, "Avalonia.MultiBinding"))
        {
            return true;
        }

        var bindingBaseType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingBase);
        if (bindingBaseType is not null && IsTypeAssignableTo(typeSymbol, bindingBaseType))
        {
            return true;
        }

        var bindingInterfaceType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface);
        if (bindingInterfaceType is not null && IsTypeAssignableTo(typeSymbol, bindingInterfaceType))
        {
            return true;
        }

        var bindingInterface2Type = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface2);
        if (bindingInterface2Type is not null && IsTypeAssignableTo(typeSymbol, bindingInterface2Type))
        {
            return true;
        }

        return false;
    }

    private static bool IsTypeByMetadataName(INamedTypeSymbol symbol, string metadataName)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Equals("global::" + metadataName, StringComparison.Ordinal);
    }

    private static ResolvedValueConversionResult CreateLiteralConversion(string expression)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Literal);
    }

    private static ResolvedValueConversionResult CreateLiteralConversion(
        string expression,
        ResolvedValueRequirements valueRequirements)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Literal,
            ValueRequirements: valueRequirements);
    }

    private static ResolvedValueConversionResult CreateMarkupExtensionConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false,
        bool requiresStaticResourceResolver = false,
        bool isRuntimeFallback = false,
        ResolvedResourceKeyExpression? resourceKey = null)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.MarkupExtension,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            IsRuntimeFallback: isRuntimeFallback,
            ResourceKey: resourceKey,
            ValueRequirements: requirements);
    }

    private static ResolvedValueConversionResult CreateBindingConversion(
        string expression,
        bool requiresRuntimeServiceProvider = false,
        bool requiresParentStack = false)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.Binding,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            ValueRequirements: requirements);
    }

    private static ResolvedValueConversionResult CreateTemplateBindingConversion(string expression)
    {
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.TemplateBinding);
    }

    private static ResolvedValueConversionResult CreateDynamicResourceBindingConversion(
        string expression,
        bool requiresRuntimeServiceProvider,
        bool requiresParentStack,
        ResolvedResourceKeyExpression? resourceKey = null)
    {
        var requirements = requiresRuntimeServiceProvider
            ? ResolvedValueRequirements.ForMarkupExtensionRuntime(requiresParentStack)
            : ResolvedValueRequirements.None;
        return new ResolvedValueConversionResult(
            Expression: expression,
            ValueKind: ResolvedValueKind.DynamicResourceBinding,
            RequiresRuntimeServiceProvider: requiresRuntimeServiceProvider,
            RequiresParentStack: requiresParentStack,
            RequiresProvideValueTarget: requirements.NeedsProvideValueTarget,
            RequiresRootObject: requirements.NeedsRootObject,
            RequiresBaseUri: requirements.NeedsBaseUri,
            ResourceKey: resourceKey,
            ValueRequirements: requirements);
    }

    private static bool TryResolveSetterValueWithPolicy(
        string rawValue,
        ITypeSymbol conversionTargetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        bool strictMode,
        bool preferTypedStaticResourceCoercion,
        bool allowObjectStringLiteralFallbackDuringConversion,
        bool allowCompatibilityStringLiteralFallback,
        string propertyName,
        string ownerDisplayName,
        int line,
        int column,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out SetterValueResolutionResult resolution,
        INamedTypeSymbol? selectorNestingTypeHint = null,
        bool setterContext = true,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        resolution = default;

        if (TryBuildRuntimeXamlFragmentExpression(
                rawValue,
                conversionTargetType,
                document,
                out var runtimeXamlSetterValue))
        {
            resolution = new SetterValueResolutionResult(
                Expression: runtimeXamlSetterValue,
                ValueKind: ResolvedValueKind.RuntimeXamlFallback,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        if (TryConvertValueConversion(
                rawValue,
                conversionTargetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedSetterValue,
                preferTypedStaticResourceCoercion: preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback: allowObjectStringLiteralFallbackDuringConversion,
                selectorNestingTypeHint: selectorNestingTypeHint,
                converterAttributes: converterAttributes))
        {
            resolution = new SetterValueResolutionResult(
                Expression: convertedSetterValue.Expression,
                ValueKind: convertedSetterValue.ValueKind,
                RequiresStaticResourceResolver: convertedSetterValue.RequiresStaticResourceResolver,
                ValueRequirements: convertedSetterValue.EffectiveRequirements);
            return true;
        }

        if (conversionTargetType.SpecialType == SpecialType.System_String)
        {
            resolution = new SetterValueResolutionResult(
                Expression: "\"" + Escape(rawValue) + "\"",
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        if (strictMode)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=StrictError (no compatibility fallback).",
                document.FilePath,
                line,
                column,
                true));
            return false;
        }

        if (TryGetAvaloniaUnsetValueExpression(compilation, out var unsetValueExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=AvaloniaProperty.UnsetValueFallback.",
                document.FilePath,
                line,
                column,
                strictMode));

            resolution = new SetterValueResolutionResult(
                Expression: unsetValueExpression,
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        if (allowCompatibilityStringLiteralFallback &&
            conversionTargetType.SpecialType == SpecialType.System_Object)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Strategy=CompatibilityStringLiteralFallback.",
                document.FilePath,
                line,
                column,
                false));
            resolution = new SetterValueResolutionResult(
                Expression: "\"" + Escape(rawValue) + "\"",
                ValueKind: ResolvedValueKind.Literal,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.None);
            return true;
        }

        var skipMessage = setterContext
            ? $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'. Setter was skipped."
            : $"Could not convert literal '{rawValue}' for '{propertyName}' on '{ownerDisplayName}'.";
        diagnostics.Add(new DiagnosticInfo(
            "AXSG0102",
            skipMessage,
            document.FilePath,
            line,
            column,
            strictMode));
        return false;
    }

    private static bool TryResolveAvaloniaPropertyValueTypeFromToken(
        string rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? defaultOwnerType,
        out ITypeSymbol? valueType)
    {
        valueType = null;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var token = rawValue.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        INamedTypeSymbol? ownerType = defaultOwnerType;
        var propertyToken = token;
        if (TrySplitOwnerQualifiedPropertyToken(
                token,
                out var ownerToken,
                out var normalizedPropertyToken))
        {
            propertyToken = normalizedPropertyToken;
            ownerType = ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace) ?? ownerType;
        }

        propertyToken = XamlTokenSplitSemantics.TrimTerminalSuffix(propertyToken, "Property");

        if (ownerType is null)
        {
            return false;
        }

        if (!TryFindAvaloniaPropertyField(ownerType, propertyToken, out _, out var propertyField))
        {
            return false;
        }

        valueType = TryGetAvaloniaPropertyValueType(propertyField.Type);
        return valueType is not null;
    }

    private static (ResolvedChildAttachmentMode Mode, string? ContentPropertyName) DetermineChildAttachment(INamedTypeSymbol? symbol)
    {
        if (symbol is null)
        {
            return (ResolvedChildAttachmentMode.None, null);
        }

        var contentPropertyName = FindContentPropertyName(symbol);
        var contentProperty = !string.IsNullOrWhiteSpace(contentPropertyName)
            ? FindProperty(symbol, contentPropertyName!)
            : null;
        if (contentProperty is not null &&
            CanUseContentPropertyForAttachment(contentProperty))
        {
            if (TryGetReadOnlyCollectionContentAttachmentMode(symbol, contentProperty, out var collectionAttachmentMode))
            {
                return (collectionAttachmentMode, null);
            }

            return (ResolvedChildAttachmentMode.Content, contentProperty.Name);
        }

        var implicitContentProperty = FindProperty(symbol, "Content");
        if (implicitContentProperty is not null &&
            CanUseContentPropertyForAttachment(implicitContentProperty))
        {
            return (ResolvedChildAttachmentMode.Content, implicitContentProperty.Name);
        }

        if (IsStyleBaseType(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        if (CanAddToCollectionProperty(symbol, "Children"))
        {
            return (ResolvedChildAttachmentMode.ChildrenCollection, null);
        }

        if (CanAddToCollectionProperty(symbol, "Items"))
        {
            return (ResolvedChildAttachmentMode.ItemsCollection, null);
        }

        if (HasDictionaryAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DictionaryAdd, null);
        }

        if (HasDirectAddMethod(symbol))
        {
            return (ResolvedChildAttachmentMode.DirectAdd, null);
        }

        return (ResolvedChildAttachmentMode.None, null);
    }

    private static string? ResolveContentPropertyTypeName(INamedTypeSymbol? ownerType, string? contentPropertyName)
    {
        if (ownerType is null || string.IsNullOrWhiteSpace(contentPropertyName))
        {
            return null;
        }

        return FindProperty(ownerType, contentPropertyName!)?
            .Type
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static bool IsStyleBaseType(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Styling.StyleBase")
            {
                return true;
            }
        }

        return false;
    }

    private static string? FindContentPropertyName(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.GetAttributes().Any(attribute =>
                        attribute.AttributeClass?.ToDisplayString() == "Avalonia.Metadata.ContentAttribute" ||
                        attribute.AttributeClass?.Name == "ContentAttribute") &&
                    CanUseContentPropertyForAttachment(property))
                {
                    return property.Name;
                }
            }
        }

        return null;
    }

    private static bool CanUseContentPropertyForAttachment(IPropertySymbol property)
    {
        if (property.SetMethod is not null)
        {
            return true;
        }

        if (property.GetMethod is null ||
            property.Type is not INamedTypeSymbol namedPropertyType)
        {
            return false;
        }

        return HasDictionaryAddMethod(namedPropertyType) || HasDirectAddMethod(namedPropertyType);
    }

    private static bool TryGetReadOnlyCollectionContentAttachmentMode(
        INamedTypeSymbol ownerType,
        IPropertySymbol property,
        out ResolvedChildAttachmentMode attachmentMode)
    {
        attachmentMode = ResolvedChildAttachmentMode.None;
        if (property.SetMethod is not null)
        {
            return false;
        }

        if (property.Name.Equals("Children", StringComparison.Ordinal) &&
            CanAddToCollectionProperty(ownerType, property.Name))
        {
            attachmentMode = ResolvedChildAttachmentMode.ChildrenCollection;
            return true;
        }

        if (property.Name.Equals("Items", StringComparison.Ordinal) &&
            CanAddToCollectionProperty(ownerType, property.Name))
        {
            attachmentMode = ResolvedChildAttachmentMode.ItemsCollection;
            return true;
        }

        return false;
    }

    private static BindingPriorityScope ResolveCurrentBindingPriorityScope(
        INamedTypeSymbol? nodeType,
        Compilation compilation,
        BindingPriorityScope inheritedScope)
    {
        if (nodeType is null)
        {
            return inheritedScope;
        }

        if (IsStyleType(nodeType, compilation) || IsControlThemeType(nodeType, compilation))
        {
            return BindingPriorityScope.Style;
        }

        if (IsTemplateScopeType(nodeType, compilation))
        {
            return BindingPriorityScope.Template;
        }

        return inheritedScope;
    }

    private static bool HasDirectAddMethod(INamedTypeSymbol type)
    {
        return CollectionAddService.HasDirectAddMethod(type);
    }

    private static bool HasDictionaryAddMethod(INamedTypeSymbol type)
    {
        return CollectionAddService.HasDictionaryAddMethod(type);
    }

    private static ImmutableArray<ResolvedCollectionAddInstruction> ResolveChildAddInstructions(
        INamedTypeSymbol? ownerType,
        ResolvedChildAttachmentMode attachmentMode,
        ImmutableArray<ResolvedObjectNode> children,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (ownerType is null || children.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
        }

        ITypeSymbol? collectionType = attachmentMode switch
        {
            ResolvedChildAttachmentMode.ChildrenCollection => FindProperty(ownerType, "Children")?.Type,
            ResolvedChildAttachmentMode.ItemsCollection => FindProperty(ownerType, "Items")?.Type,
            ResolvedChildAttachmentMode.DirectAdd => ownerType,
            _ => null
        };

        return CollectionAddService.ResolveCollectionAddInstructionsForValues(
            collectionType,
            children,
            compilation,
            document);
    }

    private static bool TryBuildKeyedDictionaryMergeContainer(
        IPropertySymbol property,
        ImmutableArray<ResolvedObjectNode> values,
        int line,
        int column,
        out ResolvedObjectNode container)
    {
        container = null!;
        if (values.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value.KeyExpression))
            {
                return false;
            }
        }

        container = new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: values,
            ChildAttachmentMode: ResolvedChildAttachmentMode.DictionaryAdd,
            ContentPropertyName: null,
            Line: line,
            Column: column,
            Condition: null);
        return true;
    }

    private static ImmutableArray<ResolvedObjectNode> MaterializePropertyElementValuesForTargetTypeIfNeeded(
        ITypeSymbol? targetType,
        ImmutableArray<ResolvedObjectNode> values,
        Compilation compilation,
        XamlDocumentModel document,
        int line,
        int column)
    {
        if (targetType is not INamedTypeSymbol namedTargetType || values.IsDefaultOrEmpty)
        {
            return values;
        }

        var (attachmentMode, contentPropertyName) = DetermineChildAttachment(namedTargetType);
        if (attachmentMode == ResolvedChildAttachmentMode.None)
        {
            return values;
        }

        var contentPropertyTypeName = attachmentMode == ResolvedChildAttachmentMode.Content
            ? ResolveContentPropertyTypeName(namedTargetType, contentPropertyName)
            : null;

        if (values.Length == 1)
        {
            var resolvedValueType = ResolveTypeToken(
                compilation,
                document,
                values[0].TypeName,
                document.ClassNamespace);

            if (resolvedValueType is null || IsTypeAssignableTo(resolvedValueType, namedTargetType))
            {
                return values;
            }
        }

        var childAddInstructions = ResolveChildAddInstructions(
            namedTargetType,
            attachmentMode,
            values,
            compilation,
            document);

        var container = new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            IsBindingObjectNode: false,
            FactoryExpression: null,
            FactoryValueRequirements: ResolvedValueRequirements.None,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: values,
            ChildAttachmentMode: attachmentMode,
            ContentPropertyName: contentPropertyName,
            Line: line,
            Column: column,
            Condition: null,
            ChildAddInstructions: childAddInstructions,
            ContentPropertyTypeName: contentPropertyTypeName);

        return ImmutableArray.Create(container);
    }

    private static bool CanAddToCollectionProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        if (HasDictionaryAddMethod(namedType))
        {
            return false;
        }

        return HasDirectAddMethod(namedType);
    }

    private static bool ShouldUseCollectionAddForContentProperty(
        IPropertySymbol contentProperty,
        ImmutableArray<ResolvedObjectNode> childValues,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (childValues.IsDefaultOrEmpty)
        {
            return false;
        }

        if (contentProperty.SetMethod is null)
        {
            return true;
        }

        foreach (var child in childValues)
        {
            var childType = ResolveTypeToken(
                compilation,
                document,
                child.TypeName,
                document.ClassNamespace);
            if (childType is null || !IsTypeAssignableTo(childType, contentProperty.Type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldUseDictionaryMergeForContentProperty(
        IPropertySymbol contentProperty,
        ImmutableArray<ResolvedObjectNode> childValues)
    {
        if (childValues.IsDefaultOrEmpty)
        {
            return false;
        }

        if (contentProperty.SetMethod is null)
        {
            return true;
        }

        foreach (var child in childValues)
        {
            if (!string.IsNullOrWhiteSpace(child.KeyExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanMergeDictionaryProperty(INamedTypeSymbol type, string propertyName)
    {
        var property = FindProperty(type, propertyName);
        if (property is null)
        {
            return false;
        }

        if (property.GetMethod is null)
        {
            return false;
        }

        var namedType = property.Type as INamedTypeSymbol;
        if (namedType is null)
        {
            return false;
        }

        return HasDictionaryAddMethod(namedType);
    }

    private static bool TryBindCollectionLiteralPropertyAssignment(
        INamedTypeSymbol objectType,
        IPropertySymbol property,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        out ResolvedPropertyElementAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (TryParseMarkupExtension(assignment.Value, out _))
        {
            return false;
        }

        if (property.SetMethod is not null ||
            property.GetMethod is null ||
            property.Type is not INamedTypeSymbol propertyType)
        {
            return false;
        }

        var isClassesLikeProperty =
            property.Name.Equals("Classes", StringComparison.Ordinal) ||
            propertyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Equals("global::Avalonia.Controls.Classes", StringComparison.Ordinal);

        ITypeSymbol? elementType = null;
        string[] literalItems;
        if (isClassesLikeProperty)
        {
            var classTokens = XamlListValueSemantics.SplitWhitespaceAndCommaTokens(assignment.Value);
            literalItems = classTokens.ToArray();
            elementType = compilation.GetSpecialType(SpecialType.System_String);
        }
        else
        {
            if (!TryGetCollectionElementType(
                    propertyType,
                    out var resolvedElementType,
                    out _,
                    out _))
            {
                return false;
            }

            if (resolvedElementType.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            elementType = resolvedElementType;
            literalItems = XamlListValueSemantics.SplitCommaSeparatedTokens(assignment.Value).ToArray();
        }

        if (elementType is null || elementType.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var values = ImmutableArray.CreateBuilder<ResolvedObjectNode>(literalItems.Length);
        foreach (var literalItem in literalItems)
        {
            values.Add(new ResolvedObjectNode(
                KeyExpression: null,
                Name: null,
                TypeName: elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                IsBindingObjectNode: false,
                FactoryExpression: "\"" + Escape(literalItem) + "\"",
                FactoryValueRequirements: ResolvedValueRequirements.None,
                UseServiceProviderConstructor: false,
                UseTopDownInitialization: false,
                PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
                PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
                EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
                Children: ImmutableArray<ResolvedObjectNode>.Empty,
                ChildAttachmentMode: ResolvedChildAttachmentMode.None,
                ContentPropertyName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition));
        }

        var resolvedValues = values.ToImmutable();
        var collectionAddInstructions = CollectionAddService.ResolveCollectionAddInstructionsForValueType(
            propertyType,
            elementType,
            resolvedValues.Length);

        resolvedAssignment = new ResolvedPropertyElementAssignment(
            PropertyName: property.Name,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            BindingPriorityExpression: null,
            IsCollectionAdd: true,
            IsDictionaryMerge: false,
            ObjectValues: resolvedValues,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            CollectionAddInstructions: collectionAddInstructions);
        return true;
    }

    private static bool TryBindAttachedPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        string targetTypeName,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? rootTypeSymbol,
        INamedTypeSymbol? explicitOwnerType,
        string? explicitPropertyName,
        string? explicitPropertyFieldName,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool isInsideDataTemplate = false,
        string? xBindDefaultMode = null)
    {
        resolvedAssignment = null;

        var attachedPropertyName = explicitPropertyName;
        var ownerType = explicitOwnerType;
        if (ownerType is null || string.IsNullOrWhiteSpace(attachedPropertyName))
        {
            if (!TrySplitOwnerQualifiedPropertyToken(
                    assignment.PropertyName,
                    out var ownerToken,
                    out var normalizedPropertyName))
            {
                return false;
            }

            attachedPropertyName = normalizedPropertyName;
            ownerType = ResolveTypeSymbol(compilation, assignment.XmlNamespace, ownerToken)
                        ?? ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        }

        if (ownerType is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(explicitPropertyFieldName) &&
            !TryFindAvaloniaPropertyField(
                ownerType,
                attachedPropertyName!,
                out _,
                out _,
                explicitPropertyFieldName))
        {
            return false;
        }

        return TryBindAvaloniaPropertyAssignment(
            targetType,
            targetTypeName,
            attachedPropertyName!,
            assignment,
            compilation,
            document,
            options,
            diagnostics,
            compiledBindings,
            unsafeAccessors,
            compileBindingsEnabled,
            nodeDataType,
            fallbackValueType: null,
            bindingPriorityScope,
            setterTargetType,
            rootTypeSymbol,
            out resolvedAssignment,
            allowCompiledBindingRegistration: true,
            explicitOwnerType: ownerType,
            explicitAvaloniaPropertyFieldName: explicitPropertyFieldName,
            isInsideDataTemplate: isInsideDataTemplate,
            xBindDefaultMode: xBindDefaultMode);
    }

    private static bool TryBindAttachedStaticSetterAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var attachedPropertyName))
        {
            return false;
        }

        if (!TryFindAttachedSetterMethod(
                ownerType!,
                attachedPropertyName,
                targetType,
                out var resolvedOwnerType,
                out var setterMethod))
        {
            return false;
        }

        var valueType = setterMethod.Parameters[1].Type;
        if (!TryConvertValueConversion(
                assignment.Value,
                valueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedValue,
                allowObjectStringLiteralFallback: !options.StrictMode))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{assignment.Value}' for attached setter '{resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{setterMethod.Name}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: setterMethod.Name,
            ValueExpression: convertedValue.Expression,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: null,
            BindingPriorityExpression: null,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: convertedValue.ValueKind,
            RequiresStaticResourceResolver: convertedValue.RequiresStaticResourceResolver,
            ValueRequirements: convertedValue.EffectiveRequirements);
        return true;
    }

    private static bool TryBindAttachedClassPropertyAssignment(
        XamlPropertyAssignment assignment,
        INamedTypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedPropertyAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var className))
        {
            return false;
        }

        var ownerTypeName = ownerType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!ownerTypeName.Equals("global::Avalonia.Controls.Classes", StringComparison.Ordinal))
        {
            return false;
        }

        var styledElementType = ResolveContractType(compilation, TypeContractId.StyledElement);
        if (styledElementType is null || !IsTypeAssignableTo(targetType, styledElementType))
        {
            return false;
        }

        if (TryParseBindingMarkup(assignment.Value, out var classBindingMarkup))
        {
            if (TryReportBindingSourceConflict(
                    classBindingMarkup,
                    diagnostics,
                    document,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode))
            {
                return true;
            }

            var normalizedBindingMarkup = NormalizeBindingQuerySyntax(classBindingMarkup);
            if (!TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    normalizedBindingMarkup,
                    setterTargetType,
                    bindingPriorityScope,
                    out var classBindingExpression))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0102",
                    $"Could not convert class binding literal '{assignment.Value}' for '{assignment.PropertyName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: "SetClass:" + className,
                ValueExpression: classBindingExpression,
                AvaloniaPropertyOwnerTypeName: null,
                AvaloniaPropertyFieldName: null,
                ClrPropertyOwnerTypeName: "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime",
                ClrPropertyTypeName: null,
                BindingPriorityExpression: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                RequiresStaticResourceResolver: false,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true));
            return true;
        }

        var boolType = compilation.GetSpecialType(SpecialType.System_Boolean);
        if (!TryConvertValueConversion(
                assignment.Value,
                boolType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedValue,
                allowObjectStringLiteralFallback: false))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert class binding literal '{assignment.Value}' for '{assignment.PropertyName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: "SetClass:" + className,
            ValueExpression: convertedValue.Expression,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime",
            ClrPropertyTypeName: null,
            BindingPriorityExpression: null,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: convertedValue.ValueKind,
            RequiresStaticResourceResolver: convertedValue.RequiresStaticResourceResolver,
            ValueRequirements: convertedValue.EffectiveRequirements);
        return true;
    }

    private static bool TryBindAttachedEventSubscription(
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        if (!TryResolveAttachedOwnerAndMember(
                assignment,
                compilation,
                document,
                out var ownerType,
                out var eventName))
        {
            return false;
        }

        var eventAssignment = new XamlPropertyAssignment(
            PropertyName: eventName,
            XmlNamespace: assignment.XmlNamespace,
            Value: assignment.Value,
            IsAttached: false,
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition);

        return TryBindEventSubscription(
            ownerType!,
            eventAssignment,
            compilation,
            nodeDataType,
            rootTypeSymbol,
            isInsideDataTemplate,
            diagnostics,
            document,
            options,
            out subscription);
    }

    private static bool TryResolveAttachedOwnerAndMember(
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        out INamedTypeSymbol? ownerType,
        out string memberName)
    {
        ownerType = null;
        memberName = string.Empty;
        if (!TrySplitOwnerQualifiedPropertyToken(
                assignment.PropertyName,
                out var ownerToken,
                out var normalizedMemberName))
        {
            return false;
        }

        ownerType = ResolveTypeSymbol(compilation, assignment.XmlNamespace, ownerToken) ??
                    ResolveTypeToken(compilation, document, ownerToken, document.ClassNamespace);
        if (ownerType is null)
        {
            return false;
        }

        memberName = normalizedMemberName;
        return true;
    }

    private static bool TryFindAttachedSetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        INamedTypeSymbol targetType,
        out INamedTypeSymbol resolvedOwnerType,
        out IMethodSymbol setterMethod)
    {
        var methodName = "Set" + NormalizePropertyName(propertyName);
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            var method = current.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(candidate =>
                    candidate.IsStatic &&
                    candidate.MethodKind == MethodKind.Ordinary &&
                    !candidate.IsGenericMethod &&
                    candidate.Parameters.Length == 2 &&
                    candidate.RefKind == RefKind.None &&
                    candidate.Parameters[0].RefKind == RefKind.None &&
                    candidate.Parameters[1].RefKind == RefKind.None &&
                    IsTypeAssignableTo(targetType, candidate.Parameters[0].Type));
            if (method is null)
            {
                continue;
            }

            resolvedOwnerType = current;
            setterMethod = method;
            return true;
        }

        resolvedOwnerType = ownerType;
        setterMethod = null!;
        return false;
    }

    private static bool TryBindAttachedSetterPropertyElementAssignment(
        INamedTypeSymbol targetType,
        INamedTypeSymbol ownerType,
        string attachedPropertyName,
        XamlPropertyElement propertyElement,
        ImmutableArray<ResolvedObjectNode> objectValues,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        out ResolvedPropertyElementAssignment? resolvedAssignment)
    {
        resolvedAssignment = null;
        if (!TryFindAttachedSetterMethod(
                ownerType,
                attachedPropertyName,
                targetType,
                out var resolvedOwnerType,
                out var setterMethod))
        {
            return false;
        }

        var parameterType = setterMethod.Parameters[1].Type;
        var assignmentValues = MaterializePropertyElementValuesForTargetTypeIfNeeded(
            parameterType,
            objectValues,
            compilation,
            document,
            propertyElement.Line,
            propertyElement.Column);

        if (assignmentValues.Length != 1)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0103",
                $"Attached property element '{propertyElement.PropertyName}' requires exactly one object value.",
                document.FilePath,
                propertyElement.Line,
                propertyElement.Column,
                options.StrictMode));
            return true;
        }

        resolvedAssignment = new ResolvedPropertyElementAssignment(
            PropertyName: setterMethod.Name,
            AvaloniaPropertyOwnerTypeName: null,
            AvaloniaPropertyFieldName: null,
            ClrPropertyOwnerTypeName: resolvedOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ClrPropertyTypeName: null,
            BindingPriorityExpression: null,
            IsCollectionAdd: false,
            IsDictionaryMerge: false,
            ObjectValues: assignmentValues,
            Line: propertyElement.Line,
            Column: propertyElement.Column,
            Condition: propertyElement.Condition);
        return true;
    }

    private static bool TryBindEventSubscription(
        INamedTypeSymbol targetType,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        bool isInsideDataTemplate,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = NormalizePropertyName(assignment.PropertyName);

        if (FindEvent(targetType, eventName) is { } eventSymbol)
        {
            if (TryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var inlineEventCode))
            {
                if (!TryBuildInlineEventCodeDefinition(
                        rawCode: inlineEventCode,
                        isLambdaExpression: CSharpMarkupExpressionSemantics.IsLambdaExpression(inlineEventCode),
                        eventName: eventName,
                        eventHandlerType: eventSymbol.Type,
                        compilation: compilation,
                        nodeDataType: nodeDataType,
                        targetType: targetType,
                        rootTypeSymbol: rootTypeSymbol,
                        diagnostics: diagnostics,
                        document: document,
                        options: options,
                        line: assignment.Line,
                        column: assignment.Column,
                        out var inlineEventDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: inlineEventDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: inlineEventDefinition);
                return true;
            }

            if (TryParseXBindMarkup(assignment.Value, out var xBindEventMarkup))
            {
                if (!TryBuildXBindEventBindingDefinition(
                        compilation,
                        document,
                        xBindEventMarkup,
                        eventName,
                        nodeDataType,
                        rootTypeSymbol,
                        targetType,
                        eventSymbol.Type,
                        isInsideDataTemplate,
                        assignment.Line,
                        assignment.Column,
                        out var xBindEventBindingDefinition,
                        out var xBindEventError))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0600",
                        xBindEventError,
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: xBindEventBindingDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: xBindEventBindingDefinition);
                return true;
            }

            if (TryParseMarkupExtension(assignment.Value, out var clrEventMarkupExtension) &&
                IsEventBindingMarkupExtension(clrEventMarkupExtension))
            {
                if (!TryBindEventBinding(
                        assignment,
                        eventName,
                        compilation,
                        eventSymbol.Type,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var eventBindingDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: eventBindingDefinition.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: eventBindingDefinition);
                return true;
            }

            if (TryBindInlineEventLambda(
                    assignment,
                    eventName,
                    compilation,
                    eventSymbol.Type,
                    nodeDataType,
                    targetType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out var inlineLambdaDefinition,
                    out var inlineLambdaHandled))
            {
                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: inlineLambdaDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.ClrEvent,
                    RoutedEventOwnerTypeName: null,
                    RoutedEventFieldName: null,
                    RoutedEventHandlerTypeName: null,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: inlineLambdaDefinition);
                return true;
            }

            if (inlineLambdaHandled)
            {
                return true;
            }

            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            var isCompatible = HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, eventSymbol.Type);
            if (!isCompatible)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.ClrEvent,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null,
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition);
            return true;
        }

        if (TryFindStaticEventField(
                targetType,
                eventName,
                out var routedEventOwnerType,
                out var routedEventField))
        {
            if (!TryResolveRoutedEventHandlerType(routedEventField.Type, compilation, out var routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event definition '{routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{routedEventField.Name}' is not compatible with Avalonia routed events.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (TryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var routedInlineEventCode))
            {
                if (!TryBuildInlineEventCodeDefinition(
                        rawCode: routedInlineEventCode,
                        isLambdaExpression: CSharpMarkupExpressionSemantics.IsLambdaExpression(routedInlineEventCode),
                        eventName: eventName,
                        eventHandlerType: routedEventHandlerTypeSymbol,
                        compilation: compilation,
                        nodeDataType: nodeDataType,
                        targetType: targetType,
                        rootTypeSymbol: rootTypeSymbol,
                        diagnostics: diagnostics,
                        document: document,
                        options: options,
                        line: assignment.Line,
                        column: assignment.Column,
                        out var inlineEventDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: inlineEventDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: inlineEventDefinition);
                return true;
            }

            if (TryParseXBindMarkup(assignment.Value, out var xBindRoutedEventMarkup))
            {
                if (!TryBuildXBindEventBindingDefinition(
                        compilation,
                        document,
                        xBindRoutedEventMarkup,
                        eventName,
                        nodeDataType,
                        rootTypeSymbol,
                        targetType,
                        routedEventHandlerTypeSymbol,
                        isInsideDataTemplate,
                        assignment.Line,
                        assignment.Column,
                        out var xBindRoutedEventBindingDefinition,
                        out var xBindRoutedEventError))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0600",
                        xBindRoutedEventError,
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: xBindRoutedEventBindingDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: xBindRoutedEventBindingDefinition);
                return true;
            }

            if (TryParseMarkupExtension(assignment.Value, out var routedEventMarkupExtension) &&
                IsEventBindingMarkupExtension(routedEventMarkupExtension))
            {
                if (!TryBindEventBinding(
                        assignment,
                        eventName,
                        compilation,
                        routedEventHandlerTypeSymbol,
                        nodeDataType,
                        rootTypeSymbol,
                        diagnostics,
                        document,
                        options,
                        out var eventBindingDefinition))
                {
                    return true;
                }

                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: eventBindingDefinition.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: eventBindingDefinition);
                return true;
            }

            if (TryBindInlineEventLambda(
                    assignment,
                    eventName,
                    compilation,
                    routedEventHandlerTypeSymbol,
                    nodeDataType,
                    targetType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    out var routedInlineLambdaDefinition,
                    out var routedInlineLambdaHandled))
            {
                subscription = new ResolvedEventSubscription(
                    EventName: eventName,
                    HandlerMethodName: routedInlineLambdaDefinition!.GeneratedMethodName,
                    Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                    RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    RoutedEventFieldName: routedEventField.Name,
                    RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    EventBindingDefinition: routedInlineLambdaDefinition);
                return true;
            }

            if (routedInlineLambdaHandled)
            {
                return true;
            }

            if (!TryParseHandlerName(assignment.Value, out var handlerMethodName))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' expects a CLR handler method name.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (rootTypeSymbol is null)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event '{eventName}' requires x:Class-backed root type for handler '{handlerMethodName}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (!HasCompatibleInstanceMethod(rootTypeSymbol, handlerMethodName!, routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Handler method '{handlerMethodName}' is not compatible with event '{eventName}' delegate type '{routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: handlerMethodName!,
                Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RoutedEventFieldName: routedEventField.Name,
                RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition);
            return true;
        }

        return false;
    }

    private static bool TryBindInlineEventLambda(
        XamlPropertyAssignment assignment,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventBindingDefinition? eventBindingDefinition,
        out bool handled)
    {
        eventBindingDefinition = null;
        handled = false;

        if (!TryParseInlineEventLambdaExpression(assignment.Value, out var lambdaExpression))
        {
            return false;
        }

        handled = true;

        if (CSharpMarkupExpressionSemantics.IsAsyncLambdaExpression(lambdaExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Inline event lambda on '{eventName}' does not support async lambdas.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        return TryBuildInlineEventCodeDefinition(
            rawCode: lambdaExpression,
            isLambdaExpression: true,
            eventName: eventName,
            eventHandlerType: eventHandlerType,
            compilation: compilation,
            nodeDataType: nodeDataType,
            targetType: targetType,
            rootTypeSymbol: rootTypeSymbol,
            diagnostics: diagnostics,
            document: document,
            options: options,
            line: assignment.Line,
            column: assignment.Column,
            out eventBindingDefinition);
    }

    private static bool TryBindInlineEventCodeSubscription(
        INamedTypeSymbol targetType,
        string propertyName,
        string rawCode,
        int line,
        int column,
        ConditionalXamlExpression? condition,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventSubscription? subscription)
    {
        subscription = null;
        var eventName = NormalizePropertyName(propertyName);
        var isLambdaExpression = CSharpMarkupExpressionSemantics.IsLambdaExpression(rawCode);

        if (FindEvent(targetType, eventName) is { } eventSymbol)
        {
            if (!TryBuildInlineEventCodeDefinition(
                    rawCode,
                    isLambdaExpression,
                    eventName,
                    eventSymbol.Type,
                    compilation,
                    nodeDataType,
                    targetType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    line,
                    column,
                    out var eventBindingDefinition))
            {
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: eventBindingDefinition!.GeneratedMethodName,
                Kind: ResolvedEventSubscriptionKind.ClrEvent,
                RoutedEventOwnerTypeName: null,
                RoutedEventFieldName: null,
                RoutedEventHandlerTypeName: null,
                Line: line,
                Column: column,
                Condition: condition,
                EventBindingDefinition: eventBindingDefinition);
            return true;
        }

        if (TryFindStaticEventField(
                targetType,
                eventName,
                out var routedEventOwnerType,
                out var routedEventField))
        {
            if (!TryResolveRoutedEventHandlerType(routedEventField.Type, compilation, out var routedEventHandlerTypeSymbol))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0600",
                    $"Event definition '{routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{routedEventField.Name}' is not compatible with Avalonia routed events.",
                    document.FilePath,
                    line,
                    column,
                    options.StrictMode));
                return true;
            }

            if (!TryBuildInlineEventCodeDefinition(
                    rawCode,
                    isLambdaExpression,
                    eventName,
                    routedEventHandlerTypeSymbol,
                    compilation,
                    nodeDataType,
                    targetType,
                    rootTypeSymbol,
                    diagnostics,
                    document,
                    options,
                    line,
                    column,
                    out var routedEventDefinition))
            {
                return true;
            }

            subscription = new ResolvedEventSubscription(
                EventName: eventName,
                HandlerMethodName: routedEventDefinition!.GeneratedMethodName,
                Kind: ResolvedEventSubscriptionKind.RoutedEvent,
                RoutedEventOwnerTypeName: routedEventOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                RoutedEventFieldName: routedEventField.Name,
                RoutedEventHandlerTypeName: routedEventHandlerTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Line: line,
                Column: column,
                Condition: condition,
                EventBindingDefinition: routedEventDefinition);
            return true;
        }

        return false;
    }

    private static bool TryBuildInlineEventCodeDefinition(
        string rawCode,
        bool isLambdaExpression,
        string eventName,
        ITypeSymbol eventHandlerType,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol targetType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        int line,
        int column,
        out ResolvedEventBindingDefinition? eventBindingDefinition)
    {
        eventBindingDefinition = null;
        var rootTypeName = rootTypeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                           ?? (document.IsClassBacked ? document.ClassFullName : null);

        if (string.IsNullOrWhiteSpace(rootTypeName))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Inline event code on '{eventName}' requires x:Class-backed root type.",
                document.FilePath,
                line,
                column,
                options.StrictMode));
            return false;
        }

        if (eventHandlerType is not INamedTypeSymbol namedDelegateType ||
            !TryBuildEventBindingDelegateSignature(
                namedDelegateType,
                out var delegateTypeName,
                out var delegateParameters))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Inline event code on '{eventName}' is not supported for delegate type '{eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                document.FilePath,
                line,
                column,
                options.StrictMode));
            return false;
        }

        var compiledDataContextLambdaExpression = (string?)null;
        var compiledRootLambdaExpression = (string?)null;

        if (nodeDataType is not null &&
            TryAnalyzeInlineEventCode(
                compilation,
                nodeDataType,
                rootTypeSymbol,
                targetType,
                namedDelegateType,
                rawCode,
                isLambdaExpression,
                out var dataContextAnalysis,
                out _))
        {
            compiledDataContextLambdaExpression = dataContextAnalysis.RewrittenLambdaExpression;
        }

        if (rootTypeSymbol is not null &&
            TryAnalyzeInlineEventCode(
                compilation,
                sourceType: null,
                rootType: rootTypeSymbol,
                targetType: targetType,
                delegateType: namedDelegateType,
                rawCode: rawCode,
                isLambdaExpression: isLambdaExpression,
                out var rootAnalysis,
                out _))
        {
            compiledRootLambdaExpression = rootAnalysis.RewrittenLambdaExpression;
        }

        if (!HasCompiledEventBindingCoverage(
                ResolvedEventBindingSourceMode.DataContextThenRoot,
                !string.IsNullOrWhiteSpace(compiledDataContextLambdaExpression),
                !string.IsNullOrWhiteSpace(compiledRootLambdaExpression)))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Inline event code on '{eventName}' requires compile-time resolvable members against x:DataType, root, or target context.",
                document.FilePath,
                line,
                column,
                options.StrictMode));
            return false;
        }

        var methodName = BuildGeneratedEventBindingMethodName(
            eventName,
            BuildInlineEventBindingStableKey(
                rawCode,
                eventHandlerType,
                nodeDataType,
                rootTypeSymbol,
                targetType,
                isLambdaExpression));
        eventBindingDefinition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: methodName,
            DelegateTypeName: delegateTypeName,
            Parameters: delegateParameters,
            TargetKind: ResolvedEventBindingTargetKind.Lambda,
            SourceMode: ResolvedEventBindingSourceMode.DataContextThenRoot,
            TargetPath: rawCode,
            ParameterPath: null,
            ParameterValueExpression: null,
            HasParameterValueExpression: false,
            PassEventArgs: false,
            DataContextTypeName: nodeDataType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            RootTypeName: rootTypeName,
            CompiledDataContextTargetPath: null,
            CompiledRootTargetPath: null,
            CompiledDataContextMethodCall: null,
            CompiledRootMethodCall: null,
            CompiledDataContextLambdaExpression: compiledDataContextLambdaExpression,
            CompiledRootLambdaExpression: compiledRootLambdaExpression,
            CompiledDataContextParameterPath: null,
            CompiledRootParameterPath: null,
            LambdaSourceTypeName: null,
            LambdaSourceDependencyExpression: null,
            LambdaContextTargetTypeName: targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            UsesInlineCodeContext: true,
            Line: line,
            Column: column);
        return true;
    }

    private static bool TryAnalyzeInlineEventCode(
        Compilation compilation,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol? targetType,
        INamedTypeSymbol delegateType,
        string rawCode,
        bool isLambdaExpression,
        out SourceContextLambdaAnalysisResult analysis,
        out string errorMessage)
    {
        return isLambdaExpression
            ? CSharpInlineCodeAnalysisService.TryAnalyzeLambda(
                compilation,
                sourceType,
                rootType,
                targetType,
                delegateType,
                rawCode,
                out analysis,
                out errorMessage)
            : CSharpInlineCodeAnalysisService.TryAnalyzeEventStatements(
                compilation,
                sourceType,
                rootType,
                targetType,
                delegateType,
                rawCode,
                out analysis,
                out errorMessage);
    }

    private static bool TryBindEventBinding(
        XamlPropertyAssignment assignment,
        string eventName,
        Compilation compilation,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        GeneratorOptions options,
        out ResolvedEventBindingDefinition eventBindingDefinition)
    {
        eventBindingDefinition = null!;

        if (rootTypeSymbol is null)
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding on '{eventName}' requires x:Class-backed root type.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryParseMarkupExtension(assignment.Value, out var markupExtension) ||
            !IsEventBindingMarkupExtension(markupExtension))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"Event '{eventName}' uses unsupported EventBinding syntax.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryParseEventBindingMarkup(
                markupExtension,
                assignment,
                compilation,
                document,
                out var parsedBinding,
                out var parseError))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                parseError ?? $"EventBinding on '{eventName}' is invalid.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        if (!TryBuildEventBindingDelegateSignature(
                eventHandlerType,
                out var delegateTypeName,
                out var delegateParameters))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding on '{eventName}' is not supported for delegate type '{eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return false;
        }

        // Soft semantic checks when data-type context is available.
        if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command &&
            !TryValidateEventBindingCommandPath(parsedBinding.SourceMode, parsedBinding.TargetPath, compilation, nodeDataType, rootTypeSymbol))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding command path '{parsedBinding.TargetPath}' could not be validated against available source types.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }
        else if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Method &&
                 !TryValidateEventBindingMethodPath(parsedBinding.SourceMode, parsedBinding.TargetPath, nodeDataType, rootTypeSymbol))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding method path '{parsedBinding.TargetPath}' could not be validated against available source types.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        var dataContextTypeName = nodeDataType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var rootTypeName = rootTypeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var compiledDataContextTargetPath = (string?)null;
        var compiledRootTargetPath = (string?)null;
        var compiledDataContextMethodCall = (ResolvedEventBindingMethodCallPlan?)null;
        var compiledRootMethodCall = (ResolvedEventBindingMethodCallPlan?)null;
        var compiledDataContextParameterPath = (string?)null;
        var compiledRootParameterPath = (string?)null;
        var delegateParameterTypes = GetEventBindingDelegateParameterTypes(eventHandlerType);
        var objectType = compilation.GetSpecialType(SpecialType.System_Object);
        var hasParameterToken = parsedBinding.HasParameterValueExpression || !string.IsNullOrWhiteSpace(parsedBinding.ParameterPath);

        if (parsedBinding.ParameterPath is { } parameterPath &&
            IsSimpleEventBindingPath(parameterPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null &&
                TryResolveMemberPathType(nodeDataType, parameterPath, out _))
            {
                compiledDataContextParameterPath = parameterPath;
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                TryResolveMemberPathType(rootTypeSymbol, parameterPath, out _))
            {
                compiledRootParameterPath = parameterPath;
            }
        }
        else if (parsedBinding.ParameterPath is not null &&
                 parsedBinding.ParameterPath.Trim().Equals(".", StringComparison.Ordinal))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null)
            {
                compiledDataContextParameterPath = ".";
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext)
            {
                compiledRootParameterPath = ".";
            }
        }

        if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command &&
            IsSimpleEventBindingPath(parsedBinding.TargetPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null &&
                TryResolveMemberPathType(nodeDataType, parsedBinding.TargetPath, out _))
            {
                compiledDataContextTargetPath = parsedBinding.TargetPath;
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext &&
                TryResolveMemberPathType(rootTypeSymbol, parsedBinding.TargetPath, out _))
            {
                compiledRootTargetPath = parsedBinding.TargetPath;
            }
        }
        else if (parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Method &&
                 IsSimpleEventBindingPath(parsedBinding.TargetPath))
        {
            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.Root &&
                nodeDataType is not null)
            {
                if (TryResolveEventBindingParameterType(
                        nodeDataType,
                        hasParameterToken,
                        compiledDataContextParameterPath,
                        parsedBinding.HasParameterValueExpression,
                        objectType,
                        out var dataContextParameterType) &&
                    TryResolveEventBindingMethodCallPlan(
                        nodeDataType,
                        parsedBinding.TargetPath,
                        delegateParameterTypes,
                        hasParameterToken,
                        parsedBinding.PassEventArgs,
                        dataContextParameterType,
                        out compiledDataContextMethodCall))
                {
                    compiledDataContextTargetPath = parsedBinding.TargetPath;
                }
            }

            if (parsedBinding.SourceMode != ResolvedEventBindingSourceMode.DataContext)
            {
                if (TryResolveEventBindingParameterType(
                        rootTypeSymbol,
                        hasParameterToken,
                        compiledRootParameterPath,
                        parsedBinding.HasParameterValueExpression,
                        objectType,
                        out var rootParameterType) &&
                    TryResolveEventBindingMethodCallPlan(
                        rootTypeSymbol,
                        parsedBinding.TargetPath,
                        delegateParameterTypes,
                        hasParameterToken,
                        parsedBinding.PassEventArgs,
                        rootParameterType,
                        out compiledRootMethodCall))
                {
                    compiledRootTargetPath = parsedBinding.TargetPath;
                }
            }
        }

        var hasCompiledDataContextTarget = parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command
            ? !string.IsNullOrWhiteSpace(compiledDataContextTargetPath)
            : compiledDataContextMethodCall is not null;
        var hasCompiledRootTarget = parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command
            ? !string.IsNullOrWhiteSpace(compiledRootTargetPath)
            : compiledRootMethodCall is not null;
        if (!HasCompiledEventBindingCoverage(parsedBinding.SourceMode, hasCompiledDataContextTarget, hasCompiledRootTarget))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0600",
                $"EventBinding {(parsedBinding.TargetKind == ResolvedEventBindingTargetKind.Command ? "command" : "method")} path '{parsedBinding.TargetPath}' requires compile-time resolvable members for source mode '{parsedBinding.SourceMode}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
        }

        var methodName = BuildGeneratedEventBindingMethodName(eventName, assignment.Line, assignment.Column);
        eventBindingDefinition = new ResolvedEventBindingDefinition(
            GeneratedMethodName: methodName,
            DelegateTypeName: delegateTypeName,
            Parameters: delegateParameters,
            TargetKind: parsedBinding.TargetKind,
            SourceMode: parsedBinding.SourceMode,
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
            Line: assignment.Line,
            Column: assignment.Column);
        return true;
    }

    private static bool TryBuildEventBindingDelegateSignature(
        ITypeSymbol eventHandlerType,
        out string delegateTypeName,
        out ImmutableArray<ResolvedEventBindingParameter> parameters)
    {
        delegateTypeName = eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        parameters = ImmutableArray<ResolvedEventBindingParameter>.Empty;

        if (eventHandlerType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.TypeKind != TypeKind.Delegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod ||
            !invokeMethod.ReturnsVoid)
        {
            return false;
        }

        var builder = ImmutableArray.CreateBuilder<ResolvedEventBindingParameter>(invokeMethod.Parameters.Length);
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            var parameter = invokeMethod.Parameters[index];
            builder.Add(new ResolvedEventBindingParameter(
                Name: "__arg" + index.ToString(CultureInfo.InvariantCulture),
                TypeName: parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        }

        parameters = builder.ToImmutable();
        return true;
    }

    private static ImmutableArray<ITypeSymbol> GetEventBindingDelegateParameterTypes(ITypeSymbol eventHandlerType)
    {
        if (eventHandlerType is not INamedTypeSymbol namedDelegate ||
            namedDelegate.TypeKind != TypeKind.Delegate ||
            namedDelegate.DelegateInvokeMethod is not { } invokeMethod)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        if (invokeMethod.Parameters.IsDefaultOrEmpty)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>(invokeMethod.Parameters.Length);
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            builder.Add(invokeMethod.Parameters[index].Type);
        }

        return builder.ToImmutable();
    }

    private static bool HasCompiledEventBindingCoverage(
        ResolvedEventBindingSourceMode sourceMode,
        bool hasDataContextTarget,
        bool hasRootTarget)
    {
        return sourceMode switch
        {
            ResolvedEventBindingSourceMode.DataContext => hasDataContextTarget,
            ResolvedEventBindingSourceMode.Root => hasRootTarget,
            _ => hasDataContextTarget || hasRootTarget
        };
    }

    private static bool TryResolveEventBindingParameterType(
        INamedTypeSymbol sourceType,
        bool hasParameterToken,
        string? compiledParameterPath,
        bool hasParameterValueExpression,
        ITypeSymbol objectType,
        out ITypeSymbol parameterType)
    {
        parameterType = objectType;
        if (!hasParameterToken)
        {
            return true;
        }

        if (compiledParameterPath is not null)
        {
            var compiledParameterPathValue = compiledParameterPath.Trim();
            if (compiledParameterPathValue.Length == 0)
            {
                return false;
            }

            if (compiledParameterPathValue.Equals(".", StringComparison.Ordinal))
            {
                parameterType = sourceType;
                return true;
            }

            return TryResolveMemberPathType(sourceType, compiledParameterPathValue, out parameterType);
        }

        if (hasParameterValueExpression)
        {
            parameterType = objectType;
            return true;
        }

        return false;
    }

    private static bool TryResolveEventBindingMethodCallPlan(
        INamedTypeSymbol sourceType,
        string methodPath,
        ImmutableArray<ITypeSymbol> delegateParameterTypes,
        bool hasParameterToken,
        bool passEventArgs,
        ITypeSymbol parameterType,
        out ResolvedEventBindingMethodCallPlan? methodCallPlan)
    {
        methodCallPlan = null;
        if (!TrySplitEventBindingMethodPath(methodPath, out var targetPath, out var methodName))
        {
            return false;
        }

        INamedTypeSymbol? targetType = sourceType;
        if (!targetPath.Equals(".", StringComparison.Ordinal))
        {
            if (!TryResolveMemberPathType(sourceType, targetPath, out var resolvedTargetType) ||
                resolvedTargetType is not INamedTypeSymbol namedTargetType)
            {
                return false;
            }

            targetType = namedTargetType;
        }

        if (targetType is null)
        {
            return false;
        }

        var candidateMethods = EnumerateEventBindingMethods(targetType, methodName)
            .OrderBy(static method => method.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToImmutableArray();
        if (candidateMethods.IsDefaultOrEmpty)
        {
            return false;
        }

        var argumentSets = BuildEventBindingMethodArgumentSets(hasParameterToken, passEventArgs);
        for (var setIndex = 0; setIndex < argumentSets.Length; setIndex++)
        {
            var argumentSet = argumentSets[setIndex];
            for (var methodIndex = 0; methodIndex < candidateMethods.Length; methodIndex++)
            {
                var candidateMethod = candidateMethods[methodIndex];
                if (candidateMethod.Parameters.Length != argumentSet.Length)
                {
                    continue;
                }

                var arguments = ImmutableArray.CreateBuilder<ResolvedEventBindingMethodArgument>(argumentSet.Length);
                var compatible = true;
                for (var parameterIndex = 0; parameterIndex < candidateMethod.Parameters.Length; parameterIndex++)
                {
                    var argumentKind = argumentSet[parameterIndex];
                    var argumentType = GetEventBindingMethodArgumentType(argumentKind, delegateParameterTypes, parameterType);
                    if (argumentType is null ||
                        !IsEventBindingMethodArgumentCompatible(argumentType, candidateMethod.Parameters[parameterIndex].Type))
                    {
                        compatible = false;
                        break;
                    }

                    arguments.Add(new ResolvedEventBindingMethodArgument(
                        argumentKind,
                        candidateMethod.Parameters[parameterIndex].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }

                if (!compatible)
                {
                    continue;
                }

                methodCallPlan = new ResolvedEventBindingMethodCallPlan(
                    TargetPath: targetPath,
                    MethodName: candidateMethod.Name,
                    Arguments: arguments.ToImmutable());
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IMethodSymbol> EnumerateEventBindingMethods(INamedTypeSymbol type, string methodName)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.IsStatic ||
                    method.MethodKind != MethodKind.Ordinary ||
                    !method.ReturnsVoid ||
                    method.IsGenericMethod ||
                    method.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
                {
                    continue;
                }

                if (string.Equals(method.Name, methodName, StringComparison.Ordinal) ||
                    string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return method;
                }
            }
        }
    }

    private static bool TrySplitEventBindingMethodPath(string methodPath, out string targetPath, out string methodName)
    {
        return EventBindingPathSemantics.TrySplitMethodPath(methodPath, out targetPath, out methodName);
    }

    private static ImmutableArray<ImmutableArray<ResolvedEventBindingMethodArgumentKind>> BuildEventBindingMethodArgumentSets(
        bool hasParameterToken,
        bool passEventArgs)
    {
        return EventBindingPathSemantics.BuildMethodArgumentSets(hasParameterToken, passEventArgs);
    }

    private static ITypeSymbol? GetEventBindingMethodArgumentType(
        ResolvedEventBindingMethodArgumentKind argumentKind,
        ImmutableArray<ITypeSymbol> delegateParameterTypes,
        ITypeSymbol parameterType)
    {
        return argumentKind switch
        {
            ResolvedEventBindingMethodArgumentKind.Sender => delegateParameterTypes.Length > 0 ? delegateParameterTypes[0] : null,
            ResolvedEventBindingMethodArgumentKind.EventArgs => delegateParameterTypes.Length > 1 ? delegateParameterTypes[1] : null,
            ResolvedEventBindingMethodArgumentKind.Parameter => parameterType,
            _ => null
        };
    }

    private static bool IsEventBindingMethodArgumentCompatible(ITypeSymbol argumentType, ITypeSymbol parameterType)
    {
        if (IsTypeAssignableTo(argumentType, parameterType))
        {
            return true;
        }

        if (argumentType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        if (parameterType is ITypeParameterSymbol)
        {
            return true;
        }

        return false;
    }

    private static bool IsEventBindingMarkupExtension(MarkupExtensionInfo markupExtension)
    {
        return BindingEventMarkupParser.IsEventBindingMarkupExtension(markupExtension);
    }

    private static bool TryParseEventBindingMarkup(
        MarkupExtensionInfo markupExtension,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        out EventBindingMarkup eventBindingMarkup,
        out string? errorMessage)
    {
        _ = assignment;
        _ = compilation;
        _ = document;
        return BindingEventMarkupParser.TryParseEventBindingMarkup(
            markupExtension,
            TryParseMarkupExtension,
            TryConvertUntypedValueExpression,
            out eventBindingMarkup,
            out errorMessage);
    }

    private static bool TryParseEventBindingSourceMode(
        MarkupExtensionInfo markupExtension,
        out ResolvedEventBindingSourceMode sourceMode,
        out string? errorMessage)
    {
        return BindingEventMarkupParser.TryParseEventBindingSourceMode(markupExtension, out sourceMode, out errorMessage);
    }

    private static bool TryParseEventBindingPath(
        string token,
        out string path,
        out string? errorMessage)
    {
        return BindingEventMarkupParser.TryParseEventBindingPath(
            token,
            TryParseMarkupExtension,
            out path,
            out errorMessage);
    }

    private static bool TryParseEventBindingParameter(
        string? parameterToken,
        Compilation compilation,
        XamlDocumentModel document,
        XamlPropertyAssignment assignment,
        out string? parameterPath,
        out string? parameterValueExpression,
        out bool hasParameterValueExpression,
        out string? errorMessage)
    {
        _ = compilation;
        _ = document;
        _ = assignment;
        return BindingEventMarkupParser.TryParseEventBindingParameter(
            parameterToken,
            TryParseMarkupExtension,
            TryConvertUntypedValueExpression,
            out parameterPath,
            out parameterValueExpression,
            out hasParameterValueExpression,
            out errorMessage);
    }

    private static bool TryValidateEventBindingBindingSource(
        BindingMarkup bindingMarkup,
        string contextName,
        out string? errorMessage)
    {
        return BindingEventMarkupParser.TryValidateEventBindingBindingSource(
            bindingMarkup,
            contextName,
            out errorMessage);
    }

    private static bool TryValidateEventBindingCommandPath(
        ResolvedEventBindingSourceMode sourceMode,
        string path,
        Compilation compilation,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol)
    {
        var commandType = ResolveContractType(compilation, TypeContractId.SystemICommand);
        if (commandType is null || string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        var validOnDataContext = sourceMode != ResolvedEventBindingSourceMode.Root &&
                                 nodeDataType is not null &&
                                 TryResolveMemberPathType(nodeDataType, path, out var dataContextPathType) &&
                                 IsTypeAssignableTo(dataContextPathType, commandType);
        var validOnRoot = sourceMode != ResolvedEventBindingSourceMode.DataContext &&
                          rootTypeSymbol is not null &&
                          TryResolveMemberPathType(rootTypeSymbol, path, out var rootPathType) &&
                          IsTypeAssignableTo(rootPathType, commandType);

        if (sourceMode == ResolvedEventBindingSourceMode.DataContext)
        {
            return validOnDataContext || nodeDataType is null;
        }

        if (sourceMode == ResolvedEventBindingSourceMode.Root)
        {
            return validOnRoot || rootTypeSymbol is null;
        }

        return validOnDataContext || validOnRoot || nodeDataType is null || rootTypeSymbol is null;
    }

    private static bool TryValidateEventBindingMethodPath(
        ResolvedEventBindingSourceMode sourceMode,
        string path,
        INamedTypeSymbol? nodeDataType,
        INamedTypeSymbol? rootTypeSymbol)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var methodName = EventBindingPathSemantics.ExtractMethodName(path);

        var validOnDataContext = sourceMode != ResolvedEventBindingSourceMode.Root &&
                                 nodeDataType is not null &&
                                 HasInstanceMethod(nodeDataType, methodName);
        var validOnRoot = sourceMode != ResolvedEventBindingSourceMode.DataContext &&
                          rootTypeSymbol is not null &&
                          HasInstanceMethod(rootTypeSymbol, methodName);

        if (sourceMode == ResolvedEventBindingSourceMode.DataContext)
        {
            return validOnDataContext || nodeDataType is null;
        }

        if (sourceMode == ResolvedEventBindingSourceMode.Root)
        {
            return validOnRoot || rootTypeSymbol is null;
        }

        return validOnDataContext || validOnRoot || nodeDataType is null || rootTypeSymbol is null;
    }

    private static bool TryResolveMemberPathType(INamedTypeSymbol rootType, string path, out ITypeSymbol resolvedType)
    {
        resolvedType = rootType;
        var normalizedPath = path.Trim();
        if (normalizedPath.Length == 0 || normalizedPath == ".")
        {
            return true;
        }

        var currentType = (ITypeSymbol)rootType;
        var segments = XamlMemberPathSemantics.SplitPathSegments(normalizedPath);
        if (segments.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var index = 0; index < segments.Length; index++)
        {
            var namedType = currentType as INamedTypeSymbol;
            if (namedType is null)
            {
                return false;
            }

            var segment = XamlMemberPathSemantics.NormalizeSegmentForMemberLookup(segments[index]);
            if (segment.Length == 0)
            {
                return false;
            }

            var member = namedType.GetMembers(segment).FirstOrDefault() ??
                         namedType.GetMembers().FirstOrDefault(candidate => candidate.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            switch (member)
            {
                case IPropertySymbol property:
                    currentType = property.Type;
                    break;
                case IFieldSymbol field:
                    currentType = field.Type;
                    break;
                default:
                    return false;
            }
        }

        resolvedType = currentType;
        return true;
    }

    private static bool IsSimpleEventBindingPath(string path)
    {
        return EventBindingPathSemantics.IsSimplePath(path);
    }

    private static bool IsSimpleEventBindingIdentifier(string value)
    {
        return EventBindingPathSemantics.IsSimpleIdentifier(value);
    }

    private static string BuildGeneratedEventBindingMethodName(string eventName, int line, int column)
    {
        return EventBindingPathSemantics.BuildGeneratedMethodName(eventName, line, column);
    }

    private static string BuildGeneratedEventBindingMethodName(string eventName, string stableKey)
    {
        return EventBindingPathSemantics.BuildGeneratedMethodName(eventName, stableKey);
    }

    private static string BuildInlineEventBindingStableKey(
        string rawCode,
        ITypeSymbol eventHandlerType,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol? rootType,
        INamedTypeSymbol targetType,
        bool isLambdaExpression)
    {
        return string.Concat(
            eventHandlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "|",
            sourceType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<null>",
            "|",
            rootType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<null>",
            "|",
            targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            "|",
            isLambdaExpression ? "lambda" : "statements",
            "|",
            rawCode.Trim());
    }

    private static bool TryBindAvaloniaPropertyAssignment(
        INamedTypeSymbol targetType,
        string targetTypeName,
        string propertyName,
        XamlPropertyAssignment assignment,
        Compilation compilation,
        XamlDocumentModel document,
        GeneratorOptions options,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        ImmutableArray<ResolvedCompiledBindingDefinition>.Builder compiledBindings,
        ImmutableArray<ResolvedUnsafeAccessorDefinition>.Builder? unsafeAccessors,
        bool compileBindingsEnabled,
        INamedTypeSymbol? nodeDataType,
        ITypeSymbol? fallbackValueType,
        BindingPriorityScope bindingPriorityScope,
        INamedTypeSymbol? setterTargetType,
        INamedTypeSymbol? rootTypeSymbol,
        out ResolvedPropertyAssignment? resolvedAssignment,
        bool allowCompiledBindingRegistration = true,
        string? compiledBindingAccessorPlaceholderToken = null,
        INamedTypeSymbol? explicitOwnerType = null,
        string? explicitAvaloniaPropertyFieldName = null,
        bool isInsideDataTemplate = false,
        string? xBindDefaultMode = null)
    {
        resolvedAssignment = null;

        if (!TryFindAvaloniaPropertyField(
                explicitOwnerType ?? targetType,
                propertyName,
                out var ownerType,
                out var propertyField,
                explicitAvaloniaPropertyFieldName))
        {
            return false;
        }

        var valueType = fallbackValueType ?? TryGetAvaloniaPropertyValueType(propertyField.Type);
        var preserveBindingValue = HasAssignBindingAttribute(
            FindProperty(explicitOwnerType ?? ownerType ?? targetType, propertyName));

        if (TryParseXBindMarkup(assignment.Value, out var xBindMarkup))
        {
            if (!TryBuildXBindBindingExpression(
                    compilation,
                    document,
                    xBindMarkup,
                    ambientSourceType: isInsideDataTemplate ? nodeDataType : rootTypeSymbol,
                    rootType: rootTypeSymbol,
                    targetType: setterTargetType ?? targetType,
                    bindingValueType: valueType,
                    bindingPriorityScope,
                    isInsideDataTemplate,
                    xBindDefaultMode ?? "OneTime",
                    out var xBindExpression,
                    out _,
                    out var xBindErrorCode,
                    out var xBindErrorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    xBindErrorCode,
                    xBindErrorMessage,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: xBindExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue);
            return true;
        }

        if (TryParseInlineCSharpMarkupExtensionCode(assignment.Value, out var inlineCode))
        {
            if (!TryBuildInlineCodeBindingExpression(
                    compilation,
                    nodeDataType,
                    rootTypeSymbol,
                    setterTargetType ?? targetType,
                    inlineCode,
                    out var inlineBindingExpression,
                    out _,
                    out _,
                    out var inlineErrorMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0112",
                    $"Inline C# for '{propertyName}' is invalid: {inlineErrorMessage}",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: inlineBindingExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue);
            return true;
        }

        if (TryResolveImplicitCSharpShorthandExpression(
                assignment.Value,
                compilation,
                document,
                options,
                nodeDataType,
                rootTypeSymbol,
                setterTargetType ?? targetType,
                unsafeAccessors,
                out var isShorthandExpression,
                out var shorthandResolution))
        {
            if (shorthandResolution.Kind == CSharpShorthandResolutionKind.BindingPath &&
                shorthandResolution.Path is not null &&
                TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    new BindingMarkup(
                        isCompiledBinding: false,
                        path: shorthandResolution.Path,
                        mode: null,
                        elementName: null,
                        relativeSource: null,
                        source: null,
                        dataType: null,
                        converter: null,
                        converterCulture: null,
                        converterParameter: null,
                        stringFormat: null,
                        fallbackValue: null,
                        targetNullValue: null,
                        delay: null,
                        priority: null,
                        updateSourceTrigger: null,
                        hasSourceConflict: false,
                        sourceConflictMessage: null),
                    setterTargetType ?? targetType,
                    bindingPriorityScope,
                    out var shorthandBindingExpression))
            {
                if (allowCompiledBindingRegistration &&
                    shorthandResolution.SourceTypeName is not null &&
                    shorthandResolution.AccessorExpression is not null)
                {
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: shorthandResolution.Path,
                        SourceTypeName: shorthandResolution.SourceTypeName,
                        ResultTypeName: shorthandResolution.ResultTypeName,
                        AccessorExpression: shorthandResolution.AccessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column));
                }

                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: shorthandBindingExpression,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue);
                return true;
            }

            if (shorthandResolution.Kind == CSharpShorthandResolutionKind.RootExpression &&
                shorthandResolution.ValueExpression is not null)
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: shorthandResolution.ValueExpression,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue);
                return true;
            }

            if (isShorthandExpression &&
                !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticId) &&
                !string.IsNullOrWhiteSpace(shorthandResolution.DiagnosticMessage))
            {
                diagnostics.Add(new DiagnosticInfo(
                    shorthandResolution.DiagnosticId!,
                    shorthandResolution.DiagnosticMessage!,
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }
        }

        var expressionBindingAccessorPlaceholderToken = compiledBindingAccessorPlaceholderToken;
        if (expressionBindingAccessorPlaceholderToken is null &&
            allowCompiledBindingRegistration)
        {
            expressionBindingAccessorPlaceholderToken = BuildCompiledBindingAccessorPlaceholderToken(
                assignment.Line,
                assignment.Column);
        }

        if (TryConvertCSharpExpressionMarkupToBindingExpression(
                assignment.Value,
                compilation,
                document,
                options,
                nodeDataType,
                expressionBindingAccessorPlaceholderToken,
                out var isExpressionMarkup,
                out var expressionBindingValueExpression,
                out var expressionAccessorExpression,
                out var normalizedExpression,
                out var expressionResultTypeName,
                out var expressionErrorCode,
                out var expressionErrorMessage))
        {
            if (allowCompiledBindingRegistration)
            {
                compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                    TargetTypeName: targetTypeName,
                    TargetPropertyName: propertyName,
                    Path: "{= " + normalizedExpression + " }",
                    SourceTypeName: nodeDataType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    ResultTypeName: expressionResultTypeName,
                    AccessorExpression: expressionAccessorExpression,
                    IsSetterBinding: false,
                    Line: assignment.Line,
                    Column: assignment.Column,
                    AccessorPlaceholderToken: expressionBindingAccessorPlaceholderToken));
            }

            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: expressionBindingValueExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue);
            return true;
        }
        if (isExpressionMarkup)
        {
            var message = expressionErrorCode == "AXSG0110"
                ? $"Expression binding for '{propertyName}' requires x:DataType in scope."
                : $"Expression binding '{assignment.Value}' is invalid for source type '{nodeDataType?.ToDisplayString() ?? "unknown"}': {expressionErrorMessage}";
            diagnostics.Add(new DiagnosticInfo(
                expressionErrorCode,
                message,
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }

        if (TryParseBindingMarkup(assignment.Value, out var bindingMarkup))
        {
            if (TryReportBindingSourceConflict(
                    bindingMarkup,
                    diagnostics,
                    document,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode))
            {
                return true;
            }

            var wantsCompiledBinding = bindingMarkup.IsCompiledBinding || compileBindingsEnabled;
            INamedTypeSymbol? compiledBindingSourceType = null;
            var requiresAmbientDataType = false;
            var hasInvalidLocalDataType = false;
            var shouldCompileBinding = wantsCompiledBinding &&
                                       TryResolveCompiledBindingSourceType(
                                           compilation,
                                           document,
                                           bindingMarkup,
                                           nodeDataType,
                                           setterTargetType ?? targetType,
                                           out compiledBindingSourceType,
                                           out requiresAmbientDataType,
                                           out hasInvalidLocalDataType);
            if (shouldCompileBinding)
            {
                if (!TryBuildCompiledBindingAccessorExpression(
                        compilation,
                        document,
                        compiledBindingSourceType!,
                        bindingMarkup.Path,
                        valueType,
                        unsafeAccessors,
                        out var compiledBindingResolution,
                        out var errorMessage))
                {
                    diagnostics.Add(new DiagnosticInfo(
                        "AXSG0111",
                        $"Compiled binding path '{bindingMarkup.Path}' is invalid for source type '{compiledBindingSourceType!.ToDisplayString()}': {errorMessage}",
                        document.FilePath,
                        assignment.Line,
                        assignment.Column,
                        options.StrictMode));
                    return true;
                }

                var commandType = ResolveContractType(compilation, TypeContractId.SystemICommand);
                var localCompiledBindingAccessorPlaceholderToken = compiledBindingAccessorPlaceholderToken;
                if (allowCompiledBindingRegistration)
                {
                    localCompiledBindingAccessorPlaceholderToken ??=
                        BuildCompiledBindingAccessorPlaceholderToken(
                            assignment.Line,
                            assignment.Column);
                    compiledBindings.Add(new ResolvedCompiledBindingDefinition(
                        TargetTypeName: targetTypeName,
                        TargetPropertyName: propertyName,
                        Path: compiledBindingResolution.NormalizedPath,
                        SourceTypeName: compiledBindingSourceType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        ResultTypeName: compiledBindingResolution.ResultTypeName,
                        AccessorExpression: compiledBindingResolution.AccessorExpression,
                        IsSetterBinding: false,
                        Line: assignment.Line,
                        Column: assignment.Column,
                        AccessorPlaceholderToken: localCompiledBindingAccessorPlaceholderToken));
                }

                if (IsCommandTargetType(valueType, commandType) &&
                    TryBuildCompiledBindingRuntimeExpression(
                        compiledBindingSourceType!,
                        compiledBindingResolution,
                        localCompiledBindingAccessorPlaceholderToken,
                        out var compiledBindingRuntimeExpression))
                {
                    resolvedAssignment = new ResolvedPropertyAssignment(
                        PropertyName: propertyName,
                        ValueExpression: compiledBindingRuntimeExpression,
                        AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        AvaloniaPropertyFieldName: propertyField.Name,
                        ClrPropertyOwnerTypeName: null,
                        ClrPropertyTypeName: null,
                        BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                            targetType,
                            propertyField,
                            compilation,
                        bindingPriorityScope),
                    Line: assignment.Line,
                    Column: assignment.Column,
                    Condition: assignment.Condition,
                    ValueKind: ResolvedValueKind.Binding,
                    ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                    PreserveBindingValue: preserveBindingValue);
                    return true;
                }
            }
            else if (wantsCompiledBinding && hasInvalidLocalDataType)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    $"Compiled binding for '{propertyName}' specifies invalid DataType '{bindingMarkup.DataType}'.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }
            else if (wantsCompiledBinding && requiresAmbientDataType)
            {
                diagnostics.Add(new DiagnosticInfo(
                    "AXSG0110",
                    $"Compiled binding for '{propertyName}' requires x:DataType in scope.",
                    document.FilePath,
                    assignment.Line,
                    assignment.Column,
                    options.StrictMode));
                return true;
            }

            if (TryBuildRuntimeBindingExpression(
                    compilation,
                    document,
                    bindingMarkup,
                    setterTargetType ?? targetType,
                    bindingPriorityScope,
                    out var bindingExpression))
            {
                resolvedAssignment = new ResolvedPropertyAssignment(
                    PropertyName: propertyName,
                    ValueExpression: bindingExpression,
                    AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    AvaloniaPropertyFieldName: propertyField.Name,
                    ClrPropertyOwnerTypeName: null,
                    ClrPropertyTypeName: null,
                    BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                        targetType,
                        propertyField,
                        compilation,
                    bindingPriorityScope),
                Line: assignment.Line,
                Column: assignment.Column,
                Condition: assignment.Condition,
                ValueKind: ResolvedValueKind.Binding,
                ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
                PreserveBindingValue: preserveBindingValue);
            }

            return shouldCompileBinding || resolvedAssignment is not null;
        }

        if (HasResolveByNameSemantics(ownerType, propertyName) &&
            TryBuildResolveByNameLiteralExpression(
                assignment.Value,
                valueType,
                out var resolveByNameValueExpression))
        {
            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: resolveByNameValueExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                bindingPriorityScope),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: ResolvedValueKind.MarkupExtension,
            ValueRequirements: ResolvedValueRequirements.ForMarkupExtensionRuntime(includeParentStack: true),
            PreserveBindingValue: preserveBindingValue);
            return true;
        }

        if (valueType is INamedTypeSymbol delegateType &&
            delegateType.TypeKind == TypeKind.Delegate &&
            TryBuildDelegateMethodGroupValueExpression(
                assignment.Value,
                delegateType,
                rootTypeSymbol,
                out var delegateMethodExpression))
        {
            resolvedAssignment = new ResolvedPropertyAssignment(
                PropertyName: propertyName,
                ValueExpression: delegateMethodExpression,
                AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AvaloniaPropertyFieldName: propertyField.Name,
                ClrPropertyOwnerTypeName: null,
                ClrPropertyTypeName: null,
                BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                    targetType,
                    propertyField,
                    compilation,
                bindingPriorityScope),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: ResolvedValueKind.Literal,
            ValueRequirements: ResolvedValueRequirements.None,
            PreserveBindingValue: preserveBindingValue);
            return true;
        }

        var valueExpression = string.Empty;
        var valueConversion = default(ResolvedValueConversionResult);
        var valueKind = ResolvedValueKind.Literal;
        var requiresStaticResourceResolver = false;
        var valueRequirements = ResolvedValueRequirements.None;
        if ((valueType is null || !TryConvertValueConversion(
                assignment.Value,
                valueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out valueConversion,
                preferTypedStaticResourceCoercion: false,
                allowObjectStringLiteralFallback: !options.StrictMode)) &&
            !TryConvertUntypedValueExpression(assignment.Value, out valueExpression))
        {
            diagnostics.Add(new DiagnosticInfo(
                "AXSG0102",
                $"Could not convert literal '{assignment.Value}' for Avalonia property '{propertyName}'.",
                document.FilePath,
                assignment.Line,
                assignment.Column,
                options.StrictMode));
            return true;
        }
        else if (!string.IsNullOrEmpty(valueConversion.Expression))
        {
            valueExpression = valueConversion.Expression;
            valueKind = valueConversion.ValueKind;
            requiresStaticResourceResolver = valueConversion.RequiresStaticResourceResolver;
            valueRequirements = valueConversion.EffectiveRequirements;
        }

        resolvedAssignment = new ResolvedPropertyAssignment(
            PropertyName: propertyName,
            ValueExpression: valueExpression,
            AvaloniaPropertyOwnerTypeName: ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            AvaloniaPropertyFieldName: propertyField.Name,
            ClrPropertyOwnerTypeName: null,
            ClrPropertyTypeName: null,
            BindingPriorityExpression: GetSetValueBindingPriorityExpression(
                targetType,
                propertyField,
                compilation,
                bindingPriorityScope),
            Line: assignment.Line,
            Column: assignment.Column,
            Condition: assignment.Condition,
            ValueKind: valueKind,
            RequiresStaticResourceResolver: requiresStaticResourceResolver,
            ValueRequirements: valueRequirements,
            PreserveBindingValue: preserveBindingValue);
        return true;
    }

    private static string BuildCompiledBindingAccessorPlaceholderToken(int line, int column)
    {
        return "__AXSG_CompiledBindingAccessor_" +
               line.ToString(CultureInfo.InvariantCulture) +
               "_" +
               column.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryReportBindingSourceConflict(
        BindingMarkup bindingMarkup,
        ImmutableArray<DiagnosticInfo>.Builder diagnostics,
        XamlDocumentModel document,
        int line,
        int column,
        bool strictMode)
    {
        if (!bindingMarkup.HasSourceConflict)
        {
            return false;
        }

        diagnostics.Add(new DiagnosticInfo(
            "AXSG0111",
            bindingMarkup.SourceConflictMessage ?? "Binding source configuration is invalid.",
            document.FilePath,
            line,
            column,
            strictMode));
        return true;
    }

    private static bool TryBuildBindingValueExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        ITypeSymbol propertyType,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (!CanAssignBindingValue(propertyType, compilation))
        {
            return false;
        }

        if (TryBuildRuntimeBindingExpression(
                compilation,
                document,
                bindingMarkup,
                setterTargetType,
                bindingPriorityScope,
                out expression))
        {
            return true;
        }

        return false;
    }

    private static bool TryBuildRuntimeBindingExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (ResolveContractType(compilation, TypeContractId.AvaloniaBinding) is not INamedTypeSymbol bindingType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();
        normalizedPath = NormalizeRuntimeBindingPath(normalizedPath);

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "DataType",
            rawValue: bindingMarkup.DataType,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            bindingType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            bindingType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\")";
            return true;
        }

        expression = "new global::Avalonia.Data.Binding(\"" + Escape(normalizedPath) + "\") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static string NormalizeRuntimeBindingPath(string path)
    {
        return XamlRuntimeBindingPathSemantics.NormalizePath(path);
    }

    private static bool TryBuildReflectionBindingExtensionExpression(
        Compilation compilation,
        XamlDocumentModel document,
        BindingMarkup bindingMarkup,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        if (ResolveContractType(compilation, TypeContractId.AvaloniaReflectionBindingExtension) is not INamedTypeSymbol reflectionBindingExtensionType)
        {
            return false;
        }

        var normalizedPath = string.IsNullOrWhiteSpace(bindingMarkup.Path)
            ? "."
            : bindingMarkup.Path.Trim();
        normalizedPath = NormalizeRuntimeBindingPath(normalizedPath);

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(bindingMarkup.Mode) &&
            TryMapBindingMode(bindingMarkup.Mode!, out var bindingModeExpression))
        {
            initializerParts.Add("Mode = " + bindingModeExpression);
        }

        if (!string.IsNullOrWhiteSpace(bindingMarkup.ElementName))
        {
            initializerParts.Add("ElementName = \"" + Escape(bindingMarkup.ElementName!) + "\"");
        }

        if (bindingMarkup.RelativeSource is not null &&
            TryBuildRelativeSourceExpression(bindingMarkup.RelativeSource.Value, compilation, document, out var relativeSourceExpression))
        {
            initializerParts.Add("RelativeSource = " + relativeSourceExpression);
        }

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Source",
            rawValue: bindingMarkup.Source,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "DataType",
            rawValue: bindingMarkup.DataType,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Converter",
            rawValue: bindingMarkup.Converter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterCulture",
            rawValue: bindingMarkup.ConverterCulture,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "ConverterParameter",
            rawValue: bindingMarkup.ConverterParameter,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "StringFormat",
            rawValue: bindingMarkup.StringFormat,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "FallbackValue",
            rawValue: bindingMarkup.FallbackValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "TargetNullValue",
            rawValue: bindingMarkup.TargetNullValue,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Delay",
            rawValue: bindingMarkup.Delay,
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "Priority",
            rawValue: !string.IsNullOrWhiteSpace(bindingMarkup.Priority)
                ? bindingMarkup.Priority
                : GetDefaultBindingPriorityToken(bindingPriorityScope),
            compilation,
            document,
            setterTargetType,
            initializerParts);
        AddBindingInitializerPart(
            reflectionBindingExtensionType,
            propertyName: "UpdateSourceTrigger",
            rawValue: bindingMarkup.UpdateSourceTrigger,
            compilation,
            document,
            setterTargetType,
            initializerParts);

        if (initializerParts.Count == 0)
        {
            expression =
                "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
                Escape(normalizedPath) +
                "\")";
            return true;
        }

        expression =
            "new global::Avalonia.Markup.Xaml.MarkupExtensions.ReflectionBindingExtension(\"" +
            Escape(normalizedPath) +
            "\") { " +
            string.Join(", ", initializerParts) +
            " }";
        return true;
    }

    private static bool TryConvertOnPlatformExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression))
        {
            return false;
        }

        if (!TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Windows"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var windowsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "macOS", "MacOS", "OSX"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var macOsExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Linux"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var linuxExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Android"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var androidExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "iOS", "IOS"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var iosExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Browser"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var browserExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnPlatform(" +
            defaultExpression +
            ", " +
            windowsExpression +
            ", " +
            macOsExpression +
            ", " +
            linuxExpression +
            ", " +
            androidExpression +
            ", " +
            iosExpression +
            ", " +
            browserExpression +
            ")");
        return true;
    }

    private static bool TryConvertOnFormFactorExpression(
        MarkupExtensionInfo markup,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;

        var defaultToken = TryGetNamedMarkupArgument(markup, "Default") ??
                           (markup.PositionalArguments.Length > 0 ? markup.PositionalArguments[0] : null);
        if (!TryConvertMarkupOptionValueExpression(
                defaultToken,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var defaultExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Desktop"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var desktopExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "Mobile"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var mobileExpression) ||
            !TryConvertMarkupOptionValueExpression(
                TryGetNamedMarkupArgument(markup, "TV"),
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var tvExpression))
        {
            return false;
        }

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideOnFormFactor(" +
            defaultExpression +
            ", " +
            desktopExpression +
            ", " +
            mobileExpression +
            ", " +
            tvExpression +
            ", " +
            MarkupContextServiceProviderToken +
            ")");
        return true;
    }

    private static bool TryConvertMarkupOptionValueExpression(
        string? rawToken,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = "null";
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return true;
        }

        return TryConvertValueExpression(
            Unquote(rawToken!),
            targetType,
            compilation,
            document,
            setterTargetType,
            bindingPriorityScope,
            out expression);
    }

    private static bool TryBuildResourceKeyExpression(
        string rawKeyToken,
        Compilation compilation,
        XamlDocumentModel document,
        out ResolvedResourceKeyExpression expression)
    {
        expression = default;
        var token = rawKeyToken.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var unquotedToken = Unquote(token);
        if (TryParseMarkupExtension(unquotedToken, out var markup))
        {
            switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
            {
                case XamlMarkupExtensionKind.Type:
                {
                    var typeToken = markup.NamedArguments.TryGetValue("Type", out var explicitType)
                        ? explicitType
                        : markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName)
                            ? explicitTypeName
                            : markup.PositionalArguments.Length > 0
                                ? markup.PositionalArguments[0]
                                : null;
                    if (string.IsNullOrWhiteSpace(typeToken))
                    {
                        return false;
                    }

                    var resolvedType = ResolveTypeToken(compilation, document, Unquote(typeToken!), document.ClassNamespace);
                    if (resolvedType is null)
                    {
                        return false;
                    }

                    expression = new ResolvedResourceKeyExpression(
                        Expression: "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")",
                        Kind: ResolvedResourceKeyKind.TypeReference);
                    return true;
                }
                case XamlMarkupExtensionKind.Static:
                {
                    var memberToken = markup.NamedArguments.TryGetValue("Member", out var explicitMember)
                        ? explicitMember
                        : markup.PositionalArguments.Length > 0
                            ? markup.PositionalArguments[0]
                            : null;
                    if (string.IsNullOrWhiteSpace(memberToken))
                    {
                        return false;
                    }

                    return TryResolveStaticMemberExpression(
                        compilation,
                        document,
                        Unquote(memberToken!),
                        out var staticMemberExpression) &&
                           TryCreateStaticMemberResourceKeyExpression(staticMemberExpression, out expression);
                }
            }
        }

        expression = new ResolvedResourceKeyExpression(
            Expression: "\"" + Escape(unquotedToken) + "\"",
            Kind: ResolvedResourceKeyKind.StringLiteral);
        return true;
    }

    private static bool TryCreateStaticMemberResourceKeyExpression(
        string staticMemberExpression,
        out ResolvedResourceKeyExpression expression)
    {
        if (string.IsNullOrWhiteSpace(staticMemberExpression))
        {
            expression = default;
            return false;
        }

        expression = new ResolvedResourceKeyExpression(
            Expression: staticMemberExpression,
            Kind: ResolvedResourceKeyKind.StaticMemberReference);
        return true;
    }

    private static void AddBindingInitializerPart(
        INamedTypeSymbol bindingType,
        string propertyName,
        string? rawValue,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        List<string> initializerParts)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        if (!TryGetWritableProperty(bindingType, propertyName, out var property))
        {
            return;
        }

        var normalizedToken = Unquote(rawValue!);
        if (!TryConvertValueExpression(
                normalizedToken,
                property.Type,
                compilation,
                document,
                setterTargetType,
                BindingPriorityScope.None,
                out var valueExpression))
        {
            return;
        }

        initializerParts.Add(propertyName + " = " + valueExpression);
    }

    private static bool TryGetWritableProperty(INamedTypeSymbol type, string propertyName, out IPropertySymbol property)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var candidate = current.GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .FirstOrDefault(item => !item.IsStatic &&
                                        !item.IsIndexer &&
                                        item.SetMethod is not null);
            if (candidate is not null)
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private static bool TryBuildRelativeSourceExpression(
        RelativeSourceMarkup relativeSourceMarkup,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (ResolveContractType(compilation, TypeContractId.AvaloniaRelativeSource) is null)
        {
            return false;
        }

        var mode = relativeSourceMarkup.Mode;
        if (string.IsNullOrWhiteSpace(mode))
        {
            mode = !string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken)
                ? "FindAncestor"
                : "Self";
        }

        if (!TryMapRelativeSourceMode(mode!, out var relativeSourceModeExpression))
        {
            return false;
        }

        var initializerParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.AncestorTypeToken))
        {
            var ancestorType = ResolveTypeToken(compilation, document, relativeSourceMarkup.AncestorTypeToken!, document.ClassNamespace);
            if (ancestorType is null)
            {
                return false;
            }

            initializerParts.Add("AncestorType = typeof(" +
                                 ancestorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                                 ")");
        }

        if (relativeSourceMarkup.AncestorLevel.HasValue)
        {
            initializerParts.Add("AncestorLevel = " + relativeSourceMarkup.AncestorLevel.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(relativeSourceMarkup.Tree) &&
            TryMapTreeType(relativeSourceMarkup.Tree!, out var treeTypeExpression))
        {
            initializerParts.Add("Tree = " + treeTypeExpression);
        }

        if (initializerParts.Count == 0)
        {
            expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ")";
            return true;
        }

        expression = "new global::Avalonia.Data.RelativeSource(" + relativeSourceModeExpression + ") { " +
                     string.Join(", ", initializerParts) +
                     " }";
        return true;
    }

    private static bool TryMapBindingMode(string modeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapBindingModeToken(modeToken, out expression);
    }

    private static bool TryMapRelativeSourceMode(string modeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapRelativeSourceModeToken(modeToken, out expression);
    }

    private static bool TryMapTreeType(string treeToken, out string expression)
    {
        return AvaloniaBindingEnumSemantics.TryMapTreeTypeToken(treeToken, out expression);
    }

    private static string? GetDefaultBindingPriorityToken(BindingPriorityScope scope)
    {
        return scope switch
        {
            BindingPriorityScope.Style => "Style",
            BindingPriorityScope.Template => "Template",
            _ => null
        };
    }

    private static string? GetSetValueBindingPriorityExpression(
        INamedTypeSymbol targetType,
        IFieldSymbol propertyField,
        Compilation compilation,
        BindingPriorityScope scope)
    {
        if (scope != BindingPriorityScope.Template)
        {
            return null;
        }

        if (!IsStyledOrAttachedAvaloniaProperty(propertyField))
        {
            return null;
        }

        if (!HasSetValueWithPriorityOverload(targetType, compilation))
        {
            return null;
        }

        return "global::Avalonia.Data.BindingPriority.Template";
    }

    private static bool IsStyledOrAttachedAvaloniaProperty(IFieldSymbol propertyField)
    {
        for (var current = propertyField.Type as INamedTypeSymbol; current is not null; current = current.BaseType)
        {
            if (current.Name is "StyledProperty" or "AvaloniaAttachedProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSetValueWithPriorityOverload(INamedTypeSymbol targetType, Compilation compilation)
    {
        var bindingPriorityType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingPriority);
        if (bindingPriorityType is null)
        {
            return false;
        }

        for (var current = targetType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("SetValue").OfType<IMethodSymbol>())
            {
                if (method.IsStatic || method.Parameters.Length != 3)
                {
                    continue;
                }

                if (!IsAvaloniaPropertyType(method.Parameters[0].Type))
                {
                    continue;
                }

                if (!SymbolEqualityComparer.Default.Equals(method.Parameters[2].Type, bindingPriorityType))
                {
                    continue;
                }

                return true;
            }
        }

        return false;
    }

    private static bool CanAssignBindingValue(ITypeSymbol propertyType, Compilation compilation)
    {
        if (propertyType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        var bindingBaseType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingBase);
        if (bindingBaseType is not null &&
            IsTypeAssignableTo(propertyType, bindingBaseType))
        {
            return true;
        }

        var iBindingType = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface);
        if (iBindingType is not null &&
            IsTypeAssignableTo(propertyType, iBindingType))
        {
            return true;
        }

        var iBinding2Type = ResolveContractType(compilation, TypeContractId.AvaloniaBindingInterface2);
        if (iBinding2Type is not null &&
            IsTypeAssignableTo(propertyType, iBinding2Type))
        {
            return true;
        }

        return false;
    }

    private static bool HasAssignBindingAttribute(IPropertySymbol? property)
    {
        if (property is null)
        {
            return false;
        }

        foreach (var attribute in property.GetAttributes())
        {
            var attributeType = attribute.AttributeClass;
            if (attributeType is null)
            {
                continue;
            }

            if (attributeType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Data.AssignBindingAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? TryGetAvaloniaPropertyValueType(ITypeSymbol propertyFieldType)
    {
        if (propertyFieldType is not INamedTypeSymbol namedType)
        {
            return null;
        }

        if (namedType.IsGenericType &&
            namedType.Name == "AvaloniaProperty" &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType &&
                current.Name == "AvaloniaProperty" &&
                current.TypeArguments.Length == 1)
            {
                return current.TypeArguments[0];
            }
        }

        return null;
    }

    private static bool TryGetAvaloniaUnsetValueExpression(Compilation compilation, out string expression)
    {
        expression = string.Empty;
        var avaloniaPropertyType = ResolveContractType(compilation, TypeContractId.AvaloniaProperty);
        if (avaloniaPropertyType is null)
        {
            return false;
        }

        var hasUnsetMember =
            avaloniaPropertyType.GetMembers("UnsetValue").OfType<IFieldSymbol>().Any(member => member.IsStatic) ||
            avaloniaPropertyType.GetMembers("UnsetValue").OfType<IPropertySymbol>().Any(member => member.IsStatic);
        if (!hasUnsetMember)
        {
            return false;
        }

        expression = "global::Avalonia.AvaloniaProperty.UnsetValue";
        return true;
    }

    private static bool TryFindAvaloniaPropertyField(
        INamedTypeSymbol ownerType,
        string propertyName,
        out INamedTypeSymbol resolvedOwnerType,
        out IFieldSymbol propertyField,
        string? explicitFieldName = null)
    {
        var fieldName = string.IsNullOrWhiteSpace(explicitFieldName)
            ? propertyName + "Property"
            : explicitFieldName is null
                ? propertyName + "Property"
                : explicitFieldName.Trim();
        for (INamedTypeSymbol? current = ownerType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is not null)
            {
                resolvedOwnerType = current;
                propertyField = field;
                return true;
            }
        }

        resolvedOwnerType = ownerType;
        propertyField = null!;
        return false;
    }

    private static bool TryConvertUntypedValueExpression(string value, out string expression)
    {
        var trimmed = value.Trim();

        if (XamlScalarLiteralSemantics.TryParseBoolean(trimmed, out var boolValue))
        {
            expression = boolValue ? "true" : "false";
            return true;
        }

        if (XamlScalarLiteralSemantics.TryParseInt32(trimmed, out var intValue))
        {
            expression = intValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (XamlScalarLiteralSemantics.TryParseDouble(trimmed, out var doubleValue))
        {
            expression = FormatDoubleLiteral(doubleValue);
            return true;
        }

        expression = "\"" + Escape(trimmed) + "\"";
        return true;
    }

    private static bool TryParseHandlerName(string value, out string? handlerName)
    {
        handlerName = null;
        if (!XamlEventHandlerNameSemantics.TryParseHandlerName(value, out var parsedHandlerName))
        {
            return false;
        }

        handlerName = parsedHandlerName;
        return true;
    }

    private static bool TryParseInlineEventLambdaExpression(string value, out string lambdaExpression)
    {
        lambdaExpression = string.Empty;
        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                value,
                implicitExpressionsEnabled: true,
                looksLikeMarkupExtensionStart: static _ => false,
                out var rawExpression,
                out _,
                out var isLambdaExpression) ||
            !isLambdaExpression)
        {
            return false;
        }

        lambdaExpression = CSharpExpressionTextSemantics.NormalizeExpressionCode(rawExpression);
        return lambdaExpression.Length > 0;
    }

    private static bool TryBuildDelegateMethodGroupValueExpression(
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
                     MarkupContextRootObjectToken +
                     ")." +
                     handlerMethodName +
                     ")";
        return true;
    }

    private static bool HasCompatibleInstanceMethod(
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

    private static bool HasInstanceMethod(INamedTypeSymbol type, string methodName)
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

    private static bool IsMethodCompatibleWithDelegate(
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
            !IsTypeAssignableTo(candidate.ReturnType, delegateInvoke.ReturnType))
        {
            return false;
        }

        for (var parameterIndex = 0; parameterIndex < delegateInvoke.Parameters.Length; parameterIndex++)
        {
            var delegateParameter = delegateInvoke.Parameters[parameterIndex];
            var candidateParameter = candidate.Parameters[parameterIndex];
            if (!IsTypeAssignableTo(delegateParameter.Type, candidateParameter.Type))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryFindStaticEventField(
        INamedTypeSymbol targetType,
        string eventName,
        out INamedTypeSymbol ownerType,
        out IFieldSymbol eventField)
    {
        var fieldName = eventName + "Event";
        for (INamedTypeSymbol? current = targetType; current is not null; current = current.BaseType)
        {
            var field = current.GetMembers(fieldName).OfType<IFieldSymbol>().FirstOrDefault(member => member.IsStatic);
            if (field is null)
            {
                continue;
            }

            ownerType = current;
            eventField = field;
            return true;
        }

        ownerType = targetType;
        eventField = null!;
        return false;
    }

    private static bool TryResolveRoutedEventHandlerType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol handlerType)
    {
        handlerType = ResolveContractType(compilation, TypeContractId.SystemDelegate) ?? compilation.ObjectType;
        if (!TryGetRoutedEventArgsType(routedEventType, compilation, out var routedEventArgsType))
        {
            return false;
        }

        var eventHandlerType = ResolveContractType(compilation, TypeContractId.SystemEventHandlerOfT);
        var eventArgsBaseType = ResolveContractType(compilation, TypeContractId.SystemEventArgs);
        if (eventHandlerType is INamedTypeSymbol eventHandlerNamed &&
            eventArgsBaseType is not null &&
            IsTypeAssignableTo(routedEventArgsType, eventArgsBaseType))
        {
            handlerType = eventHandlerNamed.Construct(routedEventArgsType);
            return true;
        }

        var routedEventHandlerType = ResolveContractType(compilation, TypeContractId.AvaloniaRoutedEventHandler);
        if (routedEventHandlerType is not null)
        {
            handlerType = routedEventHandlerType;
            return true;
        }

        return true;
    }

    private static bool TryGetRoutedEventArgsType(
        ITypeSymbol routedEventType,
        Compilation compilation,
        out ITypeSymbol routedEventArgsType)
    {
        routedEventArgsType = ResolveContractType(compilation, TypeContractId.AvaloniaRoutedEventArgs)
                              ?? ResolveContractType(compilation, TypeContractId.SystemEventArgs)
                              ?? compilation.ObjectType;

        if (routedEventType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var routedEventTypeSymbol = ResolveContractType(compilation, TypeContractId.AvaloniaRoutedEvent);
        var genericRoutedEventTypeSymbol = ResolveContractType(compilation, TypeContractId.AvaloniaGenericRoutedEvent);
        for (INamedTypeSymbol? current = namedType; current is not null; current = current.BaseType)
        {
            var isGenericRoutedEvent = genericRoutedEventTypeSymbol is not null &&
                                       SymbolEqualityComparer.Default.Equals(
                                           current.OriginalDefinition,
                                           genericRoutedEventTypeSymbol);
            if (!isGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                current.IsGenericType &&
                current.TypeArguments.Length == 1)
            {
                isGenericRoutedEvent = true;
            }

            if (isGenericRoutedEvent && current.TypeArguments.Length == 1)
            {
                routedEventArgsType = current.TypeArguments[0];
                return true;
            }

            var isNonGenericRoutedEvent = routedEventTypeSymbol is not null &&
                                          SymbolEqualityComparer.Default.Equals(current, routedEventTypeSymbol);
            if (!isNonGenericRoutedEvent &&
                current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                !current.IsGenericType)
            {
                isNonGenericRoutedEvent = true;
            }

            if (isNonGenericRoutedEvent)
            {
                return true;
            }
        }

        return false;
    }

    private static IMethodSymbol? FindParameterlessMethod(INamedTypeSymbol type, string methodName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var method = current.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault(member =>
                !member.IsStatic &&
                member.MethodKind == MethodKind.Ordinary &&
                member.Parameters.Length == 0);
            if (method is not null)
            {
                return method;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindAccessibleParameterlessMethod(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol type,
        string methodName,
        out bool foundInaccessibleMethod)
    {
        foundInaccessibleMethod = false;

        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var member in current.GetMembers(methodName).OfType<IMethodSymbol>())
            {
                if (member.IsStatic ||
                    member.MethodKind != MethodKind.Ordinary ||
                    member.Parameters.Length != 0)
                {
                    continue;
                }

                if (!compilation.IsSymbolAccessibleWithin(member, accessibilityWithin, type))
                {
                    foundInaccessibleMethod = true;
                    continue;
                }

                return member;
            }
        }

        return null;
    }

    private static IMethodSymbol? FindAttachedPropertyGetterMethod(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol targetType)
    {
        var getterName = "Get" + propertyName;
        foreach (var method in ownerType.GetMembers(getterName).OfType<IMethodSymbol>())
        {
            if (!method.IsStatic ||
                method.MethodKind != MethodKind.Ordinary ||
                method.Parameters.Length != 1)
            {
                continue;
            }

            if (IsTypeAssignableTo(targetType, method.Parameters[0].Type))
            {
                return method;
            }
        }

        return null;
    }

    private static bool IsTypeAssignableTo(ITypeSymbol sourceType, ITypeSymbol targetType)
    {
        if (AreEquivalentTypesIgnoringNullable(sourceType, targetType))
        {
            return true;
        }

        if (sourceType is INamedTypeSymbol sourceNamed)
        {
            for (INamedTypeSymbol? current = sourceNamed; current is not null; current = current.BaseType)
            {
                if (AreEquivalentTypesIgnoringNullable(current, targetType))
                {
                    return true;
                }

            }

            foreach (var implementedInterface in sourceNamed.AllInterfaces)
            {
                if (AreEquivalentTypesIgnoringNullable(implementedInterface, targetType))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsCommandTargetType(ITypeSymbol? targetPropertyType, INamedTypeSymbol? commandType)
    {
        if (targetPropertyType is null)
        {
            return false;
        }

        if (commandType is not null)
        {
            if (AreEquivalentTypesIgnoringNullable(targetPropertyType, commandType))
            {
                return true;
            }
        }

        return IsCommandMetadataType(targetPropertyType);
    }

    private static bool IsCommandMetadataType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.Name.Equals("ICommand", StringComparison.Ordinal) &&
               namedType.ContainingNamespace.ToDisplayString().Equals("System.Windows.Input", StringComparison.Ordinal);
    }

    private static bool IsCommandLikeType(ITypeSymbol type)
    {
        if (IsCommandMetadataType(type))
        {
            return true;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (namedType.AllInterfaces.Any(IsCommandMetadataType))
        {
            return true;
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (IsCommandMetadataType(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AreEquivalentTypesIgnoringNullable(ITypeSymbol left, ITypeSymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
               SymbolEqualityComparer.Default.Equals(
                   left.WithNullableAnnotation(NullableAnnotation.None),
                   right.WithNullableAnnotation(NullableAnnotation.None));
    }

    private static IEventSymbol? FindEvent(INamedTypeSymbol type, string eventName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var eventSymbol = current.GetMembers(eventName).OfType<IEventSymbol>().FirstOrDefault();
            if (eventSymbol is not null)
            {
                return eventSymbol;
            }
        }

        return null;
    }

    private static IPropertySymbol? FindProperty(INamedTypeSymbol type, string propertyName)
    {
        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property is not null)
            {
                return property;
            }
        }

        return null;
    }

    private static IPropertySymbol? FindAccessibleProperty(
        Compilation compilation,
        ISymbol accessibilityWithin,
        INamedTypeSymbol type,
        string propertyName,
        out bool foundInaccessibleProperty)
    {
        foundInaccessibleProperty = false;

        foreach (var current in EnumerateInstanceMemberLookupTypes(type))
        {
            foreach (var property in current.GetMembers(propertyName).OfType<IPropertySymbol>())
            {
                if (!compilation.IsSymbolAccessibleWithin(property, accessibilityWithin, type))
                {
                    foundInaccessibleProperty = true;
                    continue;
                }

                return property;
            }
        }

        return null;
    }

    private static ISymbol GetGeneratedCodeAccessibilityWithinSymbol(
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (document.IsClassBacked &&
            !string.IsNullOrWhiteSpace(document.ClassFullName))
        {
            var classSymbol = compilation.GetTypeByMetadataName(document.ClassFullName!);
            if (classSymbol is not null)
            {
                return classSymbol;
            }
        }

        return compilation.Assembly;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateInstanceMemberLookupTypes(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
        {
            var pending = new Stack<INamedTypeSymbol>();
            var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            pending.Push(type);

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                yield return current;

                for (var index = current.Interfaces.Length - 1; index >= 0; index--)
                {
                    pending.Push(current.Interfaces[index]);
                }
            }

            yield break;
        }

        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            yield return current;
        }
    }

    private static string NormalizePropertyName(string propertyName)
    {
        if (TrySplitOwnerQualifiedPropertyToken(propertyName, out _, out var normalized))
        {
            return normalized;
        }

        return propertyName;
    }

    private static bool IsDesignTimePropertyToken(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var trimmed = propertyName.Trim();
        return trimmed.StartsWith("Design.", StringComparison.Ordinal);
    }

    private static bool TrySplitOwnerQualifiedPropertyToken(
        string propertyToken,
        out string ownerToken,
        out string propertyName)
    {
        return XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
            propertyToken,
            out ownerToken,
            out propertyName);
    }

    private static bool HasResolvedPropertyAssignment(
        ImmutableArray<ResolvedPropertyAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasResolvedPropertyElementAssignment(
        ImmutableArray<ResolvedPropertyElementAssignment>.Builder assignments,
        string propertyName)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            if (assignments[index].PropertyName.Equals(propertyName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryConvertValueExpression(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null)
    {
        if (TryConvertValueConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion: preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback: allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback: true,
                selectorNestingTypeHint: selectorNestingTypeHint))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool TryConvertValueConversion(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true,
        bool allowObjectStringLiteralFallback = true,
        bool allowStaticParseMethodFallback = true,
        INamedTypeSymbol? selectorNestingTypeHint = null,
        ImmutableArray<AttributeData> converterAttributes = default)
    {
        conversion = default;

        if (TryConvertMarkupExtensionConversion(
                value,
                type,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion))
        {
            return true;
        }

        if (type is INamedTypeSymbol nullableType &&
            nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            nullableType.TypeArguments.Length == 1)
        {
            if (XamlScalarLiteralSemantics.IsNullLiteral(value))
            {
                conversion = CreateLiteralConversion("null");
                return true;
            }

            return TryConvertValueConversion(
                value,
                nullableType.TypeArguments[0],
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out conversion,
                preferTypedStaticResourceCoercion,
                allowObjectStringLiteralFallback,
                allowStaticParseMethodFallback,
                selectorNestingTypeHint,
                converterAttributes);
        }

        if (IsAvaloniaPropertyType(type) &&
            TryResolveAvaloniaPropertyReferenceExpression(value, compilation, document, setterTargetType, out var propertyReferenceExpression))
        {
            conversion = CreateLiteralConversion(propertyReferenceExpression);
            return true;
        }

        if (AvaloniaSelectorSemanticAdapter.IsSelectorType(type) &&
            AvaloniaSelectorSemanticAdapter.TryBuildSelectorExpression(
                value,
                compilation,
                document,
                setterTargetType,
                selectorNestingTypeHint,
                ResolveSelectorTypeToken,
                TryResolvePropertyReference,
                TryConvertUntypedValueExpression,
                TryConvertSelectorTypedValue,
                out var selectorExpression))
        {
            conversion = CreateLiteralConversion(selectorExpression);
            return true;
        }

        var escaped = Escape(value);

        var fullyQualifiedTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullyQualifiedTypeName is "global::System.Globalization.CultureInfo" or "global::System.Globalization.CultureInfo?")
        {
            conversion = CreateLiteralConversion(
                "global::System.Globalization.CultureInfo.GetCultureInfo(\"" + escaped + "\")");
            return true;
        }

        if (fullyQualifiedTypeName is "global::System.Type" or "global::System.Type?")
        {
            var resolvedType = ResolveTypeFromTypeExpression(
                compilation,
                document,
                Unquote(value),
                document.ClassNamespace);
            if (resolvedType is not null)
            {
                conversion = CreateLiteralConversion(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")");
                return true;
            }
        }

        if (fullyQualifiedTypeName is "global::System.TimeSpan" or "global::System.TimeSpan?")
        {
            if (TryConvertTimeSpanLiteralExpression(value, out var timeSpanExpression))
            {
                conversion = CreateLiteralConversion(timeSpanExpression);
                return true;
            }
        }

        if (TryConvertAssetBackedImageExpression(fullyQualifiedTypeName, value, out var assetImageExpression))
        {
            conversion = CreateLiteralConversion(assetImageExpression);
            return true;
        }

        if (fullyQualifiedTypeName is "global::Avalonia.Styling.StyleQuery" or "global::Avalonia.Styling.StyleQuery?")
        {
            if (TryConvertStyleQueryLiteralExpression(value, out var queryExpression))
            {
                conversion = CreateLiteralConversion(queryExpression);
                return true;
            }
        }

        if (fullyQualifiedTypeName is "global::Avalonia.Media.FontFeatureCollection" or "global::Avalonia.Media.FontFeatureCollection?")
        {
            if (TryConvertFontFeatureCollectionLiteralExpression(value, out var fontFeaturesExpression))
            {
                conversion = CreateLiteralConversion(fontFeaturesExpression);
                return true;
            }
        }

        if (fullyQualifiedTypeName is "global::Avalonia.Media.FontFamily" or "global::Avalonia.Media.FontFamily?")
        {
            if (TryConvertFontFamilyLiteralExpression(type, value, compilation, document, out var fontFamilyExpression))
            {
                conversion = CreateLiteralConversion(fontFamilyExpression);
                return true;
            }
        }

        if (type.SpecialType == SpecialType.System_String)
        {
            conversion = CreateLiteralConversion("\"" + escaped + "\"");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Boolean &&
            XamlScalarLiteralSemantics.TryParseBoolean(value, out var boolValue))
        {
            conversion = CreateLiteralConversion(boolValue ? "true" : "false");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int32 &&
            XamlScalarLiteralSemantics.TryParseInt32(value, out var intValue))
        {
            conversion = CreateLiteralConversion(intValue.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        if (type.SpecialType == SpecialType.System_Int64 &&
            XamlScalarLiteralSemantics.TryParseInt64(value, out var longValue))
        {
            conversion = CreateLiteralConversion(longValue.ToString(CultureInfo.InvariantCulture) + "L");
            return true;
        }

        if (type.SpecialType == SpecialType.System_Double &&
            XamlScalarLiteralSemantics.TryParseDouble(value, out var doubleValue))
        {
            conversion = CreateLiteralConversion(FormatDoubleLiteral(doubleValue));
            return true;
        }

        if (type.SpecialType == SpecialType.System_Single &&
            XamlScalarLiteralSemantics.TryParseSingle(value, out var floatValue))
        {
            conversion = CreateLiteralConversion(FormatSingleLiteral(floatValue));
            return true;
        }

        if (type.SpecialType == SpecialType.System_Decimal &&
            XamlScalarLiteralSemantics.TryParseDecimal(value, out var decimalValue))
        {
            conversion = CreateLiteralConversion(decimalValue.ToString(CultureInfo.InvariantCulture) + "m");
            return true;
        }

        if (TryConvertStaticPropertyValueExpression(type, value, out var staticPropertyExpression))
        {
            conversion = CreateLiteralConversion(staticPropertyExpression);
            return true;
        }

        if (TryConvertCollectionLiteralExpression(
                type,
                value,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var collectionExpression))
        {
            conversion = CreateLiteralConversion(collectionExpression);
            return true;
        }

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            if (TryConvertEnumValueExpression(enumType, value, out var enumExpression))
            {
                conversion = CreateLiteralConversion(enumExpression);
                return true;
            }
        }

        if (fullyQualifiedTypeName is "global::System.Uri" or "global::System.Uri?")
        {
            conversion = CreateLiteralConversion(
                "new global::System.Uri(\"" + escaped + "\", global::System.UriKind.RelativeOrAbsolute)");
            return true;
        }

        if (TryConvertAvaloniaIntrinsicLiteralExpression(type, value, compilation, out var intrinsicExpression))
        {
            conversion = CreateLiteralConversion(intrinsicExpression);
            return true;
        }

        if (TryConvertAvaloniaBrushExpression(type, value, compilation, out var brushExpression))
        {
            conversion = CreateLiteralConversion(brushExpression);
            return true;
        }

        if (TryConvertAvaloniaTransformExpression(type, value, compilation, out var transformExpression))
        {
            conversion = CreateLiteralConversion(transformExpression);
            return true;
        }

        if (TryConvertAvaloniaCursorExpression(type, value, compilation, out var cursorExpression))
        {
            conversion = CreateLiteralConversion(cursorExpression);
            return true;
        }

        if (TryConvertAvaloniaKeyGestureExpression(type, value, compilation, out var keyGestureExpression))
        {
            conversion = CreateLiteralConversion(keyGestureExpression);
            return true;
        }

        if (TryConvertByTypeConverter(
                type,
                value,
                compilation,
                out var convertedByTypeConverterExpression,
                out var typeConverterValueRequirements,
                converterAttributes))
        {
            conversion = CreateLiteralConversion(
                convertedByTypeConverterExpression,
                typeConverterValueRequirements);
            return true;
        }

        if (allowStaticParseMethodFallback &&
            TryConvertByStaticParseMethod(type, value, out var parsedExpression))
        {
            conversion = CreateLiteralConversion(parsedExpression);
            return true;
        }

        if (type.SpecialType == SpecialType.System_Object)
        {
            if (!allowObjectStringLiteralFallback)
            {
                return false;
            }

            conversion = CreateLiteralConversion("\"" + escaped + "\"");
            return true;
        }

        return false;
    }

    private static bool TryConvertValueForCollectionAdd(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback)
    {
        return TryConvertValueConversion(
            value,
            type,
            compilation,
            document,
            setterTargetType,
            (BindingPriorityScope)bindingPriorityScope,
            out conversion,
            allowObjectStringLiteralFallback: allowObjectStringLiteralFallback);
    }

    private static bool TryConvertEnumValueExpression(
        INamedTypeSymbol enumType,
        string value,
        out string expression)
    {
        expression = string.Empty;

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (XamlScalarLiteralSemantics.TryParseInt64(trimmed, out var numericValue))
        {
            expression = "(" + enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" +
                         numericValue.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        var tokens = XamlDelimitedValueSemantics.SplitEnumFlagTokens(trimmed);
        if (tokens.Length == 0)
        {
            return false;
        }

        var enumMembers = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(static member => member.HasConstantValue)
            .ToArray();
        if (enumMembers.Length == 0)
        {
            return false;
        }

        var fullyQualifiedEnumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var shortEnumTypeName = enumType.Name;
        var memberExpressions = ImmutableArray.CreateBuilder<string>(tokens.Length);
        foreach (var token in tokens)
        {
            var normalizedToken = NormalizeEnumToken(token, shortEnumTypeName);
            var enumMember = enumMembers.FirstOrDefault(member =>
                member.Name.Equals(normalizedToken, StringComparison.OrdinalIgnoreCase));
            if (enumMember is null)
            {
                return false;
            }

            memberExpressions.Add(fullyQualifiedEnumTypeName + "." + enumMember.Name);
        }

        if (memberExpressions.Count == 0)
        {
            return false;
        }

        expression = memberExpressions.Count == 1
            ? memberExpressions[0]
            : string.Join(" | ", memberExpressions);
        return true;
    }

    private static bool TryConvertStaticPropertyValueExpression(
        ITypeSymbol type,
        string value,
        out string expression)
    {
        expression = string.Empty;
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        var memberToken = token;
        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                token,
                out var ownerToken,
                out var normalizedMemberToken))
        {
            var shortOwner = namedType.Name;
            var fullyQualifiedOwner = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (fullyQualifiedOwner.StartsWith("global::", StringComparison.Ordinal))
            {
                fullyQualifiedOwner = fullyQualifiedOwner.Substring("global::".Length);
            }
            if (ownerToken.Equals(shortOwner, StringComparison.OrdinalIgnoreCase) ||
                ownerToken.Equals(fullyQualifiedOwner, StringComparison.OrdinalIgnoreCase))
            {
                memberToken = normalizedMemberToken.Trim();
            }
        }

        if (memberToken.Length == 0)
        {
            return false;
        }

        var staticProperty = namedType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property =>
                property.IsStatic &&
                !property.IsIndexer &&
                SymbolEqualityComparer.Default.Equals(property.Type, namedType) &&
                property.Name.Equals(memberToken, StringComparison.OrdinalIgnoreCase));
        if (staticProperty is not null)
        {
            expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                         "." +
                         staticProperty.Name;
            return true;
        }

        var staticField = namedType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field =>
                field.IsStatic &&
                SymbolEqualityComparer.Default.Equals(field.Type, namedType) &&
                field.Name.Equals(memberToken, StringComparison.OrdinalIgnoreCase));
        if (staticField is null)
        {
            return false;
        }

        expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "." +
                     staticField.Name;
        return true;
    }

    private static string NormalizeEnumToken(string token, string enumTypeName)
    {
        var trimmedToken = token.Trim();
        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                trimmedToken,
                out _,
                out var memberToken))
        {
            return memberToken.Trim();
        }

        return trimmedToken;
    }

    private static bool TryConvertTimeSpanLiteralExpression(string value, out string expression)
    {
        expression = string.Empty;
        if (!XamlTimeSpanLiteralSemantics.TryParse(value, out var parsedTimeSpan))
        {
            return false;
        }

        expression = "global::System.TimeSpan.FromTicks(" +
                     parsedTimeSpan.Ticks.ToString(CultureInfo.InvariantCulture) +
                     "L)";
        return true;
    }

    private static bool TryConvertAssetBackedImageExpression(
        string fullyQualifiedTypeName,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var targetMethodName = fullyQualifiedTypeName switch
        {
            "global::Avalonia.Media.IImage" or
            "global::Avalonia.Media.IImage?" or
            "global::Avalonia.Media.Imaging.Bitmap" or
            "global::Avalonia.Media.Imaging.Bitmap?" or
            "global::Avalonia.Media.IImageBrushSource" or
            "global::Avalonia.Media.IImageBrushSource?" => "LoadBitmapAsset",
            "global::Avalonia.Controls.WindowIcon" or
            "global::Avalonia.Controls.WindowIcon?" => "LoadWindowIconAsset",
            _ => null
        };
        if (targetMethodName is null)
        {
            return false;
        }

        expression = "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime." +
                     targetMethodName +
                     "(\"" +
                     Escape(Unquote(trimmed)) +
                     "\", " +
                     MarkupContextBaseUriToken +
                     ")";
        return true;
    }

    private static bool TryConvertStyleQueryLiteralExpression(string value, out string expression)
    {
        expression = string.Empty;
        if (!AvaloniaStyleQuerySemantics.TryParse(value, out var descriptor))
        {
            return false;
        }

        expression = "global::Avalonia.Styling.StyleQueries." +
                     descriptor.MethodName +
                     "(null, global::Avalonia.Styling.StyleQueryComparisonOperator." +
                     descriptor.OperatorName +
                     ", " +
                     descriptor.Value.ToString("R", CultureInfo.InvariantCulture) +
                     "d)";
        return true;
    }

    private static bool TryConvertFontFeatureCollectionLiteralExpression(string value, out string expression)
    {
        expression = string.Empty;
        var tokens = XamlDelimitedValueSemantics.SplitCollectionItems(
            value,
            new[] { "," },
            StringSplitOptions.RemoveEmptyEntries);

        var itemExpressions = new List<string>(tokens.Length);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].Trim();
            if (token.Length == 0)
            {
                continue;
            }

            itemExpressions.Add("global::Avalonia.Media.FontFeature.Parse(\"" + Escape(token) + "\")");
        }

        if (itemExpressions.Count == 0)
        {
            expression = "new global::Avalonia.Media.FontFeatureCollection()";
            return true;
        }

        expression = "new global::Avalonia.Media.FontFeatureCollection { " + string.Join(", ", itemExpressions) + " }";
        return true;
    }

    private static bool TryConvertFontFamilyLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        if (targetType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            if (HasPublicStaticProperty(
                    namedType,
                    propertyName: "Default",
                    returnTypeName: namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                expression = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ".Default";
                return true;
            }

            return false;
        }

        var escapedValue = Escape(value.Trim());
        var typeName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (HasPublicStaticMethod(
                namedType,
                methodName: "Parse",
                returnTypeName: typeName,
                parameterTypeNames: new[] { "global::System.String", "global::System.Uri" }))
        {
            var assemblyName = compilation.AssemblyName ?? "UnknownAssembly";
            var documentBaseUri = "avares://" + assemblyName + "/" + document.TargetPath;
            expression = typeName +
                         ".Parse(\"" +
                         escapedValue +
                         "\", new global::System.Uri(\"" +
                         Escape(documentBaseUri) +
                         "\", global::System.UriKind.Absolute))";
            return true;
        }

        if (HasPublicStaticMethod(
                namedType,
                methodName: "Parse",
                returnTypeName: typeName,
                parameterTypeNames: new[] { "global::System.String" }))
        {
            expression = typeName + ".Parse(\"" + escapedValue + "\")";
            return true;
        }

        return false;
    }

    private static bool TryConvertAvaloniaIntrinsicLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (targetType is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var fullyQualifiedTypeName = namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (fullyQualifiedTypeName)
        {
            case "global::Avalonia.Thickness":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseThickness(
                        value,
                        out var componentCount,
                        out var left,
                        out var top,
                        out var right,
                        out var bottom))
                {
                    return false;
                }

                if (componentCount == 1 &&
                    HasPublicConstructorWithParameterTypes(namedType, "global::System.Double"))
                {
                    expression = "new global::Avalonia.Thickness(" + FormatDoubleLiteral(left) + ")";
                    return true;
                }

                if (componentCount == 2 &&
                    HasPublicConstructorWithParameterTypes(namedType, "global::System.Double", "global::System.Double"))
                {
                    expression = "new global::Avalonia.Thickness(" +
                                 FormatDoubleLiteral(left) +
                                 ", " +
                                 FormatDoubleLiteral(top) +
                                 ")";
                    return true;
                }

                if (HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    expression = "new global::Avalonia.Thickness(" +
                                 FormatDoubleLiteral(left) +
                                 ", " +
                                 FormatDoubleLiteral(top) +
                                 ", " +
                                 FormatDoubleLiteral(right) +
                                 ", " +
                                 FormatDoubleLiteral(bottom) +
                                 ")";
                    return true;
                }

                return false;
            }
            case "global::Avalonia.CornerRadius":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseCornerRadius(
                        value,
                        out var componentCount,
                        out var topLeft,
                        out var topRight,
                        out var bottomRight,
                        out var bottomLeft))
                {
                    return false;
                }

                if (componentCount == 1 &&
                    HasPublicConstructorWithParameterTypes(namedType, "global::System.Double"))
                {
                    expression = "new global::Avalonia.CornerRadius(" + FormatDoubleLiteral(topLeft) + ")";
                    return true;
                }

                if (componentCount == 2 &&
                    HasPublicConstructorWithParameterTypes(namedType, "global::System.Double", "global::System.Double"))
                {
                    expression = "new global::Avalonia.CornerRadius(" +
                                 FormatDoubleLiteral(topLeft) +
                                 ", " +
                                 FormatDoubleLiteral(topRight) +
                                 ")";
                    return true;
                }

                if (HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    expression = "new global::Avalonia.CornerRadius(" +
                                 FormatDoubleLiteral(topLeft) +
                                 ", " +
                                 FormatDoubleLiteral(topRight) +
                                 ", " +
                                 FormatDoubleLiteral(bottomRight) +
                                 ", " +
                                 FormatDoubleLiteral(bottomLeft) +
                                 ")";
                    return true;
                }

                return false;
            }
            case "global::Avalonia.Point":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParsePoint(value, out var x, out var y) ||
                    !HasPublicConstructorWithParameterTypes(namedType, "global::System.Double", "global::System.Double"))
                {
                    return false;
                }

                expression = "new global::Avalonia.Point(" + FormatDoubleLiteral(x) + ", " + FormatDoubleLiteral(y) + ")";
                return true;
            }
            case "global::Avalonia.Vector":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseVector(value, out var x, out var y) ||
                    !HasPublicConstructorWithParameterTypes(namedType, "global::System.Double", "global::System.Double"))
                {
                    return false;
                }

                expression = "new global::Avalonia.Vector(" + FormatDoubleLiteral(x) + ", " + FormatDoubleLiteral(y) + ")";
                return true;
            }
            case "global::Avalonia.Size":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseSize(value, out var width, out var height) ||
                    !HasPublicConstructorWithParameterTypes(namedType, "global::System.Double", "global::System.Double"))
                {
                    return false;
                }

                expression = "new global::Avalonia.Size(" +
                             FormatDoubleLiteral(width) +
                             ", " +
                             FormatDoubleLiteral(height) +
                             ")";
                return true;
            }
            case "global::Avalonia.Rect":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseRect(
                        value,
                        out var x,
                        out var y,
                        out var width,
                        out var height) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    return false;
                }

                expression = "new global::Avalonia.Rect(" +
                             FormatDoubleLiteral(x) +
                             ", " +
                             FormatDoubleLiteral(y) +
                             ", " +
                             FormatDoubleLiteral(width) +
                             ", " +
                             FormatDoubleLiteral(height) +
                             ")";
                return true;
            }
            case "global::Avalonia.Matrix":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseMatrix(
                        value,
                        out var componentCount,
                        out var m11,
                        out var m12,
                        out var m21,
                        out var m22,
                        out var m31,
                        out var m32,
                        out var m13,
                        out var m23,
                        out var m33))
                {
                    return false;
                }

                if (componentCount == 6 &&
                    HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    expression = "new global::Avalonia.Matrix(" +
                                 FormatDoubleLiteral(m11) +
                                 ", " +
                                 FormatDoubleLiteral(m12) +
                                 ", " +
                                 FormatDoubleLiteral(m21) +
                                 ", " +
                                 FormatDoubleLiteral(m22) +
                                 ", " +
                                 FormatDoubleLiteral(m31) +
                                 ", " +
                                 FormatDoubleLiteral(m32) +
                                 ")";
                    return true;
                }

                if (componentCount == 9 &&
                    HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    expression = "new global::Avalonia.Matrix(" +
                                 FormatDoubleLiteral(m11) +
                                 ", " +
                                 FormatDoubleLiteral(m12) +
                                 ", " +
                                 FormatDoubleLiteral(m13) +
                                 ", " +
                                 FormatDoubleLiteral(m21) +
                                 ", " +
                                 FormatDoubleLiteral(m22) +
                                 ", " +
                                 FormatDoubleLiteral(m23) +
                                 ", " +
                                 FormatDoubleLiteral(m31) +
                                 ", " +
                                 FormatDoubleLiteral(m32) +
                                 ", " +
                                 FormatDoubleLiteral(m33) +
                                 ")";
                    return true;
                }

                return false;
            }
            case "global::Avalonia.Vector3D":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseVector3D(value, out var x, out var y, out var z) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double"))
                {
                    return false;
                }

                expression = "new global::Avalonia.Vector3D(" +
                             FormatDoubleLiteral(x) +
                             ", " +
                             FormatDoubleLiteral(y) +
                             ", " +
                             FormatDoubleLiteral(z) +
                             ")";
                return true;
            }
            case "global::Avalonia.PixelPoint":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelPoint(value, out var x, out var y) ||
                    !HasPublicConstructorWithParameterTypes(namedType, "global::System.Int32", "global::System.Int32"))
                {
                    return false;
                }

                expression = "new global::Avalonia.PixelPoint(" +
                             x.ToString(CultureInfo.InvariantCulture) +
                             ", " +
                             y.ToString(CultureInfo.InvariantCulture) +
                             ")";
                return true;
            }
            case "global::Avalonia.PixelSize":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelSize(value, out var width, out var height) ||
                    !HasPublicConstructorWithParameterTypes(namedType, "global::System.Int32", "global::System.Int32"))
                {
                    return false;
                }

                expression = "new global::Avalonia.PixelSize(" +
                             width.ToString(CultureInfo.InvariantCulture) +
                             ", " +
                             height.ToString(CultureInfo.InvariantCulture) +
                             ")";
                return true;
            }
            case "global::Avalonia.PixelRect":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParsePixelRect(
                        value,
                        out var x,
                        out var y,
                        out var width,
                        out var height) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Int32",
                        "global::System.Int32",
                        "global::System.Int32",
                        "global::System.Int32"))
                {
                    return false;
                }

                expression = "new global::Avalonia.PixelRect(" +
                             x.ToString(CultureInfo.InvariantCulture) +
                             ", " +
                             y.ToString(CultureInfo.InvariantCulture) +
                             ", " +
                             width.ToString(CultureInfo.InvariantCulture) +
                             ", " +
                             height.ToString(CultureInfo.InvariantCulture) +
                             ")";
                return true;
            }
            case "global::Avalonia.Controls.GridLength":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseGridLength(value, out var unit, out var numericValue))
                {
                    return false;
                }

                if (unit == AvaloniaGridLengthLiteralUnit.Auto &&
                    HasPublicStaticProperty(
                        namedType,
                        propertyName: "Auto",
                        returnTypeName: "global::Avalonia.Controls.GridLength"))
                {
                    expression = "global::Avalonia.Controls.GridLength.Auto";
                    return true;
                }

                if (!HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::Avalonia.Controls.GridUnitType"))
                {
                    return false;
                }

                var unitExpression = unit == AvaloniaGridLengthLiteralUnit.Star
                    ? "global::Avalonia.Controls.GridUnitType.Star"
                    : "global::Avalonia.Controls.GridUnitType.Pixel";

                expression = "new global::Avalonia.Controls.GridLength(" +
                             FormatDoubleLiteral(numericValue) +
                             ", " +
                             unitExpression +
                             ")";
                return true;
            }
            case "global::Avalonia.Controls.RowDefinition":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseGridLength(value, out var unit, out var numericValue))
                {
                    return false;
                }

                if (!HasPublicConstructorWithParameterTypes(namedType, "global::Avalonia.Controls.GridLength"))
                {
                    return false;
                }

                var gridLengthExpression = unit == AvaloniaGridLengthLiteralUnit.Auto
                    ? "global::Avalonia.Controls.GridLength.Auto"
                    : "new global::Avalonia.Controls.GridLength(" +
                      FormatDoubleLiteral(numericValue) +
                      ", " +
                      (unit == AvaloniaGridLengthLiteralUnit.Star
                          ? "global::Avalonia.Controls.GridUnitType.Star"
                          : "global::Avalonia.Controls.GridUnitType.Pixel") +
                      ")";

                expression = "new global::Avalonia.Controls.RowDefinition(" + gridLengthExpression + ")";
                return true;
            }
            case "global::Avalonia.Controls.ColumnDefinition":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseGridLength(value, out var unit, out var numericValue))
                {
                    return false;
                }

                if (!HasPublicConstructorWithParameterTypes(namedType, "global::Avalonia.Controls.GridLength"))
                {
                    return false;
                }

                var gridLengthExpression = unit == AvaloniaGridLengthLiteralUnit.Auto
                    ? "global::Avalonia.Controls.GridLength.Auto"
                    : "new global::Avalonia.Controls.GridLength(" +
                      FormatDoubleLiteral(numericValue) +
                      ", " +
                      (unit == AvaloniaGridLengthLiteralUnit.Star
                          ? "global::Avalonia.Controls.GridUnitType.Star"
                          : "global::Avalonia.Controls.GridUnitType.Pixel") +
                      ")";

                expression = "new global::Avalonia.Controls.ColumnDefinition(" + gridLengthExpression + ")";
                return true;
            }
            case "global::Avalonia.RelativePoint":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativePoint(value, out var x, out var y, out var unit) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::Avalonia.RelativeUnit"))
                {
                    return false;
                }

                var unitExpression = unit == AvaloniaRelativeUnitLiteral.Relative
                    ? "global::Avalonia.RelativeUnit.Relative"
                    : "global::Avalonia.RelativeUnit.Absolute";
                expression = "new global::Avalonia.RelativePoint(" +
                             FormatDoubleLiteral(x) +
                             ", " +
                             FormatDoubleLiteral(y) +
                             ", " +
                             unitExpression +
                             ")";
                return true;
            }
            case "global::Avalonia.RelativeScalar":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativeScalar(value, out var scalar, out var unit) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::Avalonia.RelativeUnit"))
                {
                    return false;
                }

                var unitExpression = unit == AvaloniaRelativeUnitLiteral.Relative
                    ? "global::Avalonia.RelativeUnit.Relative"
                    : "global::Avalonia.RelativeUnit.Absolute";
                expression = "new global::Avalonia.RelativeScalar(" +
                             FormatDoubleLiteral(scalar) +
                             ", " +
                             unitExpression +
                             ")";
                return true;
            }
            case "global::Avalonia.RelativeRect":
            {
                if (!XamlAvaloniaValueLiteralSemantics.TryParseRelativeRect(
                        value,
                        out var x,
                        out var y,
                        out var width,
                        out var height,
                        out var unit) ||
                    !HasPublicConstructorWithParameterTypes(
                        namedType,
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::System.Double",
                        "global::Avalonia.RelativeUnit"))
                {
                    return false;
                }

                var unitExpression = unit == AvaloniaRelativeUnitLiteral.Relative
                    ? "global::Avalonia.RelativeUnit.Relative"
                    : "global::Avalonia.RelativeUnit.Absolute";
                expression = "new global::Avalonia.RelativeRect(" +
                             FormatDoubleLiteral(x) +
                             ", " +
                             FormatDoubleLiteral(y) +
                             ", " +
                             FormatDoubleLiteral(width) +
                             ", " +
                             FormatDoubleLiteral(height) +
                             ", " +
                             unitExpression +
                             ")";
                return true;
            }
            case "global::Avalonia.Media.Color":
            {
                if (XamlAvaloniaValueLiteralSemantics.TryParseHexColor(value, out var argb) &&
                    HasPublicStaticMethod(
                        namedType,
                        methodName: "FromUInt32",
                        returnTypeName: "global::Avalonia.Media.Color",
                        parameterTypeNames: new[] { "global::System.UInt32" }))
                {
                    expression = "global::Avalonia.Media.Color.FromUInt32(" + FormatHexUInt32Literal(argb) + ")";
                    return true;
                }

                if (TryResolveAvaloniaNamedColorExpression(namedType, value, out var namedColorExpression))
                {
                    expression = namedColorExpression;
                    return true;
                }

                return false;
            }
            default:
                return false;
        }
    }

    private static bool TryResolveAvaloniaNamedColorExpression(
        INamedTypeSymbol colorType,
        string value,
        out string expression)
    {
        expression = string.Empty;
        var token = value.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (XamlPropertyTokenSemantics.TrySplitOwnerQualifiedProperty(
                token,
                out var ownerToken,
                out var memberToken) &&
            ownerToken.Equals("Colors", StringComparison.OrdinalIgnoreCase))
        {
            token = memberToken.Trim();
        }

        if (token.Length == 0 ||
            !XamlIdentifierSemantics.IsIdentifier(token))
        {
            return false;
        }

        var colorsType = colorType.ContainingNamespace.GetTypeMembers("Colors").FirstOrDefault();
        if (colorsType is null)
        {
            return false;
        }

        var property = colorsType.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(member =>
                member.IsStatic &&
                SymbolEqualityComparer.Default.Equals(member.Type, colorType) &&
                member.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
        if (property is null)
        {
            return false;
        }

        expression = "global::Avalonia.Media.Colors." + property.Name;
        return true;
    }

    private static string FormatDoubleLiteral(double value)
    {
        if (double.IsNaN(value))
        {
            return "global::System.Double.NaN";
        }

        if (double.IsPositiveInfinity(value))
        {
            return "global::System.Double.PositiveInfinity";
        }

        if (double.IsNegativeInfinity(value))
        {
            return "global::System.Double.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }

    private static string FormatSingleLiteral(float value)
    {
        if (float.IsNaN(value))
        {
            return "global::System.Single.NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "global::System.Single.PositiveInfinity";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "global::System.Single.NegativeInfinity";
        }

        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    private static string FormatHexUInt32Literal(uint value)
    {
        return "0x" + value.ToString("X8", CultureInfo.InvariantCulture) + "u";
    }

    private static bool HasPublicConstructorWithParameterTypes(
        INamedTypeSymbol type,
        params string[] parameterTypeNames)
    {
        for (var index = 0; index < type.Constructors.Length; index++)
        {
            var constructor = type.Constructors[index];
            if (constructor.IsStatic ||
                constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.Parameters.Length != parameterTypeNames.Length)
            {
                continue;
            }

            var signatureMatches = true;
            for (var parameterIndex = 0; parameterIndex < parameterTypeNames.Length; parameterIndex++)
            {
                var actualParameterTypeName = constructor.Parameters[parameterIndex].Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!TypeNameMatches(actualParameterTypeName, parameterTypeNames[parameterIndex]))
                {
                    signatureMatches = false;
                    break;
                }
            }

            if (signatureMatches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPublicStaticMethod(
        INamedTypeSymbol type,
        string methodName,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames)
    {
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol method ||
                !method.IsStatic ||
                method.DeclaredAccessibility != Accessibility.Public ||
                method.Parameters.Length != parameterTypeNames.Count ||
                !TypeNameMatches(
                    method.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    returnTypeName))
            {
                continue;
            }

            var signatureMatches = true;
            for (var index = 0; index < parameterTypeNames.Count; index++)
            {
                var actualParameterTypeName = method.Parameters[index].Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!TypeNameMatches(actualParameterTypeName, parameterTypeNames[index]))
                {
                    signatureMatches = false;
                    break;
                }
            }

            if (signatureMatches)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindPublicMethod(
        INamedTypeSymbol type,
        string methodName,
        bool isStatic,
        string returnTypeName,
        IReadOnlyList<string> parameterTypeNames,
        out IMethodSymbol method)
    {
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is not IMethodSymbol candidate ||
                candidate.IsStatic != isStatic ||
                candidate.DeclaredAccessibility != Accessibility.Public ||
                candidate.Parameters.Length != parameterTypeNames.Count ||
                !TypeNameMatches(
                    candidate.ReturnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    returnTypeName))
            {
                continue;
            }

            var signatureMatches = true;
            for (var index = 0; index < parameterTypeNames.Count; index++)
            {
                var actualParameterTypeName = candidate.Parameters[index].Type
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (!TypeNameMatches(actualParameterTypeName, parameterTypeNames[index]))
                {
                    signatureMatches = false;
                    break;
                }
            }

            if (signatureMatches)
            {
                method = candidate;
                return true;
            }
        }

        method = null!;
        return false;
    }

    private static bool HasPublicStaticProperty(
        INamedTypeSymbol type,
        string propertyName,
        string returnTypeName)
    {
        foreach (var member in type.GetMembers(propertyName))
        {
            if (member is not IPropertySymbol property ||
                !property.IsStatic ||
                property.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var propertyTypeName = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (TypeNameMatches(propertyTypeName, returnTypeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeNameMatches(string actualTypeName, string expectedTypeName)
    {
        var normalizedActualTypeName = NormalizeTypeNameForComparison(actualTypeName);
        var normalizedExpectedTypeName = NormalizeTypeNameForComparison(expectedTypeName);

        if (normalizedActualTypeName.Equals(normalizedExpectedTypeName, StringComparison.Ordinal))
        {
            return true;
        }

        if (normalizedActualTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            var nonNullableActual = normalizedActualTypeName.Substring(0, normalizedActualTypeName.Length - 1);
            if (nonNullableActual.Equals(normalizedExpectedTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (normalizedExpectedTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            var nonNullableExpected = normalizedExpectedTypeName.Substring(0, normalizedExpectedTypeName.Length - 1);
            if (normalizedActualTypeName.Equals(nonNullableExpected, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTypeNameForComparison(string typeName)
    {
        var trimmedTypeName = typeName.Trim();
        if (trimmedTypeName.EndsWith("?", StringComparison.Ordinal))
        {
            var nonNullableToken = trimmedTypeName.Substring(0, trimmedTypeName.Length - 1);
            return NormalizeTypeNameForComparison(nonNullableToken) + "?";
        }

        switch (trimmedTypeName)
        {
            case "bool":
                return "global::System.Boolean";
            case "byte":
                return "global::System.Byte";
            case "sbyte":
                return "global::System.SByte";
            case "short":
                return "global::System.Int16";
            case "ushort":
                return "global::System.UInt16";
            case "int":
                return "global::System.Int32";
            case "uint":
                return "global::System.UInt32";
            case "long":
                return "global::System.Int64";
            case "ulong":
                return "global::System.UInt64";
            case "float":
                return "global::System.Single";
            case "double":
                return "global::System.Double";
            case "decimal":
                return "global::System.Decimal";
            case "char":
                return "global::System.Char";
            case "string":
                return "global::System.String";
            case "object":
                return "global::System.Object";
            case "void":
                return "global::System.Void";
            default:
                return trimmedTypeName;
        }
    }

    private static bool TryConvertCollectionLiteralExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression)
    {
        expression = string.Empty;
        if (!TryGetCollectionElementType(
                targetType,
                out var elementType,
                out var isArrayTarget,
                out var collectionTypeForSplitConfig))
        {
            return false;
        }

        var trimEntriesFlag = (StringSplitOptions)2;
        var splitOptions = StringSplitOptions.RemoveEmptyEntries | trimEntriesFlag;
        var separators = new[] { "," };
        if (collectionTypeForSplitConfig is not null)
        {
            TryGetCollectionSplitConfiguration(
                collectionTypeForSplitConfig,
                ref separators,
                ref splitOptions,
                trimEntriesFlag);
        }

        var items = XamlDelimitedValueSemantics.SplitCollectionItems(value, separators, splitOptions);

        var itemExpressions = new List<string>(items.Length);
        foreach (var item in items)
        {
            if (!TryConvertValueExpression(
                    item,
                    elementType,
                    compilation,
                    document,
                    setterTargetType,
                    bindingPriorityScope,
                    out var itemExpression))
            {
                return false;
            }

            itemExpressions.Add(itemExpression);
        }

        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var listTypeName = "global::System.Collections.Generic.List<" + elementTypeName + ">";
        var listExpression = itemExpressions.Count == 0
            ? "new " + listTypeName + "()"
            : "new " + listTypeName + " { " + string.Join(", ", itemExpressions) + " }";

        if (isArrayTarget)
        {
            expression = "new " + elementTypeName + "[] { " + string.Join(", ", itemExpressions) + " }";
            return true;
        }

        var listTypeDefinition = ResolveContractType(compilation, TypeContractId.SystemListOfT);
        var listTypeSymbol = listTypeDefinition?.Construct(elementType);
        if (listTypeSymbol is not null &&
            IsTypeAssignableTo(listTypeSymbol, targetType))
        {
            expression = listExpression;
            return true;
        }

        if (targetType is INamedTypeSymbol namedTargetType &&
            !namedTargetType.IsAbstract)
        {
            var constructor = namedTargetType.Constructors
                .FirstOrDefault(ctor =>
                    !ctor.IsStatic &&
                    ctor.Parameters.Length == 1 &&
                    listTypeSymbol is not null &&
                    IsTypeAssignableTo(listTypeSymbol, ctor.Parameters[0].Type));
            if (constructor is not null)
            {
                expression = "new " + namedTargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                             "(" +
                             listExpression +
                             ")";
                return true;
            }
        }

        return false;
    }

    private static bool TryGetCollectionElementType(
        ITypeSymbol targetType,
        out ITypeSymbol elementType,
        out bool isArrayTarget,
        out INamedTypeSymbol? collectionTypeForSplitConfig)
    {
        elementType = null!;
        isArrayTarget = false;
        collectionTypeForSplitConfig = null;

        if (targetType.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (targetType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            isArrayTarget = true;
            return true;
        }

        if (targetType is not INamedTypeSymbol namedTargetType)
        {
            return false;
        }

        collectionTypeForSplitConfig = namedTargetType;

        if (TryGetGenericCollectionElementType(namedTargetType, out elementType))
        {
            return true;
        }

        foreach (var interfaceType in namedTargetType.AllInterfaces)
        {
            if (interfaceType is not INamedTypeSymbol namedInterface ||
                !TryGetGenericCollectionElementType(namedInterface, out elementType))
            {
                continue;
            }

            if (collectionTypeForSplitConfig is null)
            {
                collectionTypeForSplitConfig = namedInterface;
            }

            return true;
        }

        return false;
    }

    private static bool TryGetGenericCollectionElementType(
        INamedTypeSymbol type,
        out ITypeSymbol elementType)
    {
        elementType = null!;
        if (!type.IsGenericType || type.TypeArguments.Length != 1)
        {
            return false;
        }

        var definitionName = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (definitionName is
            "global::System.Collections.Generic.IEnumerable<T>" or
            "global::System.Collections.Generic.ICollection<T>" or
            "global::System.Collections.Generic.IReadOnlyCollection<T>" or
            "global::System.Collections.Generic.IList<T>" or
            "global::System.Collections.Generic.IReadOnlyList<T>")
        {
            elementType = type.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static void TryGetCollectionSplitConfiguration(
        INamedTypeSymbol collectionType,
        ref string[] separators,
        ref StringSplitOptions splitOptions,
        StringSplitOptions trimEntriesFlag)
    {
        var listAttribute = collectionType.GetAttributes()
            .FirstOrDefault(attribute =>
                attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Metadata.AvaloniaListAttribute");
        if (listAttribute is null)
        {
            return;
        }

        foreach (var namedArgument in listAttribute.NamedArguments)
        {
            var key = namedArgument.Key;
            var value = namedArgument.Value;
            if (key.Equals("Separators", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Array &&
                !value.IsNull)
            {
                var configuredSeparators = value.Values
                    .Where(item => item.Kind == TypedConstantKind.Primitive && item.Value is string)
                    .Select(item => (string)item.Value!)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();
                if (configuredSeparators.Length > 0)
                {
                    separators = configuredSeparators;
                }

                continue;
            }

            if (key.Equals("SplitOptions", StringComparison.Ordinal) &&
                value.Kind == TypedConstantKind.Enum &&
                value.Value is int configuredSplitOptions)
            {
                splitOptions = (StringSplitOptions)configuredSplitOptions;
            }
        }

        splitOptions |= trimEntriesFlag;
    }

    private static bool TryBuildRuntimeXamlFragmentExpression(
        string value,
        ITypeSymbol targetType,
        XamlDocumentModel document,
        out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (!RuntimeXamlFragmentDetectionService.IsValidFragment(trimmed))
        {
            return false;
        }

        var baseUri = string.IsNullOrWhiteSpace(document.TargetPath)
            ? document.FilePath
            : document.TargetPath;
        var escapedBaseUri = Escape(baseUri ?? string.Empty);
        var escapedXaml = Escape(trimmed);

        expression = WrapWithTargetTypeCast(
            targetType,
            "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideRuntimeXamlValue(\"" +
            escapedXaml +
            "\", " +
            MarkupContextServiceProviderToken +
            ", " +
            MarkupContextRootObjectToken +
            ", " +
            MarkupContextIntermediateRootObjectToken +
            ", " +
            MarkupContextTargetObjectToken +
            ", " +
            MarkupContextTargetPropertyToken +
            ", \"" +
            escapedBaseUri +
            "\", " +
            MarkupContextParentStackToken +
            ")");
        return true;
    }

    private static bool TryConvertAvaloniaBrushExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var trimmedValue = value.Trim();
        if (targetType.SpecialType == SpecialType.System_Object ||
            trimmedValue.Length == 0)
        {
            return false;
        }

        var iBrushType = ResolveContractType(compilation, TypeContractId.AvaloniaIBrush);
        var brushType = ResolveContractType(compilation, TypeContractId.AvaloniaBrush);
        if (iBrushType is null || brushType is null)
        {
            return false;
        }

        if (!IsTypeAssignableTo(targetType, iBrushType))
        {
            return false;
        }

        var solidColorBrushType = ResolveContractType(compilation, TypeContractId.AvaloniaSolidColorBrush);
        var colorType = ResolveContractType(compilation, TypeContractId.AvaloniaColor);
        if (solidColorBrushType is not null &&
            colorType is not null &&
            IsTypeAssignableTo(solidColorBrushType, targetType) &&
            TryConvertDeterministicSolidColorBrushExpression(
                trimmedValue,
                compilation,
                solidColorBrushType,
                colorType,
                out expression))
        {
            return true;
        }

        if (solidColorBrushType is not null &&
            targetType is INamedTypeSymbol namedTargetType &&
            SymbolEqualityComparer.Default.Equals(namedTargetType, solidColorBrushType) &&
            TryConvertByStaticParseMethod(solidColorBrushType, trimmedValue, out expression))
        {
            return true;
        }

        if (!IsTypeAssignableTo(brushType, targetType))
        {
            return false;
        }

        expression = "global::Avalonia.Media.Brush.Parse(\"" + Escape(trimmedValue) + "\")";
        return true;
    }

    private static bool TryConvertDeterministicSolidColorBrushExpression(
        string value,
        Compilation compilation,
        INamedTypeSymbol solidColorBrushType,
        INamedTypeSymbol colorType,
        out string expression)
    {
        expression = string.Empty;
        if (!TryConvertAvaloniaIntrinsicLiteralExpression(colorType, value, compilation, out var colorExpression))
        {
            return false;
        }

        if (!HasPublicConstructorWithParameterTypes(
                solidColorBrushType,
                colorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
        {
            return false;
        }

        expression = "new " +
                     solidColorBrushType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                     "(" +
                     colorExpression +
                     ")";
        return true;
    }

    private static bool TryConvertAvaloniaTransformExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || targetType.SpecialType == SpecialType.System_Object)
        {
            return false;
        }

        var transformOperationsType = ResolveContractType(compilation, TypeContractId.AvaloniaTransformOperations);
        if (transformOperationsType is null)
        {
            return false;
        }

        if (!IsTypeAssignableTo(transformOperationsType, targetType))
        {
            return false;
        }

        if (TryConvertDeterministicTransformOperationsExpression(
                trimmed,
                compilation,
                transformOperationsType,
                out expression))
        {
            return true;
        }

        expression = "global::Avalonia.Media.Transformation.TransformOperations.Parse(\"" +
                     Escape(trimmed) +
                     "\")";
        return true;
    }

    private static bool TryConvertDeterministicTransformOperationsExpression(
        string value,
        Compilation compilation,
        INamedTypeSymbol transformOperationsType,
        out string expression)
    {
        expression = string.Empty;
        if (!XamlAvaloniaTransformLiteralSemantics.TryParse(value, out var isIdentity, out var operations))
        {
            return false;
        }

        var transformOperationsTypeName = transformOperationsType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (isIdentity)
        {
            if (!HasPublicStaticProperty(
                    transformOperationsType,
                    propertyName: "Identity",
                    returnTypeName: transformOperationsTypeName))
            {
                return false;
            }

            expression = transformOperationsTypeName + ".Identity";
            return true;
        }

        if (!TryFindPublicMethod(
                transformOperationsType,
                methodName: "CreateBuilder",
                isStatic: true,
                returnTypeName: transformOperationsTypeName + ".Builder",
                parameterTypeNames: new[] { "global::System.Int32" },
                out var createBuilderMethod))
        {
            return false;
        }

        if (createBuilderMethod.ReturnType is not INamedTypeSymbol builderType)
        {
            return false;
        }

        if (!TryFindPublicMethod(
                builderType,
                methodName: "Build",
                isStatic: false,
                returnTypeName: transformOperationsTypeName,
                parameterTypeNames: Array.Empty<string>(),
                out var buildMethod))
        {
            return false;
        }

        if (!TryFindPublicMethod(
                builderType,
                methodName: "AppendTranslate",
                isStatic: false,
                returnTypeName: "global::System.Void",
                parameterTypeNames: new[] { "global::System.Double", "global::System.Double" },
                out var appendTranslateMethod))
        {
            return false;
        }

        if (!TryFindPublicMethod(
                builderType,
                methodName: "AppendScale",
                isStatic: false,
                returnTypeName: "global::System.Void",
                parameterTypeNames: new[] { "global::System.Double", "global::System.Double" },
                out var appendScaleMethod))
        {
            return false;
        }

        if (!TryFindPublicMethod(
                builderType,
                methodName: "AppendSkew",
                isStatic: false,
                returnTypeName: "global::System.Void",
                parameterTypeNames: new[] { "global::System.Double", "global::System.Double" },
                out var appendSkewMethod))
        {
            return false;
        }

        if (!TryFindPublicMethod(
                builderType,
                methodName: "AppendRotate",
                isStatic: false,
                returnTypeName: "global::System.Void",
                parameterTypeNames: new[] { "global::System.Double" },
                out var appendRotateMethod))
        {
            return false;
        }

        IMethodSymbol? appendMatrixMethod = null;
        string matrixTypeName = string.Empty;
        var usesMatrixOperation = false;
        for (var operationIndex = 0; operationIndex < operations.Length; operationIndex++)
        {
            if (operations[operationIndex].Kind == AvaloniaTransformOperationLiteralKind.Matrix)
            {
                usesMatrixOperation = true;
                break;
            }
        }

        if (usesMatrixOperation)
        {
            var matrixType = ResolveContractType(compilation, TypeContractId.AvaloniaMatrix);
            if (matrixType is null)
            {
                return false;
            }

            matrixTypeName = matrixType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!TryFindPublicMethod(
                    builderType,
                    methodName: "AppendMatrix",
                    isStatic: false,
                    returnTypeName: "global::System.Void",
                    parameterTypeNames: new[] { matrixTypeName },
                    out appendMatrixMethod))
            {
                return false;
            }

            if (!HasPublicConstructorWithParameterTypes(
                    matrixType,
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double",
                    "global::System.Double"))
            {
                return false;
            }
        }

        var statements = new List<string>(operations.Length + 1);
        statements.Add(
            "var __builder = " +
            transformOperationsTypeName +
            "." +
            createBuilderMethod.Name +
            "(" +
            operations.Length.ToString(CultureInfo.InvariantCulture) +
            ");");

        for (var index = 0; index < operations.Length; index++)
        {
            var operation = operations[index];
            switch (operation.Kind)
            {
                case AvaloniaTransformOperationLiteralKind.Translate:
                    statements.Add(
                        "__builder." +
                        appendTranslateMethod.Name +
                        "(" +
                        FormatDoubleLiteral(operation.Value1) +
                        ", " +
                        FormatDoubleLiteral(operation.Value2) +
                        ");");
                    break;
                case AvaloniaTransformOperationLiteralKind.Scale:
                    statements.Add(
                        "__builder." +
                        appendScaleMethod.Name +
                        "(" +
                        FormatDoubleLiteral(operation.Value1) +
                        ", " +
                        FormatDoubleLiteral(operation.Value2) +
                        ");");
                    break;
                case AvaloniaTransformOperationLiteralKind.Skew:
                    statements.Add(
                        "__builder." +
                        appendSkewMethod.Name +
                        "(" +
                        FormatDoubleLiteral(operation.Value1) +
                        ", " +
                        FormatDoubleLiteral(operation.Value2) +
                        ");");
                    break;
                case AvaloniaTransformOperationLiteralKind.Rotate:
                    statements.Add(
                        "__builder." +
                        appendRotateMethod.Name +
                        "(" +
                        FormatDoubleLiteral(operation.Value1) +
                        ");");
                    break;
                case AvaloniaTransformOperationLiteralKind.Matrix:
                    if (appendMatrixMethod is null || matrixTypeName.Length == 0)
                    {
                        return false;
                    }

                    statements.Add(
                        "__builder." +
                        appendMatrixMethod.Name +
                        "(new " +
                        matrixTypeName +
                        "(" +
                        FormatDoubleLiteral(operation.Value1) +
                        ", " +
                        FormatDoubleLiteral(operation.Value2) +
                        ", " +
                        FormatDoubleLiteral(operation.Value3) +
                        ", " +
                        FormatDoubleLiteral(operation.Value4) +
                        ", " +
                        FormatDoubleLiteral(operation.Value5) +
                        ", " +
                        FormatDoubleLiteral(operation.Value6) +
                        "));");
                    break;
                default:
                    return false;
            }
        }

        expression = "((global::System.Func<" +
                     transformOperationsTypeName +
                     ">)(() => { " +
                     string.Join(" ", statements) +
                     " return __builder." +
                     buildMethod.Name +
                     "(); }))()";
        return true;
    }

    private static bool TryConvertAvaloniaCursorExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (targetType.SpecialType == SpecialType.System_Object ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cursorType = ResolveContractType(compilation, TypeContractId.AvaloniaCursor);
        var standardCursorType = ResolveContractType(compilation, TypeContractId.AvaloniaStandardCursorType);
        if (cursorType is null ||
            standardCursorType is null ||
            standardCursorType.TypeKind != TypeKind.Enum)
        {
            return false;
        }

        if (!IsTypeAssignableTo(cursorType, targetType))
        {
            return false;
        }

        if (!XamlAvaloniaCursorLiteralSemantics.TryParseStandardCursorTypeMember(value, out var memberToken))
        {
            return false;
        }

        var enumMember = standardCursorType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(candidate =>
                candidate.IsStatic &&
                candidate.HasConstantValue &&
                candidate.Name.Equals(memberToken, StringComparison.OrdinalIgnoreCase));
        if (enumMember is null)
        {
            return false;
        }

        var cursorTypeName = cursorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var standardCursorTypeName = standardCursorType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!HasPublicConstructorWithParameterTypes(cursorType, standardCursorTypeName))
        {
            return false;
        }

        expression = "new " +
                     cursorTypeName +
                     "(" +
                     standardCursorTypeName +
                     "." +
                     enumMember.Name +
                     ")";
        return true;
    }

    private static bool TryConvertAvaloniaKeyGestureExpression(
        ITypeSymbol targetType,
        string value,
        Compilation compilation,
        out string expression)
    {
        expression = string.Empty;
        if (targetType.SpecialType == SpecialType.System_Object ||
            string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var keyGestureType = ResolveContractType(compilation, TypeContractId.AvaloniaKeyGesture);
        var keyType = ResolveContractType(compilation, TypeContractId.AvaloniaKey);
        var keyModifiersType = ResolveContractType(compilation, TypeContractId.AvaloniaKeyModifiers);
        if (keyGestureType is null ||
            keyType is null ||
            keyModifiersType is null ||
            keyType.TypeKind != TypeKind.Enum ||
            keyModifiersType.TypeKind != TypeKind.Enum)
        {
            return false;
        }

        if (!IsTypeAssignableTo(keyGestureType, targetType))
        {
            return false;
        }

        if (!XamlAvaloniaKeyGestureLiteralSemantics.TryParse(
                value,
                out var keyToken,
                out var modifierTokens))
        {
            return false;
        }

        if (!TryConvertEnumValueExpression(
                keyType,
                keyToken ?? "None",
                out var keyExpression))
        {
            return false;
        }

        var modifierExpressions = new List<string>(modifierTokens.Length);
        for (var index = 0; index < modifierTokens.Length; index++)
        {
            if (!TryConvertEnumValueExpression(
                    keyModifiersType,
                    modifierTokens[index],
                    out var modifierExpression))
            {
                return false;
            }

            modifierExpressions.Add(modifierExpression);
        }

        var keyGestureTypeName = keyGestureType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var keyTypeName = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var keyModifiersTypeName = keyModifiersType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!HasPublicConstructorWithParameterTypes(keyGestureType, keyTypeName, keyModifiersTypeName))
        {
            return false;
        }

        var modifiersExpression = string.Empty;
        if (modifierExpressions.Count == 0)
        {
            if (!TryConvertEnumValueExpression(keyModifiersType, "None", out modifiersExpression))
            {
                return false;
            }
        }
        else
        {
            modifiersExpression = string.Join(" | ", modifierExpressions.Distinct(StringComparer.Ordinal));
        }

        expression = "new " +
                     keyGestureTypeName +
                     "(" +
                     keyExpression +
                     ", " +
                     modifiersExpression +
                     ")";
        return true;
    }

    private static bool TryConvertMarkupExtensionExpression(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out string expression,
        bool preferTypedStaticResourceCoercion = true)
    {
        if (TryConvertMarkupExtensionConversion(
                value,
                targetType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var conversion,
                preferTypedStaticResourceCoercion))
        {
            expression = conversion.Expression;
            return true;
        }

        expression = string.Empty;
        return false;
    }

    private static bool TryConvertMarkupExtensionConversion(
        string value,
        ITypeSymbol targetType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        BindingPriorityScope bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool preferTypedStaticResourceCoercion = true)
    {
        conversion = default;
        if (!TryParseMarkupExtension(value, out var markup))
        {
            return false;
        }

        if (TryConvertXamlPrimitiveMarkupExtension(markup, targetType, out var primitiveExpression))
        {
            conversion = CreateLiteralConversion(primitiveExpression);
            return true;
        }

        switch (XamlMarkupExtensionNameSemantics.Classify(markup.Name))
        {
            case XamlMarkupExtensionKind.Binding:
            case XamlMarkupExtensionKind.CompiledBinding:
            {
                if (!TryParseBindingMarkup(value, out var bindingMarkup))
                {
                    return false;
                }

                if (bindingMarkup.HasSourceConflict)
                {
                    return false;
                }

                if (!TryBuildBindingValueExpression(
                        compilation,
                        document,
                        bindingMarkup,
                        targetType,
                        setterTargetType,
                        bindingPriorityScope,
                        out var bindingExpression))
                {
                    return false;
                }

                conversion = CreateBindingConversion(
                    bindingExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case XamlMarkupExtensionKind.Null:
            {
                conversion = CreateLiteralConversion("null");
                return true;
            }
            case XamlMarkupExtensionKind.Type:
            {
                var typeToken = markup.NamedArguments.TryGetValue("Type", out var explicitType)
                    ? explicitType
                    : markup.NamedArguments.TryGetValue("TypeName", out var explicitTypeName)
                        ? explicitTypeName
                        : markup.PositionalArguments.Length > 0
                            ? markup.PositionalArguments[0]
                            : null;
                if (string.IsNullOrWhiteSpace(typeToken))
                {
                    return false;
                }

                var resolvedType = ResolveTypeToken(compilation, document, Unquote(typeToken!), document.ClassNamespace);
                if (resolvedType is null)
                {
                    return false;
                }

                conversion = CreateLiteralConversion(
                    "typeof(" + resolvedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")");
                return true;
            }
            case XamlMarkupExtensionKind.Static:
            {
                var memberToken = markup.NamedArguments.TryGetValue("Member", out var explicitMember)
                    ? explicitMember
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(memberToken))
                {
                    return false;
                }

                if (!TryResolveStaticMemberExpression(compilation, document, Unquote(memberToken!), out var staticMemberExpression))
                {
                    return false;
                }

                conversion = CreateLiteralConversion(staticMemberExpression);
                return true;
            }
            case XamlMarkupExtensionKind.StaticResource:
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                if (!TryBuildResourceKeyExpression(keyToken!, compilation, document, out var keyExpression))
                {
                    return false;
                }
                var staticResourceExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideStaticResource(" +
                    keyExpression.Expression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                if (!preferTypedStaticResourceCoercion ||
                    targetType.SpecialType == SpecialType.System_Object)
                {
                    // Keep StaticResource values untyped for object/AP assignment paths so
                    // AvaloniaProperty.UnsetValue can flow without invalid cast exceptions.
                    conversion = CreateMarkupExtensionConversion(
                        staticResourceExpression,
                        requiresRuntimeServiceProvider: true,
                        requiresParentStack: true,
                        requiresStaticResourceResolver: true,
                        resourceKey: keyExpression);
                    return true;
                }

                var typedTargetExpression = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                conversion = CreateMarkupExtensionConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.CoerceStaticResourceValue<" +
                    typedTargetExpression +
                    ">(" +
                    staticResourceExpression +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true,
                    requiresStaticResourceResolver: true,
                    resourceKey: keyExpression);
                return true;
            }
            case XamlMarkupExtensionKind.DynamicResource:
            {
                var keyToken = markup.NamedArguments.TryGetValue("ResourceKey", out var explicitKey)
                    ? explicitKey
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(keyToken))
                {
                    return false;
                }

                if (ResolveContractType(compilation, TypeContractId.DynamicResourceExtension) is null)
                {
                    return false;
                }

                if (!TryBuildResourceKeyExpression(keyToken!, compilation, document, out var dynamicResourceKeyExpression))
                {
                    return false;
                }

                conversion = CreateDynamicResourceBindingConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideDynamicResource(" +
                    dynamicResourceKeyExpression.Expression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true,
                    resourceKey: dynamicResourceKeyExpression);
                return true;
            }
            case XamlMarkupExtensionKind.ReflectionBinding:
            {
                if (!TryParseReflectionBindingMarkup(value, out var reflectionBindingMarkup))
                {
                    return false;
                }

                if (reflectionBindingMarkup.HasSourceConflict ||
                    !TryBuildReflectionBindingExtensionExpression(
                        compilation,
                        document,
                        reflectionBindingMarkup,
                        setterTargetType,
                        bindingPriorityScope,
                        out var reflectionBindingExtensionExpression))
                {
                    return false;
                }

                conversion = CreateBindingConversion(
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReflectionBinding(" +
                    reflectionBindingExtensionExpression +
                    ", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")",
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case XamlMarkupExtensionKind.RelativeSource:
            {
                if (!TryParseRelativeSourceMarkup(value, out var relativeSourceMarkup) ||
                    !TryBuildRelativeSourceExpression(relativeSourceMarkup, compilation, document, out var relativeSourceExpression))
                {
                    return false;
                }

                conversion = CreateLiteralConversion(WrapWithTargetTypeCast(targetType, relativeSourceExpression));
                return true;
            }
            case XamlMarkupExtensionKind.OnPlatform:
            {
                if (ResolveContractType(compilation, TypeContractId.OnPlatformExtension) is null)
                {
                    return false;
                }

                if (!TryConvertOnPlatformExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var onPlatformExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    onPlatformExpression,
                    requiresRuntimeServiceProvider: true);
                return true;
            }
            case XamlMarkupExtensionKind.OnFormFactor:
            {
                if (ResolveContractType(compilation, TypeContractId.OnFormFactorExtension) is null)
                {
                    return false;
                }

                if (!TryConvertOnFormFactorExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var onFormFactorExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    onFormFactorExpression,
                    requiresRuntimeServiceProvider: true);
                return true;
            }
            case XamlMarkupExtensionKind.Reference:
            case XamlMarkupExtensionKind.ResolveByName:
            {
                var referenceName = TryGetNamedMarkupArgument(markup, "Name", "ElementName") ??
                                    (markup.PositionalArguments.Length > 0 ? Unquote(markup.PositionalArguments[0]) : null);
                if (string.IsNullOrWhiteSpace(referenceName))
                {
                    return false;
                }

                var resolveExpression =
                    "global::XamlToCSharpGenerator.Runtime.SourceGenMarkupExtensionRuntime.ProvideReference(\"" +
                    Escape(referenceName!.Trim()) +
                    "\", " +
                    MarkupContextServiceProviderToken +
                    ", " +
                    MarkupContextRootObjectToken +
                    ", " +
                    MarkupContextIntermediateRootObjectToken +
                    ", " +
                    MarkupContextTargetObjectToken +
                    ", " +
                    MarkupContextTargetPropertyToken +
                    ", " +
                    MarkupContextBaseUriToken +
                    ", " +
                    MarkupContextParentStackToken +
                    ")";
                if (!targetType.IsReferenceType &&
                    targetType.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T &&
                    targetType.SpecialType != SpecialType.System_Object)
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    WrapWithTargetTypeCast(targetType, resolveExpression),
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
            }
            case XamlMarkupExtensionKind.TemplateBinding:
            {
                var propertyToken = markup.NamedArguments.TryGetValue("Property", out var explicitProperty)
                    ? explicitProperty
                    : markup.PositionalArguments.Length > 0
                        ? markup.PositionalArguments[0]
                        : null;
                if (string.IsNullOrWhiteSpace(propertyToken))
                {
                    if (ResolveContractType(compilation, TypeContractId.AvaloniaBinding) is null)
                    {
                        return false;
                    }

                    conversion = CreateBindingConversion(
                        "new global::Avalonia.Data.Binding(\".\") { RelativeSource = new global::Avalonia.Data.RelativeSource(global::Avalonia.Data.RelativeSourceMode.TemplatedParent), Priority = global::Avalonia.Data.BindingPriority.Template }");
                    return true;
                }

                if (ResolveContractType(compilation, TypeContractId.AvaloniaTemplateBinding) is not INamedTypeSymbol templateBindingType)
                {
                    return false;
                }

                if (setterTargetType is null)
                {
                    return false;
                }

                if (!TryResolveAvaloniaPropertyReferenceExpression(
                        Unquote(propertyToken!),
                        compilation,
                        document,
                        setterTargetType,
                        out var propertyExpression))
                {
                    return false;
                }

                var initializerParts = new List<string>();
                var modeToken = TryGetNamedMarkupArgument(markup, "Mode");
                if (!string.IsNullOrWhiteSpace(modeToken) &&
                    TryMapBindingMode(modeToken!, out var bindingModeExpression) &&
                    TryGetWritableProperty(templateBindingType, "Mode", out _))
                {
                    initializerParts.Add("Mode = " + bindingModeExpression);
                }

                AddBindingInitializerPart(
                    templateBindingType,
                    propertyName: "Converter",
                    rawValue: TryGetNamedMarkupArgument(markup, "Converter"),
                    compilation,
                    document,
                    setterTargetType,
                    initializerParts);
                AddBindingInitializerPart(
                    templateBindingType,
                    propertyName: "ConverterCulture",
                    rawValue: TryGetNamedMarkupArgument(markup, "ConverterCulture"),
                    compilation,
                    document,
                    setterTargetType,
                    initializerParts);
                AddBindingInitializerPart(
                    templateBindingType,
                    propertyName: "ConverterParameter",
                    rawValue: TryGetNamedMarkupArgument(markup, "ConverterParameter"),
                    compilation,
                    document,
                    setterTargetType,
                    initializerParts);

                var templateBindingExpression = "new global::Avalonia.Data.TemplateBinding(" + propertyExpression + ")";
                if (initializerParts.Count > 0)
                {
                    templateBindingExpression += " { " + string.Join(", ", initializerParts) + " }";
                }

                conversion = CreateTemplateBindingConversion(
                    templateBindingExpression);
                return true;
            }
            default:
                if (!TryConvertGenericMarkupExtensionExpression(
                        markup,
                        targetType,
                        compilation,
                        document,
                        setterTargetType,
                        bindingPriorityScope,
                        out var genericExpression))
                {
                    return false;
                }

                conversion = CreateMarkupExtensionConversion(
                    genericExpression,
                    requiresRuntimeServiceProvider: true,
                    requiresParentStack: true);
                return true;
        }
    }

    private static bool TryParseMarkupExtension(string value, out MarkupExtensionInfo markupExtension)
    {
        return GetActiveMarkupExpressionParser().TryParseMarkupExtension(value, out markupExtension);
    }

    private static bool TryParseRelativeSourceMarkup(string value, out RelativeSourceMarkup relativeSourceMarkup)
    {
        return BindingEventMarkupParser.TryParseRelativeSourceMarkup(
            value,
            TryParseMarkupExtension,
            out relativeSourceMarkup);
    }

    private static int IndexOfTopLevel(string value, char token)
    {
        return TopLevelTextParser.IndexOfTopLevel(value, token);
    }

    private static string Unquote(string value)
    {
        return XamlQuotedValueSemantics.TrimAndUnquote(value);
    }

    private static bool IsQuotedLiteral(string value)
    {
        return XamlQuotedValueSemantics.IsWrapped(value.Trim());
    }

    private static string EscapeChar(char value)
    {
        return value switch
        {
            '\'' => "\\'",
            '\\' => "\\\\",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            '\0' => "\\0",
            _ => value.ToString()
        };
    }
}
