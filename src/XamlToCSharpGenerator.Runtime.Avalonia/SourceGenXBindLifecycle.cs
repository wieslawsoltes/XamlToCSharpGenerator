using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;

namespace XamlToCSharpGenerator.Runtime;

internal readonly record struct SourceGenAttachedBindingMetadata(
    INameScope? NameScope,
    IReadOnlyDictionary<string, string>? XmlNamespaces);

internal readonly record struct SourceGenPreparedXBindBinding(
    object Binding,
    ISourceGenXBindBindBackController? BindBackController);

internal sealed class SourceGenProvidedXBindBinding
{
    private readonly object _rootObject;
    private readonly Func<SourceGenAttachedBindingMetadata, SourceGenPreparedXBindBinding> _factory;
    private AvaloniaObject? _target;
    private AvaloniaProperty? _property;
    private object? _anchor;
    private IDisposable? _bindingSubscription;
    private ISourceGenXBindBindBackController? _bindBackController;
    private bool _isApplied;
    private SourceGenAttachedBindingMetadata _attachedMetadata;

    public SourceGenProvidedXBindBinding(object rootObject, Func<SourceGenAttachedBindingMetadata, SourceGenPreparedXBindBinding> factory)
    {
        _rootObject = rootObject ?? throw new ArgumentNullException(nameof(rootObject));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    internal void AttachMetadata(INameScope? nameScope, IReadOnlyDictionary<string, string>? xmlNamespaces)
    {
        if (nameScope is null && xmlNamespaces is null)
        {
            return;
        }

        _attachedMetadata = new SourceGenAttachedBindingMetadata(
            nameScope ?? _attachedMetadata.NameScope,
            xmlNamespaces ?? _attachedMetadata.XmlNamespaces);
    }

    internal void Apply(AvaloniaObject target, AvaloniaProperty property, object? anchor)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _property = property ?? throw new ArgumentNullException(nameof(property));
        _anchor = anchor;
        ReapplyCurrentBinding();
        SourceGenXBindLifecycleRegistry.Register(_rootObject, this);
    }

    internal void Initialize()
    {
        if (_isApplied)
        {
            return;
        }

        ReapplyCurrentBinding();
    }

    internal void Update()
    {
        _bindBackController?.FlushExplicitPendingValue();
        ReapplyCurrentBinding();
    }

    internal void StopTracking()
    {
        var hadBindingSubscription = _bindingSubscription switch
        {
            SourceGenBindingApplicationHandle applicationHandle => applicationHandle.HasAttachedBinding,
            _ => _bindingSubscription is not null
        };
        _bindingSubscription?.Dispose();
        _bindingSubscription = null;

        _bindBackController?.Dispose();
        _bindBackController = null;

        if (!hadBindingSubscription &&
            _target is not null &&
            _property is not null)
        {
            _target.ClearValue(_property);
        }

        _isApplied = false;
    }

    private void ReapplyCurrentBinding()
    {
        if (_target is null || _property is null)
        {
            return;
        }

        _bindingSubscription?.Dispose();
        _bindingSubscription = null;

        _bindBackController?.Dispose();
        _bindBackController = null;

        var preparedBinding = _factory(_attachedMetadata);
        _bindBackController = preparedBinding.BindBackController;
        _bindingSubscription = SourceGenMarkupExtensionRuntime.ApplyBindingCore(
            _target,
            _property,
            preparedBinding.Binding,
            _anchor);
        _isApplied = true;
    }
}

internal static class SourceGenXBindLifecycleRegistry
{
    private static readonly ConditionalWeakTable<object, XBindRootState> States = new();

    internal static void Register(object rootObject, SourceGenProvidedXBindBinding binding)
    {
        var state = States.GetOrCreateValue(rootObject);
        state.Register(binding);
    }

    internal static void Initialize(object rootObject)
    {
        if (States.TryGetValue(rootObject, out var state))
        {
            state.Initialize();
        }
    }

    internal static void Update(object rootObject)
    {
        if (States.TryGetValue(rootObject, out var state))
        {
            state.Update();
        }
    }

    internal static void StopTracking(object rootObject)
    {
        if (States.TryGetValue(rootObject, out var state))
        {
            state.StopTracking();
        }
    }

    internal static void Reset(object rootObject)
    {
        if (States.TryGetValue(rootObject, out var state))
        {
            state.StopTracking();
            state.Clear();
            States.Remove(rootObject);
        }
    }

    private sealed class XBindRootState
    {
        private readonly List<SourceGenProvidedXBindBinding> _bindings = new();

        public void Register(SourceGenProvidedXBindBinding binding)
        {
            if (!_bindings.Contains(binding))
            {
                _bindings.Add(binding);
            }
        }

        public void Initialize()
        {
            foreach (var binding in _bindings)
            {
                binding.Initialize();
            }
        }

        public void Update()
        {
            foreach (var binding in _bindings)
            {
                binding.Update();
            }
        }

        public void StopTracking()
        {
            foreach (var binding in _bindings)
            {
                binding.StopTracking();
            }
        }

        public void Clear()
        {
            _bindings.Clear();
        }
    }
}
