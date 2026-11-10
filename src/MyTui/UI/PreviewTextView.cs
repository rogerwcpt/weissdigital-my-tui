using System.Text;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace MyTui.UI;

/// <summary>
/// Plain-text preview. Drawn in the terminal's own colors. The stock read-only text view paints a gray
/// field and gray glyphs once the host reports its palette, which hides the file. Scrolling uses the same
/// content-size viewport as the Markdown view, so a long file gets a scrollbar. The horizontal bar is drawn
/// with a lower half block: a cell is about twice as tall as it is wide, and a full-cell bar looks twice as
/// thick as the one-column vertical bar.
/// </summary>
internal sealed class PreviewTextView : View
{
    private const char HalfBlock = '\u2584';

    private readonly HorizontalBar _horizontal;
    private string[] _lines = [""];
    private string _content = "";
    private int[] _lineStarts = [0];
    private int _anchor;
    private int _caret;
    private bool _dragging;
    private bool _syncingBar;
    private int _drawnBarColumn = int.MinValue;

    internal PreviewTextView()
    {
        CanFocus = true;
        X = 0;
        Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
        SetContentSize(new System.Drawing.Size(1, 1));
        _horizontal = new HorizontalBar(this);
        if (Padding is AdornmentImpl adornment)
        {
            adornment.GetOrCreateView().Add(_horizontal);
        }
    }

    internal bool HasHorizontalBar => _horizontal.Visible;

    /// <summary>Called with the text when a drag, double-click, or triple-click ends with a selection.</summary>
    internal Action<string>? Selected { get; set; }

    internal string SelectedText => _content[Math.Min(_anchor, _caret)..Math.Max(_anchor, _caret)];

    internal void SelectAll()
    {
        _anchor = 0;
        _caret = _content.Length;
        SetNeedsDraw();
    }

    internal string Content
    {
        get => _content;
        set
        {
            string normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            _content = normalized;
            _lines = normalized.Length == 0 ? [""] : normalized.Split('\n');
            _lineStarts = new int[_lines.Length];
            for (int i = 1; i < _lines.Length; i++)
            {
                _lineStarts[i] = _lineStarts[i - 1] + _lines[i - 1].Length + 1;
            }

            if (_dragging)
            {
                App?.Mouse.UngrabMouse();
            }

            _dragging = false;
            _anchor = _caret = 0;
            int width = 1;
            foreach (string line in _lines)
            {
                width = Math.Max(width, VisualWidth(line));
            }

            SetContentSize(new System.Drawing.Size(width, _lines.Length));
            if (Viewport.X != 0 || Viewport.Y != 0)
            {
                Viewport = Viewport with { X = 0, Y = 0 };
            }

            SetNeedsDraw();
            SyncHorizontalBar();
        }
    }

    protected override void OnViewportChanged(DrawEventArgs args)
    {
        base.OnViewportChanged(args);
        SyncHorizontalBar();
        if (args.NewViewport != args.OldViewport)
        {
            SetNeedsDraw();
        }
    }

    private void SyncHorizontalBar()
    {
        if (_syncingBar)
        {
            return;
        }

        _syncingBar = true;
        try
        {
            bool show = Viewport.Width > 1 && GetContentWidth() > Viewport.Width;
            int bottom = show ? 1 : 0;
            bool layoutChanged = _horizontal.Visible != show || Padding.Thickness.Bottom != bottom;
            if (_horizontal.Visible != show)
            {
                _horizontal.Visible = show;
            }

            if (Padding.Thickness.Bottom != bottom)
            {
                Padding.Thickness = Padding.Thickness with { Bottom = bottom };
            }

            if (layoutChanged || _drawnBarColumn != Viewport.X)
            {
                _drawnBarColumn = Viewport.X;
                _horizontal.SetNeedsDraw();
            }
        }
        finally
        {
            _syncingBar = false;
        }
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

        int top = Math.Max(0, Viewport.Y);
        int left = Math.Max(0, Viewport.X);
        SetAttribute(GetAttributeForRole(VisualRole.Normal));
        for (int row = 0; row < height; row++)
        {
            int index = top + row;
            DrawLine(row, index >= 0 && index < _lines.Length ? _lines[index] : "", width, left);
        }

        return true;
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
        {
            return ScrollBy(lines: 3, columns: 0);
        }

        if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
        {
            return ScrollBy(lines: -3, columns: 0);
        }

        if (mouse.Position is not { } position)
        {
            return base.OnMouseEvent(mouse);
        }

        int row = Math.Clamp(Viewport.Y + position.Y, 0, _lines.Length - 1);
        int column = Math.Max(0, Viewport.X + position.X);
        int offset = OffsetAt(row, column);
        if (_dragging)
        {
            _caret = offset;
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased))
            {
                _dragging = false;
                App?.Mouse.UngrabMouse();
                PublishSelection();
            }

            SetNeedsDraw();
            return true;
        }

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonTripleClicked))
        {
            _anchor = _lineStarts[row];
            _caret = row + 1 < _lines.Length ? _lineStarts[row + 1] : _content.Length;
            PublishSelection();
        }
        else if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked))
        {
            _anchor = _caret = offset;
            while (_anchor > _lineStarts[row] && !char.IsWhiteSpace(_content[_anchor - 1]))
            {
                _anchor--;
            }

            int end = _lineStarts[row] + _lines[row].Length;
            while (_caret < end && !char.IsWhiteSpace(_content[_caret]))
            {
                _caret++;
            }

            PublishSelection();
        }
        else if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            SetFocus();
            if (!mouse.Flags.HasFlag(MouseFlags.Shift))
            {
                _anchor = offset;
            }

            _caret = offset;
            _dragging = true;
            App?.Mouse.GrabMouse(this);
        }
        else
        {
            return base.OnMouseEvent(mouse);
        }

        SetNeedsDraw();
        return true;
    }

    private void PublishSelection()
    {
        string text = SelectedText;
        if (text.Length > 0)
        {
            Selected?.Invoke(text);
        }
    }

    private int OffsetAt(int row, int column)
    {
        int visual = 0;
        int offset = _lineStarts[row];
        foreach (Rune rune in _lines[row].EnumerateRunes())
        {
            int next = rune.Value == '\t' ? (visual / 4 + 1) * 4 : visual + 1;
            if (column < next)
            {
                break;
            }

            visual = next;
            offset += rune.Utf16SequenceLength;
        }

        return offset;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.A.WithCtrl)
        {
            SelectAll();
            return true;
        }

        if (key == Key.Enter && SelectedText.Length > 0)
        {
            PublishSelection();
            _anchor = _caret;
            SetNeedsDraw();
            return true;
        }

        if (key == Key.CursorLeft.WithShift || key == Key.CursorRight.WithShift)
        {
            int delta = key == Key.CursorLeft.WithShift ? -1 : 1;
            _caret = Math.Clamp(_caret + delta, 0, _content.Length);
            if (_caret > 0 && _caret < _content.Length && char.IsLowSurrogate(_content[_caret]))
            {
                _caret += delta;
            }

            SetNeedsDraw();
            return true;
        }

        if (key.IsCtrl || key.IsAlt)
        {
            return false;
        }

        int page = Math.Max(1, Math.Max(1, Viewport.Height) - 1);
        if (key == Key.CursorUp)
        {
            return ScrollBy(lines: -1, columns: 0);
        }

        if (key == Key.CursorDown)
        {
            return ScrollBy(lines: 1, columns: 0);
        }

        if (key == Key.PageUp)
        {
            return ScrollBy(lines: -page, columns: 0);
        }

        if (key == Key.PageDown)
        {
            return ScrollBy(lines: page, columns: 0);
        }

        if (key == Key.CursorLeft)
        {
            return ScrollBy(lines: 0, columns: -4);
        }

        if (key == Key.CursorRight)
        {
            return ScrollBy(lines: 0, columns: 4);
        }

        if (key == Key.Home)
        {
            return ScrollTo(line: 0, column: 0);
        }

        if (key == Key.End)
        {
            int last = Math.Max(0, GetContentHeight() - Math.Max(1, Viewport.Height));
            return ScrollTo(line: last, column: 0);
        }

        return false;
    }

    private bool ScrollBy(int lines, int columns) => ScrollTo(Viewport.Y + lines, Viewport.X + columns);

    private bool ScrollTo(int line, int column)
    {
        int maxLine = Math.Max(0, GetContentHeight() - Math.Max(1, Viewport.Height));
        int maxColumn = Math.Max(0, GetContentWidth() - Math.Max(1, Viewport.Width));
        int y = Math.Clamp(line, 0, maxLine);
        int x = Math.Clamp(column, 0, maxColumn);
        if (x != Viewport.X || y != Viewport.Y)
        {
            Viewport = Viewport with { X = x, Y = y };
            SetNeedsDraw();
        }

        return true;
    }

    private static int VisualWidth(string line)
    {
        int visual = 0;
        foreach (Rune rune in line.EnumerateRunes())
        {
            if (rune.Value == '\t')
            {
                visual = (visual / 4 + 1) * 4;
                continue;
            }

            visual++;
        }

        return visual;
    }

    private void DrawLine(int row, string line, int width, int left)
    {
        int column = 0;
        int visual = 0;
        int lineIndex = Viewport.Y + row;
        int offset = lineIndex < _lineStarts.Length ? _lineStarts[lineIndex] : _content.Length;
        TuiAttribute normal = GetAttributeForRole(VisualRole.Normal);
        TuiAttribute selected = GetAttributeForRole(VisualRole.Highlight);
        foreach (Rune rune in line.EnumerateRunes())
        {
            bool isSelected = offset >= Math.Min(_anchor, _caret) && offset < Math.Max(_anchor, _caret);
            SetAttribute(isSelected ? selected : normal);
            offset += rune.Utf16SequenceLength;
            if (rune.Value == '\t')
            {
                int stop = (visual / 4 + 1) * 4;
                while (visual < stop && column < width)
                {
                    if (visual >= left)
                    {
                        AddRune(column, row, new Rune(' '));
                        column++;
                    }

                    visual++;
                }

                continue;
            }

            if (visual >= left)
            {
                if (column >= width)
                {
                    break;
                }

                AddRune(column, row, rune);
                column++;
            }

            visual++;
        }

        SetAttribute(normal);
        while (column < width)
        {
            AddRune(column, row, new Rune(' '));
            column++;
        }
    }

    /// <summary>
    /// One row, painted with a lower half block so the bar is about as thick as the vertical bar is wide.
    /// </summary>
    private sealed class HorizontalBar : View
    {
        private readonly PreviewTextView _owner;

        internal HorizontalBar(PreviewTextView owner)
        {
            _owner = owner;
            CanFocus = false;
            Visible = false;
            X = Pos.Func(_ => owner.Padding.Thickness.Left);
            Y = Pos.AnchorEnd() - Pos.Func(_ => owner.Padding.Thickness.Bottom - 1);
            Width = Dim.Fill(Dim.Func(_ => owner.Padding.Thickness.Right));
            Height = 1;
            SetScheme(HostTheme.Preview());
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            int width = Viewport.Width;
            if (width < 1)
            {
                return true;
            }

            int content = Math.Max(1, _owner.GetContentWidth());
            int visible = Math.Max(1, _owner.Viewport.Width);
            int thumb = Math.Clamp((int)Math.Floor(visible / (double)content * width), 1, width);
            int travel = Math.Max(0, width - thumb);
            int max = Math.Max(1, content - visible);
            int position = content <= visible
                ? 0
                : (int)Math.Round(_owner.Viewport.X / (double)max * travel);
            position = Math.Clamp(position, 0, Math.Max(0, width - thumb));

            TuiAttribute track = GetAttributeForRole(VisualRole.Disabled);
            TuiAttribute thumbAttribute = GetAttributeForRole(VisualRole.Normal);
            var glyph = new Rune(HalfBlock);
            for (int column = 0; column < width; column++)
            {
                bool onThumb = column >= position && column < position + thumb;
                SetAttribute(onThumb ? thumbAttribute : track);
                AddRune(column, 0, glyph);
            }

            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Position is not { } position)
            {
                return false;
            }

            bool dragging = mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed)
                || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
                || mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased);
            if (!dragging)
            {
                return false;
            }

            int width = Math.Max(1, Viewport.Width);
            int content = Math.Max(1, _owner.GetContentWidth());
            int visible = Math.Max(1, _owner.Viewport.Width);
            int max = Math.Max(0, content - visible);
            int column = (int)Math.Round(Math.Clamp(position.X, 0, width - 1) / (double)Math.Max(1, width - 1) * max);
            _owner.ScrollTo(_owner.Viewport.Y, column);
            return true;
        }
    }
}
