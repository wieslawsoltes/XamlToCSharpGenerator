using System.Reflection;
using global::Avalonia.Markup.Xaml;

namespace XamlToCSharpGenerator.Previewer.DesignerHost;

internal static class SourceGeneratedRuntimeXamlLoaderInstaller
{
    public static void Install()
    {
        var loaderContractType = typeof(AvaloniaXamlLoader).Assembly.GetType(
            "Avalonia.Markup.Xaml.AvaloniaXamlLoader+IRuntimeXamlLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia runtime XAML loader contract was not found.");

        var proxy = RuntimeXamlLoaderProxyFactory.Create(
            loaderContractType,
            new SourceGeneratedRuntimeXamlLoader().Load);
        var locatorType = Type.GetType("Avalonia.AvaloniaLocator, Avalonia.Base", throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia locator type was not found.");
        var currentMutableProperty = locatorType.GetProperty(
            "CurrentMutable",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("AvaloniaLocator.CurrentMutable was not found.");
        var currentMutable = currentMutableProperty.GetValue(null)
            ?? throw new InvalidOperationException("AvaloniaLocator.CurrentMutable returned null.");
        var bindMethod = currentMutable.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Bind" &&
                                      method.IsGenericMethodDefinition &&
                                      method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("AvaloniaLocator.Bind<T>() was not found.");
        var binding = bindMethod.MakeGenericMethod(loaderContractType).Invoke(currentMutable, null)
            ?? throw new InvalidOperationException("Failed to create Avalonia locator binding.");
        var toConstantMethod = binding.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "ToConstant" &&
                                      method.GetParameters().Length == 1)
            ?? throw new InvalidOperationException("Avalonia locator binding does not expose ToConstant.");
        var closedToConstantMethod = toConstantMethod.IsGenericMethodDefinition
            ? toConstantMethod.MakeGenericMethod(proxy.GetType())
            : toConstantMethod;
        closedToConstantMethod.Invoke(binding, [proxy]);
    }
}
