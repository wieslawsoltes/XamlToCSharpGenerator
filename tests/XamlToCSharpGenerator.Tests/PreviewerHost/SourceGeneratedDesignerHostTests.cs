using System.Runtime.Loader;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Core;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using XamlToCSharpGenerator.Runtime;
using global::Avalonia.Markup.Xaml;
using XamlToCSharpGenerator.Previewer.DesignerHost;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

public class SourceGeneratedDesignerHostTests
{
    [Fact]
    public void Install_Registers_RuntimeLoader_Override_Without_Throwing()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var installerType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedRuntimeXamlLoaderInstaller",
            throwOnError: true)
            ?? throw new InvalidOperationException("Installer type was not found.");
        var installMethod = installerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "Install" && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("Install method was not found.");

        var exception = Record.Exception(() => installMethod.Invoke(null, null));
        if (exception is TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        Assert.Null(exception);
    }

    [Fact]
    public void Install_Avalonia_Mode_With_Preview_Size_Does_Not_Throw()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var installerType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedRuntimeXamlLoaderInstaller",
            throwOnError: true)
            ?? throw new InvalidOperationException("Installer type was not found.");
        var compilerModeType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.PreviewCompilerMode",
            throwOnError: true)
            ?? throw new InvalidOperationException("Preview compiler mode type was not found.");
        var avaloniaMode = Enum.Parse(compilerModeType, "Avalonia");
        var installMethod = installerType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                method.Name == "Install" &&
                method.GetParameters().Length == 3)
            ?? throw new InvalidOperationException("Avalonia overload of Install was not found.");

        var exception = Record.Exception(() => installMethod.Invoke(null, [avaloniaMode, 640d, 480d]));
        if (exception is TargetInvocationException invocationException)
        {
            exception = invocationException.InnerException ?? invocationException;
        }

        Assert.Null(exception);
    }

    [Fact]
    public void ProxyFactory_Invokes_Load_Delegate_Without_MethodAccess_Failure()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var factoryType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.RuntimeXamlLoaderProxyFactory",
            throwOnError: true)
            ?? throw new InvalidOperationException("Proxy factory type was not found.");
        var createMethod = factoryType.GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Create method was not found.");
        var loaderContractType = typeof(AvaloniaXamlLoader).Assembly.GetType(
            "Avalonia.Markup.Xaml.AvaloniaXamlLoader+IRuntimeXamlLoader",
            throwOnError: true)
            ?? throw new InvalidOperationException("Avalonia runtime XAML loader contract was not found.");
        var expected = new object();
        Func<RuntimeXamlLoaderDocument, RuntimeXamlLoaderConfiguration, object> loadHandler =
            (_, _) => expected;

        var proxy = createMethod.Invoke(null, [loaderContractType, loadHandler])
            ?? throw new InvalidOperationException("Create returned null.");
        var loadMethod = loaderContractType.GetMethod(
            "Load",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Load method was not found.");

        var result = loadMethod.Invoke(proxy, [null, null]);

        Assert.Same(expected, result);
    }

    [Fact]
    public void CreateEvaluatorClassName_Returns_Deterministic_Identifier()
    {
        var assembly = Assembly.Load("XamlToCSharpGenerator.Previewer.DesignerHost");
        var runtimeType = assembly.GetType(
            "XamlToCSharpGenerator.Previewer.DesignerHost.SourceGeneratedPreviewMarkupRuntime",
            throwOnError: true)
            ?? throw new InvalidOperationException("Preview markup runtime type was not found.");
        var createNameMethod = runtimeType.GetMethod(
            "CreateEvaluatorClassName",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("CreateEvaluatorClassName was not found.");

        var result = (string?)createNameMethod.Invoke(null, ["source.Quantity + 1"])
            ?? throw new InvalidOperationException("CreateEvaluatorClassName returned null.");

        Assert.StartsWith("__AXSGPreviewExpr_", result, StringComparison.Ordinal);
        Assert.Matches("^__AXSGPreviewExpr_[A-F0-9]+$", result);
        Assert.Equal(
            result,
            (string?)createNameMethod.Invoke(null, ["source.Quantity + 1"]));
    }

    [Fact]
    public void PreviewMarkupRuntime_Uses_Collectible_Load_Context_For_Evaluators()
    {
        SourceGeneratedPreviewMarkupRuntime.ClearEvaluatorCacheForTests();
        try
        {
            var loadContext = SourceGeneratedPreviewMarkupRuntime.GetEvaluatorLoadContextForTests("source.Quantity + 1");

            Assert.NotNull(loadContext);
            Assert.True(loadContext!.IsCollectible);
        }
        finally
        {
            SourceGeneratedPreviewMarkupRuntime.ClearEvaluatorCacheForTests();
        }
    }

    [Fact]
    public void PreviewMarkupRuntime_Bounds_Evaluator_Cache_And_Evicts_Oldest_Entries()
    {
        SourceGeneratedPreviewMarkupRuntime.ClearEvaluatorCacheForTests();
        try
        {
            var firstLoadContext = SourceGeneratedPreviewMarkupRuntime.GetEvaluatorLoadContextForTests("source.Quantity + 0");

            for (var index = 1; index <= SourceGeneratedPreviewMarkupRuntime.MaxCachedEvaluatorCount; index++)
            {
                _ = SourceGeneratedPreviewMarkupRuntime.GetEvaluatorLoadContextForTests($"source.Quantity + {index}");
            }

            Assert.Equal(
                SourceGeneratedPreviewMarkupRuntime.MaxCachedEvaluatorCount,
                SourceGeneratedPreviewMarkupRuntime.GetCachedEvaluatorCountForTests());

            var reloadedFirstContext = SourceGeneratedPreviewMarkupRuntime.GetEvaluatorLoadContextForTests("source.Quantity + 0");

            Assert.NotNull(firstLoadContext);
            Assert.NotNull(reloadedFirstContext);
            Assert.NotSame(firstLoadContext, reloadedFirstContext);
            Assert.IsAssignableFrom<AssemblyLoadContext>(reloadedFirstContext);
        }
        finally
        {
            SourceGeneratedPreviewMarkupRuntime.ClearEvaluatorCacheForTests();
        }
    }

    [AvaloniaFact]
    public void PreviewMarkupRuntime_Returns_Binding_For_StyledElement_ClrProperty_Targets()
    {
        var runtime = new SourceGeneratedPreviewMarkupRuntime();
        var target = new TextBlock();
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<TextBlock, string?>(
            nameof(TextBlock.Text),
            static (textBlock, value) => textBlock.Text = value);
        var provider = CreateProvideValueServiceProvider(new UserControl(), target, propertyInfo);
        var value = runtime.ProvideValue(
            code: "source.ProductName",
            codeBase64Url: null,
            dependencyNamesBase64Url: PreviewMarkupValueCodec.EncodeBase64Url("ProductName"),
            serviceProvider: provider);

        Assert.IsAssignableFrom<IBinding>(value);
    }

    [AvaloniaFact]
    public void PreviewMarkupRuntime_Returns_Binding_For_Expression_Payloads_On_ClrProperty_Targets()
    {
        var runtime = new SourceGeneratedPreviewMarkupRuntime();
        var target = new Button();
        var propertyInfo = SourceGenProvideValueTargetPropertyFactory.CreateWritable<Button, object?>(
            nameof(ContentControl.Content),
            static (button, value) => button.Content = value);
        var provider = CreateProvideValueServiceProvider(new UserControl(), target, propertyInfo);
        var value = runtime.ProvideValue(
            code: "source.FirstName + \" \" + source.LastName",
            codeBase64Url: null,
            dependencyNamesBase64Url: PreviewMarkupValueCodec.EncodeBase64Url("FirstName\nLastName"),
            serviceProvider: provider);

        Assert.IsAssignableFrom<IBinding>(value);
    }

    [AvaloniaFact]
    public void PreviewMarkupRuntime_LiveLoader_Uses_ClrProperty_Target_Metadata()
    {
        object? capturedTargetProperty = null;
        object? capturedTargetObject = null;
        object? capturedRootObject = null;

        SourceGenPreviewMarkupRuntime.Install((_, _, _, serviceProvider) =>
        {
            var provideValueTarget = Assert.IsAssignableFrom<IProvideValueTarget>(
                serviceProvider.GetService(typeof(IProvideValueTarget)));
            var rootProvider = Assert.IsAssignableFrom<IRootObjectProvider>(
                serviceProvider.GetService(typeof(IRootObjectProvider)));

            capturedTargetProperty = provideValueTarget.TargetProperty;
            capturedTargetObject = provideValueTarget.TargetObject;
            capturedRootObject = rootProvider.RootObject;
            return "Widget";
        });

        try
        {
            var root = new UserControl
            {
                DataContext = new PreviewMarkupTestViewModel
                {
                    ProductName = "Widget"
                }
            };

            var result = AvaloniaRuntimeXamlLoader.Load(
                new RuntimeXamlLoaderDocument(
                    new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
                    root,
                    BuildRuntimePreviewXaml()),
                new RuntimeXamlLoaderConfiguration
                {
                    LocalAssembly = typeof(SourceGeneratedDesignerHostTests).Assembly
                });

            Assert.Same(root, result);
            Assert.Same(root, capturedRootObject);
            Assert.IsType<TextBlock>(capturedTargetObject);
            Assert.IsAssignableFrom<IPropertyInfo>(capturedTargetProperty);
        }
        finally
        {
            SourceGenPreviewMarkupRuntime.Uninstall();
        }
    }

    [AvaloniaFact]
    public void PreviewMarkupRuntime_LiveLoader_Renders_Rewritten_InlineCode_And_ExpressionMarkup()
    {
        SourceGenPreviewMarkupRuntime.Install(new SourceGeneratedPreviewMarkupRuntime().ProvideValue);

        try
        {
            var root = new UserControl
            {
                DataContext = new PreviewMarkupTestViewModel
                {
                    ProductName = "Widget",
                    FirstName = "Ada",
                    LastName = "Lovelace"
                }
            };

            var rewrittenXaml = SourceGeneratedPreviewXamlPreprocessor.Rewrite(
                BuildAuthoredPreviewXaml(),
                typeof(SourceGeneratedDesignerHostTests).Assembly);

            var result = AvaloniaRuntimeXamlLoader.Load(
                new RuntimeXamlLoaderDocument(
                    new Uri("avares://XamlToCSharpGenerator.Tests/Preview.axaml"),
                    root,
                    rewrittenXaml),
                new RuntimeXamlLoaderConfiguration
                {
                    LocalAssembly = typeof(SourceGeneratedDesignerHostTests).Assembly
                });

            Assert.Same(root, result);

            var panel = Assert.IsType<StackPanel>(root.Content);
            var textBlock = Assert.IsType<TextBlock>(panel.Children[0]);
            var button = Assert.IsType<Button>(panel.Children[1]);

            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Widget", textBlock.Text);
            Assert.Equal("Ada Lovelace", button.Content);
        }
        finally
        {
            SourceGenPreviewMarkupRuntime.Uninstall();
        }
    }

    private static IServiceProvider CreateProvideValueServiceProvider(
        object rootObject,
        object targetObject,
        object targetProperty)
    {
        var services = new Dictionary<Type, object>
        {
            [typeof(IProvideValueTarget)] = new TestProvideValueTarget(targetObject, targetProperty),
            [typeof(IRootObjectProvider)] = new TestRootObjectProvider(rootObject)
        };
        return new DictionaryServiceProvider(services);
    }

    private sealed class DictionaryServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return services.TryGetValue(serviceType, out var service)
                ? service
                : null;
        }
    }

    private sealed class TestProvideValueTarget(object targetObject, object targetProperty) : IProvideValueTarget
    {
        public object TargetObject => targetObject;

        public object TargetProperty => targetProperty;
    }

    private sealed class TestRootObjectProvider(object rootObject) : IRootObjectProvider
    {
        public object RootObject => rootObject;

        public object IntermediateRootObject => rootObject;
    }

    private static string BuildRuntimePreviewXaml()
    {
        return """
               <UserControl xmlns="https://github.com/avaloniaui"
                            xmlns:preview="clr-namespace:XamlToCSharpGenerator.Runtime.Markup;assembly=XamlToCSharpGenerator.Runtime.Avalonia">
                   <TextBlock Text="{preview:CSharp Code=source.ProductName}" />
               </UserControl>
               """;
    }

    private static string BuildAuthoredPreviewXaml()
    {
        return """
               <UserControl xmlns="https://github.com/avaloniaui"
                            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                            xmlns:local="clr-namespace:XamlToCSharpGenerator.Tests.PreviewerHost;assembly=XamlToCSharpGenerator.Tests"
                            x:DataType="local:PreviewMarkupTestViewModel">
                   <StackPanel>
                       <TextBlock Text="{CSharp Code=source.ProductName}" />
                       <Button Content="{= FirstName + ' ' + LastName}" />
                   </StackPanel>
               </UserControl>
               """;
    }
}

public sealed class PreviewMarkupTestViewModel : System.ComponentModel.INotifyPropertyChanged
{
    private string? _productName;
    private string? _firstName;
    private string? _lastName;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string? ProductName
    {
        get => _productName;
        set
        {
            if (string.Equals(_productName, value, StringComparison.Ordinal))
            {
                return;
            }

            _productName = value;
            PropertyChanged?.Invoke(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(ProductName)));
        }
    }

    public string? FirstName
    {
        get => _firstName;
        set
        {
            if (string.Equals(_firstName, value, StringComparison.Ordinal))
            {
                return;
            }

            _firstName = value;
            PropertyChanged?.Invoke(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(FirstName)));
        }
    }

    public string? LastName
    {
        get => _lastName;
        set
        {
            if (string.Equals(_lastName, value, StringComparison.Ordinal))
            {
                return;
            }

            _lastName = value;
            PropertyChanged?.Invoke(
                this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(LastName)));
        }
    }
}
