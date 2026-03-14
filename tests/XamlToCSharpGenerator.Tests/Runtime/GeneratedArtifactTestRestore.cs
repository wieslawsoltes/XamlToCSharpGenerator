using System;
using System.Reflection;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

internal static class GeneratedArtifactTestRestore
{
    private const string RegistrationMethodName = "__RegisterXamlSourceGenArtifacts";

    public static void RestoreAllLoadedGeneratedArtifacts()
    {
        XamlSourceGenRegistry.Clear();
        XamlSourceInfoRegistry.Clear();
        XamlResourceRegistry.Clear();
        XamlControlThemeRegistry.Clear();
        XamlIncludeGraphRegistry.Clear();
        XamlSourceGenArtifactRefreshRegistry.Clear();
        XamlSourceGenTypeUriRegistry.Clear();

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (var assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            var assembly = assemblies[assemblyIndex];
            if (assembly.IsDynamic)
            {
                continue;
            }

            var types = GetLoadableTypes(assembly);
            for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
            {
                var type = types[typeIndex];
                if (type is null)
                {
                    continue;
                }

                var registrationMethod = type.GetMethod(
                    RegistrationMethodName,
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (registrationMethod is null ||
                    registrationMethod.ReturnType != typeof(void) ||
                    registrationMethod.GetParameters().Length != 0)
                {
                    continue;
                }

                registrationMethod.Invoke(obj: null, parameters: null);
            }
        }
    }

    private static Type?[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }
}
