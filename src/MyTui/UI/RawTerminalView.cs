using System.Text;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MyTui.Terminal;

namespace MyTui.UI;

/// <summary>
/// Raw PTY transcript. Phase 1 replaces this with a cell grid.
/// </summary>
#pragma warning disable CS0618
internal sealed class RawTerminalView : TextView
{
    private const int MaxChars = 100_000;
    private readonly PtySession _session;
    private readonly StringBuilder _text = new();
    private bool _carryCr;

    internal RawTerminalView(PtySession session)
    {
        _session = session;
        ReadOnly = true;
        Multiline = true;
        WordWrap = false;
        CanFocus = true;
    }

    internal void AppendRaw(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        _text.Append(Normalize(text));
        if (_text.Length > MaxChars)
        {
            _text.Remove(0, _text.Length - MaxChars);
        }

        Text = _text.ToString();
        if (Lines > 0)
        {
            MoveEnd();
        }
    }

    protected override void OnViewportChanged(DrawEventArgs args)
    {
        base.OnViewportChanged(args);
        if (Viewport.Width > 1 && Viewport.Height > 1)
        {
            _session.Resize(Viewport.Width, Viewport.Height);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            return false;
        }

        if (KeyBytes(key) is byte[] bytes)
        {
            _session.Write(bytes);
        }

        return true;
    }

    private string Normalize(string text)
    {
        text = text.Replace("\0", "", StringComparison.Ordinal);
        if (_carryCr)
        {
            text = "\r" + text;
            _carryCr = false;
        }

        if (text.EndsWith('\r') && !text.EndsWith("\r\n", StringComparison.Ordinal))
        {
            _carryCr = true;
            text = text[..^1];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static byte[]? KeyBytes(Key key)
    {
        if (key == Key.Enter)
        {
            return [0x0d];
        }

        if (key == Key.Backspace)
        {
            return [0x7f];
        }

        if (key == Key.Tab)
        {
            return [0x09];
        }

        if (key == Key.Esc)
        {
            return [0x1b];
        }

        if (key == Key.CursorUp)
        {
            return [0x1b, (byte)'[', (byte)'A'];
        }

        if (key == Key.CursorDown)
        {
            return [0x1b, (byte)'[', (byte)'B'];
        }

        if (key == Key.CursorRight)
        {
            return [0x1b, (byte)'[', (byte)'C'];
        }

        if (key == Key.CursorLeft)
        {
            return [0x1b, (byte)'[', (byte)'D'];
        }

        if (key.IsCtrl && !key.IsAlt && !key.IsShift && key.IsKeyCodeAtoZ)
        {
            int index = (int)key.NoCtrl.KeyCode - (int)Key.A.KeyCode;
            if (index is >= 0 and < 26)
            {
                return [(byte)(index + 1)];
            }
        }

        if (!key.IsCtrl && !key.IsAlt && key.TryGetPrintableRune(out Rune rune))
        {
            return Encoding.UTF8.GetBytes(rune.ToString());
        }

        return null;
    }
}
#pragma warning restore CS0618
