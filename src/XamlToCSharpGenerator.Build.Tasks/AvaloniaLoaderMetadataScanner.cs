using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace XamlToCSharpGenerator.Build.Tasks;

internal sealed class AvaloniaLoaderMetadataScanner
{
    private const string AvaloniaLoaderTypeName = "Avalonia.Markup.Xaml.AvaloniaXamlLoader";
    private const string ServiceProviderTypeName = "System.IServiceProvider";
    private const string ObjectTypeName = "System.Object";
    private const string UriTypeName = "System.Uri";
    private static readonly Dictionary<short, OpCode> SingleByteOpCodes = CreateOpCodeMap(size: 1);
    private static readonly Dictionary<short, OpCode> MultiByteOpCodes = CreateOpCodeMap(size: 2);
    private static readonly MetadataTypeNameProvider SignatureTypeNameProvider = new();

    public AvaloniaLoaderScanResult Scan(string assemblyPath)
    {
        using var stream = new FileStream(
            assemblyPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream, PEStreamOptions.Default);
        var metadataReader = peReader.GetMetadataReader();
        var methodMatches = new List<AvaloniaLoaderMethodMatch>();
        var inspectedTypeCount = 0;
        var matchedLoaderCallCount = 0;

        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            inspectedTypeCount++;
            ScanType(metadataReader, peReader, typeHandle, methodMatches, ref matchedLoaderCallCount);
        }

        return new AvaloniaLoaderScanResult(inspectedTypeCount, matchedLoaderCallCount, methodMatches);
    }

    private static void ScanType(
        MetadataReader metadataReader,
        PEReader peReader,
        TypeDefinitionHandle typeHandle,
        List<AvaloniaLoaderMethodMatch> methodMatches,
        ref int matchedLoaderCallCount)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            if (!TryScanMethod(metadataReader, peReader, methodHandle, out var methodMatch))
            {
                continue;
            }

            matchedLoaderCallCount += methodMatch.CallSites.Count;
            methodMatches.Add(methodMatch);
        }
    }

    private static bool TryScanMethod(
        MetadataReader metadataReader,
        PEReader peReader,
        MethodDefinitionHandle methodHandle,
        out AvaloniaLoaderMethodMatch methodMatch)
    {
        methodMatch = default!;
        var methodDefinition = metadataReader.GetMethodDefinition(methodHandle);
        if (methodDefinition.RelativeVirtualAddress == 0)
        {
            return false;
        }

        var methodBody = peReader.GetMethodBody(methodDefinition.RelativeVirtualAddress);
        var ilBytes = methodBody.GetILBytes();
        if (ilBytes is null || ilBytes.Length == 0)
        {
            return false;
        }

        var instructions = DecodeInstructions(metadataReader, ilBytes);
        if (instructions.Count == 0)
        {
            return false;
        }

        List<AvaloniaLoaderCallSiteMatch>? callSites = null;
        for (var instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex++)
        {
            var instruction = instructions[instructionIndex];
            if (instruction.OpCode.Value != OpCodes.Call.Value ||
                instruction.CalledMethod is not { } calledMethod ||
                !TryMatchAvaloniaLoaderCall(calledMethod, out var callKind))
            {
                continue;
            }

            if ((callKind == AvaloniaLoaderCallKind.ObjectWithoutServiceProvider ||
                 callKind == AvaloniaLoaderCallKind.ObjectWithServiceProvider) &&
                !MatchThisCall(instructions, instructionIndex - 1))
            {
                continue;
            }

            callSites ??= [];
            callSites.Add(new AvaloniaLoaderCallSiteMatch(instruction.Offset, callKind));
        }

        if (callSites is null || callSites.Count == 0)
        {
            return false;
        }

        methodMatch = new AvaloniaLoaderMethodMatch(MetadataTokens.GetToken(methodHandle), callSites);
        return true;
    }

    private static List<MetadataIlInstruction> DecodeInstructions(MetadataReader metadataReader, ReadOnlySpan<byte> ilBytes)
    {
        var instructions = new List<MetadataIlInstruction>();
        var offset = 0;

        while (offset < ilBytes.Length)
        {
            var instructionOffset = offset;
            var opCode = ReadOpCode(ilBytes, ref offset);
            var intOperand = default(int?);
            MetadataMethodReferenceInfo? calledMethod = null;

            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                    intOperand = unchecked((sbyte)ilBytes[offset]);
                    offset += 1;
                    break;
                case OperandType.ShortInlineVar:
                    intOperand = ilBytes[offset];
                    offset += 1;
                    break;
                case OperandType.InlineVar:
                    intOperand = BinaryPrimitives.ReadUInt16LittleEndian(ilBytes.Slice(offset, 2));
                    offset += 2;
                    break;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    intOperand = BinaryPrimitives.ReadInt32LittleEndian(ilBytes.Slice(offset, 4));
                    offset += 4;
                    break;
                case OperandType.InlineMethod:
                    intOperand = BinaryPrimitives.ReadInt32LittleEndian(ilBytes.Slice(offset, 4));
                    offset += 4;
                    if (TryResolveMethodReferenceInfo(metadataReader, MetadataTokens.EntityHandle(intOperand.Value), out var resolvedMethod))
                    {
                        calledMethod = resolvedMethod;
                    }

                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    offset += 8;
                    break;
                case OperandType.ShortInlineR:
                    offset += 4;
                    break;
                case OperandType.InlineSwitch:
                    var branchCount = BinaryPrimitives.ReadInt32LittleEndian(ilBytes.Slice(offset, 4));
                    offset += 4 + (branchCount * 4);
                    break;
                default:
                    throw new NotSupportedException("Unsupported IL operand type '" + opCode.OperandType + "'.");
            }

            instructions.Add(new MetadataIlInstruction(instructionOffset, opCode, intOperand, calledMethod));
        }

        return instructions;
    }

    private static OpCode ReadOpCode(ReadOnlySpan<byte> ilBytes, ref int offset)
    {
        var opCodeValue = (short)ilBytes[offset++];
        if (opCodeValue != 0xFE)
        {
            return SingleByteOpCodes[opCodeValue];
        }

        opCodeValue = (short)(0xFE00 | ilBytes[offset++]);
        return MultiByteOpCodes[opCodeValue];
    }

    private static Dictionary<short, OpCode> CreateOpCodeMap(int size)
    {
        var result = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(obj: null) is not OpCode opCode || opCode.Size != size)
            {
                continue;
            }

            result[unchecked((short)opCode.Value)] = opCode;
        }

        return result;
    }

    private static bool TryResolveMethodReferenceInfo(
        MetadataReader metadataReader,
        EntityHandle handle,
        out MetadataMethodReferenceInfo method)
    {
        method = default!;
        switch (handle.Kind)
        {
            case HandleKind.MemberReference:
            {
                var memberReference = metadataReader.GetMemberReference((MemberReferenceHandle)handle);
                var parentTypeName = GetTypeHandleFullName(metadataReader, memberReference.Parent);
                if (string.IsNullOrWhiteSpace(parentTypeName))
                {
                    return false;
                }

                var signature = memberReference.DecodeMethodSignature(SignatureTypeNameProvider, genericContext: null);
                method = new MetadataMethodReferenceInfo(
                    metadataReader.GetString(memberReference.Name),
                    parentTypeName,
                    signature.ParameterTypes.ToArray());
                return true;
            }

            case HandleKind.MethodDefinition:
            {
                var methodDefinition = metadataReader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaringTypeName = GetTypeDefinitionFullName(metadataReader, methodDefinition.GetDeclaringType());
                var signature = methodDefinition.DecodeSignature(SignatureTypeNameProvider, genericContext: null);
                method = new MetadataMethodReferenceInfo(
                    metadataReader.GetString(methodDefinition.Name),
                    declaringTypeName,
                    signature.ParameterTypes.ToArray());
                return true;
            }

            case HandleKind.MethodSpecification:
                return TryResolveMethodReferenceInfo(
                    metadataReader,
                    metadataReader.GetMethodSpecification((MethodSpecificationHandle)handle).Method,
                    out method);

            default:
                return false;
        }
    }

    private static string GetTypeHandleFullName(MetadataReader metadataReader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeDefinitionFullName(metadataReader, (TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetTypeReferenceFullName(metadataReader, (TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => metadataReader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(SignatureTypeNameProvider, genericContext: null),
            _ => string.Empty
        };
    }

    private static string GetTypeDefinitionFullName(MetadataReader metadataReader, TypeDefinitionHandle handle)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(handle);
        var typeName = metadataReader.GetString(typeDefinition.Name);
        var declaringTypeHandle = typeDefinition.GetDeclaringType();
        if (!declaringTypeHandle.IsNil)
        {
            return GetTypeDefinitionFullName(metadataReader, declaringTypeHandle) + "/" + typeName;
        }

        var typeNamespace = metadataReader.GetString(typeDefinition.Namespace);
        return string.IsNullOrWhiteSpace(typeNamespace)
            ? typeName
            : typeNamespace + "." + typeName;
    }

    private static string GetTypeReferenceFullName(MetadataReader metadataReader, TypeReferenceHandle handle)
    {
        var typeReference = metadataReader.GetTypeReference(handle);
        var typeName = metadataReader.GetString(typeReference.Name);

        return typeReference.ResolutionScope.Kind switch
        {
            HandleKind.TypeReference => GetTypeReferenceFullName(metadataReader, (TypeReferenceHandle)typeReference.ResolutionScope) + "/" + typeName,
            HandleKind.TypeDefinition => GetTypeDefinitionFullName(metadataReader, (TypeDefinitionHandle)typeReference.ResolutionScope) + "/" + typeName,
            _ => BuildNamespaceQualifiedName(metadataReader.GetString(typeReference.Namespace), typeName)
        };
    }

    private static string BuildNamespaceQualifiedName(string typeNamespace, string typeName)
    {
        return string.IsNullOrWhiteSpace(typeNamespace)
            ? typeName
            : typeNamespace + "." + typeName;
    }

    private static bool TryMatchAvaloniaLoaderCall(MetadataMethodReferenceInfo method, out AvaloniaLoaderCallKind callKind)
    {
        callKind = default;
        if (!string.Equals(method.Name, "Load", StringComparison.Ordinal) ||
            !string.Equals(method.DeclaringTypeFullName, AvaloniaLoaderTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (method.ParameterTypeNames.Count == 1 &&
            string.Equals(method.ParameterTypeNames[0], ObjectTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.ObjectWithoutServiceProvider;
            return true;
        }

        if (method.ParameterTypeNames.Count == 2 &&
            string.Equals(method.ParameterTypeNames[0], ServiceProviderTypeName, StringComparison.Ordinal) &&
            string.Equals(method.ParameterTypeNames[1], ObjectTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.ObjectWithServiceProvider;
            return true;
        }

        if (method.ParameterTypeNames.Count == 2 &&
            string.Equals(method.ParameterTypeNames[0], UriTypeName, StringComparison.Ordinal) &&
            string.Equals(method.ParameterTypeNames[1], UriTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.UriWithoutServiceProvider;
            return true;
        }

        if (method.ParameterTypeNames.Count == 3 &&
            string.Equals(method.ParameterTypeNames[0], ServiceProviderTypeName, StringComparison.Ordinal) &&
            string.Equals(method.ParameterTypeNames[1], UriTypeName, StringComparison.Ordinal) &&
            string.Equals(method.ParameterTypeNames[2], UriTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.UriWithServiceProvider;
            return true;
        }

        return false;
    }

    private static bool MatchThisCall(IReadOnlyList<MetadataIlInstruction> instructions, int instructionIndex)
    {
        while (instructionIndex >= 0 && instructions[instructionIndex].OpCode.Value == OpCodes.Nop.Value)
        {
            instructionIndex--;
        }

        if (instructionIndex < 0)
        {
            return false;
        }

        var instruction = instructions[instructionIndex];
        if (instruction.OpCode.Value == OpCodes.Ldarg_0.Value ||
            (instruction.OpCode.Value == OpCodes.Ldarg.Value && instruction.IntOperand == 0))
        {
            return true;
        }

        return instruction.OpCode.Value == OpCodes.Call.Value &&
               instruction.CalledMethod is
               {
                   Name: "CheckThis",
                   DeclaringTypeFullName: "Microsoft.FSharp.Core.LanguagePrimitives/IntrinsicFunctions"
               };
    }

    private readonly record struct MetadataIlInstruction(
        int Offset,
        OpCode OpCode,
        int? IntOperand,
        MetadataMethodReferenceInfo? CalledMethod);

    private sealed record MetadataMethodReferenceInfo(
        string Name,
        string DeclaringTypeFullName,
        IReadOnlyList<string> ParameterTypeNames);

    private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape)
        {
            return elementType + "[" + new string(',', Math.Max(0, shape.Rank - 1)) + "]";
        }

        public string GetByReferenceType(string elementType)
        {
            return elementType + "&";
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return "methodptr";
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index)
        {
            return "!!" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            return "!" + index;
        }

        public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType;
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.TypedReference => TypedReferenceSignatureTypeProvider.TypedReferenceTypeName,
                _ => typeCode.ToString()
            };
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return GetTypeDefinitionFullName(reader, handle);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return GetTypeReferenceFullName(reader, handle);
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }

    private static class TypedReferenceSignatureTypeProvider
    {
        public const string TypedReferenceTypeName = "System.TypedReference";
    }
}

internal sealed record AvaloniaLoaderScanResult(
    int InspectedTypeCount,
    int MatchedLoaderCallCount,
    IReadOnlyList<AvaloniaLoaderMethodMatch> MethodMatches);

internal sealed record AvaloniaLoaderMethodMatch(
    int MethodMetadataToken,
    IReadOnlyList<AvaloniaLoaderCallSiteMatch> CallSites);

internal sealed record AvaloniaLoaderCallSiteMatch(
    int IlOffset,
    AvaloniaLoaderCallKind Kind);
