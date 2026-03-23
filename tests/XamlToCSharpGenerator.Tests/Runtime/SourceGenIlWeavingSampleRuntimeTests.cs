using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using XamlToCSharpGenerator.Runtime;
using XamlToCSharpGenerator.Tests.Build;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("BuildSerial")]
public class SourceGenIlWeavingSampleRuntimeTests
{
    [AvaloniaFact]
    public void MainWindow_Woven_Loader_Preserves_HotReload_Registration()
    {
        ResetHotReloadState();

        try
        {
            var sampleAssembly = GetSampleAssembly();
            var mainWindowType = sampleAssembly.GetType("SourceGenIlWeavingSample.MainWindow", throwOnError: true)!;
            var mainWindow = Assert.IsAssignableFrom<Window>(Activator.CreateInstance(mainWindowType));
            var statusText = mainWindow.FindControl<TextBlock>("StatusText");

            Assert.NotNull(statusText);
            Assert.Equal("Woven Loader Active", statusText.Text);

            var trackedDocuments = XamlSourceGenHotReloadManager.GetTrackedDocuments();
            Assert.Contains(
                trackedDocuments,
                document =>
                    string.Equals(document.TrackingType.FullName, "SourceGenIlWeavingSample.MainWindow", StringComparison.Ordinal) &&
                    document.BuildUri is not null &&
                    document.BuildUri.EndsWith("/MainWindow.axaml", StringComparison.Ordinal));
        }
        finally
        {
            XamlSourceGenHotReloadManager.ClearRegistrations();
        }
    }

    [AvaloniaFact]
    public void ServiceProviderPanel_Woven_Loader_Preserves_ServiceProvider_Overload()
    {
        ResetHotReloadState();

        try
        {
            var sampleAssembly = GetSampleAssembly();
            var serviceProviderPanelType = sampleAssembly.GetType("SourceGenIlWeavingSample.ServiceProviderPanel", throwOnError: true)!;
            var serviceProviderPanel = Assert.IsAssignableFrom<UserControl>(
                Activator.CreateInstance(serviceProviderPanelType, new TestServiceProvider()));
            var statusText = serviceProviderPanel.FindControl<TextBlock>("ServiceProviderStatus");

            Assert.NotNull(statusText);
            Assert.Equal("Service Provider Loader Active", statusText.Text);

            var trackedDocuments = XamlSourceGenHotReloadManager.GetTrackedDocuments();
            Assert.Contains(
                trackedDocuments,
                document =>
                    string.Equals(document.TrackingType.FullName, "SourceGenIlWeavingSample.ServiceProviderPanel", StringComparison.Ordinal) &&
                    document.BuildUri is not null &&
                    document.BuildUri.EndsWith("/ServiceProviderPanel.axaml", StringComparison.Ordinal));
        }
        finally
        {
            XamlSourceGenHotReloadManager.ClearRegistrations();
        }
    }

    private static Assembly GetSampleAssembly()
    {
        var assemblyPath = SourceGenIlWeavingSampleBuildHarness.GetDebugAssemblyPath();
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(
                   assembly => string.Equals(assembly.Location, assemblyPath, StringComparison.OrdinalIgnoreCase)) ??
               AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
    }

    private static void ResetHotReloadState()
    {
        XamlSourceGenHotReloadManager.DisableIdePollingFallback();
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Enable();
        AvaloniaSourceGeneratedXamlLoader.Enable();
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
