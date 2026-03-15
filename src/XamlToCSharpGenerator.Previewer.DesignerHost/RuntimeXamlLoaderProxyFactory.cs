using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class RuntimeXamlLoaderProxyFactory
{
    private static readonly object Sync = new();
    private static Type? s_proxyType;

    public static object Create(Type interfaceType, SourceGeneratedRuntimeXamlLoader target)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(target);

        if (!interfaceType.IsInterface)
        {
            throw new ArgumentException("Runtime loader contract must be an interface.", nameof(interfaceType));
        }

        lock (Sync)
        {
            s_proxyType ??= BuildProxyType(interfaceType);
            return Activator.CreateInstance(s_proxyType, target)
                ?? throw new InvalidOperationException("Failed to create runtime XAML loader proxy.");
        }
    }

    private static Type BuildProxyType(Type interfaceType)
    {
        var assemblyName = new AssemblyName("XamlToCSharpGenerator.Previewer.DesignerHost.Dynamic");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        ApplyIgnoresAccessChecksTo(assemblyBuilder, interfaceType.Assembly);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("Main");
        var typeBuilder = moduleBuilder.DefineType(
            "RuntimeXamlLoaderProxy",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);

        typeBuilder.AddInterfaceImplementation(interfaceType);

        var targetField = typeBuilder.DefineField(
            "_target",
            typeof(SourceGeneratedRuntimeXamlLoader),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [typeof(SourceGeneratedRuntimeXamlLoader)]);
        var ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, targetField);
        ctorIl.Emit(OpCodes.Ret);

        var interfaceMethod = interfaceType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IRuntimeXamlLoader.Load method was not found.");
        var parameterTypes = interfaceMethod.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        var proxyMethod = typeBuilder.DefineMethod(
            interfaceMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            interfaceMethod.ReturnType,
            parameterTypes);
        var proxyIl = proxyMethod.GetILGenerator();
        proxyIl.Emit(OpCodes.Ldarg_0);
        proxyIl.Emit(OpCodes.Ldfld, targetField);
        proxyIl.Emit(OpCodes.Ldarg_1);
        proxyIl.Emit(OpCodes.Ldarg_2);
        proxyIl.Emit(
            OpCodes.Callvirt,
            typeof(SourceGeneratedRuntimeXamlLoader).GetMethod(nameof(SourceGeneratedRuntimeXamlLoader.Load), parameterTypes)
            ?? throw new InvalidOperationException("SourceGeneratedRuntimeXamlLoader.Load method was not found."));
        proxyIl.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(proxyMethod, interfaceMethod);

        return typeBuilder.CreateType()
            ?? throw new InvalidOperationException("Failed to build runtime XAML loader proxy type.");
    }

    private static void ApplyIgnoresAccessChecksTo(AssemblyBuilder assemblyBuilder, Assembly targetAssembly)
    {
        var targetAssemblyName = targetAssembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(targetAssemblyName))
        {
            throw new InvalidOperationException("Target assembly name is unavailable for access-check override.");
        }

        var attributeConstructor = typeof(IgnoresAccessChecksToAttribute).GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("IgnoresAccessChecksToAttribute constructor was not found.");
        var attributeBuilder = new CustomAttributeBuilder(attributeConstructor, [targetAssemblyName]);
        assemblyBuilder.SetCustomAttribute(attributeBuilder);
    }
}
