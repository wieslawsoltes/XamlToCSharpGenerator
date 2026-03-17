using XamlToCSharpGenerator.PreviewerHost;
using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.Tests.PreviewerHost;

using KeyEventMessage = global::Avalonia.Remote.Protocol.Input.KeyEventMessage;
using RemoteKey = global::Avalonia.Remote.Protocol.Input.Key;
using RemoteInputModifiers = global::Avalonia.Remote.Protocol.Input.InputModifiers;
using RemotePhysicalKey = global::Avalonia.Remote.Protocol.Input.PhysicalKey;
using TextInputEventMessage = global::Avalonia.Remote.Protocol.Input.TextInputEventMessage;

public sealed class PreviewKeyboardInputMapperTests
{
    [Fact]
    public void TryCreateKeyEvent_Uses_Logical_Key_From_Key_And_Physical_Key_From_Code()
    {
        var request = new AxsgPreviewHostInputRequest
        {
            EventType = "key",
            IsDown = true,
            Key = "a",
            Code = "KeyQ",
            Location = 0,
            KeySymbol = "a",
            Modifiers = new AxsgPreviewHostInputModifiers(
                Alt: false,
                Control: true,
                Shift: false,
                Meta: false)
        };

        bool created = PreviewKeyboardInputMapper.TryCreateKeyEvent(request, out KeyEventMessage keyEvent);

        Assert.True(created);
        Assert.True(keyEvent.IsDown);
        Assert.Equal(RemoteKey.A, keyEvent.Key);
        Assert.Equal(RemotePhysicalKey.Q, keyEvent.PhysicalKey);
        Assert.Equal("a", keyEvent.KeySymbol);
        Assert.Equal([RemoteInputModifiers.Control], keyEvent.Modifiers);
    }

    [Fact]
    public void TryCreateKeyEvent_Maps_Right_Sided_Modifier_From_Code()
    {
        var request = new AxsgPreviewHostInputRequest
        {
            EventType = "key",
            IsDown = false,
            Key = "Shift",
            Code = "ShiftRight",
            Location = 2,
            Modifiers = new AxsgPreviewHostInputModifiers(
                Alt: false,
                Control: false,
                Shift: true,
                Meta: false)
        };

        bool created = PreviewKeyboardInputMapper.TryCreateKeyEvent(request, out KeyEventMessage keyEvent);

        Assert.True(created);
        Assert.False(keyEvent.IsDown);
        Assert.Equal(RemoteKey.RightShift, keyEvent.Key);
        Assert.Equal(RemotePhysicalKey.ShiftRight, keyEvent.PhysicalKey);
        Assert.Equal([RemoteInputModifiers.Shift], keyEvent.Modifiers);
    }

    [Fact]
    public void TryCreateKeyEvent_Maps_Numpad_Digit_From_Location()
    {
        var request = new AxsgPreviewHostInputRequest
        {
            EventType = "key",
            IsDown = true,
            Key = "1",
            Code = "Numpad1",
            Location = 3
        };

        bool created = PreviewKeyboardInputMapper.TryCreateKeyEvent(request, out KeyEventMessage keyEvent);

        Assert.True(created);
        Assert.Equal(RemoteKey.NumPad1, keyEvent.Key);
        Assert.Equal(RemotePhysicalKey.NumPad1, keyEvent.PhysicalKey);
    }

    [Fact]
    public void CreateTextInputEvent_Maps_Text_And_Meta_Modifier()
    {
        var request = new AxsgPreviewHostInputRequest
        {
            EventType = "text",
            Text = "\r",
            Modifiers = new AxsgPreviewHostInputModifiers(
                Alt: false,
                Control: false,
                Shift: false,
                Meta: true)
        };

        TextInputEventMessage? textEvent = PreviewKeyboardInputMapper.CreateTextInputEvent(request);

        Assert.NotNull(textEvent);
        Assert.Equal("\r", textEvent!.Text);
        Assert.Equal([RemoteInputModifiers.Windows], textEvent.Modifiers);
    }
}
