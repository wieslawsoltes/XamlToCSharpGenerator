using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.XamlIl.Runtime;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenMarkupExtensionRuntime
{
    private static readonly ConcurrentDictionary<string, Type?> BindingTypeCache = new(StringComparer.Ordinal);
    private const int MaxDeferredBindingRetryCount = 12;
    private static readonly bool BindingTraceEnabled = IsEnvironmentEnabled("AXSG_BINDING_TRACE");

    public static object? ProvideStaticResource(
        object resourceKey,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var extension = new StaticResourceExtension(resourceKey);
        return ProvideStaticResourceExtension(
            extension,
            targetObject,
            targetProperty,
            baseUri,
            parentStack,
            contextProvider);
    }

    public static T CoerceStaticResourceValue<T>(object? value)
    {
        return CoerceMarkupExtensionValue<T>(value);
    }

    public static T CoerceMarkupExtensionValue<T>(object? value)
    {
        if (ReferenceEquals(value, AvaloniaProperty.UnsetValue) || value is null)
        {
            return default!;
        }

        if (value is IDeferredContent deferredContent)
        {
            var deferredValue = deferredContent.Build(serviceProvider: null);
            if (ReferenceEquals(deferredValue, AvaloniaProperty.UnsetValue) || deferredValue is null)
            {
                return default!;
            }

            if (deferredValue is IDeferredContent)
            {
                return default!;
            }

            if (deferredValue is T deferredTyped)
            {
                return deferredTyped;
            }

            return (T)deferredValue;
        }

        if (value is T typed)
        {
            return typed;
        }

        return (T)value;
    }

    public static object? ProvideRuntimeXamlValue(
        string xaml,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return null;
        }

        var normalizedXaml = NormalizeRuntimeXamlValue(xaml);
        if (string.IsNullOrWhiteSpace(normalizedXaml))
        {
            return null;
        }

        var localAssembly = rootObject?.GetType().Assembly
            ?? intermediateRootObject?.GetType().Assembly
            ?? targetObject?.GetType().Assembly
            ?? typeof(SourceGenMarkupExtensionRuntime).Assembly;
        var localAssemblyName = localAssembly.GetName().Name ?? "sourcegen";
        var resolvedBaseUri = ResolveRuntimeBaseUri(parentServiceProvider, baseUri, localAssemblyName);

        var options = AvaloniaSourceGeneratedXamlLoader.RuntimeCompilationOptions;
        options.EnableRuntimeCompilationFallback = true;
        options.StrictMode = false;

        var configuration = new RuntimeXamlLoaderConfiguration
        {
            LocalAssembly = localAssembly
        };

        var document = new RuntimeXamlLoaderDocument(resolvedBaseUri, rootInstance: null, normalizedXaml)
        {
            ServiceProvider = parentServiceProvider
        };

        return SourceGenRuntimeXamlCompiler.Load(document, configuration, options);
    }

    public static object? ProvideMarkupExtensionValue(
        object? extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        if (extension is null)
        {
            return null;
        }

        object? value;
        if (extension is MarkupExtension markupExtension)
        {
            value = ProvideMarkupExtension(
                markupExtension,
                parentServiceProvider,
                rootObject,
                intermediateRootObject,
                targetObject,
                targetProperty,
                baseUri,
                parentStack);
        }
        else if (extension is OnPlatformExtension onPlatform)
        {
            var platformValue = ProvideOnPlatform(
                onPlatform.Default,
                onPlatform.Windows,
                onPlatform.macOS,
                onPlatform.Linux,
                onPlatform.Android,
                onPlatform.iOS,
                onPlatform.Browser);
            value = platformValue;
        }
        else if (extension is OnFormFactorExtension onFormFactor)
        {
            var formFactorValue = ProvideOnFormFactor(
                onFormFactor.Default,
                onFormFactor.Desktop,
                onFormFactor.Mobile,
                onFormFactor.TV,
                parentServiceProvider);
            value = formFactorValue;
        }
        else
        {
            value = extension;
        }

        return CoerceMarkupExtensionResultForTargetProperty(value, targetProperty);
    }

    private static object? CoerceMarkupExtensionResultForTargetProperty(object? value, object? targetProperty)
    {
        if (value is IDeferredContent deferredContent)
        {
            var deferredValue = deferredContent.Build(serviceProvider: null);
            if (!ReferenceEquals(deferredValue, AvaloniaProperty.UnsetValue) &&
                deferredValue is not null &&
                deferredValue is not IDeferredContent)
            {
                return CoerceMarkupExtensionResultForTargetProperty(deferredValue, targetProperty);
            }

            if (targetProperty is AvaloniaProperty)
            {
                return AvaloniaProperty.UnsetValue;
            }
        }

        if (value is null ||
            targetProperty is not AvaloniaProperty avaloniaProperty)
        {
            return value;
        }

        var targetType = avaloniaProperty.PropertyType;
        if (targetType.IsInstanceOfType(value))
        {
            return value;
        }

        if ((targetType == typeof(Avalonia.Media.IBrush) ||
             targetType == typeof(Avalonia.Media.Brush)) &&
            value is Avalonia.Media.Color colorValue)
        {
            return new Avalonia.Media.Immutable.ImmutableSolidColorBrush(colorValue);
        }

        if (value is not string textValue)
        {
            return value;
        }

        var trimmed = textValue.Trim();
        if (trimmed.Length == 0)
        {
            return value;
        }

        if (targetType == typeof(Avalonia.Media.IBrush) ||
            targetType == typeof(Avalonia.Media.Brush))
        {
            return Avalonia.Media.Brush.Parse(trimmed);
        }

        if (targetType == typeof(Avalonia.Media.Color))
        {
            return Avalonia.Media.Color.Parse(trimmed);
        }

        return value;
    }

    public static IBinding? ProvideDynamicResource(
        object resourceKey,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var extension = new DynamicResourceExtension(resourceKey);
        return extension.ProvideValue(contextProvider);
    }

    public static T? AttachBindingNameScope<T>(T? binding, object? nameScope)
        where T : class
    {
        if (binding is Binding dataBinding && dataBinding.TypeResolver is null)
        {
            dataBinding.TypeResolver = ResolveBindingType;
        }

        if (binding is BindingBase bindingBase &&
            nameScope is INameScope typedNameScope)
        {
            bindingBase.NameScope = new WeakReference<INameScope?>(typedNameScope);
        }

        return binding;
    }

    public static void ApplyBinding(object? target, AvaloniaProperty property, object? value, object? anchor = null)
    {
        if (target is not AvaloniaObject avaloniaObject || property is null)
        {
            return;
        }

        if (value is IBinding binding)
        {
            bool TryApplyBindingNow(string stage)
            {
                if (TryBind(avaloniaObject, property, binding, anchor, out var deferredException))
                {
                    TraceBinding($"Applied binding ({stage}): {DescribeBindingTarget(avaloniaObject, property)}.");
                    return true;
                }

                if (deferredException is not null)
                {
                    TraceBinding($"Deferred binding ({stage}): {DescribeBindingTarget(avaloniaObject, property)}. {deferredException.Message}");
                }

                return false;
            }

            if (TryApplyBindingNow("immediate"))
            {
                return;
            }

            ScheduleBindingRetry(avaloniaObject, property, binding, anchor);

            void AttachDeferredRetryHooks(StyledElement observedElement, Visual? observedVisual, string ownerKind)
            {
                void TryApplyAndDetachHandlers()
                {
                    if (!TryApplyBindingNow("event/" + ownerKind))
                    {
                        return;
                    }

                    observedElement.PropertyChanged -= OnPropertyChanged;
                    if (observedVisual is not null)
                    {
                        observedVisual.AttachedToVisualTree -= OnAttachedToVisualTree;
                    }
                }

                void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
                {
                    if (args.Property != StyledElement.DataContextProperty &&
                        !string.Equals(args.Property.Name, "TemplatedParent", StringComparison.Ordinal))
                    {
                        return;
                    }

                    TryApplyAndDetachHandlers();
                }

                void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs args)
                {
                    TryApplyAndDetachHandlers();
                }

                observedElement.PropertyChanged += OnPropertyChanged;
                if (observedVisual is not null)
                {
                    observedVisual.AttachedToVisualTree += OnAttachedToVisualTree;
                }
            }

            if (target is StyledElement styledTarget)
            {
                AttachDeferredRetryHooks(styledTarget, target as Visual, "target");
            }

            if (anchor is StyledElement styledAnchor &&
                (target is not StyledElement styledTargetReference || !ReferenceEquals(styledAnchor, styledTargetReference)))
            {
                AttachDeferredRetryHooks(styledAnchor, styledAnchor as Visual, "anchor");
            }

            return;
        }

        avaloniaObject.SetValue(property, value);
    }

    public static object? ResolveBindingAnchor(object? target, IReadOnlyList<object>? parentStack)
    {
        if (target is StyledElement)
        {
            return target;
        }

        if (parentStack is null || parentStack.Count == 0)
        {
            return null;
        }

        StyledElement? fallbackAnchor = null;
        for (var index = 0; index < parentStack.Count; index++)
        {
            var candidate = parentStack[index];
            if (ReferenceEquals(candidate, target) || candidate is not StyledElement styledElement)
            {
                continue;
            }

            fallbackAnchor ??= styledElement;

            if (styledElement.TemplatedParent is not null ||
                styledElement is Visual visual && TopLevel.GetTopLevel(visual) is not null)
            {
                return styledElement;
            }
        }

        return fallbackAnchor;
    }

    private static bool IsDataContextUnavailableException(InvalidOperationException exception)
    {
        return exception.Message.IndexOf("DataContext", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsTemplatedParentUnavailableException(InvalidOperationException exception)
    {
        return exception.Message.IndexOf("TemplatedParent", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsDeferredBindingContextException(InvalidOperationException exception)
    {
        return IsDataContextUnavailableException(exception) ||
               IsTemplatedParentUnavailableException(exception);
    }

    private static bool IsTemplatedParentRelativeSource(RelativeSource? relativeSource)
    {
        return relativeSource?.Mode == RelativeSourceMode.TemplatedParent;
    }

    private static bool IsBindingSourceUnset(Binding binding)
    {
        var source = binding.Source;
        return source is null || ReferenceEquals(source, AvaloniaProperty.UnsetValue);
    }

    private static StyledElement? ResolveTemplatedParentAnchor(StyledElement anchor)
    {
        for (StyledElement? current = anchor; current is not null; current = current.Parent as StyledElement)
        {
            if (current.TemplatedParent is not null)
            {
                return current;
            }
        }

        return null;
    }

    private static bool TryPrepareBindingForNonStyledTemplatedParentTarget(
        AvaloniaObject target,
        IBinding binding,
        object? anchor,
        out InvalidOperationException? deferredException)
    {
        deferredException = null;

        if (binding is not Binding dataBinding ||
            !IsTemplatedParentRelativeSource(dataBinding.RelativeSource) ||
            !IsBindingSourceUnset(dataBinding) ||
            dataBinding.ElementName is not null)
        {
            return true;
        }

        StyledElement? styledAnchor = target as StyledElement;
        if (styledAnchor is null && anchor is StyledElement providedAnchor)
        {
            styledAnchor = providedAnchor;
        }

        if (styledAnchor is null)
        {
            TraceBinding(
                $"Deferred templated-parent binding preparation: target={target.GetType().FullName}, anchor=<null>, path={dataBinding.Path}.");
            deferredException = new InvalidOperationException("Cannot find a StyledElement to get a TemplatedParent.");
            return false;
        }

        var templatedParentAnchor = ResolveTemplatedParentAnchor(styledAnchor);
        if (templatedParentAnchor?.TemplatedParent is null &&
            anchor is StyledElement alternateAnchor &&
            !ReferenceEquals(alternateAnchor, styledAnchor))
        {
            templatedParentAnchor = ResolveTemplatedParentAnchor(alternateAnchor);
        }

        if (templatedParentAnchor?.TemplatedParent is null)
        {
            TraceBinding(
                $"Deferred templated-parent binding preparation: target={target.GetType().FullName}, anchor={styledAnchor.GetType().FullName}, path={dataBinding.Path}.");
            deferredException = new InvalidOperationException("Cannot find a StyledElement to get a TemplatedParent.");
            return false;
        }

        // Non-StyledElement targets (e.g., ColumnDefinition) cannot resolve TemplatedParent by themselves.
        // Rewriting to explicit Source keeps binding semantics while making it bindable.
        dataBinding.Source = templatedParentAnchor.TemplatedParent;
        dataBinding.RelativeSource = null;
        TraceBinding(
            $"Rewrote templated-parent binding: target={target.GetType().FullName}, anchor={templatedParentAnchor.GetType().FullName}, source={templatedParentAnchor.TemplatedParent.GetType().FullName}, path={dataBinding.Path}.");
        return true;
    }

    private static void ScheduleBindingRetry(AvaloniaObject target, AvaloniaProperty property, IBinding binding, object? anchor)
    {
        void TryApply(int attempt)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (TryBind(target, property, binding, anchor, out var deferredException))
                {
                    TraceBinding($"Applied binding (retry {attempt + 1}/{MaxDeferredBindingRetryCount}): {DescribeBindingTarget(target, property)}.");
                    return;
                }

                if (deferredException is not null)
                {
                    TraceBinding($"Deferred binding (retry {attempt + 1}/{MaxDeferredBindingRetryCount}): {DescribeBindingTarget(target, property)}. {deferredException.Message}");
                }

                if (attempt + 1 < MaxDeferredBindingRetryCount)
                {
                    var nextAttempt = attempt + 1;
                    var delayMilliseconds = Math.Min(16 * nextAttempt, 120);
                    DispatcherTimer.RunOnce(
                        () => TryApply(nextAttempt),
                        TimeSpan.FromMilliseconds(delayMilliseconds),
                        DispatcherPriority.Background);
                }
            }, attempt == 0 ? DispatcherPriority.Loaded : DispatcherPriority.Background);
        }

        TryApply(0);
    }

    private static bool TryBind(AvaloniaObject target, AvaloniaProperty property, IBinding binding, object? anchor, out InvalidOperationException? deferredException)
    {
        deferredException = null;

        if (!TryPrepareBindingForNonStyledTemplatedParentTarget(target, binding, anchor, out deferredException))
        {
            return false;
        }

        try
        {
            if (anchor is not null)
            {
#pragma warning disable CS0618
                target.Bind(property, binding, anchor);
#pragma warning restore CS0618
            }
            else
            {
                target.Bind(property, binding);
            }

            return true;
        }
        catch (InvalidOperationException exception) when (IsDeferredBindingContextException(exception))
        {
            if (BindingTraceEnabled)
            {
                var relativeSourceMode = binding is Binding deferredBinding && deferredBinding.RelativeSource is not null
                    ? deferredBinding.RelativeSource.Mode.ToString()
                    : "<none>";
                var sourceType = binding is Binding deferredBindingWithSource && deferredBindingWithSource.Source is not null
                    ? deferredBindingWithSource.Source.GetType().FullName
                    : "<null>";
                var sourceIsUnset = binding is Binding deferredBindingWithSourceCheck && IsBindingSourceUnset(deferredBindingWithSourceCheck);
                var elementName = binding is Binding deferredBindingWithElement ? deferredBindingWithElement.ElementName : null;
                TraceBinding(
                    $"Deferred bind details: target={target.GetType().FullName}.{property.Name}, bindingType={binding.GetType().FullName}, relativeSource={relativeSourceMode}, sourceType={sourceType}, sourceIsUnset={sourceIsUnset}, elementName={elementName ?? "<null>"}, anchor={(anchor as object)?.GetType().FullName ?? "<null>"}.");
            }
            deferredException = exception;
            return false;
        }
    }

    private static bool IsEnvironmentEnabled(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void TraceBinding(string message)
    {
        if (!BindingTraceEnabled)
        {
            return;
        }

        Console.WriteLine($"[AXSG.Binding] {message}");
    }

    private static string DescribeBindingTarget(AvaloniaObject target, AvaloniaProperty property)
    {
        return $"{target.GetType().FullName}.{property.Name}";
    }

    public static Avalonia.Media.Imaging.Bitmap? LoadBitmapAsset(string path, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmedPath = path.Trim();
        var uri = trimmedPath.StartsWith("/", StringComparison.Ordinal)
            ? new Uri(trimmedPath, UriKind.Relative)
            : new Uri(trimmedPath, UriKind.RelativeOrAbsolute);

        if (uri.IsAbsoluteUri && uri.IsFile)
        {
            return new Avalonia.Media.Imaging.Bitmap(uri.LocalPath);
        }

        var contextBaseUri = TryCreateAbsoluteUri(baseUri);
        return new Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(uri, contextBaseUri));
    }

    public static WindowIcon? LoadWindowIconAsset(string path, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmedPath = path.Trim();
        var uri = trimmedPath.StartsWith("/", StringComparison.Ordinal)
            ? new Uri(trimmedPath, UriKind.Relative)
            : new Uri(trimmedPath, UriKind.RelativeOrAbsolute);

        if (uri.IsAbsoluteUri && uri.IsFile)
        {
            return new WindowIcon(uri.LocalPath);
        }

        var contextBaseUri = TryCreateAbsoluteUri(baseUri);
        return new WindowIcon(AssetLoader.Open(uri, contextBaseUri));
    }

    public static Avalonia.Media.FontFeatureCollection? ParseFontFeatureCollection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collection = new Avalonia.Media.FontFeatureCollection();
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            var token = parts[index];
            if (token.Length == 0)
            {
                continue;
            }

            collection.Add(Avalonia.Media.FontFeature.Parse(token));
        }

        return collection;
    }

    public static Avalonia.Media.FontFamily ParseFontFamily(string? value, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Avalonia.Media.FontFamily.Default;
        }

        var trimmedValue = value.Trim();
        var resolvedBaseUri = TryCreateAbsoluteUri(baseUri);
        resolvedBaseUri ??= new Uri("avares://sourcegen/");

        try
        {
            return Avalonia.Media.FontFamily.Parse(trimmedValue, resolvedBaseUri);
        }
        catch (ArgumentException)
        {
            // Keep font parsing non-fatal when a family source can't be resolved.
            return Avalonia.Media.FontFamily.Default;
        }
    }

    public static void ApplyClassValue(object? target, string className, object? value)
    {
        if (target is not StyledElement styledElement || string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        var normalizedClassName = className.Trim();
        if (value is IBinding binding)
        {
            styledElement.Bind(StyledElementExtensions.GetClassProperty(normalizedClassName), binding);
            return;
        }

        var enabled = value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsedBool) => parsedBool,
            _ => value is not null
        };

        styledElement.Classes.Set(normalizedClassName, enabled);
    }

    private static Uri? TryCreateAbsoluteUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var resolvedUri)
            ? resolvedUri
            : null;
    }

    private static Type ResolveBindingType(string? xmlNamespace, string name)
    {
        var key = (xmlNamespace ?? string.Empty) + "|" + name;
        var resolvedType = BindingTypeCache.GetOrAdd(key, static cacheKey =>
        {
            var separatorIndex = cacheKey.IndexOf('|');
            var namespacePart = separatorIndex >= 0 ? cacheKey[..separatorIndex] : string.Empty;
            var typeNamePart = separatorIndex >= 0 ? cacheKey[(separatorIndex + 1)..] : cacheKey;
            return ResolveBindingTypeCore(namespacePart, typeNamePart);
        });

        if (resolvedType is not null)
        {
            return resolvedType;
        }

        throw new InvalidOperationException($"Unable to resolve type '{xmlNamespace}:{name}'.");
    }

    private static Type? ResolveBindingTypeCore(string? xmlNamespace, string name)
    {
        return SourceGenKnownTypeRegistry.TryResolve(xmlNamespace, name, out var resolvedType)
            ? resolvedType
            : null;
    }

    public static IBinding? ProvideReflectionBinding(
        ReflectionBindingExtension extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        return extension.ProvideValue(contextProvider);
    }

    public static IBinding ProvideExpressionBinding<TSource>(
        Func<TSource, object?> evaluator,
        IReadOnlyList<string>? dependencyNames,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
        where TSource : class
    {
        _ = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var multiBinding = new MultiBinding
        {
            Converter = new SourceGenExpressionMultiValueConverter<TSource>(evaluator),
            Mode = BindingMode.OneWay
        };

        // First binding carries the source object used by the generated evaluator delegate.
        multiBinding.Bindings.Add(new Binding("."));

        if (dependencyNames is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependencyName in dependencyNames)
            {
                if (string.IsNullOrWhiteSpace(dependencyName))
                {
                    continue;
                }

                var trimmedDependencyName = dependencyName.Trim();
                if (!seen.Add(trimmedDependencyName))
                {
                    continue;
                }

                multiBinding.Bindings.Add(new Binding(trimmedDependencyName));
            }
        }

        return multiBinding;
    }

    public static object? ProvideMarkupExtension(
        object extension,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        return extension switch
        {
            StaticResourceExtension staticResourceExtension => ProvideStaticResourceExtension(
                staticResourceExtension,
                targetObject,
                targetProperty,
                baseUri,
                parentStack,
                contextProvider),
            DynamicResourceExtension dynamicResourceExtension => dynamicResourceExtension.ProvideValue(contextProvider),
            ResolveByNameExtension resolveByNameExtension => resolveByNameExtension.ProvideValue(contextProvider),
            RelativeSourceExtension relativeSourceExtension => relativeSourceExtension.ProvideValue(contextProvider),
            ReflectionBindingExtension reflectionBindingExtension => reflectionBindingExtension.ProvideValue(contextProvider),
            CompiledBindingExtension compiledBindingExtension => compiledBindingExtension.ProvideValue(contextProvider),
            MarkupExtension markupExtension => markupExtension.ProvideValue(contextProvider),
            _ => throw new NotSupportedException(
                $"Unsupported markup extension type '{extension.GetType().FullName}'.")
        };
    }

    public static object? ProvideReference(
        string name,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var extension = new ResolveByNameExtension(name);
        var value = extension.ProvideValue(contextProvider);
        if (ReferenceEquals(value, AvaloniaProperty.UnsetValue))
        {
            return SourceGenNameReferenceHelper.ResolveByName(targetObject, name);
        }

        return value;
    }

    public static object? ProvideOnPlatform(
        object? defaultValue,
        object? windows,
        object? macOs,
        object? linux,
        object? android,
        object? ios,
        object? browser)
    {
        if (OperatingSystem.IsWindows() && windows is not null)
        {
            return windows;
        }

        if (OperatingSystem.IsMacOS() && macOs is not null)
        {
            return macOs;
        }

        if (OperatingSystem.IsLinux() && linux is not null)
        {
            return linux;
        }

        if (OperatingSystem.IsAndroid() && android is not null)
        {
            return android;
        }

        if (OperatingSystem.IsIOS() && ios is not null)
        {
            return ios;
        }

        if (OperatingSystem.IsBrowser() && browser is not null)
        {
            return browser;
        }

        return defaultValue;
    }

    public static object? ProvideOnFormFactor(
        object? defaultValue,
        object? desktop,
        object? mobile,
        object? tv,
        IServiceProvider? parentServiceProvider)
    {
        var serviceProvider = parentServiceProvider ?? EmptyServiceProvider.Instance;
        if (desktop is not null &&
            OnFormFactorExtension.ShouldProvideOption(serviceProvider, FormFactorType.Desktop))
        {
            return desktop;
        }

        if (mobile is not null &&
            OnFormFactorExtension.ShouldProvideOption(serviceProvider, FormFactorType.Mobile))
        {
            return mobile;
        }

        if (defaultValue is null)
        {
            if (desktop is not null &&
                (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()))
            {
                return desktop;
            }

            if (mobile is not null &&
                (OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()))
            {
                return mobile;
            }

            if (tv is not null && desktop is null && mobile is null)
            {
                return tv;
            }

            return desktop ?? mobile ?? tv;
        }

        return defaultValue;
    }

    private static bool TryResolveStaticResourceFallback(
        object resourceKey,
        object targetObject,
        string? baseUri,
        IServiceProvider? serviceProvider,
        IReadOnlyList<object>? parentStack,
        out object? resolved)
    {
        if (resourceKey is null ||
            ReferenceEquals(resourceKey, AvaloniaProperty.UnsetValue))
        {
            resolved = null;
            return false;
        }

        if (SourceGenStaticResourceResolver.TryResolve(
                targetObject,
                resourceKey,
                baseUri,
                out resolved,
                serviceProvider,
                parentStack))
        {
            return true;
        }

        resolved = null;
        return false;
    }

    private static object? ProvideStaticResourceExtension(
        StaticResourceExtension extension,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack,
        IServiceProvider serviceProvider)
    {
        if (ShouldDeferStaticResourceResolution(targetObject, targetProperty))
        {
            return new DeferredStaticResourceContent(
                extension.ResourceKey ?? AvaloniaProperty.UnsetValue,
                targetObject,
                baseUri,
                parentStack,
                serviceProvider);
        }

        return ResolveStaticResourceExtensionImmediate(
            extension,
            targetObject,
            baseUri,
            parentStack,
            serviceProvider);
    }

    private static object? ResolveStaticResourceExtensionImmediate(
        StaticResourceExtension extension,
        object targetObject,
        string? baseUri,
        IReadOnlyList<object>? parentStack,
        IServiceProvider? serviceProvider)
    {
        var effectiveServiceProvider = serviceProvider ?? CreateContextProvider(
            parentServiceProvider: null,
            rootObject: targetObject,
            intermediateRootObject: targetObject,
            targetObject: targetObject,
            targetProperty: null,
            baseUri: baseUri,
            parentStack: parentStack);
        var resourceKey = extension.ResourceKey ?? AvaloniaProperty.UnsetValue;
        var effectiveParentStack = BuildEffectiveParentStack(effectiveServiceProvider, parentStack);

        try
        {
            var value = extension.ProvideValue(effectiveServiceProvider);
            if (!ReferenceEquals(value, AvaloniaProperty.UnsetValue))
            {
                return value;
            }

            if (TryResolveStaticResourceFallback(
                    resourceKey,
                    targetObject,
                    baseUri,
                    effectiveServiceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            // Keep delayed resource-resolution behavior for control target properties.
            return AvaloniaProperty.UnsetValue;
        }
        catch (KeyNotFoundException)
        {
            if (TryResolveStaticResourceFallback(
                    resourceKey,
                    targetObject,
                    baseUri,
                    effectiveServiceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            // Keep resource lookup resilient while merged dictionaries/styles are
            // still materializing; deferred content will re-attempt resolution.
            return new DeferredStaticResourceContent(
                resourceKey,
                targetObject,
                baseUri,
                effectiveParentStack,
                effectiveServiceProvider);
        }
    }

    private static bool ShouldDeferStaticResourceResolution(
        object targetObject,
        object? targetProperty)
    {
        // Dictionary StaticResource aliases depend on ambient owner context
        // (merged dictionaries/theme variants). Resolve them lazily once attached.
        return targetProperty is null && targetObject is IResourceDictionary;
    }

    private static IReadOnlyList<object>? BuildEffectiveParentStack(
        IServiceProvider? parentServiceProvider,
        IReadOnlyList<object>? explicitParentStack)
    {
        List<object>? parents = null;
        HashSet<object>? seen = null;

        void AddParent(object? parent)
        {
            if (parent is null)
            {
                return;
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!seen.Add(parent))
            {
                return;
            }

            parents ??= new List<object>();
            parents.Add(parent);
        }

        if (explicitParentStack is not null)
        {
            for (var index = 0; index < explicitParentStack.Count; index++)
            {
                AddParent(explicitParentStack[index]);
            }
        }

        if (parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) is IAvaloniaXamlIlParentStackProvider upstreamStack)
        {
            foreach (var upstreamParent in upstreamStack.Parents)
            {
                AddParent(upstreamParent);
            }
        }

        return parents;
    }

    private static MarkupExtensionServiceProvider CreateContextProvider(
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        return new MarkupExtensionServiceProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            ResolveBaseUri(parentServiceProvider, baseUri),
            BuildParentStack(targetObject, parentStack));
    }

    private static Uri ResolveBaseUri(IServiceProvider? parentServiceProvider, string? baseUri)
    {
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext parentUriContext)
        {
            return parentUriContext.BaseUri;
        }

        if (!string.IsNullOrWhiteSpace(baseUri) &&
            Uri.TryCreate(baseUri, UriKind.RelativeOrAbsolute, out var resolvedBaseUri))
        {
            return resolvedBaseUri;
        }

        return new Uri("avares://sourcegen/");
    }

    private static object[] BuildParentStack(object targetObject, IReadOnlyList<object>? parentStack)
    {
        if (parentStack is null || parentStack.Count == 0)
        {
            return [targetObject];
        }

        var stack = new object[parentStack.Count];
        for (var index = 0; index < parentStack.Count; index++)
        {
            stack[index] = parentStack[index];
        }

        return stack;
    }

    private static string NormalizeRuntimeXamlValue(string xaml)
    {
        var trimmed = xaml.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '<')
        {
            return trimmed;
        }

        try
        {
            var element = XElement.Parse(trimmed, LoadOptions.PreserveWhitespace);
            if (element.Name.LocalName.EndsWith(".Value", StringComparison.Ordinal) &&
                element.Elements().FirstOrDefault() is { } innerValue)
            {
                return innerValue.ToString(SaveOptions.DisableFormatting);
            }
        }
        catch
        {
            // Keep original value when fragment parsing fails; downstream loader reports details.
        }

        return trimmed;
    }

    private static Uri ResolveRuntimeBaseUri(
        IServiceProvider? parentServiceProvider,
        string? baseUri,
        string localAssemblyName)
    {
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext parentUriContext)
        {
            return parentUriContext.BaseUri;
        }

        if (!string.IsNullOrWhiteSpace(baseUri))
        {
            var trimmed = baseUri.Trim();
            if (Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var resolved))
            {
                if (resolved.IsAbsoluteUri)
                {
                    return resolved;
                }

                var normalizedPath = trimmed.Replace('\\', '/').TrimStart('/');
                if (normalizedPath.Length > 0)
                {
                    return new Uri("avares://" + localAssemblyName + "/" + normalizedPath);
                }
            }
        }

        return new Uri("avares://" + localAssemblyName + "/");
    }

    private sealed class DeferredStaticResourceContent : IDeferredContent
    {
        private readonly object _resourceKey;
        private readonly object _anchor;
        private readonly string? _baseUri;
        private readonly object[]? _parentStack;
        private readonly IServiceProvider? _creationServiceProvider;

        public DeferredStaticResourceContent(
            object resourceKey,
            object anchor,
            string? baseUri,
            IReadOnlyList<object>? parentStack,
            IServiceProvider? creationServiceProvider)
        {
            _resourceKey = resourceKey;
            _anchor = anchor;
            _baseUri = baseUri;
            _creationServiceProvider = creationServiceProvider;
            if (parentStack is { Count: > 0 })
            {
                _parentStack = new object[parentStack.Count];
                for (var index = 0; index < parentStack.Count; index++)
                {
                    _parentStack[index] = parentStack[index];
                }
            }
        }

        public object? Build(IServiceProvider? serviceProvider)
        {
            var effectiveServiceProvider = serviceProvider ?? _creationServiceProvider ?? CreateContextProvider(
                parentServiceProvider: null,
                rootObject: _anchor,
                intermediateRootObject: _anchor,
                targetObject: _anchor,
                targetProperty: null,
                baseUri: _baseUri,
                parentStack: _parentStack);

            try
            {
                return ResolveStaticResourceExtensionImmediate(
                    new StaticResourceExtension(_resourceKey),
                    _anchor,
                    _baseUri,
                    _parentStack,
                    effectiveServiceProvider);
            }
            catch (KeyNotFoundException)
            {
                // Deferred entries may be touched while merged dictionaries are still materializing.
                // Keep lookup lazy and let a later access attempt resolve the resource.
                return AvaloniaProperty.UnsetValue;
            }
        }
    }

    private sealed class MarkupExtensionServiceProvider :
        IServiceProvider,
        IProvideValueTarget,
        IRootObjectProvider,
        IUriContext,
        IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly object _rootObject;
        private readonly object _intermediateRootObject;
        private readonly object _targetObject;
        private readonly object? _targetProperty;
        private readonly object[] _parentStack;
        private readonly Uri _baseUri;

        public MarkupExtensionServiceProvider(
            IServiceProvider? parentServiceProvider,
            object rootObject,
            object intermediateRootObject,
            object targetObject,
            object? targetProperty,
            Uri baseUri,
            object[] parentStack)
        {
            _parentServiceProvider = parentServiceProvider;
            _rootObject = rootObject;
            _intermediateRootObject = intermediateRootObject;
            _targetObject = targetObject;
            _targetProperty = targetProperty;
            _baseUri = baseUri;
            _parentStack = parentStack;
        }

        public object TargetObject => _targetObject;

        public object TargetProperty => _targetProperty ?? AvaloniaProperty.UnsetValue;

        public object RootObject => _rootObject;

        public object IntermediateRootObject => _intermediateRootObject;

        public Uri BaseUri
        {
            get => _baseUri;
            set { }
        }

        public IEnumerable<object> Parents => EnumerateParents();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IProvideValueTarget))
            {
                return this;
            }

            if (serviceType == typeof(IRootObjectProvider))
            {
                return this;
            }

            if (serviceType == typeof(IUriContext))
            {
                return this;
            }

            if (serviceType == typeof(IAvaloniaXamlIlParentStackProvider))
            {
                return this;
            }

            if (serviceType == typeof(INameScope))
            {
                if (_parentServiceProvider?.GetService(typeof(INameScope)) is INameScope nameScope)
                {
                    return nameScope;
                }

                if (_targetObject is StyledElement styledElement &&
                    NameScope.GetNameScope(styledElement) is { } elementNameScope)
                {
                    return elementNameScope;
                }
            }

            return _parentServiceProvider?.GetService(serviceType);
        }

        private IEnumerable<object> EnumerateParents()
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < _parentStack.Length; index++)
            {
                var parent = _parentStack[index];
                if (seen.Add(parent))
                {
                    yield return parent;
                }
            }

            if (_parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) is IAvaloniaXamlIlParentStackProvider upstreamStack)
            {
                foreach (var upstreamParent in upstreamStack.Parents)
                {
                    if (seen.Add(upstreamParent))
                    {
                        yield return upstreamParent;
                    }
                }
            }
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
