using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using XamlToCSharpGenerator.Core.Models;
using XamlToCSharpGenerator.Core.Parsing;

namespace XamlToCSharpGenerator.Framework.Shared.Binding;

public sealed class CollectionAddBindingService
{
    public delegate INamedTypeSymbol? ResolveTypeTokenDelegate(
        Compilation compilation,
        XamlDocumentModel document,
        string token,
        string fallbackClrNamespace);

    public delegate bool IsTypeAssignableDelegate(ITypeSymbol sourceType, ITypeSymbol targetType);

    public delegate bool TryGetCollectionElementTypeDelegate(
        ITypeSymbol type,
        out ITypeSymbol elementType,
        out bool isArrayTarget,
        out INamedTypeSymbol? collectionTypeForSplitConfig);

    public delegate bool TryConvertValueDelegate(
        string value,
        ITypeSymbol type,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        out ResolvedValueConversionResult conversion,
        bool allowObjectStringLiteralFallback);

    public delegate string EscapeTextDelegate(string value);

    private readonly ResolveTypeTokenDelegate _resolveTypeToken;
    private readonly IsTypeAssignableDelegate _isTypeAssignable;
    private readonly TryGetCollectionElementTypeDelegate _tryGetCollectionElementType;
    private readonly TryConvertValueDelegate _tryConvertValue;
    private readonly EscapeTextDelegate _escapeText;

    public CollectionAddBindingService(
        ResolveTypeTokenDelegate resolveTypeToken,
        IsTypeAssignableDelegate isTypeAssignable,
        TryGetCollectionElementTypeDelegate tryGetCollectionElementType,
        TryConvertValueDelegate tryConvertValue,
        EscapeTextDelegate escapeText)
    {
        _resolveTypeToken = resolveTypeToken ?? throw new ArgumentNullException(nameof(resolveTypeToken));
        _isTypeAssignable = isTypeAssignable ?? throw new ArgumentNullException(nameof(isTypeAssignable));
        _tryGetCollectionElementType = tryGetCollectionElementType ?? throw new ArgumentNullException(nameof(tryGetCollectionElementType));
        _tryConvertValue = tryConvertValue ?? throw new ArgumentNullException(nameof(tryConvertValue));
        _escapeText = escapeText ?? throw new ArgumentNullException(nameof(escapeText));
    }

    public bool HasDirectAddMethod(INamedTypeSymbol type)
    {
        return TryResolveCollectionAddInstruction(type, valueType: null, out _);
    }

    public bool HasDictionaryAddMethod(INamedTypeSymbol type)
    {
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(type))
        {
            var hasAdd = current.GetMembers("Add").OfType<IMethodSymbol>().Any(IsDictionaryAddMethodCandidate);
            if (hasAdd)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryCreateCollectionContentValue(
        string text,
        ITypeSymbol collectionType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        bool allowObjectStringLiteralFallback,
        int line,
        int column,
        out ResolvedObjectNode valueNode,
        out ResolvedCollectionAddInstruction addInstruction)
    {
        valueNode = null!;
        addInstruction = null!;

        if (collectionType is not INamedTypeSymbol namedCollectionType)
        {
            return false;
        }

        CollectionAddCandidate addCandidate = default;
        var stringType = compilation.GetSpecialType(SpecialType.System_String);
        var hasTextSpecificCandidate = false;
        if (stringType is not null &&
            TryResolveCollectionAddCandidate(namedCollectionType, valueType: stringType, out var textSpecificCandidate))
        {
            addCandidate = textSpecificCandidate;
            hasTextSpecificCandidate = true;
        }

        if (!hasTextSpecificCandidate &&
            !TryResolveCollectionAddCandidate(namedCollectionType, valueType: null, out addCandidate))
        {
            return false;
        }

        var targetValueType = addCandidate.ParameterType;
        if (!hasTextSpecificCandidate &&
            targetValueType.SpecialType == SpecialType.System_Object &&
            _tryGetCollectionElementType(collectionType, out var collectionElementType, out _, out _))
        {
            targetValueType = collectionElementType;
        }

        if (TryBuildCollectionTextValueNode(
                text,
                targetValueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                allowObjectStringLiteralFallback,
                line,
                column,
                out valueNode))
        {
            addInstruction = CreateInstruction(namedCollectionType, addCandidate);
            return true;
        }

        return false;
    }

    public ImmutableArray<ResolvedCollectionAddInstruction> ResolveCollectionAddInstructionsForValues(
        ITypeSymbol? collectionType,
        ImmutableArray<ResolvedObjectNode> values,
        Compilation compilation,
        XamlDocumentModel document)
    {
        if (collectionType is not INamedTypeSymbol namedCollectionType || values.IsDefaultOrEmpty)
        {
            return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
        }

        var resolvedInstructions = ImmutableArray.CreateBuilder<ResolvedCollectionAddInstruction>(values.Length);
        foreach (var value in values)
        {
            var valueType = _resolveTypeToken(compilation, document, value.TypeName, document.ClassNamespace);
            if (!TryResolveCollectionAddInstruction(namedCollectionType, valueType, out var instruction))
            {
                if (string.IsNullOrWhiteSpace(value.KeyExpression) ||
                    !TryResolveDictionaryAddInstruction(namedCollectionType, valueType, out instruction))
                {
                    return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
                }
            }

            resolvedInstructions.Add(instruction);
        }

        return resolvedInstructions.ToImmutable();
    }

    public ImmutableArray<ResolvedCollectionAddInstruction> ResolveCollectionAddInstructionsForValueType(
        ITypeSymbol? collectionType,
        ITypeSymbol valueType,
        int count)
    {
        if (collectionType is not INamedTypeSymbol namedCollectionType || count <= 0)
        {
            return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
        }

        if (!TryResolveCollectionAddInstruction(namedCollectionType, valueType, out var instruction))
        {
            return ImmutableArray<ResolvedCollectionAddInstruction>.Empty;
        }

        var resolvedInstructions = ImmutableArray.CreateBuilder<ResolvedCollectionAddInstruction>(count);
        for (var index = 0; index < count; index++)
        {
            resolvedInstructions.Add(instruction);
        }

        return resolvedInstructions.ToImmutable();
    }

    public bool TryResolveCollectionAddInstruction(
        INamedTypeSymbol collectionType,
        ITypeSymbol? valueType,
        out ResolvedCollectionAddInstruction instruction)
    {
        instruction = null!;
        if (!TryResolveCollectionAddCandidate(collectionType, valueType, out var selectedCandidate))
        {
            return false;
        }

        instruction = CreateInstruction(collectionType, selectedCandidate);
        return true;
    }

    private bool TryResolveCollectionAddCandidate(
        INamedTypeSymbol collectionType,
        ITypeSymbol? valueType,
        out CollectionAddCandidate candidate)
    {
        var candidates = GetCollectionAddCandidates(collectionType);
        if (TrySelectCollectionAddCandidate(candidates, valueType, out candidate))
        {
            return true;
        }

        return TryResolveInterfaceCollectionAddCandidate(collectionType, valueType, out candidate);
    }

    private bool TryResolveDictionaryAddInstruction(
        INamedTypeSymbol collectionType,
        ITypeSymbol? valueType,
        out ResolvedCollectionAddInstruction instruction)
    {
        instruction = null!;
        if (!TryResolveDictionaryAddCandidate(collectionType, valueType, out var candidate))
        {
            return false;
        }

        var keyTypeName = candidate.KeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var valueTypeName = candidate.ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        instruction = new ResolvedCollectionAddInstruction(
            CanInvokeAddWithoutReceiverCast(collectionType, candidate.ReceiverTypeSymbol, candidate.ReceiverTypeName)
                ? string.Empty
                : candidate.ReceiverTypeName,
            candidate.MethodName,
            "global::System.Collections.Generic.KeyValuePair<" + keyTypeName + ", " + valueTypeName + ">");
        return true;
    }

    private bool TryResolveDictionaryAddCandidate(
        INamedTypeSymbol collectionType,
        ITypeSymbol? valueType,
        out DictionaryAddCandidate candidate)
    {
        candidate = default;
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(collectionType))
        {
            foreach (var method in current.GetMembers("Add").OfType<IMethodSymbol>())
            {
                if (!IsDictionaryAddMethodCandidate(method))
                {
                    continue;
                }

                var candidateValueType = method.Parameters[1].Type;
                if (valueType is not null && !_isTypeAssignable(valueType, candidateValueType))
                {
                    continue;
                }

                candidate = new DictionaryAddCandidate(
                    current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    method.Name,
                    method.Parameters[0].Type,
                    candidateValueType,
                    current);
                return true;
            }
        }

        return false;
    }

    private bool TryResolveInterfaceCollectionAddCandidate(
        INamedTypeSymbol collectionType,
        ITypeSymbol? valueType,
        out CollectionAddCandidate candidate)
    {
        candidate = default;
        if (!IsReadOnlyCollectionInterface(collectionType))
        {
            return false;
        }

        if (!_tryGetCollectionElementType(collectionType, out var elementType, out _, out _))
        {
            return false;
        }

        if (valueType is not null && !_isTypeAssignable(valueType, elementType))
        {
            return false;
        }

        var elementTypeName = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        candidate = new CollectionAddCandidate(
            "global::System.Collections.Generic.ICollection<" + elementTypeName + ">",
            "Add",
            elementType,
            receiverTypeSymbol: null,
            receiverIsInterface: true);
        return true;
    }

    private static bool IsReadOnlyCollectionInterface(INamedTypeSymbol collectionType)
    {
        if (collectionType.TypeKind != TypeKind.Interface)
        {
            return false;
        }

        var definitionName = collectionType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return definitionName is
            "global::System.Collections.Generic.IReadOnlyCollection<T>" or
            "global::System.Collections.Generic.IReadOnlyList<T>";
    }

    private ResolvedCollectionAddInstruction CreateInstruction(
        INamedTypeSymbol collectionType,
        CollectionAddCandidate candidate)
    {
        return new ResolvedCollectionAddInstruction(
            ReceiverTypeName: CanInvokeAddWithoutReceiverCast(collectionType, candidate.ReceiverTypeSymbol, candidate.ReceiverTypeName)
                ? string.Empty
                : candidate.ReceiverTypeName,
            MethodName: candidate.MethodName,
            ParameterTypeName: candidate.ParameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private bool CanInvokeAddWithoutReceiverCast(
        INamedTypeSymbol collectionType,
        INamedTypeSymbol? receiverTypeSymbol,
        string receiverTypeName)
    {
        if (receiverTypeSymbol?.TypeKind == TypeKind.Interface)
        {
            return false;
        }

        if (receiverTypeSymbol is not null &&
            _isTypeAssignable(collectionType, receiverTypeSymbol))
        {
            return true;
        }

        var collectionTypeName = collectionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return string.Equals(receiverTypeName, collectionTypeName, StringComparison.Ordinal);
    }

    private static ImmutableArray<CollectionAddCandidate> GetCollectionAddCandidates(INamedTypeSymbol collectionType)
    {
        var candidates = ImmutableArray.CreateBuilder<CollectionAddCandidate>();
        var seenSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var current in EnumerateTypeHierarchyAndInterfaces(collectionType))
        {
            AddCollectionAddCandidates(current, "Add", candidates, seenSignatures);
            AddCollectionAddCandidates(current, "AddChild", candidates, seenSignatures);
        }

        return candidates.ToImmutable();
    }

    private static void AddCollectionAddCandidates(
        INamedTypeSymbol receiverType,
        string methodName,
        ImmutableArray<CollectionAddCandidate>.Builder candidates,
        HashSet<string> seenSignatures)
    {
        foreach (var method in receiverType.GetMembers(methodName).OfType<IMethodSymbol>())
        {
            if (!IsCollectionAddMethodCandidate(method))
            {
                continue;
            }

            var parameterType = method.Parameters[0].Type;
            var receiverTypeName = receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var parameterTypeName = parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var signature = receiverTypeName + "|" + method.Name + "|" + parameterTypeName;
            if (!seenSignatures.Add(signature))
            {
                continue;
            }

            candidates.Add(new CollectionAddCandidate(
                receiverTypeName,
                method.Name,
                parameterType,
                receiverType,
                receiverType.TypeKind == TypeKind.Interface));
        }
    }

    private static bool IsCollectionAddMethodCandidate(IMethodSymbol method)
    {
        if (method.IsStatic || method.MethodKind != MethodKind.Ordinary || method.Parameters.Length != 1)
        {
            return false;
        }

        if (method.ContainingType is null)
        {
            return false;
        }

        return method.ContainingType.TypeKind == TypeKind.Interface || method.DeclaredAccessibility == Accessibility.Public;
    }

    private static bool IsDictionaryAddMethodCandidate(IMethodSymbol method)
    {
        if (method.IsStatic || method.MethodKind != MethodKind.Ordinary || method.Parameters.Length != 2)
        {
            return false;
        }

        if (method.ContainingType is null)
        {
            return false;
        }

        return method.ContainingType.TypeKind == TypeKind.Interface || method.DeclaredAccessibility == Accessibility.Public;
    }

    private bool TrySelectCollectionAddCandidate(
        ImmutableArray<CollectionAddCandidate> candidates,
        ITypeSymbol? valueType,
        out CollectionAddCandidate selected)
    {
        selected = default;
        if (candidates.IsDefaultOrEmpty)
        {
            return false;
        }

        var hasCompatibleCandidate = false;
        foreach (var candidate in candidates)
        {
            if (valueType is not null && !_isTypeAssignable(valueType, candidate.ParameterType))
            {
                continue;
            }

            if (!hasCompatibleCandidate || IsBetterCollectionAddCandidate(candidate, selected, valueType))
            {
                selected = candidate;
                hasCompatibleCandidate = true;
            }
        }

        if (hasCompatibleCandidate)
        {
            return true;
        }

        selected = candidates[0];
        for (var index = 1; index < candidates.Length; index++)
        {
            var candidate = candidates[index];
            if (IsBetterCollectionAddCandidate(candidate, selected, valueType: null))
            {
                selected = candidate;
            }
        }

        return true;
    }

    private bool IsBetterCollectionAddCandidate(
        CollectionAddCandidate candidate,
        CollectionAddCandidate current,
        ITypeSymbol? valueType)
    {
        if (valueType is not null)
        {
            var candidateIsExact = SymbolEqualityComparer.Default.Equals(candidate.ParameterType, valueType);
            var currentIsExact = SymbolEqualityComparer.Default.Equals(current.ParameterType, valueType);
            if (candidateIsExact != currentIsExact)
            {
                return candidateIsExact;
            }

            var candidateMoreSpecific = _isTypeAssignable(candidate.ParameterType, current.ParameterType) &&
                                        !_isTypeAssignable(current.ParameterType, candidate.ParameterType);
            var currentMoreSpecific = _isTypeAssignable(current.ParameterType, candidate.ParameterType) &&
                                      !_isTypeAssignable(candidate.ParameterType, current.ParameterType);
            if (candidateMoreSpecific != currentMoreSpecific)
            {
                return candidateMoreSpecific;
            }
        }

        var candidateIsAdd = candidate.MethodName.Equals("Add", StringComparison.Ordinal);
        var currentIsAdd = current.MethodName.Equals("Add", StringComparison.Ordinal);
        if (candidateIsAdd != currentIsAdd)
        {
            return candidateIsAdd;
        }

        var candidatePrefersInterfaceCast = ShouldPreferInterfaceReceiver(candidate, current);
        var currentPrefersInterfaceCast = ShouldPreferInterfaceReceiver(current, candidate);
        if (candidatePrefersInterfaceCast != currentPrefersInterfaceCast)
        {
            return candidatePrefersInterfaceCast;
        }

        if (candidate.ReceiverIsInterface != current.ReceiverIsInterface)
        {
            return !candidate.ReceiverIsInterface;
        }

        var receiverComparison = string.CompareOrdinal(candidate.ReceiverTypeName, current.ReceiverTypeName);
        if (receiverComparison != 0)
        {
            return receiverComparison < 0;
        }

        var candidateParameterName = candidate.ParameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var currentParameterName = current.ParameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var parameterComparison = string.CompareOrdinal(candidateParameterName, currentParameterName);
        if (parameterComparison != 0)
        {
            return parameterComparison < 0;
        }

        return string.CompareOrdinal(candidate.MethodName, current.MethodName) < 0;
    }

    private static bool ShouldPreferInterfaceReceiver(
        CollectionAddCandidate candidate,
        CollectionAddCandidate other)
    {
        return candidate.ReceiverIsInterface &&
               IsICollectionInterface(candidate.ReceiverTypeName) &&
               IsCollectionBaseReceiver(other.ReceiverTypeSymbol);
    }

    private static bool IsICollectionInterface(string receiverTypeName)
    {
        return receiverTypeName.StartsWith("global::System.Collections.Generic.ICollection<", StringComparison.Ordinal);
    }

    private static bool IsCollectionBaseReceiver(INamedTypeSymbol? receiverTypeSymbol)
    {
        return receiverTypeSymbol?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            == "global::System.Collections.ObjectModel.Collection<T>";
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypeHierarchyAndInterfaces(INamedTypeSymbol type)
    {
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (visited.Add(current))
            {
                yield return current;
            }

            foreach (var interfaceType in current.AllInterfaces)
            {
                if (visited.Add(interfaceType))
                {
                    yield return interfaceType;
                }
            }
        }
    }

    private static ResolvedObjectNode CreateInlineFactoryValueNode(
        string typeName,
        string factoryExpression,
        ResolvedValueRequirements valueRequirements,
        int line,
        int column)
    {
        return new ResolvedObjectNode(
            KeyExpression: null,
            Name: null,
            TypeName: typeName,
            IsBindingObjectNode: false,
            FactoryExpression: factoryExpression,
            FactoryValueRequirements: valueRequirements,
            UseServiceProviderConstructor: false,
            UseTopDownInitialization: false,
            PropertyAssignments: ImmutableArray<ResolvedPropertyAssignment>.Empty,
            PropertyElementAssignments: ImmutableArray<ResolvedPropertyElementAssignment>.Empty,
            EventSubscriptions: ImmutableArray<ResolvedEventSubscription>.Empty,
            Children: ImmutableArray<ResolvedObjectNode>.Empty,
            ChildAttachmentMode: ResolvedChildAttachmentMode.None,
            ContentPropertyName: null,
            Line: line,
            Column: column,
            Condition: null);
    }

    private bool TryBuildCollectionTextValueNode(
        string text,
        ITypeSymbol targetValueType,
        Compilation compilation,
        XamlDocumentModel document,
        INamedTypeSymbol? setterTargetType,
        int bindingPriorityScope,
        bool allowObjectStringLiteralFallback,
        int line,
        int column,
        out ResolvedObjectNode valueNode)
    {
        if (targetValueType.SpecialType == SpecialType.System_String)
        {
            valueNode = CreateInlineFactoryValueNode(
                "global::System.String",
                "\"" + _escapeText(text) + "\"",
                ResolvedValueRequirements.None,
                line,
                column);
            return true;
        }

        if (_tryConvertValue(
                text,
                targetValueType,
                compilation,
                document,
                setterTargetType,
                bindingPriorityScope,
                out var convertedElementValue,
                allowObjectStringLiteralFallback))
        {
            valueNode = CreateInlineFactoryValueNode(
                targetValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                convertedElementValue.Expression,
                convertedElementValue.EffectiveRequirements,
                line,
                column);
            return true;
        }

        valueNode = null!;
        return false;
    }

    private readonly struct CollectionAddCandidate
    {
        public CollectionAddCandidate(
            string receiverTypeName,
            string methodName,
            ITypeSymbol parameterType,
            INamedTypeSymbol? receiverTypeSymbol = null,
            bool receiverIsInterface = false)
        {
            ReceiverTypeName = receiverTypeName;
            MethodName = methodName;
            ParameterType = parameterType;
            ReceiverTypeSymbol = receiverTypeSymbol;
            ReceiverIsInterface = receiverIsInterface;
        }

        public string ReceiverTypeName { get; }
        public string MethodName { get; }
        public ITypeSymbol ParameterType { get; }
        public INamedTypeSymbol? ReceiverTypeSymbol { get; }
        public bool ReceiverIsInterface { get; }
    }

    private readonly struct DictionaryAddCandidate
    {
        public DictionaryAddCandidate(
            string receiverTypeName,
            string methodName,
            ITypeSymbol keyType,
            ITypeSymbol valueType,
            INamedTypeSymbol? receiverTypeSymbol = null)
        {
            ReceiverTypeName = receiverTypeName;
            MethodName = methodName;
            KeyType = keyType;
            ValueType = valueType;
            ReceiverTypeSymbol = receiverTypeSymbol;
        }

        public string ReceiverTypeName { get; }
        public string MethodName { get; }
        public ITypeSymbol KeyType { get; }
        public ITypeSymbol ValueType { get; }
        public INamedTypeSymbol? ReceiverTypeSymbol { get; }
    }
}
