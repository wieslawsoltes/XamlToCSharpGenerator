using System;
using XamlToCSharpGenerator.Runtime;

namespace XamlToCSharpGenerator.Tests.Runtime;

[Collection("RuntimeStateful")]
public class XamlSourceGenHotReloadManagerTests
{
    [Fact]
    public void UpdateApplication_Reloads_Registered_Instance_For_Matching_Type()
    {
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new ReloadTargetA();
        XamlSourceGenHotReloadManager.Register(instance, target =>
        {
            ((ReloadTargetA)target).ReloadCount++;
            reloadCount++;
        });

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetA)]);

        Assert.Equal(1, reloadCount);
        Assert.Equal(1, instance.ReloadCount);
    }

    [Fact]
    public void UpdateApplication_Does_Not_Reload_When_Disabled()
    {
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Disable();

        var reloadCount = 0;
        var instance = new ReloadTargetB();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetB)]);

        Assert.Equal(0, reloadCount);
    }

    [Fact]
    public void UpdateApplication_With_Null_Types_Reloads_All_Tracked_Types()
    {
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Enable();

        var firstCount = 0;
        var secondCount = 0;
        var first = new ReloadTargetC();
        var second = new ReloadTargetD();

        XamlSourceGenHotReloadManager.Register(first, _ => firstCount++);
        XamlSourceGenHotReloadManager.Register(second, _ => secondCount++);

        XamlSourceGenHotReloadManager.UpdateApplication(null);

        Assert.Equal(1, firstCount);
        Assert.Equal(1, secondCount);
    }

    [Fact]
    public void UpdateApplication_Normalizes_Generic_Type_Keys()
    {
        XamlSourceGenHotReloadManager.ClearRegistrations();
        XamlSourceGenHotReloadManager.Enable();

        var reloadCount = 0;
        var instance = new GenericReloadTarget<int>();
        XamlSourceGenHotReloadManager.Register(instance, _ => reloadCount++);

        XamlSourceGenHotReloadManager.UpdateApplication([typeof(GenericReloadTarget<int>)]);

        Assert.Equal(1, reloadCount);
    }

    [Fact]
    public void UpdateApplication_Raises_HotReloaded_Event()
    {
        XamlSourceGenHotReloadManager.ClearRegistrations();
        Type[]? observedTypes = null;

        void Handler(Type[]? updatedTypes)
        {
            observedTypes = updatedTypes;
        }

        XamlSourceGenHotReloadManager.Enable();
        XamlSourceGenHotReloadManager.HotReloaded += Handler;
        try
        {
            XamlSourceGenHotReloadManager.UpdateApplication([typeof(ReloadTargetEvent)]);
        }
        finally
        {
            XamlSourceGenHotReloadManager.HotReloaded -= Handler;
        }

        Assert.NotNull(observedTypes);
        Assert.Single(observedTypes!);
        Assert.Equal(typeof(ReloadTargetEvent), observedTypes![0]);
    }

    private sealed class ReloadTargetA
    {
        public int ReloadCount { get; set; }
    }

    private sealed class ReloadTargetB
    {
    }

    private sealed class ReloadTargetC
    {
    }

    private sealed class ReloadTargetD
    {
    }

    private sealed class ReloadTargetEvent
    {
    }

    private sealed class GenericReloadTarget<T>
    {
    }
}
