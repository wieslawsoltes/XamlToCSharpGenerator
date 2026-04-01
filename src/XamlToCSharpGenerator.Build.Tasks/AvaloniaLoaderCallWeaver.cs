using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    private const string UriTypeName = "System.Uri";
    private const string SourceGeneratedRuntimeLoaderAssemblyName = "XamlToCSharpGenerator.Runtime.Avalonia";

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

        var backend = DetermineBackend(configuration);
        return backend switch
        {
            AvaloniaLoaderWeaverBackend.Metadata => RewriteWithMetadataPlan(configuration),
            _ => RewriteWithLegacyCecilScan(configuration)
        };
    }

    private static AvaloniaLoaderWeaverBackend DetermineBackend(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        var backend = configuration.Backend?.Trim();
        if (string.IsNullOrWhiteSpace(backend))
        {
            return AvaloniaLoaderWeaverBackend.Metadata;
        }

        if (string.Equals(backend, "Metadata", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "SystemReflectionMetadata", StringComparison.OrdinalIgnoreCase))
        {
            return AvaloniaLoaderWeaverBackend.Metadata;
        }

        if (string.Equals(backend, "Cecil", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(backend, "MonoCecil", StringComparison.OrdinalIgnoreCase))
        {
            return AvaloniaLoaderWeaverBackend.Cecil;
        }

        throw new InvalidOperationException(
            "Unsupported IL weaving backend '" + configuration.Backend +
            "'. Supported values are 'Metadata' and 'Cecil'.");
    }

    private static AvaloniaLoaderCallWeaverResult RewriteWithMetadataPlan(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        var scanResult = new AvaloniaLoaderMetadataScanner().Scan(configuration.AssemblyPath);
        var accumulator = new AvaloniaLoaderCallWeaverAccumulator
        {
            InspectedTypeCount = scanResult.InspectedTypeCount,
            MatchedLoaderCallCount = scanResult.MatchedLoaderCallCount
        };

        if (scanResult.MethodMatches.Count == 0)
        {
            return accumulator.ToResult();
        }

        var symbolHandling = DetermineSymbolHandling(configuration);
        var readerParameters = CreateReaderParameters(configuration, symbolHandling);

        using var assembly = AssemblyDefinition.ReadAssembly(configuration.AssemblyPath, readerParameters);
        var methodsByToken = EnumerateTypes(assembly.MainModule.Types)
            .SelectMany(static type => type.Methods)
            .ToDictionary(static method => method.MetadataToken.ToInt32());
        var uriLoaderReference = EnsureRuntimeLoaderAssemblyReference(assembly.MainModule, configuration.ReferencePaths);
        var initializerCache = new Dictionary<string, AvaloniaLoaderGeneratedInitializerCacheEntry>(StringComparer.Ordinal);

        foreach (var methodMatch in scanResult.MethodMatches)
        {
            if (!methodsByToken.TryGetValue(methodMatch.MethodMetadataToken, out var method))
            {
                continue;
            }

            RewriteMethodFromMetadataPlan(method, methodMatch, initializerCache, uriLoaderReference, accumulator);
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

    private static AvaloniaLoaderCallWeaverResult RewriteWithLegacyCecilScan(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        var symbolHandling = DetermineSymbolHandling(configuration);
        var readerParameters = CreateReaderParameters(configuration, symbolHandling);

        using var assembly = AssemblyDefinition.ReadAssembly(configuration.AssemblyPath, readerParameters);
        var uriLoaderReference = EnsureRuntimeLoaderAssemblyReference(assembly.MainModule, configuration.ReferencePaths);
        var accumulator = new AvaloniaLoaderCallWeaverAccumulator();

        foreach (var type in assembly.MainModule.Types)
        {
            RewriteType(type, uriLoaderReference, accumulator);
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

    private static void RewriteType(
        TypeDefinition type,
        AssemblyNameReference uriLoaderReference,
        AvaloniaLoaderCallWeaverAccumulator accumulator)
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

                accumulator.MatchedLoaderCallCount++;

                var replacementMethod = GetReplacementMethod(
                    callKind,
                    calledMethod,
                    initializerWithoutServiceProvider,
                    initializerWithServiceProvider,
                    uriLoaderReference,
                    type.Module);
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
            RewriteType(nestedType, uriLoaderReference, accumulator);
        }
    }

    private static void RewriteMethodFromMetadataPlan(
        MethodDefinition method,
        AvaloniaLoaderMethodMatch methodMatch,
        Dictionary<string, AvaloniaLoaderGeneratedInitializerCacheEntry> initializerCache,
        AssemblyNameReference uriLoaderReference,
        AvaloniaLoaderCallWeaverAccumulator accumulator)
    {
        var methodBody = method.Body;
        var instructions = methodBody.Instructions;
        var declaringType = method.DeclaringType;
        var cacheKey = declaringType.FullName;
        if (!initializerCache.TryGetValue(cacheKey, out var initializerCacheEntry))
        {
            initializerCacheEntry = new AvaloniaLoaderGeneratedInitializerCacheEntry(
                FindGeneratedInitializer(declaringType, hasServiceProvider: false),
                FindGeneratedInitializer(declaringType, hasServiceProvider: true),
                declaringType.Methods.Any(static candidate => candidate.Name == SourceGeneratedPopulateMethodName));
            initializerCache[cacheKey] = initializerCacheEntry;
        }

        foreach (var callSite in methodMatch.CallSites)
        {
            var instruction = instructions.FirstOrDefault(candidate => candidate.Offset == callSite.IlOffset);
            if (instruction is null ||
                instruction.OpCode != OpCodes.Call ||
                instruction.Operand is not MethodReference calledMethod ||
                !TryMatchAvaloniaLoaderCall(calledMethod, out var actualCallKind) ||
                actualCallKind != callSite.Kind)
            {
                continue;
            }

            var instructionIndex = instructions.IndexOf(instruction);
            if ((callSite.Kind == AvaloniaLoaderCallKind.ObjectWithoutServiceProvider ||
                 callSite.Kind == AvaloniaLoaderCallKind.ObjectWithServiceProvider) &&
                !MatchThisCall(instructions, instructionIndex - 1))
            {
                continue;
            }

            var replacementMethod = GetReplacementMethod(
                callSite.Kind,
                calledMethod,
                initializerCacheEntry.WithoutServiceProvider,
                initializerCacheEntry.WithServiceProvider,
                uriLoaderReference,
                method.Module);
            if (replacementMethod is null)
            {
                if (initializerCacheEntry.HasSourceGeneratedPopulateMethod)
                {
                    accumulator.MissingInitializerMessages.Add(
                        "Found '" + AvaloniaLoaderTypeName + ".Load' call in '" + method.FullName +
                        "' but the AXSG-generated initializer overload '" + GeneratedInitializerMethodName +
                        "' was not emitted for '" + declaringType.FullName + "'.");
                }

                continue;
            }

            instruction.Operand = replacementMethod;
            accumulator.RewrittenCallCount++;
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

    private static MethodReference? GetReplacementMethod(
        AvaloniaLoaderCallKind callKind,
        MethodReference originalLoaderMethod,
        MethodDefinition? initializerWithoutServiceProvider,
        MethodDefinition? initializerWithServiceProvider,
        AssemblyNameReference uriLoaderReference,
        ModuleDefinition module)
    {
        return callKind switch
        {
            AvaloniaLoaderCallKind.ObjectWithoutServiceProvider => initializerWithoutServiceProvider,
            AvaloniaLoaderCallKind.ObjectWithServiceProvider => initializerWithServiceProvider,
            AvaloniaLoaderCallKind.UriWithoutServiceProvider => CreateUriLoaderMethodReference(
                module,
                uriLoaderReference,
                originalLoaderMethod),
            AvaloniaLoaderCallKind.UriWithServiceProvider => CreateUriLoaderMethodReference(
                module,
                uriLoaderReference,
                originalLoaderMethod),
            _ => null
        };
    }

    private static bool TryMatchAvaloniaLoaderCall(MethodReference method, out AvaloniaLoaderCallKind callKind)
    {
        callKind = default;

        if (!string.Equals(method.Name, "Load", StringComparison.Ordinal) ||
            !string.Equals(method.DeclaringType.FullName, AvaloniaLoaderTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        if (method.Parameters.Count == 1 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, ObjectTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.ObjectWithoutServiceProvider;
            return true;
        }

        if (method.Parameters.Count == 2 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, ServiceProviderTypeName, StringComparison.Ordinal) &&
            string.Equals(method.Parameters[1].ParameterType.FullName, ObjectTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.ObjectWithServiceProvider;
            return true;
        }

        if (method.Parameters.Count == 2 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, UriTypeName, StringComparison.Ordinal) &&
            string.Equals(method.Parameters[1].ParameterType.FullName, UriTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.UriWithoutServiceProvider;
            return true;
        }

        if (method.Parameters.Count == 3 &&
            string.Equals(method.Parameters[0].ParameterType.FullName, ServiceProviderTypeName, StringComparison.Ordinal) &&
            string.Equals(method.Parameters[1].ParameterType.FullName, UriTypeName, StringComparison.Ordinal) &&
            string.Equals(method.Parameters[2].ParameterType.FullName, UriTypeName, StringComparison.Ordinal))
        {
            callKind = AvaloniaLoaderCallKind.UriWithServiceProvider;
            return true;
        }

        return false;
    }

    private static MethodReference CreateUriLoaderMethodReference(
        ModuleDefinition module,
        AssemblyNameReference runtimeAssemblyReference,
        MethodReference originalLoaderMethod)
    {
        var runtimeLoaderType = new TypeReference(
            "XamlToCSharpGenerator.Runtime",
            "AvaloniaSourceGeneratedXamlLoader",
            module,
            runtimeAssemblyReference);

        var uriLoaderMethod = new MethodReference("Load", module.TypeSystem.Object, runtimeLoaderType)
        {
            HasThis = false
        };
        for (var index = 0; index < originalLoaderMethod.Parameters.Count; index++)
        {
            var parameter = originalLoaderMethod.Parameters[index];
            uriLoaderMethod.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        }

        return module.ImportReference(uriLoaderMethod);
    }

    private static AssemblyNameReference EnsureRuntimeLoaderAssemblyReference(
        ModuleDefinition module,
        IReadOnlyList<string>? referencePaths)
    {
        var existingReference = module.AssemblyReferences.FirstOrDefault(reference =>
            string.Equals(reference.Name, SourceGeneratedRuntimeLoaderAssemblyName, StringComparison.Ordinal));
        if (existingReference is not null)
        {
            return existingReference;
        }

        AssemblyNameReference runtimeAssemblyReference;
        var runtimeAssemblyPath = referencePaths?.FirstOrDefault(path =>
            string.Equals(
                Path.GetFileNameWithoutExtension(path),
                SourceGeneratedRuntimeLoaderAssemblyName,
                StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(runtimeAssemblyPath) &&
            File.Exists(runtimeAssemblyPath))
        {
            var assemblyName = AssemblyName.GetAssemblyName(runtimeAssemblyPath);
            runtimeAssemblyReference = new AssemblyNameReference(
                assemblyName.Name ?? SourceGeneratedRuntimeLoaderAssemblyName,
                assemblyName.Version ?? new Version(0, 0, 0, 0))
            {
                Culture = assemblyName.CultureName,
                PublicKeyToken = assemblyName.GetPublicKeyToken()
            };
        }
        else
        {
            runtimeAssemblyReference = new AssemblyNameReference(
                SourceGeneratedRuntimeLoaderAssemblyName,
                new Version(0, 0, 0, 0));
        }

        module.AssemblyReferences.Add(runtimeAssemblyReference);
        return runtimeAssemblyReference;
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

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> rootTypes)
    {
        foreach (var rootType in rootTypes)
        {
            yield return rootType;

            foreach (var nestedType in EnumerateTypes(rootType.NestedTypes))
            {
                yield return nestedType;
            }
        }
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

    private static ReaderParameters CreateReaderParameters(
        AvaloniaLoaderCallWeaverConfiguration configuration,
        AvaloniaLoaderSymbolHandling symbolHandling)
    {
        return new ReaderParameters
        {
            InMemory = true,
            ReadSymbols = symbolHandling != AvaloniaLoaderSymbolHandling.None,
            SymbolReaderProvider = CreateSymbolReaderProvider(symbolHandling),
            AssemblyResolver = CreateAssemblyResolver(configuration)
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

    private static IAssemblyResolver CreateAssemblyResolver(AvaloniaLoaderCallWeaverConfiguration configuration)
    {
        var resolver = new DefaultAssemblyResolver();
        var searchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddSearchDirectory(searchDirectories, Path.GetDirectoryName(configuration.AssemblyPath));
        AddSearchDirectory(searchDirectories, configuration.ProjectDirectory);

        if (configuration.ReferencePaths is not null)
        {
            foreach (var referencePath in configuration.ReferencePaths)
            {
                if (string.IsNullOrWhiteSpace(referencePath))
                {
                    continue;
                }

                if (Directory.Exists(referencePath))
                {
                    AddSearchDirectory(searchDirectories, referencePath);
                    continue;
                }

                AddSearchDirectory(searchDirectories, Path.GetDirectoryName(referencePath));
            }
        }

        foreach (var searchDirectory in searchDirectories)
        {
            resolver.AddSearchDirectory(searchDirectory);
        }

        return resolver;
    }

    private static void AddSearchDirectory(HashSet<string> searchDirectories, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        searchDirectories.Add(Path.GetFullPath(path));
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

        if (configuration.PublicSign || configuration.DelaySign)
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

    private sealed record AvaloniaLoaderGeneratedInitializerCacheEntry(
        MethodDefinition? WithoutServiceProvider,
        MethodDefinition? WithServiceProvider,
        bool HasSourceGeneratedPopulateMethod);

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
    bool PublicSign,
    bool DelaySign,
    string? ProjectDirectory,
    IReadOnlyList<string>? ReferencePaths,
    string? Backend);

internal enum AvaloniaLoaderSymbolHandling
{
    None,
    SidecarPdb,
    EmbeddedPortablePdb
}

internal enum AvaloniaLoaderWeaverBackend
{
    Metadata,
    Cecil
}
