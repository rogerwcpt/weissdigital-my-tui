using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using MyTui.Terminal;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace MyTui.UI;

/// <summary>
/// Blits one session's VT grid. The TextView from Phase 0 is gone.
/// </summary>
internal sealed class CellGridView : View
{
    private TerminalSession? _session;
    private bool _cursorOn = true;
    private PaintedCell[] _cache = [];
    private bool _forceFull = true;
    private bool _cachedCursor;
    private int _cachedCursorX = -1;
    private int _cachedCursorY = -1;

    internal CellGridView(Func<Key, bool> chords, Action start, Action close)
    {
        _chords = chords;
        _start = start;
        _close = close;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    internal bool CursorBlinking { get; private set; }

    /// <summary>Resize the PTY only while the Terminal tab is the one on screen.</summary>
    internal bool Live { get; set; } = true;

    private Func<Key, bool> _chords { get; }

    private Action _start { get; }

    private Action _close { get; }

    internal TerminalSession? Session
    {
        get => _session;
        set
        {
            if (!ReferenceEquals(_session, value))
            {
                _forceFull = true;
            }

            _session = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Next paint writes every cell. Used after a dialog has covered the grid.</summary>
    internal void Repaint()
    {
        _forceFull = true;
        SetNeedsDraw();
    }

    internal void Blink(bool cursorOn)
    {
        if (_cursorOn == cursorOn)
        {
            return;
        }

        _cursorOn = cursorOn;
        if (HasFocus && CursorBlinking)
        {
            SetNeedsDraw();
        }
    }

    protected override void OnViewportChanged(DrawEventArgs args)
    {
        base.OnViewportChanged(args);
        _forceFull = true;
        if (Live && Viewport.Width > 1 && Viewport.Height > 1)
        {
            _session?.Resize(Viewport.Width, Viewport.Height);
        }
    }

    protected override void OnVisibleChanged()
    {
        base.OnVisibleChanged();
        if (Visible)
        {
            _forceFull = true;
        }
    }

    /// <summary>
    /// The framework would otherwise fill every cell before we draw. We own the whole grid,
    /// so a fill plus a full rewrite is two paints. Returning true keeps the previous cells
    /// and <see cref="OnDrawingContent"/> writes only what changed.
    /// </summary>
    protected override bool OnClearingViewport() => true;

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            return false;
        }

        if (_chords(key))
        {
            return true;
        }

        if (_session is not null && key.IsShift && !key.IsCtrl && !key.IsAlt)
        {
            int page = Math.Max(1, _session.Rows - 1);
            if (key == Key.PageUp.WithShift)
            {
                _session.ScrollBy(-page);
                return true;
            }

            if (key == Key.PageDown.WithShift)
            {
                _session.ScrollBy(page);
                return true;
            }

            if (key == Key.Home.WithShift)
            {
                _session.ScrollToTop();
                return true;
            }

            if (key == Key.End.WithShift)
            {
                _session.ScrollToBottom();
                return true;
            }
        }

        bool idle = _session is null || _session.HasExited || !_session.IsRunning;
        if (idle && key == Key.Enter)
        {
            _start();
            return true;
        }

        if (idle && SessionScreen.IsCloseKey(key))
        {
            _close();
            return true;
        }

        if (idle)
        {
            return true;
        }

        if (_session is not TerminalSession session)
        {
            return true;
        }

        session.RevealBottom();
        byte[]? encoded = session.Encode(key);
        if (encoded is { Length: > 0 })
        {
            session.Write(encoded);
        }

        return true;
    }

    protected override bool OnPaste(string text)
    {
        if (_session is not TerminalSession session || session.HasExited || !session.IsRunning || text.Length == 0)
        {
            return false;
        }

        session.Paste(text);
        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width < 1 || height < 1)
        {
            return true;
        }

        ScreenFrame? frame = _session?.CaptureForDraw();
        CursorBlinking = frame?.CursorBlinking ?? false;
        PaintedCell blank = frame?.Cell(0, 0) ?? PaintedCell.Blank;
        bool showCursor = HasFocus && frame is { CursorVisible: true } && (!CursorBlinking || _cursorOn);
        bool color = HostTheme.ColorEnabled;
        int count = width * height;
        bool full = _forceFull || _cache.Length != count;
        if (full)
        {
            _cache = new PaintedCell[count];
        }

        TuiAttribute? ink = null;
        int cursorX = frame?.CursorX ?? -1;
        int cursorY = frame?.CursorY ?? -1;
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                PaintedCell cell = frame?.Cell(column, row) ?? blank;
                bool cursor = showCursor && column == cursorX && row == cursorY;
                int index = (row * width) + column;
                bool wasCursor = _cachedCursor && column == _cachedCursorX && row == _cachedCursorY;
                if (!full && _cache[index].Equals(cell) && cursor == wasCursor)
                {
                    continue;
                }

                _cache[index] = cell;
                TuiAttribute attribute = AttributeFor(cell, cursor, color);
                if (ink is null || !ink.Value.Equals(attribute))
                {
                    SetAttribute(attribute);
                    ink = attribute;
                }

                AddRune(column, row, Glyph(cell));
            }
        }

        _forceFull = false;
        _cachedCursor = showCursor;
        _cachedCursorX = cursorX;
        _cachedCursorY = cursorY;
        return true;
    }

    private static TuiAttribute AttributeFor(PaintedCell cell, bool cursor, bool color)
    {
        if (!color)
        {
            TextStyle plain = TextStyle.None;
            if (cell.Bold)
            {
                plain |= TextStyle.Bold;
            }

            if (cell.Italic)
            {
                plain |= TextStyle.Italic;
            }

            if (cell.Underline)
            {
                plain |= TextStyle.Underline;
            }

            if (cursor)
            {
                plain |= TextStyle.Reverse;
            }

            return new TuiAttribute(Color.None, Color.None, plain);
        }

        byte fr = cell.ForegroundRed;
        byte fg = cell.ForegroundGreen;
        byte fb = cell.ForegroundBlue;
        byte br = cell.BackgroundRed;
        byte bg = cell.BackgroundGreen;
        byte bb = cell.BackgroundBlue;
        if (cursor)
        {
            (fr, br) = (br, fr);
            (fg, bg) = (bg, fg);
            (fb, bb) = (bb, fb);
        }

        TextStyle style = TextStyle.None;
        if (cell.Bold)
        {
            style |= TextStyle.Bold;
        }

        if (cell.Italic)
        {
            style |= TextStyle.Italic;
        }

        if (cell.Faint)
        {
            style |= TextStyle.Faint;
        }

        if (cell.Underline)
        {
            style |= TextStyle.Underline;
        }

        return new TuiAttribute(new Color(fr, fg, fb), new Color(br, bg, bb), style);
    }

    private static Rune Glyph(PaintedCell cell) =>
        cell.Grapheme.Length == 0 ? new Rune(' ') : Rune.GetRuneAt(cell.Grapheme, 0);
}
