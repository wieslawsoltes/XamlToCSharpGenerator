using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class RuntimeXamlLoaderProxyFactory
{
    private static readonly object Sync = new();
    private static Type? s_proxyType;
    private static Type? s_delegateType;

    public static object Create(
        Type interfaceType,
        Func<RuntimeXamlLoaderDocument, RuntimeXamlLoaderConfiguration, object> loadHandler)
    {
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(loadHandler);

        if (!interfaceType.IsInterface)
        {
            throw new ArgumentException("Runtime loader contract must be an interface.", nameof(interfaceType));
        }

        lock (Sync)
        {
            s_delegateType ??= BuildDelegateType(interfaceType);
            s_proxyType ??= BuildProxyType(interfaceType, s_delegateType);
            return Activator.CreateInstance(s_proxyType, loadHandler)
                ?? throw new InvalidOperationException("Failed to create runtime XAML loader proxy.");
        }
    }

    private static Type BuildDelegateType(Type interfaceType)
    {
        var interfaceMethod = interfaceType.GetMethod("Load", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("IRuntimeXamlLoader.Load method was not found.");
        var parameterTypes = interfaceMethod.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        if (parameterTypes.Length != 2)
        {
            throw new InvalidOperationException("IRuntimeXamlLoader.Load signature is not supported.");
        }

        return typeof(Func<,,>).MakeGenericType(
            parameterTypes[0],
            parameterTypes[1],
            interfaceMethod.ReturnType);
    }

    private static Type BuildProxyType(Type interfaceType, Type delegateType)
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
            "_loadHandler",
            delegateType,
            FieldAttributes.Private | FieldAttributes.InitOnly);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [delegateType]);
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
            delegateType.GetMethod("Invoke", BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Runtime XAML loader delegate Invoke method was not found."));
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
