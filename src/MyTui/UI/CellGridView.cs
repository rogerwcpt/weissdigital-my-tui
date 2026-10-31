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

    internal CellGridView(Action onNew, Action onPanes)
    {
        OnNew = onNew;
        OnPanes = onPanes;
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    internal bool CursorBlinking { get; private set; }

    private Action OnNew { get; }

    private Action OnPanes { get; }

    internal TerminalSession? Session
    {
        get => _session;
        set
        {
            _session = value;
            SetNeedsDraw();
        }
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
        if (Viewport.Width > 1 && Viewport.Height > 1)
        {
            _session?.Resize(Viewport.Width, Viewport.Height);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            return false;
        }

        if (key == Key.N.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            OnNew();
            return true;
        }

        if (key == Key.O.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            OnPanes();
            return true;
        }

        if (_session is null || !_session.IsRunning)
        {
            return true;
        }

        byte[]? encoded = _session.Encode(key);
        if (encoded is { Length: > 0 })
        {
            _session.Write(encoded);
        }

        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        base.OnDrawingContent(context);
        int width = Viewport.Width;
        int height = Viewport.Height;
        if (width < 1 || height < 1)
        {
            return true;
        }

        ScreenFrame? frame = _session?.Capture();
        CursorBlinking = frame?.CursorBlinking ?? false;
        PaintedCell blank = frame?.Cell(0, 0) ?? PaintedCell.Blank;
        bool showCursor = HasFocus && frame is { CursorVisible: true } && (!CursorBlinking || _cursorOn);
        for (int row = 0; row < height; row++)
        {
            for (int column = 0; column < width; column++)
            {
                PaintedCell cell = frame?.Cell(column, row) ?? blank;
                bool cursor = showCursor && frame is not null && column == frame.CursorX && row == frame.CursorY;
                DrawCell(column, row, cell, cursor);
            }
        }

        return true;
    }

    private void DrawCell(int column, int row, PaintedCell cell, bool cursor)
    {
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

        SetAttribute(new TuiAttribute(new Color(fr, fg, fb), new Color(br, bg, bb), style));
        Rune rune = cell.Grapheme.Length == 0
            ? new Rune(' ')
            : cell.Grapheme.EnumerateRunes().First();
        AddRune(column, row, rune);
    }
}
