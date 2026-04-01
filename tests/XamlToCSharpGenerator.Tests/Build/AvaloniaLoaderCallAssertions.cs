using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace XamlToCSharpGenerator.Tests.Build;

internal static class AvaloniaLoaderCallAssertions
{
    public static void AssertMethodCallsGeneratedInitializer(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        int explicitParameterCount,
        IReadOnlyList<string> expectedInitializerParameterTypes)
    {
        var type = assembly.MainModule.Types.Single(candidate => candidate.FullName == typeFullName);
        var method = type.Methods.Single(candidate =>
            candidate.Name == methodName &&
            candidate.Parameters.Count == explicitParameterCount);
        var calledMethods = method.Body.Instructions
            .Where(static instruction => instruction.OpCode == OpCodes.Call)
            .Select(static instruction => instruction.Operand as MethodReference)
            .Where(static methodReference => methodReference is not null)
            .Cast<MethodReference>()
            .ToArray();

        Assert.Contains(
            calledMethods,
            calledMethod =>
                string.Equals(calledMethod.Name, "__InitializeXamlSourceGenComponent", StringComparison.Ordinal) &&
                string.Equals(calledMethod.DeclaringType.FullName, typeFullName, StringComparison.Ordinal) &&
                calledMethod.Parameters.Count == expectedInitializerParameterTypes.Count &&
                calledMethod.Parameters.Select(static parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(expectedInitializerParameterTypes));
    }

    public static void AssertMethodCallsAvaloniaLoader(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        int explicitParameterCount,
        IReadOnlyList<string> expectedLoaderParameterTypes)
    {
        var type = assembly.MainModule.Types.Single(candidate => candidate.FullName == typeFullName);
        var method = type.Methods.Single(candidate =>
            candidate.Name == methodName &&
            candidate.Parameters.Count == explicitParameterCount);
        var calledMethods = method.Body.Instructions
            .Where(static instruction => instruction.OpCode == OpCodes.Call)
            .Select(static instruction => instruction.Operand as MethodReference)
            .Where(static methodReference => methodReference is not null)
            .Cast<MethodReference>()
            .ToArray();

        Assert.Contains(
            calledMethods,
            calledMethod =>
                string.Equals(calledMethod.Name, "Load", StringComparison.Ordinal) &&
                string.Equals(calledMethod.DeclaringType.FullName, "Avalonia.Markup.Xaml.AvaloniaXamlLoader", StringComparison.Ordinal) &&
                calledMethod.Parameters.Count == expectedLoaderParameterTypes.Count &&
                calledMethod.Parameters.Select(static parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(expectedLoaderParameterTypes));
    }

    public static void AssertMethodCallsSourceGeneratedUriLoader(
        AssemblyDefinition assembly,
        string typeFullName,
        string methodName,
        int explicitParameterCount,
        IReadOnlyList<string> expectedLoaderParameterTypes)
    {
        var type = assembly.MainModule.Types.Single(candidate => candidate.FullName == typeFullName);
        var method = type.Methods.Single(candidate =>
            candidate.Name == methodName &&
            candidate.Parameters.Count == explicitParameterCount);
        var calledMethods = method.Body.Instructions
            .Where(static instruction => instruction.OpCode == OpCodes.Call)
            .Select(static instruction => instruction.Operand as MethodReference)
            .Where(static methodReference => methodReference is not null)
            .Cast<MethodReference>()
            .ToArray();

        Assert.Contains(
            calledMethods,
            calledMethod =>
                string.Equals(calledMethod.Name, "Load", StringComparison.Ordinal) &&
                string.Equals(calledMethod.DeclaringType.FullName, "XamlToCSharpGenerator.Runtime.AvaloniaSourceGeneratedXamlLoader", StringComparison.Ordinal) &&
                calledMethod.Parameters.Count == expectedLoaderParameterTypes.Count &&
                calledMethod.Parameters.Select(static parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(expectedLoaderParameterTypes));
    }

    public static void AssertNoAvaloniaLoaderCallsRemain(AssemblyDefinition assembly, string targetNamespace)
    {
        foreach (var type in EnumerateTypes(assembly.MainModule.Types))
        {
            if (!string.Equals(type.Namespace, targetNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var method in type.Methods.Where(static candidate => candidate.HasBody))
            {
                Assert.DoesNotContain(
                    method.Body.Instructions,
                    instruction =>
                        instruction.OpCode == OpCodes.Call &&
                        instruction.Operand is MethodReference calledMethod &&
                        string.Equals(calledMethod.Name, "Load", StringComparison.Ordinal) &&
                        string.Equals(
                            calledMethod.DeclaringType.FullName,
                            "Avalonia.Markup.Xaml.AvaloniaXamlLoader",
                            StringComparison.Ordinal));
            }
        }
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
}
