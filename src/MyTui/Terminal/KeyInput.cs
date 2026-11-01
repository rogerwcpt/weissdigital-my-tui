using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ghostty.Vt;
using Ghostty.Vt.Enums;
using Terminal.Gui.Input;
using VtTerminal = Ghostty.Vt.Terminal;

namespace MyTui.Terminal;

/// <summary>
/// Terminal.Gui key to libghostty-vt bytes. Modifier bits are the binding's own:
/// shift 1, control 2, alt 4, super 8. Press action is 1.
/// </summary>
internal static class KeyInput
{
    internal const ushort Shift = 1;
    internal const ushort Control = 2;
    internal const ushort Alt = 4;

    internal static byte[]? Encode(VtTerminal terminal, KeyEncoder encoder, Key key)
    {
        if (!TryDescribe(key, out GhosttyKey ghostty, out string? text, out ushort modifiers))
        {
            if (!key.IsCtrl && !key.IsAlt && key.TryGetPrintableRune(out Rune rune))
            {
                return Encoding.UTF8.GetBytes(rune.ToString());
            }

            return null;
        }

        using var evt = new KeyEvent
        {
            Key = (int)ghostty,
            Action = 1,
            Modifiers = modifiers,
            Text = text,
        };
        encoder.ConfigureFromTerminal(terminal);
        // Encode() returns a span into its own stack buffer. Copying that span
        // races the next call and can turn the first typed letter into a NUL.
        byte[] encoded = EncodeInto(encoder, evt);
        // After an app turns on the kitty keyboard protocol, the encoder can
        // answer Ctrl+D with the letter d, or with nothing. The classic byte
        // is what Ctrl-C and Ctrl-D still mean to a shell and to pi.
        if (ControlByte(key) is byte classic && (encoded.Length == 0 || IsPlainLetter(encoded, key)))
        {
            return [classic];
        }

        return encoded;
    }

    private static unsafe byte[] EncodeInto(KeyEncoder encoder, KeyEvent evt)
    {
        nint encoderHandle = EncoderPointer(encoder);
        nint eventHandle = EventPointer(evt);
        byte[] buffer = new byte[256];
        fixed (byte* pointer = buffer)
        {
            nuint length;
            Ghostty.Vt.Raw.GhosttyResult result = Ghostty.Vt.Raw.GhosttyVtNative.ghostty_key_encoder_encode(
                encoderHandle,
                (Ghostty.Vt.Raw.GhosttyKeyEventImpl*)eventHandle,
                (sbyte*)pointer,
                (nuint)buffer.Length,
                &length);
            if (result != Ghostty.Vt.Raw.GhosttyResult.GHOSTTY_SUCCESS)
            {
                throw new IOException($"key encode failed: {result}");
            }

            return buffer.AsSpan(0, checked((int)length)).ToArray();
        }
    }

    private static byte? ControlByte(Key key)
    {
        if (!key.IsCtrl || key.IsAlt || key.IsShift)
        {
            return null;
        }

        Key bare = key.NoCtrl.NoAlt.NoShift;
        if (!bare.IsKeyCodeAtoZ)
        {
            return null;
        }

        int index = (int)bare.KeyCode - (int)Key.A.KeyCode;
        return index is >= 0 and < 26 ? (byte)(index + 1) : null;
    }

    private static bool IsPlainLetter(byte[] encoded, Key key)
    {
        if (encoded.Length != 1)
        {
            return false;
        }

        Key bare = key.NoCtrl.NoAlt.NoShift;
        if (!bare.IsKeyCodeAtoZ)
        {
            return false;
        }

        int index = (int)bare.KeyCode - (int)Key.A.KeyCode;
        if (index is < 0 or > 25)
        {
            return false;
        }

        return encoded[0] == (byte)('a' + index) || encoded[0] == (byte)('A' + index);
    }

    private static nint EncoderPointer(KeyEncoder encoder)
    {
        object handle = EncoderHandleField.GetValue(encoder)
            ?? throw new InvalidOperationException("Key encoder handle is missing.");
        return ((SafeHandle)handle).DangerousGetHandle();
    }

    private static nint EventPointer(KeyEvent evt)
    {
        object handle = EventHandleField.GetValue(evt)
            ?? throw new InvalidOperationException("Key event handle is missing.");
        return (nint)handle;
    }

    private static readonly FieldInfo EncoderHandleField = typeof(KeyEncoder).GetField(
        "_handle",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("KeyEncoder._handle was not found.");

    private static readonly FieldInfo EventHandleField = typeof(KeyEvent).GetField(
        "_handle",
        BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("KeyEvent._handle was not found.");

    internal static bool TryDescribe(Key key, out GhosttyKey ghostty, out string? text, out ushort modifiers)
    {
        ghostty = GhosttyKey.Unidentified;
        text = null;
        modifiers = 0;
        if (key.IsShift)
        {
            modifiers |= Shift;
        }

        if (key.IsCtrl)
        {
            modifiers |= Control;
        }

        if (key.IsAlt)
        {
            modifiers |= Alt;
        }

        Key bare = key.NoCtrl.NoAlt.NoShift;
        if (!TryMap(bare, out ghostty))
        {
            return false;
        }

        if (!key.IsCtrl && !key.IsAlt && key.TryGetPrintableRune(out Rune rune))
        {
            text = rune.ToString();
        }
        else if (!key.IsCtrl && !key.IsAlt && ghostty is >= GhosttyKey.A and <= GhosttyKey.Z)
        {
            char letter = (char)('a' + ((int)ghostty - (int)GhosttyKey.A));
            text = (key.IsShift ? char.ToUpperInvariant(letter) : letter).ToString();
        }

        return true;
    }

    private static bool TryMap(Key bare, out GhosttyKey ghostty)
    {
        if (bare == Key.Enter)
        {
            ghostty = GhosttyKey.Enter;
            return true;
        }

        if (bare == Key.Backspace)
        {
            ghostty = GhosttyKey.Backspace;
            return true;
        }

        if (bare == Key.Tab)
        {
            ghostty = GhosttyKey.Tab;
            return true;
        }

        if (bare == Key.Esc)
        {
            ghostty = GhosttyKey.Escape;
            return true;
        }

        if (bare == Key.Space)
        {
            ghostty = GhosttyKey.Space;
            return true;
        }

        if (bare == Key.CursorUp)
        {
            ghostty = GhosttyKey.ArrowUp;
            return true;
        }

        if (bare == Key.CursorDown)
        {
            ghostty = GhosttyKey.ArrowDown;
            return true;
        }

        if (bare == Key.CursorLeft)
        {
            ghostty = GhosttyKey.ArrowLeft;
            return true;
        }

        if (bare == Key.CursorRight)
        {
            ghostty = GhosttyKey.ArrowRight;
            return true;
        }

        if (bare == Key.Home)
        {
            ghostty = GhosttyKey.Home;
            return true;
        }

        if (bare == Key.End)
        {
            ghostty = GhosttyKey.End;
            return true;
        }

        if (bare == Key.PageUp)
        {
            ghostty = GhosttyKey.PageUp;
            return true;
        }

        if (bare == Key.PageDown)
        {
            ghostty = GhosttyKey.PageDown;
            return true;
        }

        if (bare == Key.Delete || bare == Key.DeleteChar)
        {
            ghostty = GhosttyKey.Delete;
            return true;
        }

        if (bare == Key.InsertChar)
        {
            ghostty = GhosttyKey.Insert;
            return true;
        }

        if (bare.IsKeyCodeAtoZ)
        {
            int index = (int)bare.KeyCode - (int)Key.A.KeyCode;
            ghostty = (GhosttyKey)((int)GhosttyKey.A + index);
            return index is >= 0 and < 26;
        }

        int digit = (int)bare.KeyCode - (int)Key.D0.KeyCode;
        if (digit is >= 0 and <= 9)
        {
            ghostty = (GhosttyKey)((int)GhosttyKey.Digit0 + digit);
            return true;
        }

        int function = (int)bare.KeyCode - (int)Key.F1.KeyCode;
        if (function is >= 0 and <= 11)
        {
            ghostty = (GhosttyKey)((int)GhosttyKey.F1 + function);
            return true;
        }

        if (bare.TryGetPrintableRune(out Rune rune) && TryPunctuation(rune, out ghostty))
        {
            return true;
        }

        ghostty = GhosttyKey.Unidentified;
        return false;
    }

    private static bool TryPunctuation(Rune rune, out GhosttyKey ghostty)
    {
        ghostty = rune.Value switch
        {
            '`' or '~' => GhosttyKey.Backquote,
            '-' or '_' => GhosttyKey.Minus,
            '=' or '+' => GhosttyKey.Equal,
            '[' or '{' => GhosttyKey.BracketLeft,
            ']' or '}' => GhosttyKey.BracketRight,
            '\\' or '|' => GhosttyKey.Backslash,
            ';' or ':' => GhosttyKey.Semicolon,
            '\'' or '"' => GhosttyKey.Quote,
            ',' or '<' => GhosttyKey.Comma,
            '.' or '>' => GhosttyKey.Period,
            '/' or '?' => GhosttyKey.Slash,
            ' ' => GhosttyKey.Space,
            _ => GhosttyKey.Unidentified,
        };
        return ghostty != GhosttyKey.Unidentified;
    }
}
