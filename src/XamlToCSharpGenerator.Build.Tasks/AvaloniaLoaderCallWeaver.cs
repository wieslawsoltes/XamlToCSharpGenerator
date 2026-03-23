using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;
using Mono.Collections.Generic;

namespace XamlToCSharpGenerator.Build.Tasks;

internal sealed class AvaloniaLoaderCallWeaver
{
    private const string AvaloniaLoaderTypeName = "Avalonia.Markup.Xaml.AvaloniaXamlLoader";
    private const string GeneratedInitializerMethodName = "__InitializeXamlSourceGenComponent";
    private const string SourceGeneratedPopulateMethodName = "__PopulateGeneratedObjectGraph";
    private const string ServiceProviderTypeName = "System.IServiceProvider";
    private const string ObjectTypeName = "System.Object";

    public AvaloniaLoaderCallWeaverResult Rewrite(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        if (configuration is null)
        {
            throw new ArgumentNullException(nameof(configuration));
        }

        if (string.IsNullOrWhiteSpace(configuration.AssemblyPath))
        {
            throw new ArgumentException("Assembly path must not be null or whitespace.", nameof(configuration));
        }

        if (!File.Exists(configuration.AssemblyPath))
        {
            throw new FileNotFoundException("Assembly to weave was not found.", configuration.AssemblyPath);
        }

        var symbolHandling = DetermineSymbolHandling(configuration);
        var readerParameters = CreateReaderParameters(symbolHandling);

        using var assembly = AssemblyDefinition.ReadAssembly(configuration.AssemblyPath, readerParameters);
        var accumulator = new AvaloniaLoaderCallWeaverAccumulator();

        foreach (var type in assembly.MainModule.Types)
        {
            RewriteType(type, accumulator);
        }

        var result = accumulator.ToResult();
        if (result.FatalErrorMessages.Count > 0 ||
            (configuration.FailOnMissingGeneratedInitializer && result.MissingInitializerMessages.Count > 0) ||
            result.RewrittenCallCount == 0)
        {
            return result;
        }

        var writerParameters = CreateWriterParameters(symbolHandling);
        if (!TryConfigureStrongName(configuration, assembly, writerParameters, accumulator))
        {
            return accumulator.ToResult();
        }

        assembly.Write(configuration.AssemblyPath, writerParameters);
        return accumulator.ToResult();
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
                        accumulator.MissingInitializerMessages.Add(
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

        public List<string> MissingInitializerMessages { get; } = [];

        public List<string> FatalErrorMessages { get; } = [];

        public AvaloniaLoaderCallWeaverResult ToResult()
        {
            return new AvaloniaLoaderCallWeaverResult(
                InspectedTypeCount,
                MatchedLoaderCallCount,
                RewrittenCallCount,
                MissingInitializerMessages,
                FatalErrorMessages);
        }
    }

    private static ReaderParameters CreateReaderParameters(AvaloniaLoaderSymbolHandling symbolHandling)
    {
        return new ReaderParameters
        {
            InMemory = true,
            ReadSymbols = symbolHandling != AvaloniaLoaderSymbolHandling.None,
            SymbolReaderProvider = CreateSymbolReaderProvider(symbolHandling)
        };
    }

    private static WriterParameters CreateWriterParameters(AvaloniaLoaderSymbolHandling symbolHandling)
    {
        return new WriterParameters
        {
            WriteSymbols = symbolHandling != AvaloniaLoaderSymbolHandling.None,
            SymbolWriterProvider = CreateSymbolWriterProvider(symbolHandling)
        };
    }

    private static ISymbolReaderProvider? CreateSymbolReaderProvider(AvaloniaLoaderSymbolHandling symbolHandling)
    {
        return symbolHandling switch
        {
            AvaloniaLoaderSymbolHandling.EmbeddedPortablePdb => new EmbeddedPortablePdbReaderProvider(),
            AvaloniaLoaderSymbolHandling.SidecarPdb => new PdbReaderProvider(),
            _ => null
        };
    }

    private static ISymbolWriterProvider? CreateSymbolWriterProvider(AvaloniaLoaderSymbolHandling symbolHandling)
    {
        return symbolHandling switch
        {
            AvaloniaLoaderSymbolHandling.EmbeddedPortablePdb => new EmbeddedPortablePdbWriterProvider(),
            AvaloniaLoaderSymbolHandling.SidecarPdb => new PdbWriterProvider(),
            _ => null
        };
    }

    private static AvaloniaLoaderSymbolHandling DetermineSymbolHandling(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        if (!configuration.DebugSymbols)
        {
            return AvaloniaLoaderSymbolHandling.None;
        }

        var debugType = configuration.DebugType ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(debugType) &&
            debugType.IndexOf("embedded", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AvaloniaLoaderSymbolHandling.EmbeddedPortablePdb;
        }

        var pdbPath = Path.ChangeExtension(configuration.AssemblyPath, ".pdb");
        return File.Exists(pdbPath)
            ? AvaloniaLoaderSymbolHandling.SidecarPdb
            : AvaloniaLoaderSymbolHandling.None;
    }

    private static bool TryConfigureStrongName(
        AvaloniaLoaderCallWeaverConfiguration configuration,
        AssemblyDefinition assembly,
        WriterParameters writerParameters,
        AvaloniaLoaderCallWeaverAccumulator accumulator)
    {
        if (!assembly.Name.HasPublicKey)
        {
            return true;
        }

        var keyFilePath = ResolveKeyFilePath(configuration.ProjectDirectory, configuration.AssemblyOriginatorKeyFile);
        if (!string.IsNullOrWhiteSpace(keyFilePath))
        {
            if (!File.Exists(keyFilePath))
            {
                accumulator.FatalErrorMessages.Add(
                    "Refused to rewrite signed assembly '" + configuration.AssemblyPath +
                    "' because the strong-name key file '" + keyFilePath + "' was not found.");
                return false;
            }

            writerParameters.StrongNameKeyBlob = File.ReadAllBytes(keyFilePath);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(configuration.KeyContainerName))
        {
            writerParameters.StrongNameKeyContainer = configuration.KeyContainerName;
            return true;
        }

        accumulator.FatalErrorMessages.Add(
            "Refused to rewrite signed assembly '" + configuration.AssemblyPath +
            "' because no strong-name key file or key container was provided to re-sign the rewritten output.");
        return false;
    }

    private static string? ResolveKeyFilePath(string? projectDirectory, string? keyFilePath)
    {
        if (string.IsNullOrWhiteSpace(keyFilePath))
        {
            return null;
        }

        if (Path.IsPathRooted(keyFilePath))
        {
            return keyFilePath;
        }

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            return Path.GetFullPath(Path.Combine(projectDirectory, keyFilePath));
        }

        return Path.GetFullPath(keyFilePath);
    }
}

internal sealed record AvaloniaLoaderCallWeaverResult(
    int InspectedTypeCount,
    int MatchedLoaderCallCount,
    int RewrittenCallCount,
    IReadOnlyList<string> MissingInitializerMessages,
    IReadOnlyList<string> FatalErrorMessages);

internal sealed record AvaloniaLoaderCallWeaverConfiguration(
    string AssemblyPath,
    bool FailOnMissingGeneratedInitializer,
    bool DebugSymbols,
    string? DebugType,
    string? AssemblyOriginatorKeyFile,
    string? KeyContainerName,
    string? ProjectDirectory);

internal enum AvaloniaLoaderSymbolHandling
{
    None,
    SidecarPdb,
    EmbeddedPortablePdb
}
