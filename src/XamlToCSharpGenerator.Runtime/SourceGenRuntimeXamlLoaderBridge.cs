using System;
using System.Reflection;
using System.Reflection.Emit;
using Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenRuntimeXamlLoaderBridge
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void EnsureRegistered()
    {
        lock (Sync)
        {
            if (_registered)
            {
                return;
            }

            var runtimeLoaderInterface = ResolveRuntimeLoaderInterface();
            if (runtimeLoaderInterface is null)
            {
                return;
            }

            var locator = ResolveMutableLocator();
            if (locator is null)
            {
                return;
            }

            var proxyInstance = CreateProxyInstance(runtimeLoaderInterface);
            BindRuntimeLoader(locator, runtimeLoaderInterface, proxyInstance);
            _registered = true;
        }
    }

    public static object DispatchLoad(object document, object configuration)
    {
        if (document is not RuntimeXamlLoaderDocument runtimeDocument)
        {
            throw new ArgumentException(
                "Unexpected runtime loader document type: " + document.GetType().FullName,
                nameof(document));
        }

        if (configuration is not RuntimeXamlLoaderConfiguration runtimeConfiguration)
        {
            throw new ArgumentException(
                "Unexpected runtime loader configuration type: " + configuration.GetType().FullName,
                nameof(configuration));
        }

        return AvaloniaSourceGeneratedXamlLoader.Load(runtimeDocument, runtimeConfiguration);
    }

    private static Type? ResolveRuntimeLoaderInterface()
    {
        return typeof(AvaloniaXamlLoader).GetNestedType(
            "IRuntimeXamlLoader",
            BindingFlags.NonPublic);
    }

    private static object? ResolveMutableLocator()
    {
        var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base", throwOnError: false);
        if (locatorType is null)
        {
            return null;
        }

        var currentMutable = locatorType.GetProperty(
            "CurrentMutable",
            BindingFlags.Public | BindingFlags.Static);
        if (currentMutable?.GetValue(null) is { } mutable)
        {
            return mutable;
        }

        var current = locatorType.GetProperty(
            "Current",
            BindingFlags.Public | BindingFlags.Static);
        return current?.GetValue(null);
    }

    private static void BindRuntimeLoader(object locator, Type runtimeLoaderInterface, object proxyInstance)
    {
        var bindToSelf = locator.GetType().GetMethod("BindToSelf", BindingFlags.Public | BindingFlags.Instance);
        if (bindToSelf is null || !bindToSelf.IsGenericMethodDefinition)
        {
            throw new InvalidOperationException(
                "Unable to locate Avalonia locator generic BindToSelf method.");
        }

        var closedBind = bindToSelf.MakeGenericMethod(runtimeLoaderInterface);
        _ = closedBind.Invoke(locator, [proxyInstance]);
    }

    private static object CreateProxyInstance(Type runtimeLoaderInterface)
    {
        var loadMethod = runtimeLoaderInterface.GetMethod(
            "Load",
            [typeof(RuntimeXamlLoaderDocument), typeof(RuntimeXamlLoaderConfiguration)])
            ?? throw new InvalidOperationException(
                "Runtime loader interface does not expose expected Load method.");

        var assemblyName = new AssemblyName(
            "XamlToCSharpGenerator.Runtime.LoaderBridgeProxy." + Guid.NewGuid().ToString("N"));
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var ignoresAccessChecksCtor = typeof(System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute)
            .GetConstructor([typeof(string)])
            ?? throw new InvalidOperationException("IgnoresAccessChecksToAttribute constructor is missing.");
        var avaloniaMarkupAssemblyName = typeof(AvaloniaXamlLoader).Assembly.GetName().Name
            ?? throw new InvalidOperationException("Unable to resolve Avalonia.Markup.Xaml assembly name.");
        assemblyBuilder.SetCustomAttribute(
            new CustomAttributeBuilder(ignoresAccessChecksCtor, [avaloniaMarkupAssemblyName]));

        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(
            "SourceGenRuntimeXamlLoaderProxy",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed);
        typeBuilder.AddInterfaceImplementation(runtimeLoaderInterface);
        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

        var parameterTypes = new[] { typeof(RuntimeXamlLoaderDocument), typeof(RuntimeXamlLoaderConfiguration) };
        var methodBuilder = typeBuilder.DefineMethod(
            loadMethod.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(object),
            parameterTypes);
        methodBuilder.DefineParameter(1, ParameterAttributes.None, "document");
        methodBuilder.DefineParameter(2, ParameterAttributes.None, "configuration");

        var il = methodBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        var dispatchMethod = typeof(SourceGenRuntimeXamlLoaderBridge).GetMethod(
            nameof(DispatchLoad),
            BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("DispatchLoad method is missing.");
        il.Emit(OpCodes.Call, dispatchMethod);
        il.Emit(OpCodes.Ret);
        typeBuilder.DefineMethodOverride(methodBuilder, loadMethod);

        var proxyType = typeBuilder.CreateType()
            ?? throw new InvalidOperationException("Unable to create runtime loader proxy type.");
        return Activator.CreateInstance(proxyType)
            ?? throw new InvalidOperationException("Unable to instantiate runtime loader proxy type.");
    }
}
