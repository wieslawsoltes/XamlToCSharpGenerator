using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Data;
using global::Avalonia.Data.Converters;
using global::Avalonia.Data.Core;
using global::Avalonia.LogicalTree;
using global::Avalonia.Markup.Xaml;
using global::Avalonia.Markup.Xaml.Converters;
using global::Avalonia.Markup.Xaml.MarkupExtensions;
using global::Avalonia.Markup.Xaml.XamlIl.Runtime;
using global::Avalonia.Platform;
using global::Avalonia.Styling;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using XamlToCSharpGenerator.MiniLanguageParsing.Bindings;

namespace XamlToCSharpGenerator.Runtime;

public static class SourceGenMarkupExtensionRuntime
{
    private enum DeferredBindingFailureReason
    {
        None,
        DataContextUnavailable,
        TemplatedParentUnavailable,
        AncestorUnavailable
    }

    private static readonly ConcurrentDictionary<string, Type?> BindingTypeCache = new(StringComparer.Ordinal);
    private const int MaxDeferredBindingRetryCount = 12;
    private static readonly bool BindingTraceEnabled = IsEnvironmentEnabled("AXSG_BINDING_TRACE");
    private static readonly bool BindingTraceVerboseEnabled = IsEnvironmentEnabled("AXSG_BINDING_TRACE_VERBOSE");

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

        if ((targetType == typeof(global::Avalonia.Media.IBrush) ||
             targetType == typeof(global::Avalonia.Media.Brush)) &&
            value is global::Avalonia.Media.Color colorValue)
        {
            return new global::Avalonia.Media.Immutable.ImmutableSolidColorBrush(colorValue);
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

        if (targetType == typeof(global::Avalonia.Media.IBrush) ||
            targetType == typeof(global::Avalonia.Media.Brush))
        {
            return global::Avalonia.Media.Brush.Parse(trimmed);
        }

        if (targetType == typeof(global::Avalonia.Media.Color))
        {
            return global::Avalonia.Media.Color.Parse(trimmed);
        }

        return value;
    }

    public static object? ProvideDynamicResource(
        object resourceKey,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        if (targetObject is AvaloniaObject &&
            ShouldUseDetachedDynamicResourceLookup(targetObject) &&
            BuildDetachedDynamicResourceLookupChain(targetObject, parentServiceProvider, parentStack) is { Count: > 0 } detachedLookupChain)
        {
            return InstancedBinding.OneWay(
                new DetachedDynamicResourceObservable(
                    resourceKey,
                    detachedLookupChain,
                    GetStaticResourceTargetType(targetObject, targetProperty)),
                ResolveDynamicResourcePriority(parentServiceProvider));
        }

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

    private static bool ShouldUseDetachedDynamicResourceLookup(object targetObject)
    {
        return targetObject switch
        {
            StyledElement styledTarget => !HasActiveBindingAnchorContext(styledTarget),
            _ => true
        };
    }

    public static T? AttachBindingNameScope<T>(T? binding, object? nameScope)
        where T : class
    {
        return AttachBindingNameScope(binding, nameScope, xmlNamespaces: null);
    }

    public static T? AttachBindingNameScope<T>(
        T? binding,
        object? nameScope,
        IReadOnlyDictionary<string, string>? xmlNamespaces)
        where T : class
    {
        var typedNameScope = nameScope as INameScope;
        AttachBindingMetadata(binding, typedNameScope, xmlNamespaces);

        return binding;
    }

    private static void AttachBindingMetadata(
        object? binding,
        INameScope? nameScope,
        IReadOnlyDictionary<string, string>? xmlNamespaces)
    {
        if (binding is null)
        {
            return;
        }

        if (binding is SourceGenProvidedXBindBinding xBindBinding)
        {
            xBindBinding.AttachMetadata(nameScope, xmlNamespaces);
            return;
        }

        if (binding is Binding dataBinding)
        {
            if (xmlNamespaces is not null && xmlNamespaces.Count > 0)
            {
                dataBinding.TypeResolver = CreateBindingTypeResolver(xmlNamespaces);
            }
            else if (dataBinding.TypeResolver is null)
            {
                dataBinding.TypeResolver = ResolveBindingType;
            }
        }

        if (binding is BindingBase bindingBase && nameScope is not null)
        {
            bindingBase.NameScope = new WeakReference<INameScope?>(nameScope);
        }

        if (binding is not MultiBinding multiBinding)
        {
            return;
        }

        foreach (var childBinding in multiBinding.Bindings)
        {
            AttachBindingMetadata(childBinding, nameScope, xmlNamespaces);
        }
    }

    public static void ApplyBinding(object? target, AvaloniaProperty property, object? value, object? anchor = null)
    {
        if (target is not AvaloniaObject avaloniaObject || property is null)
        {
            return;
        }

        if (value is SourceGenProvidedXBindBinding xBindBinding)
        {
            xBindBinding.Apply(avaloniaObject, property, anchor);
            return;
        }

        ApplyBindingCore(avaloniaObject, property, value, anchor);
    }

    public static void InitializeXBind(object rootObject)
    {
        ArgumentNullException.ThrowIfNull(rootObject);
        SourceGenXBindLifecycleRegistry.Initialize(rootObject);
    }

    public static void UpdateXBind(object rootObject)
    {
        ArgumentNullException.ThrowIfNull(rootObject);
        SourceGenXBindLifecycleRegistry.Update(rootObject);
    }

    public static void StopTrackingXBind(object rootObject)
    {
        ArgumentNullException.ThrowIfNull(rootObject);
        SourceGenXBindLifecycleRegistry.StopTracking(rootObject);
    }

    public static void ResetXBind(object rootObject)
    {
        ArgumentNullException.ThrowIfNull(rootObject);
        SourceGenXBindLifecycleRegistry.Reset(rootObject);
    }

    internal static IDisposable? ApplyBindingCore(AvaloniaObject avaloniaObject, AvaloniaProperty property, object? value, object? anchor = null)
    {
        if (value is InstancedBinding instancedBinding)
        {
#pragma warning disable CS0618
            return BindingOperations.Apply(avaloniaObject, property, instancedBinding);
#pragma warning restore CS0618
        }

        if (value is IBinding binding)
        {
            var bindingHandle = new SourceGenBindingApplicationHandle();

            bool TryApplyBindingNow(
                string stage,
                out InvalidOperationException? deferredException,
                out DeferredBindingFailureReason deferredReason)
            {
                deferredException = null;
                deferredReason = DeferredBindingFailureReason.None;
                if (bindingHandle.IsDisposed)
                {
                    return false;
                }

                if (bindingHandle.HasAttachedBinding)
                {
                    return true;
                }

                if (TryBind(
                        avaloniaObject,
                        property,
                        binding,
                        anchor,
                        out var appliedBindingExpression,
                        out deferredException))
                {
                    if (!bindingHandle.TrySetBindingExpression(appliedBindingExpression))
                    {
                        return false;
                    }

                    if (!string.Equals(stage, "immediate", StringComparison.Ordinal) ||
                        BindingTraceVerboseEnabled)
                    {
                        TraceBinding($"Applied binding ({stage}): {DescribeBindingTarget(avaloniaObject, property)}.");
                    }
                    return true;
                }

                if (deferredException is not null)
                {
                    deferredReason = ResolveDeferredBindingFailureReason(avaloniaObject, binding, anchor, deferredException);
                    TraceBinding($"Deferred binding ({stage}/{deferredReason}): {DescribeBindingTarget(avaloniaObject, property)}. {deferredException.Message}");
                }

                return false;
            }

            if (TryApplyBindingNow("immediate", out _, out var initialDeferredReason))
            {
                return bindingHandle;
            }

            void AttachDeferredRetryHooks(StyledElement observedElement, Visual? observedVisual, string ownerKind)
            {
                void DetachHandlers()
                {
                    observedElement.PropertyChanged -= OnPropertyChanged;
                    observedElement.AttachedToLogicalTree -= OnAttachedToLogicalTree;
                    if (observedVisual is not null)
                    {
                        observedVisual.AttachedToVisualTree -= OnAttachedToVisualTree;
                    }
                }

                if (!bindingHandle.TryRegisterCleanup(DetachHandlers))
                {
                    return;
                }

                void TryApplyAndDetachHandlers()
                {
                    if (bindingHandle.IsDisposed || bindingHandle.HasAttachedBinding)
                    {
                        DetachHandlers();
                        return;
                    }

                    if (!TryApplyBindingNow("event/" + ownerKind, out _, out _))
                    {
                        return;
                    }

                    DetachHandlers();
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

                void OnAttachedToLogicalTree(object? sender, LogicalTreeAttachmentEventArgs args)
                {
                    TryApplyAndDetachHandlers();
                }

                observedElement.PropertyChanged += OnPropertyChanged;
                observedElement.AttachedToLogicalTree += OnAttachedToLogicalTree;
                if (observedVisual is not null)
                {
                    observedVisual.AttachedToVisualTree += OnAttachedToVisualTree;
                }
            }

            if (avaloniaObject is StyledElement styledTarget)
            {
                AttachDeferredRetryHooks(styledTarget, avaloniaObject as Visual, "target");
            }

            if (anchor is StyledElement styledAnchor &&
                (avaloniaObject is not StyledElement styledTargetReference || !ReferenceEquals(styledAnchor, styledTargetReference)))
            {
                AttachDeferredRetryHooks(styledAnchor, styledAnchor as Visual, "anchor");
            }

            if (ShouldScheduleTimedBindingRetry(avaloniaObject, binding, anchor, initialDeferredReason))
            {
                ScheduleBindingRetry(avaloniaObject, property, binding, anchor, bindingHandle);
            }

            return bindingHandle;
        }

        avaloniaObject.SetValue(property, value);
        return null;
    }

    public static object? ResolveBindingAnchor(object? target, IReadOnlyList<object>? parentStack)
    {
        var styledTarget = target as StyledElement;
        if (styledTarget is not null &&
            HasActiveBindingAnchorContext(styledTarget))
        {
            return styledTarget;
        }

        if (parentStack is null || parentStack.Count == 0)
        {
            return styledTarget;
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

            if (HasActiveBindingAnchorContext(styledElement))
            {
                return styledElement;
            }
        }

        return fallbackAnchor ?? styledTarget;
    }

    private static bool HasActiveBindingAnchorContext(StyledElement styledElement)
    {
        return styledElement.IsSet(StyledElement.DataContextProperty) ||
               styledElement.TemplatedParent is not null ||
               styledElement is Visual visual && TopLevel.GetTopLevel(visual) is not null;
    }

    private static DeferredBindingFailureReason ClassifyDeferredBindingFailure(
        AvaloniaObject target,
        IBinding binding,
        object? anchor)
    {
        if (binding is not Binding dataBinding)
        {
            return DeferredBindingFailureReason.None;
        }

        if (IsTemplatedParentBindingWithoutAnchor(dataBinding, target, anchor))
        {
            return DeferredBindingFailureReason.TemplatedParentUnavailable;
        }

        if (IsAncestorBindingWithoutReadyAnchor(dataBinding, target, anchor))
        {
            return DeferredBindingFailureReason.AncestorUnavailable;
        }

        if (IsDataContextBindingWithoutSource(dataBinding) &&
            !HasBindingDataContext(target, anchor))
        {
            return DeferredBindingFailureReason.DataContextUnavailable;
        }

        return DeferredBindingFailureReason.None;
    }

    private static DeferredBindingFailureReason ResolveDeferredBindingFailureReason(
        AvaloniaObject target,
        IBinding binding,
        object? anchor,
        InvalidOperationException? exception)
    {
        var deferredReason = ClassifyDeferredBindingFailure(target, binding, anchor);
        if (deferredReason != DeferredBindingFailureReason.None || exception is null)
        {
            return deferredReason;
        }

        var message = exception.Message;
        if (message.IndexOf("Cannot find a DataContext", StringComparison.Ordinal) >= 0)
        {
            return DeferredBindingFailureReason.DataContextUnavailable;
        }

        if (message.IndexOf("Cannot find a StyledElement to get a TemplatedParent", StringComparison.Ordinal) >= 0)
        {
            return DeferredBindingFailureReason.TemplatedParentUnavailable;
        }

        if (message.IndexOf("Cannot find an ILogical to get a visual ancestor", StringComparison.Ordinal) >= 0 ||
            message.IndexOf("Cannot find an ILogical to get a logical ancestor", StringComparison.Ordinal) >= 0)
        {
            return DeferredBindingFailureReason.AncestorUnavailable;
        }

        return DeferredBindingFailureReason.None;
    }

    private static bool IsDataContextBindingWithoutSource(Binding binding)
    {
        return IsBindingSourceUnset(binding) &&
               binding.ElementName is null &&
               binding.RelativeSource is null;
    }

    private static bool HasBindingDataContext(object? target, object? anchor)
    {
        return HasDataContext(target) || HasDataContext(anchor);
    }

    private static bool HasDataContext(object? candidate)
    {
        if (candidate is not StyledElement styledElement)
        {
            return false;
        }

        var dataContext = styledElement.DataContext;
        return dataContext is not null &&
               !ReferenceEquals(dataContext, AvaloniaProperty.UnsetValue);
    }

    private static bool IsTemplatedParentBindingWithoutAnchor(Binding binding, AvaloniaObject target, object? anchor)
    {
        if (!IsTemplatedParentRelativeSource(binding.RelativeSource) ||
            !IsBindingSourceUnset(binding) ||
            binding.ElementName is not null)
        {
            return false;
        }

        StyledElement? styledAnchor = target as StyledElement;
        if (styledAnchor is null && anchor is StyledElement providedAnchor)
        {
            styledAnchor = providedAnchor;
        }

        if (styledAnchor is null)
        {
            return true;
        }

        var templatedParentAnchor = ResolveTemplatedParentAnchor(styledAnchor);
        if (templatedParentAnchor?.TemplatedParent is not null)
        {
            return false;
        }

        if (anchor is StyledElement alternateAnchor &&
            !ReferenceEquals(alternateAnchor, styledAnchor))
        {
            var alternateTemplatedParentAnchor = ResolveTemplatedParentAnchor(alternateAnchor);
            if (alternateTemplatedParentAnchor?.TemplatedParent is not null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAncestorBindingWithoutReadyAnchor(Binding binding, AvaloniaObject target, object? anchor)
    {
        if (!IsFindAncestorRelativeSource(binding.RelativeSource) ||
            !IsBindingSourceUnset(binding) ||
            binding.ElementName is not null)
        {
            return false;
        }

        return !HasAncestorBindingAnchor(target) &&
               !HasAncestorBindingAnchor(anchor);
    }

    private static bool ShouldScheduleTimedBindingRetry(
        AvaloniaObject target,
        IBinding _,
        object? anchor,
        DeferredBindingFailureReason deferredReason)
    {
        if (deferredReason == DeferredBindingFailureReason.None)
        {
            return false;
        }

        if (deferredReason == DeferredBindingFailureReason.DataContextUnavailable)
        {
            return false;
        }

        if (deferredReason == DeferredBindingFailureReason.TemplatedParentUnavailable ||
            deferredReason == DeferredBindingFailureReason.AncestorUnavailable)
        {
            // For styled targets/anchors, tree and property change handlers already
            // re-apply when the required context becomes available.
            if (target is StyledElement ||
                anchor is StyledElement)
            {
                return false;
            }

            // Non-styled targets without anchor notifications need timer-based retries.
            return true;
        }

        return false;
    }

    private static bool IsTemplatedParentRelativeSource(RelativeSource? relativeSource)
    {
        return relativeSource?.Mode == RelativeSourceMode.TemplatedParent;
    }

    private static bool IsFindAncestorRelativeSource(RelativeSource? relativeSource)
    {
        return relativeSource?.Mode == RelativeSourceMode.FindAncestor;
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

    private static bool HasAncestorBindingAnchor(object? candidate)
    {
        if (candidate is not StyledElement styledElement)
        {
            return false;
        }

        if (candidate is ILogical logical && logical.IsAttachedToLogicalTree)
        {
            return true;
        }

        return candidate is Visual visual && TopLevel.GetTopLevel(visual) is not null;
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

    private static bool TryPrepareBindingForNonLogicalAncestorTarget(
        AvaloniaObject target,
        IBinding binding,
        object? anchor,
        out InvalidOperationException? deferredException)
    {
        deferredException = null;

        if (binding is not Binding dataBinding ||
            !IsFindAncestorRelativeSource(dataBinding.RelativeSource) ||
            !IsBindingSourceUnset(dataBinding) ||
            dataBinding.ElementName is not null ||
            target is ILogical)
        {
            return true;
        }

        if (TryResolveAncestorBindingSource(anchor, dataBinding.RelativeSource!, out var ancestorSource))
        {
            dataBinding.Source = ancestorSource;
            dataBinding.RelativeSource = null;
            TraceBinding(
                $"Rewrote ancestor binding: target={target.GetType().FullName}, anchor={(anchor as object)?.GetType().FullName ?? "<null>"}, source={ancestorSource.GetType().FullName}, path={dataBinding.Path}.");
            return true;
        }

        TraceBinding(
            $"Deferred ancestor binding preparation: target={target.GetType().FullName}, anchor={(anchor as object)?.GetType().FullName ?? "<null>"}, path={dataBinding.Path}.");
        deferredException = new InvalidOperationException("Cannot find an ILogical to get a visual ancestor.");
        return false;
    }

    private static bool TryPrepareBindingForNonStyledDataContextTarget(
        AvaloniaObject target,
        IBinding binding,
        object? anchor,
        out InvalidOperationException? deferredException)
    {
        deferredException = null;

        if (binding is not Binding dataBinding ||
            target is StyledElement ||
            !IsBindingSourceUnset(dataBinding) ||
            dataBinding.ElementName is not null ||
            dataBinding.RelativeSource is not null ||
            IsRootedBindingPath(dataBinding.Path))
        {
            return true;
        }

        if (anchor is not StyledElement styledAnchor)
        {
            TraceBinding(
                $"Deferred data-context binding preparation: target={target.GetType().FullName}, anchor=<null>, path={dataBinding.Path ?? "<null>"}.");
            deferredException = new InvalidOperationException("Cannot find a DataContext to bind to.");
            return false;
        }

        dataBinding.Source = styledAnchor;
        dataBinding.Path = QualifyDataContextPath(dataBinding.Path);
        TraceBinding(
            $"Rewrote data-context binding: target={target.GetType().FullName}, anchor={styledAnchor.GetType().FullName}, path={dataBinding.Path}.");
        return true;
    }

    private static string QualifyDataContextPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            string.Equals(path, ".", StringComparison.Ordinal))
        {
            return "DataContext";
        }

        return path[0] == '['
            ? $"DataContext{path}"
            : $"DataContext.{path}";
    }

    private static bool IsRootedBindingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var trimmedPath = path.Trim();
        return BindingSourceQuerySemantics.TryParseElementName(trimmedPath, out _) ||
               BindingSourceQuerySemantics.TryParseSelf(trimmedPath, out _) ||
               BindingSourceQuerySemantics.TryParseParent(trimmedPath, out _);
    }

    private static bool TryPrepareBindingForDetachedTarget(
        AvaloniaObject target,
        object? binding,
        object? anchor,
        out InvalidOperationException? deferredException)
    {
        deferredException = null;

        if (binding is null)
        {
            return true;
        }

        if (binding is MultiBinding multiBinding)
        {
            for (var index = 0; index < multiBinding.Bindings.Count; index++)
            {
                if (!TryPrepareBindingForDetachedTarget(target, multiBinding.Bindings[index], anchor, out deferredException))
                {
                    return false;
                }
            }

            return true;
        }

        if (binding is not IBinding avaloniaBinding)
        {
            return true;
        }

        if (!TryPrepareBindingForNonStyledTemplatedParentTarget(target, avaloniaBinding, anchor, out deferredException))
        {
            return false;
        }

        if (!TryPrepareBindingForNonLogicalAncestorTarget(target, avaloniaBinding, anchor, out deferredException))
        {
            return false;
        }

        if (!TryPrepareBindingForNonStyledDataContextTarget(target, avaloniaBinding, anchor, out deferredException))
        {
            return false;
        }

        return true;
    }

    private static void ScheduleBindingRetry(
        AvaloniaObject target,
        AvaloniaProperty property,
        IBinding binding,
        object? anchor,
        SourceGenBindingApplicationHandle bindingHandle)
    {
        var fallbackSynchronizationContext = SynchronizationContext.Current as AvaloniaSynchronizationContext;

        async Task ScheduleDelayedRetryAsync(int attempt, int delayMilliseconds)
        {
            await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            TryApply(attempt);
        }

        void TryApply(int attempt)
        {
            if (bindingHandle.IsDisposed || bindingHandle.HasAttachedBinding)
            {
                return;
            }

            if (!SourceGenDispatcherRuntime.TryPost(() =>
            {
                if (bindingHandle.IsDisposed || bindingHandle.HasAttachedBinding)
                {
                    return;
                }

                if (TryBind(target, property, binding, anchor, out var bindingExpression, out var deferredException))
                {
                    if (!bindingHandle.TrySetBindingExpression(bindingExpression))
                    {
                        return;
                    }

                    TraceBinding($"Applied binding (retry {attempt + 1}/{MaxDeferredBindingRetryCount}): {DescribeBindingTarget(target, property)}.");
                    return;
                }

                if (deferredException is not null)
                {
                    var deferredReason = ResolveDeferredBindingFailureReason(target, binding, anchor, deferredException);
                    TraceBinding($"Deferred binding (retry {attempt + 1}/{MaxDeferredBindingRetryCount}/{deferredReason}): {DescribeBindingTarget(target, property)}. {deferredException.Message}");
                }

                if (!bindingHandle.IsDisposed &&
                    !bindingHandle.HasAttachedBinding &&
                    attempt + 1 < MaxDeferredBindingRetryCount)
                {
                    var nextAttempt = attempt + 1;
                    var delayMilliseconds = Math.Min(16 * nextAttempt, 120);
                    if (SourceGenDispatcherRuntime.HasControlledUiDispatcher())
                    {
                        DispatcherTimer.RunOnce(
                            () => TryApply(nextAttempt),
                            TimeSpan.FromMilliseconds(delayMilliseconds),
                            DispatcherPriority.Background);
                    }
                    else
                    {
                        _ = ScheduleDelayedRetryAsync(nextAttempt, delayMilliseconds);
                    }
                }
            }, attempt == 0 ? DispatcherPriority.Loaded : DispatcherPriority.Background, fallbackSynchronizationContext))
            {
                if (bindingHandle.IsDisposed ||
                    bindingHandle.HasAttachedBinding ||
                    attempt + 1 >= MaxDeferredBindingRetryCount)
                {
                    return;
                }

                var nextAttempt = attempt + 1;
                var delayMilliseconds = Math.Min(16 * nextAttempt, 120);
                _ = ScheduleDelayedRetryAsync(nextAttempt, delayMilliseconds);
            }
        }

        TryApply(0);
    }

    private static bool TryBind(
        AvaloniaObject target,
        AvaloniaProperty property,
        IBinding binding,
        object? anchor,
        out IDisposable? bindingExpression,
        out InvalidOperationException? deferredException)
    {
        bindingExpression = null;
        deferredException = null;

        if (!TryPrepareBindingForDetachedTarget(target, binding, anchor, out deferredException))
        {
            return false;
        }

        try
        {
            if (anchor is not null)
            {
#pragma warning disable CS0618
                bindingExpression = target.Bind(property, binding, anchor);
#pragma warning restore CS0618
            }
            else
            {
                bindingExpression = target.Bind(property, binding);
            }

            return true;
        }
        catch (InvalidOperationException exception)
        {
            var deferredReason = ResolveDeferredBindingFailureReason(target, binding, anchor, exception);
            if (deferredReason == DeferredBindingFailureReason.None)
            {
                throw;
            }

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

    private static bool TryResolveAncestorBindingSource(
        object? anchor,
        RelativeSource relativeSource,
        out object ancestorSource)
    {
        var ancestorLevel = relativeSource.AncestorLevel > 0 ? relativeSource.AncestorLevel : 1;
        var ancestorType = relativeSource.AncestorType;

        if (anchor is Visual visual &&
            TopLevel.GetTopLevel(visual) is not null &&
            TryResolveVisualAncestorBindingSource(visual, ancestorType, ancestorLevel, out ancestorSource))
        {
            return true;
        }

        if (anchor is ILogical logical &&
            logical.IsAttachedToLogicalTree &&
            TryResolveLogicalAncestorBindingSource(logical, ancestorType, ancestorLevel, out ancestorSource))
        {
            return true;
        }

        ancestorSource = null!;
        return false;
    }

    private static bool TryResolveVisualAncestorBindingSource(
        Visual anchor,
        Type? ancestorType,
        int ancestorLevel,
        out object ancestorSource)
    {
        if (MatchesAncestorBindingSource(anchor, ancestorType))
        {
            ancestorLevel--;
            if (ancestorLevel == 0)
            {
                ancestorSource = anchor;
                return true;
            }
        }

        foreach (var ancestor in anchor.GetVisualAncestors())
        {
            if (!MatchesAncestorBindingSource(ancestor, ancestorType))
            {
                continue;
            }

            ancestorLevel--;
            if (ancestorLevel == 0)
            {
                ancestorSource = ancestor;
                return true;
            }
        }

        ancestorSource = null!;
        return false;
    }

    private static bool TryResolveLogicalAncestorBindingSource(
        ILogical anchor,
        Type? ancestorType,
        int ancestorLevel,
        out object ancestorSource)
    {
        if (MatchesAncestorBindingSource(anchor, ancestorType))
        {
            ancestorLevel--;
            if (ancestorLevel == 0)
            {
                ancestorSource = anchor;
                return true;
            }
        }

        foreach (var ancestor in anchor.GetLogicalAncestors())
        {
            if (!MatchesAncestorBindingSource(ancestor, ancestorType))
            {
                continue;
            }

            ancestorLevel--;
            if (ancestorLevel == 0)
            {
                ancestorSource = ancestor;
                return true;
            }
        }

        ancestorSource = null!;
        return false;
    }

    private static bool MatchesAncestorBindingSource(object candidate, Type? ancestorType)
    {
        return ancestorType is null || ancestorType.IsInstanceOfType(candidate);
    }

    public static global::Avalonia.Media.Imaging.Bitmap? LoadBitmapAsset(string path, string? baseUri)
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
            return new global::Avalonia.Media.Imaging.Bitmap(uri.LocalPath);
        }

        var contextBaseUri = TryCreateAbsoluteUri(baseUri);
        return new global::Avalonia.Media.Imaging.Bitmap(AssetLoader.Open(uri, contextBaseUri));
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

    public static global::Avalonia.Media.FontFeatureCollection? ParseFontFeatureCollection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collection = new global::Avalonia.Media.FontFeatureCollection();
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < parts.Length; index++)
        {
            var token = parts[index];
            if (token.Length == 0)
            {
                continue;
            }

            collection.Add(global::Avalonia.Media.FontFeature.Parse(token));
        }

        return collection;
    }

    public static global::Avalonia.Media.FontFamily ParseFontFamily(string? value, string? baseUri)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return global::Avalonia.Media.FontFamily.Default;
        }

        var trimmedValue = value.Trim();
        var resolvedBaseUri = TryCreateAbsoluteUri(baseUri);
        resolvedBaseUri ??= new Uri("avares://sourcegen/");

        try
        {
            return global::Avalonia.Media.FontFamily.Parse(trimmedValue, resolvedBaseUri);
        }
        catch (ArgumentException)
        {
            // Keep font parsing non-fatal when a family source can't be resolved.
            return global::Avalonia.Media.FontFamily.Default;
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

    private static Func<string?, string, Type> CreateBindingTypeResolver(IReadOnlyDictionary<string, string>? xmlNamespaces)
    {
        if (xmlNamespaces is null || xmlNamespaces.Count == 0)
        {
            return ResolveBindingType;
        }

        return (xmlNamespace, name) => ResolveBindingType(xmlNamespace, name, xmlNamespaces);
    }

    private static Type ResolveBindingType(string? xmlNamespace, string name)
    {
        return ResolveBindingType(xmlNamespace, name, xmlNamespaces: null);
    }

    private static Type ResolveBindingType(
        string? xmlNamespace,
        string name,
        IReadOnlyDictionary<string, string>? xmlNamespaces)
    {
        var normalizedXmlNamespace = NormalizeBindingXmlNamespace(xmlNamespace, xmlNamespaces);
        var key = (normalizedXmlNamespace ?? string.Empty) + "|" + name;
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

    private static string? NormalizeBindingXmlNamespace(
        string? xmlNamespace,
        IReadOnlyDictionary<string, string>? xmlNamespaces)
    {
        if (string.IsNullOrWhiteSpace(xmlNamespace) ||
            xmlNamespaces is null ||
            xmlNamespaces.Count == 0)
        {
            return xmlNamespace;
        }

        return xmlNamespaces.TryGetValue(xmlNamespace, out var mappedXmlNamespace)
            ? mappedXmlNamespace
            : xmlNamespace;
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

    public static IBinding ProvideInlineCodeBinding<TSource, TRoot, TTarget>(
        Func<TSource, TRoot, TTarget, object?> evaluator,
        IReadOnlyList<string>? dependencyNames,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
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
            Converter = new SourceGenInlineCodeMultiValueConverter<TSource, TRoot, TTarget>(
                evaluator,
                rootObject,
                targetObject),
            Mode = BindingMode.OneWay
        };

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

    public static object ProvideXBindExpressionBinding<TSource, TRoot, TTarget>(
        Func<TSource, TRoot, TTarget, object?> evaluator,
        SourceGenBindingDependency source,
        IReadOnlyList<SourceGenBindingDependency>? dependencies,
        BindingMode mode,
        Action<TSource, object?>? bindBack,
        Type? bindBackValueType,
        object? converter,
        CultureInfo? converterCulture,
        object? converterParameter,
        string? stringFormat,
        object? fallbackValue,
        object? targetNullValue,
        int delay,
        UpdateSourceTrigger updateSourceTrigger,
        BindingPriority priority,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        return new SourceGenProvidedXBindBinding(
            rootObject,
            metadata => CreatePreparedXBindBinding(
                evaluator,
                source,
                dependencies,
                mode,
                bindBack,
                bindBackValueType,
                converter,
                converterCulture,
                converterParameter,
                stringFormat,
                fallbackValue,
                targetNullValue,
                delay,
                updateSourceTrigger,
                priority,
                parentServiceProvider,
                rootObject,
                intermediateRootObject,
                targetObject,
                targetProperty,
                baseUri,
                parentStack,
                metadata.NameScope,
                metadata.XmlNamespaces));
    }

    private static SourceGenPreparedXBindBinding CreatePreparedXBindBinding<TSource, TRoot, TTarget>(
        Func<TSource, TRoot, TTarget, object?> evaluator,
        SourceGenBindingDependency source,
        IReadOnlyList<SourceGenBindingDependency>? dependencies,
        BindingMode mode,
        Action<TSource, object?>? bindBack,
        Type? bindBackValueType,
        object? converter,
        CultureInfo? converterCulture,
        object? converterParameter,
        string? stringFormat,
        object? fallbackValue,
        object? targetNullValue,
        int delay,
        UpdateSourceTrigger updateSourceTrigger,
        BindingPriority priority,
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack,
        INameScope? attachedNameScope,
        IReadOnlyDictionary<string, string>? attachedXmlNamespaces)
    {
        var contextProvider = CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);

        var multiBinding = new MultiBinding
        {
            Converter = new SourceGenInlineCodeMultiValueConverter<TSource, TRoot, TTarget>(
                evaluator,
                rootObject,
                targetObject,
                converter as IValueConverter),
            ConverterCulture = converterCulture,
            ConverterParameter = converterParameter,
            FallbackValue = fallbackValue ?? AvaloniaProperty.UnsetValue,
            TargetNullValue = targetNullValue ?? AvaloniaProperty.UnsetValue,
            StringFormat = stringFormat,
            Mode = bindBack is null ? mode : BindingMode.OneWay,
            Priority = priority
        };

        multiBinding.Bindings.Add(CreateXBindChildBinding(source, rootObject, targetObject));

        if (dependencies is not null)
        {
            var seen = new HashSet<SourceGenBindingDependency>();
            foreach (var dependency in dependencies)
            {
                if (!seen.Add(dependency))
                {
                    continue;
                }

                multiBinding.Bindings.Add(CreateXBindChildBinding(dependency, rootObject, targetObject));
            }
        }

        var nameScope = attachedNameScope ?? contextProvider.GetService(typeof(INameScope)) as INameScope;
        AttachBindingMetadata(multiBinding, nameScope, attachedXmlNamespaces);

        if (bindBack is null || mode != BindingMode.TwoWay)
        {
            return new SourceGenPreparedXBindBinding(multiBinding, null);
        }

        if (targetObject is not AvaloniaObject avaloniaTarget ||
            targetProperty is not AvaloniaProperty avaloniaProperty)
        {
            return new SourceGenPreparedXBindBinding(multiBinding, null);
        }

        var anchor = ResolveBindingAnchor(targetObject, parentStack);
        var forward = multiBinding.Initiate(avaloniaTarget, avaloniaProperty, anchor);
        if (forward is null)
        {
            return new SourceGenPreparedXBindBinding(multiBinding, null);
        }

        var bindBackObserver = new SourceGenXBindBindBackObserver<TSource>(
            source,
            bindBack,
            rootObject,
            targetObject,
            avaloniaTarget,
            converter as IValueConverter,
            converterCulture,
            converterParameter,
            bindBackValueType,
            delay,
            updateSourceTrigger);
        return new SourceGenPreparedXBindBinding(
            InstancedBinding.TwoWay(forward.Source, bindBackObserver, priority),
            bindBackObserver);
    }

    public static T? ResolveNamedElement<T>(
        object? targetObject,
        object? rootObject,
        string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return default;
        }

        if (TryResolveNamedElement(targetObject, name, out var element) ||
            TryResolveNamedElement(rootObject, name, out element))
        {
            if (element is T typedElement)
            {
                return typedElement;
            }
        }

        return default;
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

    private static IBinding CreateXBindChildBinding(
        SourceGenBindingDependency dependency,
        object rootObject,
        object targetObject)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(dependency.Path)
            ? "."
            : dependency.Path!.Trim();

        return dependency.SourceKind switch
        {
            SourceGenBindingSourceKind.Root => new Binding(normalizedPath)
            {
                Source = rootObject
            },
            SourceGenBindingSourceKind.Target => new Binding(normalizedPath)
            {
                Source = targetObject
            },
            SourceGenBindingSourceKind.ElementName => new Binding(normalizedPath)
            {
                ElementName = dependency.ElementName
            },
            SourceGenBindingSourceKind.TemplatedParent => new Binding(normalizedPath)
            {
                RelativeSource = dependency.RelativeSource
            },
            SourceGenBindingSourceKind.FindAncestor => new Binding(normalizedPath)
            {
                RelativeSource = dependency.RelativeSource
            },
            SourceGenBindingSourceKind.ExplicitSource => new Binding(normalizedPath)
            {
                Source = dependency.Source
            },
            _ => new Binding(normalizedPath)
        };
    }

    internal static bool TryResolveDependencySource(
        SourceGenBindingDependency dependency,
        object? targetObject,
        object? anchorObject,
        object? rootObject,
        out object? value)
    {
        value = dependency.SourceKind switch
        {
            SourceGenBindingSourceKind.Root => rootObject,
            SourceGenBindingSourceKind.Target => targetObject,
            SourceGenBindingSourceKind.ElementName => ResolveNamedElement<object>(
                targetObject,
                rootObject,
                dependency.ElementName ?? string.Empty),
            SourceGenBindingSourceKind.TemplatedParent => TryResolveTemplatedParentDependencySource(
                anchorObject ?? targetObject,
                out var templatedParent)
                ? templatedParent
                : null,
            SourceGenBindingSourceKind.FindAncestor => dependency.RelativeSource is not null &&
                                                       TryResolveAncestorBindingSource(
                                                           anchorObject ?? targetObject,
                                                           dependency.RelativeSource,
                                                           out var ancestorSource)
                ? ancestorSource
                : null,
            SourceGenBindingSourceKind.ExplicitSource => dependency.Source,
            _ => TryGetDataContext(targetObject) ?? TryGetDataContext(anchorObject) ?? TryGetDataContext(rootObject)
        };

        return value is not null;
    }

    public static bool TryResolveXBindDependency<T>(
        SourceGenBindingDependency dependency,
        object? targetObject,
        object? anchorObject,
        object? rootObject,
        out T value)
    {
        value = default!;
        if (!TryResolveDependencySource(dependency, targetObject, anchorObject, rootObject, out var resolved))
        {
            return false;
        }

        if (resolved is T typed)
        {
            value = typed;
            return true;
        }

        return false;
    }

    private static bool TryResolveTemplatedParentDependencySource(object? anchor, out object? value)
    {
        if (anchor is StyledElement styledAnchor)
        {
            value = ResolveTemplatedParentAnchor(styledAnchor)?.TemplatedParent;
            return value is not null;
        }

        value = null;
        return false;
    }

    private static object? TryGetDataContext(object? value)
    {
        return value switch
        {
            IDataContextProvider provider => provider.DataContext,
            _ => null
        };
    }

    private static bool TryResolveNamedElement(object? scopeRoot, string name, out object? element)
    {
        element = SourceGenNameReferenceHelper.ResolveByName(scopeRoot, name);
        return element is not null;
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

    /// <summary>
    /// Creates the XAML service-provider context used when constructing a non-root object
    /// via an <see cref="IServiceProvider"/> constructor.
    /// </summary>
    public static IServiceProvider CreateObjectConstructionServiceProvider(
        IServiceProvider? parentServiceProvider,
        object rootObject,
        object intermediateRootObject,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        return new ObjectConstructionServiceProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            ResolveBaseUri(
                parentServiceProvider,
                baseUri,
                rootObject,
                intermediateRootObject,
                intermediateRootObject),
            BuildParentStack(intermediateRootObject, parentStack));
    }

    /// <summary>
    /// Creates the type-descriptor context used by source-generated type-converter invocations.
    /// </summary>
    public static ITypeDescriptorContext CreateTypeConverterContext(
        IServiceProvider? parentServiceProvider,
        object? rootObject,
        object? intermediateRootObject,
        object? targetObject,
        object? targetProperty,
        string? baseUri,
        IReadOnlyList<object>? parentStack)
    {
        return CreateContextProvider(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject,
            targetProperty,
            baseUri,
            parentStack);
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
            targetProperty,
            baseUri,
            parentStack,
            serviceProvider);
    }

    private static object? ResolveStaticResourceExtensionImmediate(
        StaticResourceExtension extension,
        object targetObject,
        object? targetProperty,
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

        if (TryResolveStaticResourceWithoutAvaloniaExtension(
                resourceKey,
                targetObject,
                targetProperty,
                baseUri,
                effectiveServiceProvider,
                effectiveParentStack,
                out var resolvedWithoutExtension))
        {
            return resolvedWithoutExtension;
        }

        try
        {
            var value = extension.ProvideValue(effectiveServiceProvider);
            if (!ReferenceEquals(value, AvaloniaProperty.UnsetValue))
            {
                return value;
            }

            if (TryResolveStaticResourceWithoutAvaloniaExtension(
                    resourceKey,
                    targetObject,
                    targetProperty,
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
            if (TryResolveStaticResourceWithoutAvaloniaExtension(
                    resourceKey,
                    targetObject,
                    targetProperty,
                    baseUri,
                    effectiveServiceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            if (targetObject is AvaloniaObject)
            {
                return AvaloniaProperty.UnsetValue;
            }

            throw;
        }
        catch (XamlLoadException)
        {
            if (TryResolveStaticResourceWithoutAvaloniaExtension(
                    resourceKey,
                    targetObject,
                    targetProperty,
                    baseUri,
                    effectiveServiceProvider,
                    effectiveParentStack,
                    out var resolvedFallback))
            {
                return resolvedFallback;
            }

            if (targetObject is AvaloniaObject)
            {
                return AvaloniaProperty.UnsetValue;
            }

            throw;
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
        object? rootObject,
        object? intermediateRootObject,
        object? targetObject,
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
            ResolveBaseUri(parentServiceProvider, baseUri, rootObject, intermediateRootObject, targetObject),
            BuildParentStack(targetObject, parentStack));
    }

    private static Uri ResolveBaseUri(
        IServiceProvider? parentServiceProvider,
        string? baseUri,
        object? rootObject,
        object? intermediateRootObject,
        object? targetObject)
    {
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext parentUriContext &&
            parentUriContext.BaseUri is { } parentBaseUri)
        {
            if (parentBaseUri.IsAbsoluteUri)
            {
                return parentBaseUri;
            }
        }

        if (TryResolveExplicitAbsoluteBaseUri(baseUri, out var explicitBaseUri))
        {
            return explicitBaseUri;
        }

        var fallbackAssemblyName = ResolveFallbackAssemblyName(
            parentServiceProvider,
            rootObject,
            intermediateRootObject,
            targetObject);
        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext relativeParentUriContext &&
            relativeParentUriContext.BaseUri is { } relativeParentBaseUri &&
            TryResolveAbsoluteBaseUri(
                relativeParentBaseUri.OriginalString,
                fallbackAssemblyName,
                out var normalizedParentBaseUri))
        {
            return normalizedParentBaseUri;
        }

        if (TryResolveAbsoluteBaseUri(baseUri, fallbackAssemblyName, out var resolvedBaseUri))
        {
            return resolvedBaseUri;
        }

        return new Uri("avares://" + fallbackAssemblyName + "/");
    }

    private static bool TryResolveExplicitAbsoluteBaseUri(string? baseUri, out Uri resolvedBaseUri)
    {
        resolvedBaseUri = default!;
        if (string.IsNullOrWhiteSpace(baseUri))
        {
            return false;
        }

        var trimmedBaseUri = baseUri.Trim();
        if (!HasExplicitUriScheme(trimmedBaseUri) ||
            !Uri.TryCreate(trimmedBaseUri, UriKind.Absolute, out var absoluteBaseUri))
        {
            return false;
        }

        resolvedBaseUri = absoluteBaseUri;
        return true;
    }

    private static bool TryResolveAbsoluteBaseUri(
        string? baseUri,
        string fallbackAssemblyName,
        out Uri resolvedBaseUri)
    {
        resolvedBaseUri = default!;
        if (string.IsNullOrWhiteSpace(baseUri))
        {
            return false;
        }

        var trimmedBaseUri = baseUri.Trim();
        if (HasExplicitUriScheme(trimmedBaseUri) &&
            Uri.TryCreate(trimmedBaseUri, UriKind.Absolute, out var absoluteBaseUri))
        {
            resolvedBaseUri = absoluteBaseUri;
            return true;
        }

        if (!Uri.TryCreate(trimmedBaseUri, UriKind.RelativeOrAbsolute, out var candidateUri) ||
            candidateUri.IsAbsoluteUri)
        {
            return false;
        }

        var normalizedPath = trimmedBaseUri.Replace('\\', '/').TrimStart('/');
        resolvedBaseUri = normalizedPath.Length == 0
            ? new Uri("avares://" + fallbackAssemblyName + "/")
            : new Uri("avares://" + fallbackAssemblyName + "/" + normalizedPath);
        return true;
    }

    private static bool TryResolveStaticResourceWithoutAvaloniaExtension(
        object resourceKey,
        object targetObject,
        object? targetProperty,
        string? baseUri,
        IServiceProvider? serviceProvider,
        IReadOnlyList<object>? parentStack,
        out object? resolved)
    {
        if (!TryResolveStaticResourceFallback(
                resourceKey,
                targetObject,
                baseUri,
                serviceProvider,
                parentStack,
                out var fallbackResolved))
        {
            resolved = null;
            return false;
        }

        resolved = CoerceStaticResourceResolvedValue(fallbackResolved, targetObject, targetProperty);
        return true;
    }

    private static object? CoerceStaticResourceResolvedValue(
        object? value,
        object targetObject,
        object? targetProperty)
    {
        var targetType = GetStaticResourceTargetType(targetObject, targetProperty);
        return ColorToBrushConverter.Convert(value, targetType);
    }

    private static Type? GetStaticResourceTargetType(object targetObject, object? targetProperty)
    {
        if (targetObject is Setter { Property: { } setterProperty })
        {
            return setterProperty.PropertyType;
        }

        return targetProperty switch
        {
            AvaloniaProperty avaloniaProperty => avaloniaProperty.PropertyType,
            IPropertyInfo propertyInfo => propertyInfo.PropertyType,
            _ => null
        };
    }

    private static bool HasExplicitUriScheme(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.IndexOf("://", StringComparison.Ordinal) > 0;
    }

    private static string ResolveFallbackAssemblyName(
        IServiceProvider? parentServiceProvider,
        object? rootObject,
        object? intermediateRootObject,
        object? targetObject)
    {
        if (TryGetAssemblyName(rootObject, out var rootAssemblyName))
        {
            return rootAssemblyName;
        }

        if (TryGetAssemblyName(intermediateRootObject, out var intermediateAssemblyName))
        {
            return intermediateAssemblyName;
        }

        if (TryGetAssemblyName(targetObject, out var targetAssemblyName))
        {
            return targetAssemblyName;
        }

        if (parentServiceProvider?.GetService(typeof(IRootObjectProvider)) is IRootObjectProvider rootObjectProvider)
        {
            if (TryGetAssemblyName(rootObjectProvider.RootObject, out var providerRootAssemblyName))
            {
                return providerRootAssemblyName;
            }

            if (TryGetAssemblyName(rootObjectProvider.IntermediateRootObject, out var providerIntermediateAssemblyName))
            {
                return providerIntermediateAssemblyName;
            }
        }

        if (parentServiceProvider?.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget provideValueTarget &&
            TryGetAssemblyName(provideValueTarget.TargetObject, out var providerTargetAssemblyName))
        {
            return providerTargetAssemblyName;
        }

        if (parentServiceProvider?.GetService(typeof(IUriContext)) is IUriContext uriContext &&
            uriContext.BaseUri is { IsAbsoluteUri: true } providerBaseUri &&
            !string.IsNullOrWhiteSpace(providerBaseUri.Host))
        {
            return providerBaseUri.Host;
        }

        return "sourcegen";
    }

    private static bool TryGetAssemblyName(object? candidate, out string assemblyName)
    {
        assemblyName = string.Empty;
        if (candidate is null || ReferenceEquals(candidate, AvaloniaProperty.UnsetValue))
        {
            return false;
        }

        var candidateType = candidate as Type ?? candidate.GetType();
        var resolvedAssemblyName = candidateType.Assembly.GetName().Name;
        if (string.IsNullOrWhiteSpace(resolvedAssemblyName))
        {
            return false;
        }

        assemblyName = resolvedAssemblyName;
        return true;
    }

    private static object[] BuildParentStack(object? targetObject, IReadOnlyList<object>? parentStack)
    {
        if (parentStack is null || parentStack.Count == 0)
        {
            return targetObject is null ? [] : [targetObject];
        }

        var stack = new object[parentStack.Count];
        for (var index = 0; index < parentStack.Count; index++)
        {
            stack[index] = parentStack[index];
        }

        return stack;
    }

    private static IReadOnlyList<object>? BuildDetachedDynamicResourceLookupChain(
        object targetObject,
        IServiceProvider? parentServiceProvider,
        IReadOnlyList<object>? parentStack)
    {
        List<object>? lookupChain = null;
        HashSet<object>? seen = null;

        void AddLookupCandidate(object? candidate)
        {
            if (candidate is not IResourceProvider &&
                candidate is not IResourceHost)
            {
                return;
            }

            seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
            if (!seen.Add(candidate))
            {
                return;
            }

            lookupChain ??= new List<object>();
            lookupChain.Add(candidate);
        }

        AddLookupCandidate(targetObject);

        if (parentStack is not null)
        {
            for (var index = 0; index < parentStack.Count; index++)
            {
                AddLookupCandidate(parentStack[index]);
            }
        }

        if (parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlParentStackProvider)) is IAvaloniaXamlIlParentStackProvider upstreamStack)
        {
            // Detached resource-backed objects (for example SolidColorBrush entries inside theme
            // dictionaries) still need the ambient merged-dictionary providers from the outer load.
            foreach (var upstreamParent in upstreamStack.Parents)
            {
                AddLookupCandidate(upstreamParent);
            }
        }

        return lookupChain;
    }

    private static ThemeVariant? ResolveDetachedDynamicResourceThemeVariant(IReadOnlyList<object> lookupChain)
    {
        for (var index = 0; index < lookupChain.Count; index++)
        {
            if (lookupChain[index] is IThemeVariantProvider { Key: { } setKey })
            {
                return setKey;
            }
        }

        return null;
    }

    private static BindingPriority ResolveDynamicResourcePriority(IServiceProvider? parentServiceProvider)
    {
        return parentServiceProvider?.GetService(typeof(IAvaloniaXamlIlControlTemplateProvider)) is not null
            ? BindingPriority.Template
            : BindingPriority.LocalValue;
    }

    private sealed class DetachedDynamicResourceObservable : IObservable<object?>
    {
        private readonly object _resourceKey;
        private readonly object[] _lookupChain;
        private readonly Type? _targetType;

        public DetachedDynamicResourceObservable(
            object resourceKey,
            IReadOnlyList<object> lookupChain,
            Type? targetType)
        {
            _resourceKey = resourceKey;
            _lookupChain = new object[lookupChain.Count];
            for (var index = 0; index < lookupChain.Count; index++)
            {
                _lookupChain[index] = lookupChain[index];
            }

            _targetType = targetType;
        }

        public IDisposable Subscribe(IObserver<object?> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            return new DetachedDynamicResourceSubscription(_resourceKey, _lookupChain, _targetType, observer);
        }
    }

    private sealed class DetachedDynamicResourceSubscription : IDisposable
    {
        private readonly object _resourceKey;
        private readonly object[] _lookupChain;
        private readonly Type? _targetType;
        private readonly IObserver<object?> _observer;
        private readonly List<IResourceProvider> _providers = new();
        private readonly List<IResourceHost> _hosts = new();
        private readonly List<IThemeVariantHost> _themeHosts = new();
        private bool _disposed;

        public DetachedDynamicResourceSubscription(
            object resourceKey,
            object[] lookupChain,
            Type? targetType,
            IObserver<object?> observer)
        {
            _resourceKey = resourceKey;
            _lookupChain = lookupChain;
            _targetType = targetType;
            _observer = observer;

            SubscribeToProviders();
            RefreshHostSubscriptions();
            PublishCurrent();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            for (var index = 0; index < _providers.Count; index++)
            {
                _providers[index].OwnerChanged -= OnProviderOwnerChanged;
            }

            UnsubscribeHosts();
        }

        private void SubscribeToProviders()
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < _lookupChain.Length; index++)
            {
                if (_lookupChain[index] is not IResourceProvider provider ||
                    !seen.Add(provider))
                {
                    continue;
                }

                provider.OwnerChanged += OnProviderOwnerChanged;
                _providers.Add(provider);
            }
        }

        private void RefreshHostSubscriptions()
        {
            UnsubscribeHosts();

            var subscribeToThemeChanges = ResolveDetachedDynamicResourceThemeVariant(_lookupChain) is null;
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < _lookupChain.Length; index++)
            {
                if (_lookupChain[index] is IResourceHost resourceHost &&
                    seen.Add(resourceHost))
                {
                    SubscribeHost(resourceHost, subscribeToThemeChanges);
                }

                if (_lookupChain[index] is IResourceProvider { Owner: { } owner } &&
                    seen.Add(owner))
                {
                    SubscribeHost(owner, subscribeToThemeChanges);
                }
            }
        }

        private void SubscribeHost(IResourceHost resourceHost, bool subscribeToThemeChanges)
        {
            resourceHost.ResourcesChanged += OnResourcesChanged;
            _hosts.Add(resourceHost);

            if (subscribeToThemeChanges &&
                resourceHost is IThemeVariantHost themeVariantHost)
            {
                themeVariantHost.ActualThemeVariantChanged += OnThemeVariantChanged;
                _themeHosts.Add(themeVariantHost);
            }
        }

        private void UnsubscribeHosts()
        {
            for (var index = 0; index < _hosts.Count; index++)
            {
                _hosts[index].ResourcesChanged -= OnResourcesChanged;
            }

            for (var index = 0; index < _themeHosts.Count; index++)
            {
                _themeHosts[index].ActualThemeVariantChanged -= OnThemeVariantChanged;
            }

            _hosts.Clear();
            _themeHosts.Clear();
        }

        private void OnProviderOwnerChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            RefreshHostSubscriptions();
            PublishCurrent();
        }

        private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            PublishCurrent();
        }

        private void OnThemeVariantChanged(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            PublishCurrent();
        }

        private void PublishCurrent()
        {
            _observer.OnNext(ResolveCurrentValue());
        }

        private object? ResolveCurrentValue()
        {
            var themeVariant = ResolveDetachedDynamicResourceThemeVariant(_lookupChain) ?? ResolveCurrentThemeVariant();
            for (var index = 0; index < _lookupChain.Length; index++)
            {
                if (TryResolveResourceValue(_lookupChain[index], themeVariant, out var resolved))
                {
                    return ColorToBrushConverter.Convert(resolved, _targetType);
                }
            }

            return AvaloniaProperty.UnsetValue;
        }

        private ThemeVariant? ResolveCurrentThemeVariant()
        {
            for (var index = 0; index < _lookupChain.Length; index++)
            {
                if (_lookupChain[index] is IThemeVariantHost themeVariantHost)
                {
                    return themeVariantHost.ActualThemeVariant;
                }

                if (_lookupChain[index] is IResourceProvider { Owner: IThemeVariantHost ownerThemeVariantHost })
                {
                    return ownerThemeVariantHost.ActualThemeVariant;
                }
            }

            return null;
        }

        private bool TryResolveResourceValue(object lookupEntry, ThemeVariant? themeVariant, out object? value)
        {
            if (lookupEntry is IResourceHost resourceHost &&
                resourceHost.TryFindResource(_resourceKey, themeVariant, out value))
            {
                return true;
            }

            if (lookupEntry is IResourceProvider resourceProvider)
            {
                if (resourceProvider.TryGetResource(_resourceKey, themeVariant, out value))
                {
                    return true;
                }

                if (resourceProvider.Owner is { } owner &&
                    owner.TryFindResource(_resourceKey, themeVariant, out value))
                {
                    return true;
                }
            }

            value = null;
            return false;
        }
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
            if (HasExplicitUriScheme(trimmed) &&
                Uri.TryCreate(trimmed, UriKind.RelativeOrAbsolute, out var resolved))
            {
                if (resolved.IsAbsoluteUri)
                {
                    return resolved;
                }
            }

            var normalizedPath = trimmed.Replace('\\', '/').TrimStart('/');
            if (normalizedPath.Length > 0)
            {
                return new Uri("avares://" + localAssemblyName + "/" + normalizedPath);
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
                    targetProperty: null,
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
        ITypeDescriptorContext,
        IProvideValueTarget,
        IRootObjectProvider,
        IUriContext,
        IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly object? _rootObject;
        private readonly object? _intermediateRootObject;
        private readonly object? _targetObject;
        private readonly object? _targetProperty;
        private readonly object[] _parentStack;
        private readonly Uri _baseUri;
        private readonly PropertyDescriptor? _propertyDescriptor;

        public MarkupExtensionServiceProvider(
            IServiceProvider? parentServiceProvider,
            object? rootObject,
            object? intermediateRootObject,
            object? targetObject,
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
            _propertyDescriptor = BuildPropertyDescriptor(targetProperty, targetObject);
        }

        public object TargetObject => _targetObject!;

        public object TargetProperty => _targetProperty ?? AvaloniaProperty.UnsetValue;

        public object RootObject => _rootObject!;

        public object IntermediateRootObject => _intermediateRootObject!;

        public IContainer? Container => null;

        public object? Instance => _targetObject;

        public PropertyDescriptor? PropertyDescriptor => _propertyDescriptor;

        public Uri BaseUri
        {
            get => _baseUri;
            set { }
        }

        public IEnumerable<object> Parents => EnumerateParents();

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(ITypeDescriptorContext))
            {
                return this;
            }

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

        public void OnComponentChanged()
        {
            throw new NotSupportedException();
        }

        public bool OnComponentChanging()
        {
            throw new NotSupportedException();
        }

        private IEnumerable<object> EnumerateParents()
        {
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            for (var index = 0; index < _parentStack.Length; index++)
            {
                var parent = _parentStack[index];
                if (parent is null)
                {
                    continue;
                }

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

        private static PropertyDescriptor? BuildPropertyDescriptor(object? targetProperty, object? targetObject)
        {
            return targetProperty switch
            {
                null => null,
                PropertyDescriptor propertyDescriptor => propertyDescriptor,
                IPropertyInfo propertyInfo => new SourceGenPropertyDescriptor(
                    propertyInfo,
                    ResolveComponentType(propertyInfo, targetObject)),
                _ => null
            };
        }

        private static Type ResolveComponentType(IPropertyInfo propertyInfo, object? targetObject)
        {
            if (targetObject is not null)
            {
                return targetObject.GetType();
            }

            if (propertyInfo is AvaloniaProperty avaloniaProperty)
            {
                return avaloniaProperty.OwnerType;
            }

            return typeof(object);
        }
    }

    private sealed class SourceGenPropertyDescriptor : PropertyDescriptor
    {
        private readonly IPropertyInfo _propertyInfo;
        private readonly Type _componentType;

        public SourceGenPropertyDescriptor(IPropertyInfo propertyInfo, Type componentType)
            : base(propertyInfo.Name, null)
        {
            _propertyInfo = propertyInfo;
            _componentType = componentType;
        }

        public override Type ComponentType => _componentType;

        public override bool IsReadOnly => !_propertyInfo.CanSet;

        public override Type PropertyType => _propertyInfo.PropertyType;

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object? GetValue(object? component)
        {
            if (component is null)
            {
                return null;
            }

            return _propertyInfo.Get(component);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object? component, object? value)
        {
            if (component is null || !_propertyInfo.CanSet)
            {
                return;
            }

            _propertyInfo.Set(component, value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }
    }

    private sealed class ObjectConstructionServiceProvider :
        IServiceProvider,
        IRootObjectProvider,
        IUriContext,
        IAvaloniaXamlIlParentStackProvider
    {
        private readonly IServiceProvider? _parentServiceProvider;
        private readonly object _rootObject;
        private readonly object _intermediateRootObject;
        private readonly object[] _parentStack;
        private readonly Uri _baseUri;

        public ObjectConstructionServiceProvider(
            IServiceProvider? parentServiceProvider,
            object rootObject,
            object intermediateRootObject,
            Uri baseUri,
            object[] parentStack)
        {
            _parentServiceProvider = parentServiceProvider;
            _rootObject = rootObject;
            _intermediateRootObject = intermediateRootObject;
            _baseUri = baseUri;
            _parentStack = parentStack;
        }

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

                if (_intermediateRootObject is StyledElement styledElement &&
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
