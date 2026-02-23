using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotReloadStateTrackerTests
{
    private static readonly SourceGenHotReloadCleanupDescriptor[] EmptyDescriptors =
        Array.Empty<SourceGenHotReloadCleanupDescriptor>();

    [Fact]
    public void Reconcile_Clears_Removed_Collection_Members_Including_ResourceDictionaries()
    {
        var target = new CollectionStateTarget();
        target.Styles.Add("accent");
        target.Resources["Accent"] = "Orange";
        target.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            ["Nested"] = "value"
        });

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            [
                new SourceGenHotReloadCleanupDescriptor("Styles", static instance =>
                {
                    var typed = (CollectionStateTarget)instance;
                    XamlSourceGenHotReloadStateTracker.TryClearCollection(typed.Styles);
                }),
                new SourceGenHotReloadCleanupDescriptor("Resources", static instance =>
                {
                    var typed = (CollectionStateTarget)instance;
                    XamlSourceGenHotReloadStateTracker.TryClearCollection(typed.Resources);
                })
            ],
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        target.Styles.Add("warning");
        target.Resources["Warning"] = "Red";
        target.Resources.MergedDictionaries.Add(new ResourceDictionary());

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Empty(target.Styles);
        Assert.Empty(target.Resources);
        Assert.Empty(target.Resources.MergedDictionaries);
    }

    [Fact]
    public void Reconcile_Clears_Removed_StyledElement_Styles_And_Style_Resources()
    {
        var target = new StyledElementStateTarget();
        target.Styles.Add(new Style());
        target.Styles.Resources["Accent"] = "Orange";

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            [
                new SourceGenHotReloadCleanupDescriptor("Styles", static instance =>
                {
                    var typed = (StyledElementStateTarget)instance;
                    XamlSourceGenHotReloadStateTracker.TryClearCollection(typed.Styles);
                })
            ],
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        target.Styles.Add(new Style());
        target.Styles.Resources["Warning"] = "Red";

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Empty(target.Styles);
        Assert.Empty(target.Styles.Resources);
    }

    [Fact]
    public void Reconcile_Clears_Removed_Clr_Property_Assignments()
    {
        var target = new CollectionStateTarget { Title = "Dashboard" };

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            [
                new SourceGenHotReloadCleanupDescriptor("Title", static instance =>
                {
                    ((CollectionStateTarget)instance).Title = default;
                })
            ],
            EmptyDescriptors,
            clearSelfCollection: false);

        target.Title = "Updated";

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Null(target.Title);
    }

    [Fact]
    public void Reconcile_Clears_Removed_Clr_Field_Assignments()
    {
        var target = new FieldStateTarget
        {
            Title = "Dashboard",
            Count = 42
        };

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            [
                new SourceGenHotReloadCleanupDescriptor("Title", static instance =>
                {
                    ((FieldStateTarget)instance).Title = default;
                }),
                new SourceGenHotReloadCleanupDescriptor("Count", static instance =>
                {
                    ((FieldStateTarget)instance).Count = default;
                })
            ],
            EmptyDescriptors,
            clearSelfCollection: false);

        target.Title = "Updated";
        target.Count = 7;

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Null(target.Title);
        Assert.Equal(0, target.Count);
    }

    [Fact]
    public void Reconcile_Clears_Removed_Avalonia_Property_Assignments()
    {
        var target = new AvaloniaStateTarget
        {
            Text = "Initial"
        };

        var token = "global::" + typeof(AvaloniaStateTarget).FullName + ".TextProperty";
        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            [
                new SourceGenHotReloadCleanupDescriptor(token, static instance =>
                {
                    if (instance is AvaloniaObject avaloniaObject)
                    {
                        avaloniaObject.ClearValue(AvaloniaStateTarget.TextProperty);
                    }
                })
            ],
            clearSelfCollection: false);

        target.Text = "Updated";

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Null(target.Text);
    }

    [Fact]
    public void Reconcile_Detaches_Removed_Root_Clr_Event_Subscriptions()
    {
        var target = new EventSubscriptionTarget();

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false,
            rootEventSubscriptions:
            [
                new SourceGenHotReloadCleanupDescriptor("C|Tick|OnTick|||", static instance =>
                {
                    ((EventSubscriptionTarget)instance).Detach();
                })
            ]);

        target.Attach();
        target.RaiseTick();
        Assert.Equal(1, target.Invocations);

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false,
            rootEventSubscriptions: EmptyDescriptors);

        target.RaiseTick();
        Assert.Equal(1, target.Invocations);
    }

    [Fact]
    public void Reconcile_Calls_Clear_Method_For_Custom_Collection_Types()
    {
        var target = new CustomCollectionTarget();
        target.Tokens.Add("alpha");

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            [
                new SourceGenHotReloadCleanupDescriptor("Tokens", static instance =>
                {
                    ((CustomCollectionTarget)instance).Tokens.Clear();
                })
            ],
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Equal(1, target.Tokens.ClearCalls);
        Assert.Empty(target.Tokens.Values);
    }

    [Fact]
    public void Reconcile_Swallows_ItemsSourceStyle_Collection_Exceptions()
    {
        var target = new ThrowingListTarget();

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            [
                new SourceGenHotReloadCleanupDescriptor("Items", static instance =>
                {
                    var typed = (ThrowingListTarget)instance;
                    XamlSourceGenHotReloadStateTracker.TryClearCollection(typed.Items);
                })
            ],
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        var exception = Record.Exception(() =>
            XamlSourceGenHotReloadStateTracker.Reconcile(
                target,
                EmptyDescriptors,
                EmptyDescriptors,
                EmptyDescriptors,
                clearSelfCollection: false));

        Assert.Null(exception);
    }

    [Fact]
    public void Reconcile_Clears_Root_Collection_When_Previous_Pass_Required_Self_Clear()
    {
        var target = new RootCollectionStateTarget
        {
            "A",
            "B"
        };

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: true);

        target.Add("C");

        XamlSourceGenHotReloadStateTracker.Reconcile(
            target,
            EmptyDescriptors,
            EmptyDescriptors,
            EmptyDescriptors,
            clearSelfCollection: false);

        Assert.Empty(target);
    }

    private sealed class CollectionStateTarget
    {
        public List<string> Styles { get; } = [];

        public ResourceDictionary Resources { get; } = [];

        public string? Title { get; set; }
    }

    private sealed class ThrowingListTarget
    {
        public ThrowingList Items { get; } = new();
    }

    private sealed class FieldStateTarget
    {
        public string? Title;

        public int Count;
    }

    private sealed class StyledElementStateTarget : StyledElement
    {
    }

    private sealed class ThrowingList : IList
    {
        public int Count => 0;

        public object SyncRoot { get; } = new();

        public bool IsSynchronized => false;

        public bool IsReadOnly => throw new InvalidOperationException("ItemsSource is active.");

        public bool IsFixedSize => false;

        public object? this[int index]
        {
            get => null;
            set
            {
            }
        }

        public int Add(object? value)
        {
            return -1;
        }

        public void Clear()
        {
            throw new InvalidOperationException("ItemsSource is active.");
        }

        public bool Contains(object? value)
        {
            return false;
        }

        public int IndexOf(object? value)
        {
            return -1;
        }

        public void Insert(int index, object? value)
        {
        }

        public void Remove(object? value)
        {
        }

        public void RemoveAt(int index)
        {
        }

        public void CopyTo(Array array, int index)
        {
        }

        public IEnumerator GetEnumerator()
        {
            return Array.Empty<object>().GetEnumerator();
        }
    }

    private sealed class RootCollectionStateTarget : List<string>
    {
    }

    private sealed class CustomCollectionTarget
    {
        public CustomClearCollection Tokens { get; } = new();
    }

    private sealed class EventSubscriptionTarget
    {
        public event EventHandler? Tick;

        public int Invocations { get; private set; }

        public void Attach()
        {
            Tick += OnTick;
        }

        public void Detach()
        {
            Tick -= OnTick;
        }

        public void RaiseTick()
        {
            Tick?.Invoke(this, EventArgs.Empty);
        }

        private void OnTick(object? sender, EventArgs args)
        {
            Invocations++;
        }
    }

    private sealed class CustomClearCollection
    {
        public List<string> Values { get; } = [];

        public int ClearCalls { get; private set; }

        public void Add(string value)
        {
            Values.Add(value);
        }

        public void Clear()
        {
            ClearCalls++;
            Values.Clear();
        }
    }

    private sealed class AvaloniaStateTarget : AvaloniaObject
    {
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<AvaloniaStateTarget, string?>(nameof(Text));

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }
}
