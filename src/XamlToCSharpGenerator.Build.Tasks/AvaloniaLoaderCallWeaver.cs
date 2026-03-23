using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace XamlToCSharpGenerator.Build.Tasks;

internal sealed class AvaloniaLoaderCallWeaver
{
    private const string AvaloniaLoaderTypeName = "Avalonia.Markup.Xaml.AvaloniaXamlLoader";
    private const string GeneratedInitializerMethodName = "__InitializeXamlSourceGenComponent";
    private const string SourceGeneratedPopulateMethodName = "__PopulateGeneratedObjectGraph";
    private const string ServiceProviderTypeName = "System.IServiceProvider";
    private const string ObjectTypeName = "System.Object";

    public AvaloniaLoaderCallWeaverResult Rewrite(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            throw new ArgumentException("Assembly path must not be null or whitespace.", nameof(assemblyPath));
        }

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("Assembly to weave was not found.", assemblyPath);
        }

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        var readSymbols = File.Exists(pdbPath);
        var readerParameters = new ReaderParameters
        {
            InMemory = true,
            ReadSymbols = readSymbols,
            SymbolReaderProvider = readSymbols ? new PortablePdbReaderProvider() : null
        };

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
        var accumulator = new AvaloniaLoaderCallWeaverAccumulator();

        foreach (var type in assembly.MainModule.Types)
        {
            RewriteType(type, accumulator);
        }

        var result = accumulator.ToResult();
        if (result.ErrorMessages.Count > 0 || result.RewrittenCallCount == 0)
        {
            return result;
        }

        var writerParameters = new WriterParameters
        {
            WriteSymbols = readSymbols,
            SymbolWriterProvider = readSymbols ? new PortablePdbWriterProvider() : null
        };

        assembly.Write(assemblyPath, writerParameters);
        return result;
    }

    private static void RewriteType(TypeDefinition type, AvaloniaLoaderCallWeaverAccumulator accumulator)
    {
        accumulator.InspectedTypeCount++;

        var initializerWithoutServiceProvider = FindGeneratedInitializer(type, hasServiceProvider: false);
        var initializerWithServiceProvider = FindGeneratedInitializer(type, hasServiceProvider: true);
        var hasSourceGeneratedPopulateMethod = type.Methods.Any(static method => method.Name == SourceGeneratedPopulateMethodName);

        foreach (var method in type.Methods)
        {
            if (!method.HasBody)
            {
                continue;
            }

            var instructions = method.Body.Instructions;
            for (var instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex++)
            {
                if (instructions[instructionIndex].OpCode != OpCodes.Call ||
                    instructions[instructionIndex].Operand is not MethodReference calledMethod ||
                    !TryMatchAvaloniaLoaderCall(calledMethod, out var hasServiceProvider) ||
                    !MatchThisCall(instructions, instructionIndex - 1))
                {
                    continue;
                }

                accumulator.MatchedLoaderCallCount++;

                var replacementMethod = hasServiceProvider
                    ? initializerWithServiceProvider
                    : initializerWithoutServiceProvider;
                if (replacementMethod is null)
                {
                    if (hasSourceGeneratedPopulateMethod)
                    {
                        accumulator.ErrorMessages.Add(
                            "Found '" + AvaloniaLoaderTypeName + ".Load' call in '" + method.FullName +
                            "' but the AXSG-generated initializer overload '" + GeneratedInitializerMethodName +
                            "' was not emitted for '" + type.FullName + "'.");
                    }

                    continue;
                }

                instructions[instructionIndex].Operand = replacementMethod;
                accumulator.RewrittenCallCount++;
            }
        }

        foreach (var nestedType in type.NestedTypes)
        {
            RewriteType(nestedType, accumulator);
        }
    }

    private static MethodDefinition? FindGeneratedInitializer(TypeDefinition type, bool hasServiceProvider)
    {
        foreach (var method in type.Methods)
        {
            if (!method.IsStatic || method.Name != GeneratedInitializerMethodName)
            {
                continue;
            }

            if (!hasServiceProvider &&
                method.Parameters.Count == 1 &&
                string.Equals(method.Parameters[0].ParameterType.FullName, type.FullName, StringComparison.Ordinal))
            {
                return method;
            }

            if (hasServiceProvider &&
                method.Parameters.Count == 2 &&
                string.Equals(method.Parameters[0].ParameterType.FullName, ServiceProviderTypeName, StringComparison.Ordinal) &&
                string.Equals(method.Parameters[1].ParameterType.FullName, type.FullName, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }

    private static bool TryMatchAvaloniaLoaderCall(MethodReference method, out bool hasServiceProvider)
    {
        hasServiceProvider = false;

        if (!string.Equals(method.Name, "Load", StringComparison.Ordinal) ||
            !string.Equals(method.DeclaringType.FullName, AvaloniaLoaderTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (method.Parameters.Count == 1 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, ObjectTypeName, StringComparison.Ordinal))
        {
            return true;
        }

        if (method.Parameters.Count == 2 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, ServiceProviderTypeName, StringComparison.Ordinal) &&
            string.Equals(method.Parameters[1].ParameterType.FullName, ObjectTypeName, StringComparison.Ordinal))
        {
            hasServiceProvider = true;
            return true;
        }

        return false;
    }

    private static bool MatchThisCall(Collection<Instruction> instructions, int instructionIndex)
    {
        while (instructionIndex >= 0 && instructions[instructionIndex].OpCode == OpCodes.Nop)
        {
            instructionIndex--;
        }

        if (instructionIndex < 0)
        {
            return false;
        }

        var instruction = instructions[instructionIndex];
        if (instruction.OpCode == OpCodes.Ldarg_0 ||
            (instruction.OpCode == OpCodes.Ldarg && instruction.Operand?.Equals(0) == true))
        {
            return true;
        }

        if (instruction.OpCode == OpCodes.Call &&
            instruction.Operand is GenericInstanceMethod genericMethod &&
            string.Equals(genericMethod.Name, "CheckThis", StringComparison.Ordinal) &&
            string.Equals(
                genericMethod.DeclaringType.FullName,
                "Microsoft.FSharp.Core.LanguagePrimitives/IntrinsicFunctions",
                StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private sealed class AvaloniaLoaderCallWeaverAccumulator
    {
        public int InspectedTypeCount { get; set; }

        public int MatchedLoaderCallCount { get; set; }

        public int RewrittenCallCount { get; set; }

        public List<string> ErrorMessages { get; } = [];

        public AvaloniaLoaderCallWeaverResult ToResult()
        {
            return new AvaloniaLoaderCallWeaverResult(
                InspectedTypeCount,
                MatchedLoaderCallCount,
                RewrittenCallCount,
                ErrorMessages);
        }
    }
}

internal sealed record AvaloniaLoaderCallWeaverResult(
    int InspectedTypeCount,
    int MatchedLoaderCallCount,
    int RewrittenCallCount,
    IReadOnlyList<string> ErrorMessages);
