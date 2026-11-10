using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
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
    private CellRange? _range;
    private CellRange? _paintedRange;
    private int _anchorColumn;
    private int _anchorRow;
    private bool _dragging;
    private bool _moved;
    private bool _keepSelection;
    private bool _linkOpened;
    private bool _forwarding;
    private int _forwardColumn = -1;
    private int _forwardRow = -1;
    private int _forwardButton = -1;
    private int _gridWidth = -1;
    private int _gridHeight = -1;

    internal CellGridView(Func<Key, bool, bool> chords, Action start, Action<string> copyText, Action<string> openLink)
    {
        _chords = chords;
        _start = start;
        _copyText = copyText;
        _openLink = openLink;
        CanFocus = true;
        MousePositionTracking = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    internal bool CursorBlinking { get; private set; }

    /// <summary>Resize the PTY only while the Terminal tab is the one on screen.</summary>
    internal bool Live { get; set; } = true;

    private Func<Key, bool, bool> _chords { get; }

    private Action _start { get; }

    private Action<string> _copyText { get; }

    private Action<string> _openLink { get; }

    internal TerminalSession? Session
    {
        get => _session;
        set
        {
            if (!ReferenceEquals(_session, value))
            {
                _forceFull = true;
                ClearSelection();
            }

            _session = value;
            SetNeedsDraw();
        }
    }

    /// <summary>A dialog is covering the grid. A paint in that state must not update the cache.</summary>
    internal bool Covered { get; set; }

    /// <summary>Next paint writes every cell. Used after a dialog or another tab has covered the grid.</summary>
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
        if (Viewport.Width != _gridWidth || Viewport.Height != _gridHeight)
        {
            _gridWidth = Viewport.Width;
            _gridHeight = Viewport.Height;
            ClearSelection();
        }
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
        // Every path below consumes the key. Handled stops a bubbled Ctrl+Q from also quitting.
        key.Handled = true;
        bool idle = _session is null || _session.HasExited || !_session.IsRunning;
        if (_chords(key, !idle))
        {
            return true;
        }

        if (_session is not null && key.IsShift && !key.IsCtrl && !key.IsAlt)
        {
            int page = Math.Max(1, _session.Rows - 1);
            if (key == Key.PageUp.WithShift)
            {
                ClearSelection();
                _session.ScrollBy(-page);
                return true;
            }

            if (key == Key.PageDown.WithShift)
            {
                ClearSelection();
                _session.ScrollBy(page);
                return true;
            }

            if (key == Key.Home.WithShift)
            {
                ClearSelection();
                _session.ScrollToTop();
                return true;
            }

            if (key == Key.End.WithShift)
            {
                ClearSelection();
                _session.ScrollToBottom();
                return true;
            }
        }

        if (idle && key == Key.Enter)
        {
            _start();
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

        ClearSelection();
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

        ClearSelection();
        session.Paste(text);
        return true;
    }

    /// <summary>Text of the current drag or word selection. Empty when nothing is selected.</summary>
    internal string? SelectionText()
    {
        if (_range is not CellRange range || _session is not TerminalSession session)
        {
            return null;
        }

        string text = session.CopyRange(range.Column1, range.Row1, range.Column2, range.Row2);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        bool up = mouse.Flags.HasFlag(MouseFlags.WheeledUp);
        bool down = mouse.Flags.HasFlag(MouseFlags.WheeledDown);
        if (up || down)
        {
            return Wheel(mouse, up);
        }

        if (_session is not TerminalSession session || mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        int column = Math.Clamp((int)position.X, 0, Math.Max(0, Viewport.Width - 1));
        int row = Math.Clamp((int)position.Y, 0, Math.Max(0, Viewport.Height - 1));
        MouseFlags flags = mouse.Flags;
        bool ctrl = flags.HasFlag(MouseFlags.Ctrl);
        bool shift = flags.HasFlag(MouseFlags.Shift);
        if (_dragging)
        {
            if (column != _anchorColumn || row != _anchorRow)
            {
                _moved = true;
                SetRange(new CellRange(_anchorColumn, _anchorRow, column, row));
            }

            if (flags.HasFlag(MouseFlags.LeftButtonReleased))
            {
                EndDrag(publish: _moved);
            }

            return true;
        }

        // A program that tracks the mouse owns the click, including its selection and links.
        // Shift keeps the drag here.
        if (!shift && session.IsRunning && !session.HasExited && session.TracksMouse)
        {
            ForwardPointer(session, flags, column, row, ctrl, flags.HasFlag(MouseFlags.Alt));
            return true;
        }

        bool left = IsLeft(flags);
        bool right = IsRight(flags);
        bool pressed = flags.HasFlag(MouseFlags.LeftButtonPressed);
        bool released = flags.HasFlag(MouseFlags.LeftButtonReleased);
        bool linkGesture = right || (ctrl && left);
        bool linkPress = pressed
            || flags.HasFlag(MouseFlags.LeftButtonClicked)
            || flags.HasFlag(MouseFlags.RightButtonPressed)
            || flags.HasFlag(MouseFlags.RightButtonClicked);
        if (linkGesture && linkPress)
        {
            if (!_linkOpened && session.UrlAt(column, row) is string url)
            {
                ReleaseDrag();
                _openLink(url);
                _linkOpened = true;
            }

            return true;
        }

        if (flags.HasFlag(MouseFlags.LeftButtonTripleClicked) && !(ctrl && !shift))
        {
            SelectLine(session, row);
            return true;
        }

        if (flags.HasFlag(MouseFlags.LeftButtonDoubleClicked) && !(ctrl && !shift))
        {
            SelectWord(session, column, row);
            return true;
        }

        if (released || flags.HasFlag(MouseFlags.RightButtonReleased))
        {
            _linkOpened = false;
        }

        if (right || (ctrl && !shift))
        {
            return true;
        }

        if (pressed)
        {
            SetFocus();
            App?.Mouse.GrabMouse(this);
            _dragging = true;
            _moved = false;
            _keepSelection = false;
            if (shift && _range is CellRange)
            {
                SetRange(new CellRange(_anchorColumn, _anchorRow, column, row));
            }
            else
            {
                _anchorColumn = column;
                _anchorRow = row;
                SetRange(null);
            }

            return true;
        }

        if (flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            if (_moved || _keepSelection)
            {
                _moved = false;
                _keepSelection = false;
                return true;
            }

            SetRange(null);
            return true;
        }

        return base.OnMouseEvent(mouse);
    }

    private bool Wheel(Mouse mouse, bool up)
    {
        if (_session is not TerminalSession session)
        {
            return true;
        }

        int column = mouse.Position is { } position ? Math.Clamp((int)position.X, 0, Math.Max(0, Viewport.Width - 1)) : 0;
        int row = mouse.Position is { } point ? Math.Clamp((int)point.Y, 0, Math.Max(0, Viewport.Height - 1)) : 0;
        if (session.HasExited || !session.IsRunning)
        {
            ClearSelection();
            session.ScrollBy(up ? -3 : 3);
            return true;
        }

        byte[]? bytes = session.Wheel(up, column, row, out bool scroll);
        if (scroll)
        {
            ClearSelection();
            session.ScrollBy(up ? -3 : 3);
        }
        else if (bytes is { Length: > 0 })
        {
            session.Write(bytes);
        }

        return true;
    }

    private void ForwardPointer(TerminalSession session, MouseFlags flags, int column, int row, bool ctrl, bool alt)
    {
        if (!TryPointer(flags, out PointerKind kind, out int button))
        {
            return;
        }

        if (kind == PointerKind.Press)
        {
            ClearSelection();
            SetFocus();
            App?.Mouse.GrabMouse(this);
            _forwarding = true;
            _forwardColumn = -1;
        }
        else if (kind == PointerKind.Release && !_forwarding)
        {
            WritePointer(session, column, row, PointerKind.Press, button, ctrl, alt);
        }

        bool sameCell = kind == PointerKind.Motion
            && column == _forwardColumn
            && row == _forwardRow
            && button == _forwardButton;
        if (!sameCell)
        {
            WritePointer(session, column, row, kind, button, ctrl, alt);
            _forwardColumn = column;
            _forwardRow = row;
            _forwardButton = button;
        }

        if (kind == PointerKind.Release && _forwarding)
        {
            _forwarding = false;
            App?.Mouse.UngrabMouse();
        }
    }

    private static void WritePointer(
        TerminalSession session,
        int column,
        int row,
        PointerKind kind,
        int button,
        bool ctrl,
        bool alt)
    {
        byte[]? bytes = session.EncodePointer(column, row, kind, button, ctrl, alt);
        if (bytes is { Length: > 0 })
        {
            session.Write(bytes);
        }
    }

    private static bool TryPointer(MouseFlags flags, out PointerKind kind, out int button)
    {
        kind = PointerKind.Press;
        button = 0;
        bool leftRelease = flags.HasFlag(MouseFlags.LeftButtonReleased);
        bool middleRelease = flags.HasFlag(MouseFlags.MiddleButtonReleased);
        bool rightRelease = flags.HasFlag(MouseFlags.RightButtonReleased);
        if (leftRelease || middleRelease || rightRelease)
        {
            kind = PointerKind.Release;
            button = ButtonNumber(leftRelease, middleRelease);
            return true;
        }

        bool left = flags.HasFlag(MouseFlags.LeftButtonPressed);
        bool middle = flags.HasFlag(MouseFlags.MiddleButtonPressed);
        bool right = flags.HasFlag(MouseFlags.RightButtonPressed);
        if (flags.HasFlag(MouseFlags.PositionReport))
        {
            kind = PointerKind.Motion;
            button = left || middle || right ? ButtonNumber(left, middle) : 3;
            return true;
        }

        if (left || middle || right)
        {
            kind = PointerKind.Press;
            button = ButtonNumber(left, middle);
            return true;
        }

        return false;
    }

    private static int ButtonNumber(bool left, bool middle)
    {
        if (left)
        {
            return 0;
        }

        if (middle)
        {
            return 1;
        }

        return 2;
    }

    private void SelectWord(TerminalSession session, int column, int row)
    {
        if (!session.TryWord(column, row, out CellRange range))
        {
            SetRange(null);
            return;
        }

        (int startColumn, int startRow, _, _) = range.Ordered();
        _anchorColumn = startColumn;
        _anchorRow = startRow;
        _keepSelection = true;
        ReleaseDrag();
        SetRange(range);
        Publish(session, range);
    }

    private void SelectLine(TerminalSession session, int row)
    {
        if (!session.TryLine(row, out CellRange range))
        {
            SetRange(null);
            return;
        }

        (int startColumn, int startRow, _, _) = range.Ordered();
        _anchorColumn = startColumn;
        _anchorRow = startRow;
        _keepSelection = true;
        ReleaseDrag();
        SetRange(range);
        Publish(session, range);
    }

    private void EndDrag(bool publish)
    {
        ReleaseDrag();
        if (publish && _range is CellRange range && _session is TerminalSession session)
        {
            Publish(session, range);
            return;
        }

        if (!publish)
        {
            SetRange(null);
        }
    }

    private void Publish(TerminalSession session, CellRange range)
    {
        string text = session.CopyRange(range.Column1, range.Row1, range.Column2, range.Row2);
        if (!string.IsNullOrWhiteSpace(text))
        {
            _copyText(text);
        }
    }

    private void SetRange(CellRange? range)
    {
        if (_range == range)
        {
            return;
        }

        _range = range;
        SetNeedsDraw();
    }

    private void ReleaseDrag()
    {
        if (!_dragging && !_forwarding)
        {
            return;
        }

        _dragging = false;
        _forwarding = false;
        App?.Mouse.UngrabMouse();
    }

    private void ClearSelection()
    {
        ReleaseDrag();

        _moved = false;
        _keepSelection = false;
        _linkOpened = false;
        _forwardColumn = -1;
        _forwardRow = -1;
        _forwardButton = -1;
        SetRange(null);
    }

    private static bool IsLeft(MouseFlags flags) =>
        flags.HasFlag(MouseFlags.LeftButtonPressed)
        || flags.HasFlag(MouseFlags.LeftButtonReleased)
        || flags.HasFlag(MouseFlags.LeftButtonClicked)
        || flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)
        || flags.HasFlag(MouseFlags.LeftButtonTripleClicked);

    private static bool IsRight(MouseFlags flags) =>
        flags.HasFlag(MouseFlags.RightButtonPressed)
        || flags.HasFlag(MouseFlags.RightButtonReleased)
        || flags.HasFlag(MouseFlags.RightButtonClicked);

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (Covered)
        {
            _forceFull = true;
            return true;
        }

        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width < 1 || height < 1)
        {
            _forceFull = true;
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
                bool selected = _range is CellRange range && range.Contains(column, row);
                int index = (row * width) + column;
                bool wasCursor = _cachedCursor && column == _cachedCursorX && row == _cachedCursorY;
                bool wasSelected = _paintedRange is CellRange painted && painted.Contains(column, row);
                if (!full && _cache[index].Equals(cell) && cursor == wasCursor && selected == wasSelected)
                {
                    continue;
                }

                _cache[index] = cell;
                if (cell.WideTail && column > 0 && (frame?.Cell(column - 1, row).Grapheme.GetColumns(false) ?? 0) > 1)
                {
                    continue;
                }

                TuiAttribute attribute = AttributeFor(cell, cursor ^ selected, color);
                if (ink is null || !ink.Value.Equals(attribute))
                {
                    SetAttribute(attribute);
                    ink = attribute;
                }

                if (cell.Grapheme.GetRuneCount() > 1)
                {
                    AddStr(column, row, cell.Grapheme);
                }
                else
                {
                    AddRune(column, row, Glyph(cell));
                }
            }
        }

        _forceFull = false;
        _cachedCursor = showCursor;
        _cachedCursorX = cursorX;
        _cachedCursorY = cursorY;
        _paintedRange = _range;
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
