using XamlToCSharpGenerator.RemoteProtocol.Preview;

namespace XamlToCSharpGenerator.PreviewerHost;

using KeyEventMessage = global::Avalonia.Remote.Protocol.Input.KeyEventMessage;
using RemoteInputModifiers = global::Avalonia.Remote.Protocol.Input.InputModifiers;
using RemoteKey = global::Avalonia.Remote.Protocol.Input.Key;
using RemotePhysicalKey = global::Avalonia.Remote.Protocol.Input.PhysicalKey;
using TextInputEventMessage = global::Avalonia.Remote.Protocol.Input.TextInputEventMessage;

internal static class PreviewKeyboardInputMapper
{
    private static readonly Dictionary<string, RemoteKey> NamedKeys = new(StringComparer.Ordinal)
    {
        ["Backspace"] = RemoteKey.Back,
        ["Tab"] = RemoteKey.Tab,
        ["Enter"] = RemoteKey.Enter,
        ["Escape"] = RemoteKey.Escape,
        ["Esc"] = RemoteKey.Escape,
        ["PageUp"] = RemoteKey.PageUp,
        ["PageDown"] = RemoteKey.PageDown,
        ["End"] = RemoteKey.End,
        ["Home"] = RemoteKey.Home,
        ["ArrowLeft"] = RemoteKey.Left,
        ["Left"] = RemoteKey.Left,
        ["ArrowUp"] = RemoteKey.Up,
        ["Up"] = RemoteKey.Up,
        ["ArrowRight"] = RemoteKey.Right,
        ["Right"] = RemoteKey.Right,
        ["ArrowDown"] = RemoteKey.Down,
        ["Down"] = RemoteKey.Down,
        ["Insert"] = RemoteKey.Insert,
        ["Delete"] = RemoteKey.Delete,
        ["CapsLock"] = RemoteKey.CapsLock,
        ["NumLock"] = RemoteKey.NumLock,
        ["ScrollLock"] = RemoteKey.Scroll,
        ["PrintScreen"] = RemoteKey.PrintScreen,
        ["Pause"] = RemoteKey.Pause,
        ["ContextMenu"] = RemoteKey.Apps,
        ["Apps"] = RemoteKey.Apps,
        ["BrowserBack"] = RemoteKey.BrowserBack,
        ["BrowserForward"] = RemoteKey.BrowserForward,
        ["BrowserRefresh"] = RemoteKey.BrowserRefresh,
        ["BrowserStop"] = RemoteKey.BrowserStop,
        ["BrowserSearch"] = RemoteKey.BrowserSearch,
        ["BrowserFavorites"] = RemoteKey.BrowserFavorites,
        ["BrowserHome"] = RemoteKey.BrowserHome,
        ["AudioVolumeMute"] = RemoteKey.VolumeMute,
        ["AudioVolumeDown"] = RemoteKey.VolumeDown,
        ["AudioVolumeUp"] = RemoteKey.VolumeUp,
        ["MediaTrackNext"] = RemoteKey.MediaNextTrack,
        ["MediaTrackPrevious"] = RemoteKey.MediaPreviousTrack,
        ["MediaStop"] = RemoteKey.MediaStop,
        ["MediaPlayPause"] = RemoteKey.MediaPlayPause,
        ["LaunchMail"] = RemoteKey.LaunchMail,
        ["MediaSelect"] = RemoteKey.SelectMedia,
        ["LaunchApp1"] = RemoteKey.LaunchApplication1,
        ["LaunchApp2"] = RemoteKey.LaunchApplication2
    };

    private static readonly Dictionary<string, RemoteKey> CodeKeys = new(StringComparer.Ordinal)
    {
        ["Minus"] = RemoteKey.OemMinus,
        ["Equal"] = RemoteKey.OemPlus,
        ["BracketLeft"] = RemoteKey.OemOpenBrackets,
        ["BracketRight"] = RemoteKey.OemCloseBrackets,
        ["Backslash"] = RemoteKey.OemPipe,
        ["IntlBackslash"] = RemoteKey.OemBackslash,
        ["Semicolon"] = RemoteKey.OemSemicolon,
        ["Quote"] = RemoteKey.OemQuotes,
        ["Backquote"] = RemoteKey.OemTilde,
        ["Comma"] = RemoteKey.OemComma,
        ["Period"] = RemoteKey.OemPeriod,
        ["Slash"] = RemoteKey.OemQuestion,
        ["Space"] = RemoteKey.Space,
        ["ShiftLeft"] = RemoteKey.LeftShift,
        ["ShiftRight"] = RemoteKey.RightShift,
        ["ControlLeft"] = RemoteKey.LeftCtrl,
        ["ControlRight"] = RemoteKey.RightCtrl,
        ["AltLeft"] = RemoteKey.LeftAlt,
        ["AltRight"] = RemoteKey.RightAlt,
        ["MetaLeft"] = RemoteKey.LWin,
        ["MetaRight"] = RemoteKey.RWin,
        ["OSLeft"] = RemoteKey.LWin,
        ["OSRight"] = RemoteKey.RWin,
        ["NumpadAdd"] = RemoteKey.Add,
        ["NumpadSubtract"] = RemoteKey.Subtract,
        ["NumpadMultiply"] = RemoteKey.Multiply,
        ["NumpadDivide"] = RemoteKey.Divide,
        ["NumpadDecimal"] = RemoteKey.Decimal,
        ["NumpadComma"] = RemoteKey.Separator,
        ["NumpadEnter"] = RemoteKey.Enter,
        ["BrowserBack"] = RemoteKey.BrowserBack,
        ["BrowserForward"] = RemoteKey.BrowserForward,
        ["BrowserRefresh"] = RemoteKey.BrowserRefresh,
        ["BrowserStop"] = RemoteKey.BrowserStop,
        ["BrowserSearch"] = RemoteKey.BrowserSearch,
        ["BrowserFavorites"] = RemoteKey.BrowserFavorites,
        ["BrowserHome"] = RemoteKey.BrowserHome,
        ["AudioVolumeMute"] = RemoteKey.VolumeMute,
        ["AudioVolumeDown"] = RemoteKey.VolumeDown,
        ["AudioVolumeUp"] = RemoteKey.VolumeUp,
        ["MediaTrackNext"] = RemoteKey.MediaNextTrack,
        ["MediaTrackPrevious"] = RemoteKey.MediaPreviousTrack,
        ["MediaStop"] = RemoteKey.MediaStop,
        ["MediaPlayPause"] = RemoteKey.MediaPlayPause,
        ["LaunchMail"] = RemoteKey.LaunchMail,
        ["MediaSelect"] = RemoteKey.SelectMedia,
        ["LaunchApp1"] = RemoteKey.LaunchApplication1,
        ["LaunchApp2"] = RemoteKey.LaunchApplication2
    };

    public static bool TryCreateKeyEvent(AxsgPreviewHostInputRequest request, out KeyEventMessage keyEvent)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.EventType, "key", StringComparison.Ordinal))
        {
            keyEvent = default!;
            return false;
        }

        RemoteKey key = MapKey(request.Key, request.Code, request.Location);
        RemotePhysicalKey physicalKey = MapPhysicalKey(request.Code);
        string? keySymbol = string.IsNullOrEmpty(request.KeySymbol) ? null : request.KeySymbol;

        if (key == RemoteKey.None && physicalKey == RemotePhysicalKey.None && keySymbol is null)
        {
            keyEvent = default!;
            return false;
        }

        keyEvent = new KeyEventMessage
        {
            IsDown = request.IsDown.GetValueOrDefault(),
            Key = key,
            PhysicalKey = physicalKey,
            KeySymbol = keySymbol,
            Modifiers = MapModifiers(request.Modifiers)
        };
        return true;
    }

    public static TextInputEventMessage? CreateTextInputEvent(AxsgPreviewHostInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.EventType, "text", StringComparison.Ordinal) ||
            string.IsNullOrEmpty(request.Text))
        {
            return null;
        }

        return new TextInputEventMessage
        {
            Text = request.Text,
            Modifiers = MapModifiers(request.Modifiers)
        };
    }

    internal static RemoteInputModifiers[]? MapModifiers(AxsgPreviewHostInputModifiers? modifiers)
    {
        if (modifiers is null)
        {
            return null;
        }

        RemoteInputModifiers[] buffer = new RemoteInputModifiers[4];
        int count = 0;

        if (modifiers.Alt)
        {
            buffer[count++] = RemoteInputModifiers.Alt;
        }

        if (modifiers.Control)
        {
            buffer[count++] = RemoteInputModifiers.Control;
        }

        if (modifiers.Shift)
        {
            buffer[count++] = RemoteInputModifiers.Shift;
        }

        if (modifiers.Meta)
        {
            buffer[count++] = RemoteInputModifiers.Windows;
        }

        if (count == 0)
        {
            return null;
        }

        return buffer[..count];
    }

    private static RemoteKey MapKey(string? key, string? code, int? location)
    {
        if (TryMapKeyFromKeyValue(key, code, location, out RemoteKey mapped))
        {
            return mapped;
        }

        return TryMapKeyFromCode(code, out mapped) ? mapped : RemoteKey.None;
    }

    private static RemotePhysicalKey MapPhysicalKey(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return RemotePhysicalKey.None;
        }

        string normalized = NormalizePhysicalKeyCode(code);
        return Enum.TryParse(normalized, ignoreCase: false, out RemotePhysicalKey physicalKey)
            ? physicalKey
            : RemotePhysicalKey.None;
    }

    private static bool TryMapKeyFromKeyValue(string? keyValue, string? code, int? location, out RemoteKey key)
    {
        key = RemoteKey.None;
        if (string.IsNullOrEmpty(keyValue))
        {
            return false;
        }

        if (keyValue.Length == 1)
        {
            char character = keyValue[0];
            if (character == ' ')
            {
                key = RemoteKey.Space;
                return true;
            }

            if (char.IsLetter(character))
            {
                return Enum.TryParse(char.ToUpperInvariant(character).ToString(), ignoreCase: false, out key);
            }

            if (char.IsDigit(character))
            {
                if (IsNumpad(code, location) &&
                    Enum.TryParse("NumPad" + character, ignoreCase: false, out key))
                {
                    return true;
                }

                return Enum.TryParse("D" + character, ignoreCase: false, out key);
            }

            if (IsNumpad(code, location))
            {
                key = character switch
                {
                    '+' => RemoteKey.Add,
                    '-' => RemoteKey.Subtract,
                    '*' => RemoteKey.Multiply,
                    '/' => RemoteKey.Divide,
                    '.' => RemoteKey.Decimal,
                    ',' => RemoteKey.Separator,
                    _ => RemoteKey.None
                };
                return key != RemoteKey.None;
            }
        }

        if (TryMapFunctionKey(keyValue, out key))
        {
            return true;
        }

        if (string.Equals(keyValue, "Shift", StringComparison.Ordinal) ||
            string.Equals(keyValue, "Control", StringComparison.Ordinal) ||
            string.Equals(keyValue, "Alt", StringComparison.Ordinal) ||
            string.Equals(keyValue, "Meta", StringComparison.Ordinal) ||
            string.Equals(keyValue, "OS", StringComparison.Ordinal))
        {
            return TryMapKeyFromCode(code, out key);
        }

        return NamedKeys.TryGetValue(keyValue, out key);
    }

    private static bool TryMapFunctionKey(string keyValue, out RemoteKey key)
    {
        key = RemoteKey.None;
        if (keyValue.Length < 2 || keyValue[0] != 'F')
        {
            return false;
        }

        if (!int.TryParse(keyValue.AsSpan(1), out int functionKeyNumber) || functionKeyNumber is < 1 or > 24)
        {
            return false;
        }

        return Enum.TryParse("F" + functionKeyNumber, ignoreCase: false, out key);
    }

    private static bool TryMapKeyFromCode(string? code, out RemoteKey key)
    {
        key = RemoteKey.None;
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }

        if (CodeKeys.TryGetValue(code, out key))
        {
            return true;
        }

        if (code.StartsWith("Key", StringComparison.Ordinal) &&
            code.Length == 4 &&
            char.IsLetter(code[3]))
        {
            return Enum.TryParse(code.AsSpan(3).ToString(), ignoreCase: false, out key);
        }

        if (code.StartsWith("Digit", StringComparison.Ordinal) &&
            code.Length == "Digit0".Length &&
            char.IsDigit(code[5]))
        {
            return Enum.TryParse("D" + code[5], ignoreCase: false, out key);
        }

        if (code.StartsWith("Numpad", StringComparison.Ordinal) &&
            code.Length == "Numpad0".Length &&
            char.IsDigit(code[6]))
        {
            return Enum.TryParse("NumPad" + code[6], ignoreCase: false, out key);
        }

        if (Enum.TryParse(code, ignoreCase: false, out key))
        {
            return true;
        }

        return false;
    }

    private static bool IsNumpad(string? code, int? location)
    {
        return (!string.IsNullOrEmpty(code) && code.StartsWith("Numpad", StringComparison.Ordinal)) ||
               location == 3;
    }

    private static string NormalizePhysicalKeyCode(string code)
    {
        if (code.StartsWith("Key", StringComparison.Ordinal) &&
            code.Length == 4 &&
            char.IsLetter(code[3]))
        {
            return code.AsSpan(3).ToString();
        }

        if (code.StartsWith("Numpad", StringComparison.Ordinal))
        {
            return "NumPad" + code["Numpad".Length..];
        }

        if (string.Equals(code, "OSLeft", StringComparison.Ordinal))
        {
            return "MetaLeft";
        }

        if (string.Equals(code, "OSRight", StringComparison.Ordinal))
        {
            return "MetaRight";
        }

        return code;
    }
}
