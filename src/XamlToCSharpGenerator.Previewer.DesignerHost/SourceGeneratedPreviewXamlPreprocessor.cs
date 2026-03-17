using System.Reflection;
using System.Xml.Linq;
using XamlToCSharpGenerator.Core.Parsing;
using XamlToCSharpGenerator.ExpressionSemantics;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedPreviewXamlPreprocessor
{
    private const string RuntimeMarkupNamespace = "using:XamlToCSharpGenerator.Runtime.Markup";
    private const string Xaml2006Namespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string XmlnsNamespacePrefix = "xmlns";
    private static readonly XNamespace XmlnsNamespace = XNamespace.Xmlns;
    private static readonly XNamespace XamlNamespace = Xaml2006Namespace;
    private static readonly MarkupExpressionParser MarkupParser = new();

    private readonly record struct PreviewAssignmentContext(bool IsEvent);

    public static string Rewrite(string xamlText, Assembly localAssembly)
    {
        ArgumentException.ThrowIfNullOrEmpty(xamlText);
        ArgumentNullException.ThrowIfNull(localAssembly);

        var document = XDocument.Parse(xamlText, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        if (document.Root is null)
        {
            return xamlText;
        }

        var analysis = PreviewExpressionAnalysisContext.ForAssembly(localAssembly);
        var runtimePrefix = EnsurePreviewMarkupNamespacePrefix(document.Root);
        var rootType = ResolveRootType(document.Root, localAssembly);
        var rootTargetType = ResolveElementType(document.Root, localAssembly);
        var changed = RewriteElement(
            document.Root,
            inheritedDataType: null,
            rootType,
            rootTargetType,
            runtimePrefix,
            analysis,
            localAssembly);

        return changed ? document.ToString(SaveOptions.DisableFormatting) : xamlText;
    }

    private static bool RewriteElement(
        XElement element,
        Type? inheritedDataType,
        Type? rootType,
        Type? currentTargetType,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis,
        Assembly localAssembly)
    {
        var changed = false;
        var currentDataType = ResolveDataType(element, inheritedDataType, localAssembly);

        if (IsInlineCSharpElement(element))
        {
            var assignmentContext = element.Parent is XElement parentElement
                ? ResolveElementAssignmentContext(parentElement, currentTargetType)
                : default;
            return RewriteInlineCSharpElement(
                element,
                currentDataType,
                rootType,
                currentTargetType,
                assignmentContext,
                analysis);
        }

        var attributes = element.Attributes().ToArray();
        for (var index = 0; index < attributes.Length; index++)
        {
            var attribute = attributes[index];
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name == XamlNamespace + "DataType")
            {
                continue;
            }

            if (!TryRewriteAttributeValue(
                    element,
                    attribute,
                    attribute.Value,
                    currentDataType,
                    rootType,
                    currentTargetType,
                    runtimePrefix,
                    analysis,
                    localAssembly,
                    out var rewritten))
            {
                continue;
            }

            if (!string.Equals(attribute.Value, rewritten, StringComparison.Ordinal))
            {
                attribute.Value = rewritten;
                changed = true;
            }
        }

        var childNodes = element.Nodes().ToArray();
        for (var index = 0; index < childNodes.Length; index++)
        {
            switch (childNodes[index])
            {
                case XElement childElement:
                    if (IsInlineCSharpElement(childElement))
                    {
                        var assignmentContext = ResolveElementAssignmentContext(element, currentTargetType);
                        if (RewriteInlineCSharpElement(
                                childElement,
                                currentDataType,
                                rootType,
                                currentTargetType,
                                assignmentContext,
                                analysis))
                        {
                            changed = true;
                        }

                        continue;
                    }

                    var childTargetType = ResolveChildTargetType(childElement, currentTargetType, localAssembly);
                    if (RewriteElement(
                            childElement,
                            currentDataType,
                            rootType,
                            childTargetType,
                            runtimePrefix,
                            analysis,
                            localAssembly))
                    {
                        changed = true;
                    }

                    break;

                case XText textNode:
                    var textAssignmentContext = ResolveElementAssignmentContext(element, currentTargetType);
                    if (!TryRewriteExpressionMarkup(
                            element,
                            textNode.Value,
                            currentDataType,
                            rootType,
                            currentTargetType,
                            textAssignmentContext,
                            runtimePrefix,
                            analysis,
                            localAssembly,
                            out var rewrittenText))
                    {
                        continue;
                    }

                    if (!string.Equals(textNode.Value, rewrittenText, StringComparison.Ordinal))
                    {
                        textNode.Value = rewrittenText;
                        changed = true;
                    }

                    break;
            }
        }

        return changed;
    }

    private static bool TryRewriteAttributeValue(
        XElement contextElement,
        XAttribute attribute,
        string rawValue,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis,
        Assembly localAssembly,
        out string rewrittenValue)
    {
        var assignmentContext = ResolveAttributeAssignmentContext(targetType, attribute);
        if (TryRewriteCompactInlineCSharpMarkup(
                rawValue,
                sourceType,
                rootType,
                targetType,
                assignmentContext,
                runtimePrefix,
                analysis,
                out rewrittenValue))
        {
            return true;
        }

        return TryRewriteExpressionMarkup(
            contextElement,
            rawValue,
            sourceType,
            rootType,
            targetType,
            assignmentContext,
            runtimePrefix,
            analysis,
            localAssembly,
            out rewrittenValue);
    }

    private static bool TryRewriteExpressionMarkup(
        XElement contextElement,
        string rawValue,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        PreviewAssignmentContext assignmentContext,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis,
        Assembly localAssembly,
        out string rewrittenValue)
    {
        rewrittenValue = string.Empty;
        if (!CSharpMarkupExpressionSemantics.TryParseMarkupExpression(
                rawValue,
                implicitExpressionsEnabled: true,
                innerExpression => LooksLikeMarkupExtensionStart(contextElement, innerExpression, localAssembly),
                out var rawExpression,
                out _,
                out _))
        {
            return false;
        }

        BuildPreviewCodePayload(
            rawExpression,
            sourceType,
            rootType,
            targetType,
            assignmentContext,
            analysis,
            out var rewrittenExpression,
            out var dependencyNames);
        rewrittenValue = BuildPreviewMarkupExtensionValue(
            rewrittenExpression,
            dependencyNames,
            runtimePrefix);
        return true;
    }

    private static bool TryRewriteCompactInlineCSharpMarkup(
        string rawValue,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        PreviewAssignmentContext assignmentContext,
        string runtimePrefix,
        PreviewExpressionAnalysisContext analysis,
        out string rewrittenValue)
    {
        rewrittenValue = string.Empty;
        if (!TryExtractCompactInlineCSharpCode(rawValue, out var rawCode, out var dependencyNames))
        {
            return false;
        }

        BuildPreviewCodePayload(
            rawCode,
            sourceType,
            rootType,
            targetType,
            assignmentContext,
            analysis,
            out var rewrittenCode,
            out var rewrittenDependencyNames);
        rewrittenValue = BuildPreviewMarkupExtensionValue(
            rewrittenCode,
            rewrittenDependencyNames.Count == 0 ? dependencyNames : rewrittenDependencyNames,
            runtimePrefix);
        return true;
    }

    private static bool RewriteInlineCSharpElement(
        XElement element,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        PreviewAssignmentContext assignmentContext,
        PreviewExpressionAnalysisContext analysis)
    {
        if (!TryExtractInlineCSharpElementCode(element, out var rawCode, out var dependencyNames))
        {
            return false;
        }

        BuildPreviewCodePayload(
            rawCode,
            sourceType,
            rootType,
            targetType,
            assignmentContext,
            analysis,
            out var rewrittenCode,
            out var rewrittenDependencyNames);
        var finalDependencyNames = rewrittenDependencyNames.Count == 0 ? dependencyNames : rewrittenDependencyNames;
        var encodedCode = PreviewMarkupValueCodec.EncodeBase64Url(rewrittenCode);
        var encodedDependencies = finalDependencyNames.Count == 0
            ? null
            : PreviewMarkupValueCodec.EncodeBase64Url(string.Join("\n", finalDependencyNames));

        var changed = false;
        changed |= SetAttributeValue(element, "Code", null);
        changed |= SetAttributeValue(element, "CodeBase64Url", encodedCode);
        changed |= SetAttributeValue(element, "DependencyNamesBase64Url", encodedDependencies);

        if (element.Nodes().Any())
        {
            element.RemoveNodes();
            changed = true;
        }

        return changed;
    }

    private static void BuildPreviewCodePayload(
        string rawCode,
        Type? sourceType,
        Type? rootType,
        Type? targetType,
        PreviewAssignmentContext assignmentContext,
        PreviewExpressionAnalysisContext analysis,
        out string rewrittenCode,
        out IReadOnlyList<string> dependencyNames)
    {
        rewrittenCode = NormalizeExplicitScopeShorthand(rawCode);
        dependencyNames = Array.Empty<string>();

        var isLambdaExpression = CSharpMarkupExpressionSemantics.IsLambdaExpression(rewrittenCode);
        string analyzedCode;
        IReadOnlyList<string> analyzedDependencies;
        var didRewrite = assignmentContext.IsEvent
            ? isLambdaExpression
                ? analysis.TryRewritePreviewLambda(
                    sourceType,
                    rootType,
                    targetType,
                    rewrittenCode,
                    out analyzedCode,
                    out analyzedDependencies,
                    out _)
                : analysis.TryRewritePreviewStatements(
                    sourceType,
                    rootType,
                    targetType,
                    rewrittenCode,
                    out analyzedCode,
                    out analyzedDependencies,
                    out _)
            : analysis.TryRewritePreviewExpression(
                sourceType,
                rootType,
                targetType,
                rewrittenCode,
                out analyzedCode,
                out analyzedDependencies,
                out _);
        if (didRewrite)
        {
            rewrittenCode = analyzedCode;
            dependencyNames = analyzedDependencies;
            return;
        }

        if (TryRewriteSimpleShorthandFallback(rawCode, sourceType, rootType, out var fallbackCode))
        {
            rewrittenCode = fallbackCode;
        }
    }

    private static PreviewAssignmentContext ResolveAttributeAssignmentContext(Type? targetType, XAttribute attribute)
    {
        return ResolveAssignmentContext(targetType, attribute.Name.LocalName);
    }

    private static PreviewAssignmentContext ResolveElementAssignmentContext(XElement element, Type? targetType)
    {
        if (!IsPropertyElement(element))
        {
            return default;
        }

        var localName = element.Name.LocalName;
        var separatorIndex = localName.LastIndexOf('.');
        var memberName = separatorIndex >= 0 ? localName[(separatorIndex + 1)..] : localName;
        return ResolveAssignmentContext(targetType, memberName);
    }

    private static PreviewAssignmentContext ResolveAssignmentContext(Type? targetType, string? memberName)
    {
        if (targetType is null ||
            string.IsNullOrWhiteSpace(memberName))
        {
            return default;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
        return targetType.GetEvent(memberName, flags) is not null
            ? new PreviewAssignmentContext(IsEvent: true)
            : default;
    }

    private static string NormalizeExplicitScopeShorthand(string rawCode)
    {
        if (!CSharpMarkupExpressionSemantics.TryParseSimpleShorthandPath(rawCode, out var shorthand))
        {
            return rawCode.Trim();
        }

        return shorthand.Scope switch
        {
            CSharpShorthandExpressionScope.BindingContext => shorthand.Path == "."
                ? "source"
                : "source." + shorthand.Path,
            CSharpShorthandExpressionScope.Root => shorthand.Path == "."
                ? "root"
                : "root." + shorthand.Path,
            _ => rawCode.Trim()
        };
    }

    private static bool TryRewriteSimpleShorthandFallback(
        string rawCode,
        Type? sourceType,
        Type? rootType,
        out string rewrittenCode)
    {
        rewrittenCode = string.Empty;
        if (!CSharpMarkupExpressionSemantics.TryParseSimpleShorthandPath(rawCode, out var shorthand))
        {
            return false;
        }

        switch (shorthand.Scope)
        {
            case CSharpShorthandExpressionScope.BindingContext:
                rewrittenCode = shorthand.Path == "."
                    ? "source"
                    : "source." + shorthand.Path;
                return true;

            case CSharpShorthandExpressionScope.Root:
                rewrittenCode = shorthand.Path == "."
                    ? "root"
                    : "root." + shorthand.Path;
                return true;

            case CSharpShorthandExpressionScope.Auto:
                if (CanResolveMemberPath(sourceType, shorthand.Path))
                {
                    rewrittenCode = shorthand.Path == "."
                        ? "source"
                        : "source." + shorthand.Path;
                    return true;
                }

                if (CanResolveMemberPath(rootType, shorthand.Path))
                {
                    rewrittenCode = shorthand.Path == "."
                        ? "root"
                        : "root." + shorthand.Path;
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool CanResolveMemberPath(Type? rootType, string path)
    {
        if (rootType is null ||
            string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path == ".")
        {
            return true;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        var currentType = rootType;
        for (var index = 0; index < segments.Length; index++)
        {
            if (!TryResolveMemberType(currentType, segments[index], out currentType))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryResolveMemberType(Type? declaringType, string memberName, out Type? memberType)
    {
        memberType = null;
        if (declaringType is null ||
            string.IsNullOrWhiteSpace(memberName))
        {
            return false;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        if (declaringType.GetProperty(memberName, flags) is PropertyInfo propertyInfo)
        {
            memberType = propertyInfo.PropertyType;
            return true;
        }

        if (declaringType.GetField(memberName, flags) is FieldInfo fieldInfo)
        {
            memberType = fieldInfo.FieldType;
            return true;
        }

        return false;
    }

    private static bool LooksLikeMarkupExtensionStart(
        XElement contextElement,
        string expressionBody,
        Assembly localAssembly)
    {
        if (!MarkupParser.TryParseMarkupExtension("{" + expressionBody + "}", out var markupExtension))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(markupExtension.Name))
        {
            return false;
        }

        if (XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) != XamlMarkupExtensionKind.Unknown)
        {
            return true;
        }

        return ResolveMarkupExtensionType(contextElement, markupExtension.Name, localAssembly) is not null;
    }

    private static Type? ResolveMarkupExtensionType(XElement contextElement, string rawToken, Assembly localAssembly)
    {
        SplitQualifiedName(rawToken, out var prefix, out var localName);
        foreach (var candidateName in XamlMarkupExtensionNameSemantics.EnumerateClrExtensionTypeTokens(localName))
        {
            var candidateToken = string.IsNullOrWhiteSpace(prefix)
                ? candidateName
                : prefix + ":" + candidateName;
            var resolved = ResolveTypeReference(contextElement, candidateToken, localAssembly);
            if (resolved is not null &&
                resolved.GetMethod("ProvideValue", BindingFlags.Instance | BindingFlags.Public) is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private static bool TryExtractCompactInlineCSharpCode(
        string rawValue,
        out string rawCode,
        out IReadOnlyList<string> dependencyNames)
    {
        rawCode = string.Empty;
        dependencyNames = Array.Empty<string>();
        if (!MarkupParser.TryParseMarkupExtension(rawValue, out var markupExtension) ||
            XamlMarkupExtensionNameSemantics.Classify(markupExtension.Name) != XamlMarkupExtensionKind.CSharp)
        {
            return false;
        }

        rawCode = ExtractCode(markupExtension.NamedArguments, markupExtension.PositionalArguments);
        if (rawCode.Length == 0)
        {
            return false;
        }

        dependencyNames = DecodeDependencyNames(
            markupExtension.NamedArguments.TryGetValue("DependencyNamesBase64Url", out var encodedDependencyNames)
                ? encodedDependencyNames
                : null);
        return true;
    }

    private static bool TryExtractInlineCSharpElementCode(
        XElement element,
        out string rawCode,
        out IReadOnlyList<string> dependencyNames)
    {
        rawCode = string.Empty;
        dependencyNames = Array.Empty<string>();
        if (!IsInlineCSharpElement(element))
        {
            return false;
        }

        var codeBase64Attribute = element.Attribute("CodeBase64Url");
        if (codeBase64Attribute is not null &&
            TryDecodeBase64Url(codeBase64Attribute.Value, out var decodedCode))
        {
            rawCode = decodedCode;
        }
        else if (element.Attribute("Code") is { } codeAttribute &&
                 !string.IsNullOrWhiteSpace(codeAttribute.Value))
        {
            rawCode = codeAttribute.Value;
        }
        else
        {
            rawCode = element.Value;
        }

        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        dependencyNames = DecodeDependencyNames(element.Attribute("DependencyNamesBase64Url")?.Value);
        return true;
    }

    private static string ExtractCode(
        IReadOnlyDictionary<string, string> namedArguments,
        IReadOnlyList<string> positionalArguments)
    {
        if (namedArguments.TryGetValue("CodeBase64Url", out var encodedCode) &&
            TryDecodeBase64Url(encodedCode, out var decodedCode))
        {
            return decodedCode;
        }

        if (namedArguments.TryGetValue("Code", out var directCode))
        {
            return directCode;
        }

        return positionalArguments.Count > 0 ? positionalArguments[0] : string.Empty;
    }

    private static IReadOnlyList<string> DecodeDependencyNames(string? encodedDependencyNames)
    {
        if (string.IsNullOrWhiteSpace(encodedDependencyNames) ||
            !TryDecodeBase64Url(encodedDependencyNames, out var decodedDependencyNames) ||
            string.IsNullOrWhiteSpace(decodedDependencyNames))
        {
            return Array.Empty<string>();
        }

        return decodedDependencyNames
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryDecodeBase64Url(string value, out string decodedValue)
    {
        try
        {
            decodedValue = PreviewMarkupValueCodec.DecodeBase64Url(value.Trim());
            return true;
        }
        catch
        {
            decodedValue = string.Empty;
            return false;
        }
    }

    private static string BuildPreviewMarkupExtensionValue(
        string code,
        IReadOnlyList<string> dependencyNames,
        string runtimePrefix)
    {
        var encodedExpression = PreviewMarkupValueCodec.EncodeBase64Url(code);
        var dependencyValue = dependencyNames.Count == 0
            ? string.Empty
            : ", DependencyNamesBase64Url=" + PreviewMarkupValueCodec.EncodeBase64Url(string.Join("\n", dependencyNames));
        var markupExtensionName = string.IsNullOrWhiteSpace(runtimePrefix)
            ? "CSharp"
            : runtimePrefix + ":CSharp";
        return "{" +
            markupExtensionName +
            " CodeBase64Url=" +
            encodedExpression +
            dependencyValue +
            "}";
    }

    private static Type? ResolveChildTargetType(XElement childElement, Type? parentTargetType, Assembly localAssembly)
    {
        return IsPropertyElement(childElement)
            ? parentTargetType
            : ResolveElementType(childElement, localAssembly);
    }

    private static Type? ResolveRootType(XElement root, Assembly localAssembly)
    {
        var xClassAttribute = root.Attribute(XamlNamespace + "Class");
        if (xClassAttribute is not null &&
            !string.IsNullOrWhiteSpace(xClassAttribute.Value))
        {
            var classType = ResolveClassType(localAssembly, xClassAttribute.Value.Trim());
            if (classType is not null)
            {
                return classType;
            }
        }

        return ResolveElementType(root, localAssembly);
    }

    private static Type? ResolveClassType(Assembly localAssembly, string fullTypeName)
    {
        if (fullTypeName.Length == 0)
        {
            return null;
        }

        var resolvedType = localAssembly.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
        if (resolvedType is not null)
        {
            return resolvedType;
        }

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < loadedAssemblies.Length; index++)
        {
            resolvedType = loadedAssemblies[index].GetType(fullTypeName, throwOnError: false, ignoreCase: false);
            if (resolvedType is not null)
            {
                return resolvedType;
            }
        }

        return null;
    }

    private static Type? ResolveDataType(XElement element, Type? inheritedDataType, Assembly localAssembly)
    {
        var attribute = element.Attribute(XamlNamespace + "DataType");
        if (attribute is null ||
            !PreviewTypeExpressionParser.TryExtractTypeToken(attribute.Value, out var typeToken))
        {
            return inheritedDataType;
        }

        return ResolveTypeReference(element, typeToken, localAssembly) ?? inheritedDataType;
    }

    private static Type? ResolveElementType(XElement element, Assembly localAssembly)
    {
        if (IsPropertyElement(element))
        {
            return null;
        }

        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        var rawTypeName = string.IsNullOrWhiteSpace(prefix)
            ? element.Name.LocalName
            : prefix + ":" + element.Name.LocalName;
        return ResolveTypeReference(element, rawTypeName, localAssembly);
    }

    private static Type? ResolveTypeReference(XElement element, string rawValue, Assembly localAssembly)
    {
        var trimmed = rawValue.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return null;
        }

        SplitQualifiedName(trimmed, out var prefix, out var typeName);
        if (typeName.Length == 0)
        {
            return null;
        }

        XNamespace xmlNamespace;
        if (string.IsNullOrWhiteSpace(prefix))
        {
            xmlNamespace = element.GetDefaultNamespace();
        }
        else
        {
            xmlNamespace = element.GetNamespaceOfPrefix(prefix!) ?? XNamespace.None;
        }

        if (SourceGenKnownTypeRegistry.TryResolve(xmlNamespace.NamespaceName, typeName, out var resolvedType))
        {
            return resolvedType;
        }

        if (TryResolveClrNamespaceType(xmlNamespace.NamespaceName, typeName, localAssembly, out resolvedType))
        {
            return resolvedType;
        }

        if (typeName.Contains('.', StringComparison.Ordinal))
        {
            return ResolveClassType(localAssembly, typeName);
        }

        return null;
    }

    private static bool TryResolveClrNamespaceType(
        string xmlNamespace,
        string typeName,
        Assembly localAssembly,
        out Type? resolvedType)
    {
        resolvedType = null;
        ParseClrNamespace(xmlNamespace, out var clrNamespace, out var assemblyName);
        if (string.IsNullOrWhiteSpace(clrNamespace))
        {
            return false;
        }

        var candidateFullName = clrNamespace + "." + typeName;
        foreach (var assembly in EnumerateCandidateAssemblies(localAssembly, assemblyName))
        {
            resolvedType = assembly.GetType(candidateFullName, throwOnError: false, ignoreCase: false);
            if (resolvedType is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Assembly> EnumerateCandidateAssemblies(Assembly localAssembly, string? assemblyName)
    {
        if (!string.IsNullOrWhiteSpace(assemblyName))
        {
            var seenAssemblies = new HashSet<string>(StringComparer.Ordinal);
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var index = 0; index < loadedAssemblies.Length; index++)
            {
                var assembly = loadedAssemblies[index];
                if (AssemblyMatchesName(assembly, assemblyName) &&
                    seenAssemblies.Add(assembly.FullName ?? assemblyName))
                {
                    yield return assembly;
                }
            }

            if (TryLoadAssemblyByName(assemblyName, out var loadedAssembly) &&
                loadedAssembly is not null &&
                seenAssemblies.Add(loadedAssembly.FullName ?? assemblyName))
            {
                yield return loadedAssembly;
            }

            yield break;
        }

        yield return localAssembly;

        var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var index = 0; index < allAssemblies.Length; index++)
        {
            var assembly = allAssemblies[index];
            if (!ReferenceEquals(assembly, localAssembly))
            {
                yield return assembly;
            }
        }
    }

    private static bool AssemblyMatchesName(Assembly assembly, string requestedAssemblyName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(requestedAssemblyName);

        if (string.Equals(assembly.FullName, requestedAssemblyName, StringComparison.Ordinal))
        {
            return true;
        }

        var loadedName = assembly.GetName().Name;
        if (string.Equals(loadedName, requestedAssemblyName, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var requestedName = new AssemblyName(requestedAssemblyName).Name;
            return !string.IsNullOrWhiteSpace(requestedName) &&
                   string.Equals(loadedName, requestedName, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryLoadAssemblyByName(string assemblyName, out Assembly? assembly)
    {
        assembly = null;

        try
        {
            assembly = Assembly.Load(new AssemblyName(assemblyName));
            return assembly is not null;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseClrNamespace(string namespaceUri, out string? clrNamespace, out string? assemblyName)
    {
        clrNamespace = null;
        assemblyName = null;

        if (namespaceUri.StartsWith("using:", StringComparison.Ordinal))
        {
            clrNamespace = namespaceUri["using:".Length..].Trim();
            return;
        }

        if (!namespaceUri.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            return;
        }

        var payload = namespaceUri["clr-namespace:".Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        foreach (var segment in payload.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (segment.StartsWith("assembly=", StringComparison.OrdinalIgnoreCase))
            {
                assemblyName = segment["assembly=".Length..].Trim();
            }
            else if (clrNamespace is null)
            {
                clrNamespace = segment;
            }
        }
    }

    private static void SplitQualifiedName(string rawValue, out string? prefix, out string localName)
    {
        prefix = null;
        localName = rawValue;

        var separatorIndex = rawValue.IndexOf(':');
        if (separatorIndex <= 0)
        {
            return;
        }

        prefix = rawValue[..separatorIndex];
        localName = rawValue[(separatorIndex + 1)..];
    }

    private static bool IsInlineCSharpElement(XElement element)
    {
        if (!string.Equals(element.Name.LocalName, "CSharp", StringComparison.Ordinal))
        {
            return false;
        }

        var namespaceUri = element.Name.NamespaceName;
        return string.Equals(namespaceUri, RuntimeMarkupNamespace, StringComparison.Ordinal) ||
               string.Equals(namespaceUri, "using:XamlToCSharpGenerator.Runtime", StringComparison.Ordinal) ||
               string.Equals(namespaceUri, "clr-namespace:XamlToCSharpGenerator.Runtime", StringComparison.Ordinal) ||
               string.Equals(namespaceUri, "clr-namespace:XamlToCSharpGenerator.Runtime.Markup", StringComparison.Ordinal) ||
               string.Equals(namespaceUri, "https://github.com/avaloniaui", StringComparison.Ordinal);
    }

    private static bool IsPropertyElement(XElement element)
    {
        return element.Name.LocalName.Contains('.', StringComparison.Ordinal);
    }

    private static bool SetAttributeValue(XElement element, string attributeName, string? value)
    {
        var attribute = element.Attribute(attributeName);
        if (string.IsNullOrEmpty(value))
        {
            if (attribute is null)
            {
                return false;
            }

            attribute.Remove();
            return true;
        }

        if (attribute is null)
        {
            element.SetAttributeValue(attributeName, value);
            return true;
        }

        if (string.Equals(attribute.Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        attribute.Value = value;
        return true;
    }

    private static string EnsurePreviewMarkupNamespacePrefix(XElement root)
    {
        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration ||
                !string.Equals(attribute.Value, RuntimeMarkupNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var prefix = attribute.Name.LocalName;
            return prefix == XmlnsNamespacePrefix ? string.Empty : prefix;
        }

        var index = 0;
        while (true)
        {
            var prefix = index == 0 ? "axsg" : "axsg" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (root.GetNamespaceOfPrefix(prefix) is not null)
            {
                index++;
                continue;
            }

            root.SetAttributeValue(XmlnsNamespace + prefix, RuntimeMarkupNamespace);
            return prefix;
        }
    }
}
