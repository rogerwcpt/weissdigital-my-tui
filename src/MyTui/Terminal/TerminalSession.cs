using System.Text;
using Ghostty.Vt;
using Ghostty.Vt.Enums;
using Ghostty.Vt.Types;
using Terminal.Gui.Input;
using VtTerminal = Ghostty.Vt.Terminal;

namespace MyTui.Terminal;

/// <summary>
/// One shell: a PTY plus a libghostty-vt grid. Unfocused sessions keep both.
/// Scrollback stays inside the VT (10_000 lines). The viewport moves with ScrollViewport*.
/// </summary>
internal sealed class TerminalSession : IDisposable
{
    private readonly Lock _gate = new();
    private readonly VtTerminal _vt;
    private readonly KeyEncoder _encoder = new();
    private readonly RenderState _render = new();
    private readonly string _name;
    private PtySession? _pty;
    private string _title = "";
    private int _dirty = 1;
    private int _bell;
    private bool _disposed;
    private PaintedCell[] _drawA = [];
    private PaintedCell[] _drawB = [];
    private bool _drawBNext;

    private TerminalSession(string name, int columns, int rows)
    {
        _name = name;
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        _vt = new VtTerminal(Columns, Rows, Configure);
        _vt.SetColorPalette(TerminalPalette.Apply(_vt.ColorPaletteDefault));
    }

    internal int Columns { get; private set; }

    internal int Rows { get; private set; }

    internal int Pid => _pty?.Pid ?? 0;

    internal int? ExitCode => _pty?.ExitCode;

    internal bool IsRunning => _pty is not null && PtySession.IsRunning(_pty.Pid);

    /// <summary>The shell has a foreground command or a child process.</summary>
    internal bool IsBusy => _pty?.IsBusy == true;

    internal bool HasExited => _pty is null || _pty.PollExited();

    internal string Label
    {
        get
        {
            string name = string.IsNullOrWhiteSpace(_title) ? _name : _title;
            return IsRunning ? name : name + " · exited";
        }
    }

    /// <summary>Raised on the PTY reader thread. Handlers must not touch the UI or take session locks.</summary>
    internal event Action? Changed;

    internal static TerminalSession Start(
        string file,
        IReadOnlyList<string> arguments,
        int columns,
        int rows,
        string? name = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string? workingDirectory = null)
    {
        var session = new TerminalSession(name ?? Path.GetFileName(file), columns, rows);
        try
        {
            session._pty = PtySession.Start(file, arguments, session.Columns, session.Rows, environment, workingDirectory);
            session._pty.Output += session.OnOutput;
            session._pty.StartReading();
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    internal byte[]? Encode(Key key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return KeyInput.Encode(_vt, _encoder, key);
        }
    }

    internal void Write(ReadOnlySpan<byte> data) => _pty?.Write(data);

    internal void Kill() => _pty?.Kill();

    internal bool BracketedPaste
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _vt.ModeGet(TerminalMode.BracketedPaste);
            }
        }
    }

    internal int ScrollOffset
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _vt.ScrollOffset;
            }
        }
    }

    internal void ScrollBy(int delta)
    {
        if (delta == 0)
        {
            return;
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _vt.ScrollViewportBy(delta);
        }

        MarkDirty();
    }

    internal void ScrollToTop()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _vt.ScrollViewportToTop();
        }

        MarkDirty();
    }

    internal void ScrollToBottom()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _vt.ScrollViewportToBottom();
        }

        MarkDirty();
    }

    /// <summary>Typing or pasting returns to the prompt when the viewport is in the history.</summary>
    internal void RevealBottom()
    {
        lock (_gate)
        {
            if (_disposed || _vt.ScrollOffset == 0)
            {
                return;
            }

            _vt.ScrollViewportToBottom();
        }

        MarkDirty();
    }

    internal void Paste(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        byte[] encoded;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_vt.ScrollOffset != 0)
            {
                _vt.ScrollViewportToBottom();
            }

            bool bracketed = _vt.ModeGet(TerminalMode.BracketedPaste);
            encoded = PasteInput.Encode(Encoding.UTF8.GetBytes(text), bracketed);
        }

        MarkDirty();
        Write(encoded);
    }

    internal void Resize(int columns, int rows)
    {
        if (columns < 1 || rows < 1)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || (columns == Columns && rows == Rows))
            {
                return;
            }

            _vt.Resize(columns, rows);
            Columns = columns;
            Rows = rows;
        }

        _pty?.Resize(columns, rows);
        MarkDirty();
    }

    internal bool IsDirty => Volatile.Read(ref _dirty) == 1;

    internal bool ConsumeDirty() => Interlocked.Exchange(ref _dirty, 0) == 1;

    internal bool ConsumeBell() => Interlocked.Exchange(ref _bell, 0) == 1;

    /// <summary>Wheel over the grid. Scroll is the shell case. Bytes go straight to the program.</summary>
    internal byte[]? Wheel(bool up, int column, int row, out bool scroll)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (TracksMouseCore())
            {
                scroll = false;
                return MouseInput.Wheel(up, column, row, _vt.ModeGet(TerminalMode.MouseSGR));
            }

            bool alternate = _vt.ModeGet(TerminalMode.AltScreen)
                || _vt.ModeGet(TerminalMode.AltScreenLegacy)
                || _vt.ModeGet(TerminalMode.AltScreenNoSaveCursor);
            if (alternate)
            {
                scroll = false;
                return MouseInput.Arrows(up, 3, _vt.ModeGet(TerminalMode.CursorKeys));
            }

            scroll = true;
            return null;
        }
    }

    /// <summary>
    /// Press, release, or motion for a program that tracks the mouse. Null when this event is
    /// outside the mode it enabled, or the session is gone.
    /// </summary>
    internal byte[]? EncodePointer(int column, int row, PointerKind kind, int button, bool ctrl, bool alt)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!TracksMouseCore() || !PointerWanted(kind, button))
            {
                return null;
            }

            int modifiers = (alt ? 8 : 0) + (ctrl ? 16 : 0);
            bool sgr = _vt.ModeGet(TerminalMode.MouseSGR) || _vt.ModeGet(TerminalMode.SgrPixelsMouse);
            bool urxvt = !sgr && _vt.ModeGet(TerminalMode.UrxvtMouse);
            bool utf8 = !sgr && !urxvt && _vt.ModeGet(TerminalMode.Utf8Mouse);
            return MouseInput.Pointer(
                column,
                row,
                button,
                release: kind == PointerKind.Release,
                motion: kind == PointerKind.Motion,
                modifiers,
                sgr,
                urxvt,
                utf8);
        }
    }

    /// <summary>The program asked for mouse clicks. An unmodified click is encoded to it.</summary>
    internal bool TracksMouse
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && TracksMouseCore();
            }
        }
    }

    internal string CopyRange(int column1, int row1, int column2, int row2)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return "";
            }

            ReadViewport(out string[][] rows, out bool[] wrapped);
            return GridText.Copy(rows, wrapped, column1, row1, column2, row2);
        }
    }

    internal bool TryWord(int column, int row, out CellRange range)
    {
        lock (_gate)
        {
            range = default;
            if (_disposed)
            {
                return false;
            }

            ReadViewport(out string[][] rows, out bool[] wrapped);
            return GridText.TryWord(rows, wrapped, column, row, out range);
        }
    }

    internal bool TryLine(int row, out CellRange range)
    {
        lock (_gate)
        {
            range = default;
            if (_disposed)
            {
                return false;
            }

            ReadViewport(out string[][] rows, out bool[] wrapped);
            return GridText.TryLine(rows, wrapped, row, out range);
        }
    }

    /// <summary>OSC 8 target when the cell has one, otherwise a URL written in the text.</summary>
    internal string? UrlAt(int column, int row)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            string? osc = HyperlinkLookup.Read(_vt, column, row);
            if (osc is not null)
            {
                return osc.Length == 0 ? null : osc;
            }

            ReadViewport(out string[][] rows, out bool[] wrapped);
            return GridText.UrlAt(rows, wrapped, column, row);
        }
    }

    /// <summary>The child is between CSI ? 2026 h and CSI ? 2026 l, so the screen is half written.</summary>
    internal bool InSynchronizedUpdate
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _vt.ModeGet(TerminalMode.SynchronizedOutput);
            }
        }
    }

    internal ScreenFrame Capture()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _render.Update(_vt);
            return Copy(_render, plainText: true, buffer: null);
        }
    }

    /// <summary>
    /// Grid for painting. Skips the plain-text copy and reuses one of two buffers so a frame
    /// the view is still reading is not overwritten.
    /// </summary>
    internal ScreenFrame CaptureForDraw()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _render.Update(_vt);
            ref PaintedCell[] slot = ref (_drawBNext ? ref _drawB : ref _drawA);
            ScreenFrame frame = Copy(_render, plainText: false, buffer: slot);
            slot = frame.Cells;
            _drawBNext = !_drawBNext;
            return frame;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        if (_pty is not null)
        {
            _pty.Output -= OnOutput;
            _pty.Dispose();
            _pty = null;
        }

        lock (_gate)
        {
            _encoder.Dispose();
            _render.Dispose();
            _vt.Dispose();
        }
    }

    private void Configure(TerminalOptions options)
    {
        options.MaxScrollbackLines = 10_000;
        options.OnWritePty = WriteReply;
        options.OnTitleChanged = OnTitleChanged;
        options.OnBell = () => Interlocked.Exchange(ref _bell, 1);
    }

    private void OnTitleChanged()
    {
        _title = Sanitize(_vt.Title);
        MarkDirty();
    }

    private void WriteReply(ReadOnlySpan<byte> data)
    {
        try
        {
            _pty?.Write(data);
        }
        catch (ObjectDisposedException)
        {
            // The session is closing. Drop the VT's reply.
        }
    }

    private void OnOutput(byte[] bytes)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _vt.VTWrite(bytes);
        }

        MarkDirty();
        Changed?.Invoke();
    }

    private void MarkDirty() => Interlocked.Exchange(ref _dirty, 1);

    private bool TracksMouseCore() =>
        _vt.ModeGet(TerminalMode.MouseNormal)
        || _vt.ModeGet(TerminalMode.MouseButton)
        || _vt.ModeGet(TerminalMode.MouseAny)
        || _vt.ModeGet(TerminalMode.MouseX10);

    private bool PointerWanted(PointerKind kind, int button)
    {
        bool any = _vt.ModeGet(TerminalMode.MouseAny);
        bool buttons = _vt.ModeGet(TerminalMode.MouseButton);
        bool normal = _vt.ModeGet(TerminalMode.MouseNormal);
        bool x10 = _vt.ModeGet(TerminalMode.MouseX10);
        if (kind == PointerKind.Motion)
        {
            if (any)
            {
                return true;
            }

            return buttons && button <= 2;
        }

        if (kind == PointerKind.Release)
        {
            return normal || buttons || any;
        }

        return x10 || normal || buttons || any;
    }

    private void ReadViewport(out string[][] graphemes, out bool[] wrapped)
    {
        _render.Update(_vt);
        graphemes = new string[Rows][];
        wrapped = new bool[Rows];
        int rowIndex = 0;
        foreach (RenderStateRow row in _render.Rows)
        {
            if (rowIndex >= Rows)
            {
                break;
            }

            var line = new string[Columns];
            int column = 0;
            foreach (Cell cell in row.Cells)
            {
                if (column >= Columns)
                {
                    break;
                }

                bool tail = cell.Wide == CellWide.SpacerTail;
                if (tail)
                {
                    line[column] = "";
                }
                else if (cell.Style.Invisible || string.IsNullOrEmpty(cell.Grapheme))
                {
                    line[column] = " ";
                }
                else
                {
                    line[column] = cell.Grapheme;
                }
                column++;
            }

            while (column < Columns)
            {
                line[column++] = " ";
            }

            graphemes[rowIndex] = line;
            wrapped[rowIndex] = row.Wrap;
            rowIndex++;
        }

        while (rowIndex < Rows)
        {
            graphemes[rowIndex] = new string[Columns];
            Array.Fill(graphemes[rowIndex], " ");
            rowIndex++;
        }
    }

    private ScreenFrame Copy(RenderState state, bool plainText, PaintedCell[]? buffer)
    {
        RenderStateColors colors = state.Colors;
        int count = Columns * Rows;
        PaintedCell[] cells = buffer is { Length: > 0 } && buffer.Length == count ? buffer : new PaintedCell[count];
        int used = 0;
        var plain = plainText ? new StringBuilder() : null;
        foreach (RenderStateRow row in state.Rows)
        {
            if (used >= cells.Length)
            {
                break;
            }

            int column = 0;
            var line = plainText ? new StringBuilder() : null;
            foreach (Cell cell in row.Cells)
            {
                if (used >= cells.Length)
                {
                    break;
                }

                PaintedCell painted = Paint(cell, colors);
                cells[used++] = painted;
                if (line is not null && !painted.WideTail)
                {
                    line.Append(painted.Grapheme.Length == 0 ? " " : painted.Grapheme);
                }

                column++;
                if (column >= Columns)
                {
                    break;
                }
            }

            while (column < Columns && used < cells.Length)
            {
                cells[used++] = Blank(colors);
                line?.Append(' ');
                column++;
            }

            if (plain is not null)
            {
                if (plain.Length > 0)
                {
                    plain.Append('\n');
                }

                plain.Append(line!.ToString().TrimEnd());
            }
        }

        while (used < cells.Length)
        {
            cells[used++] = Blank(colors);
        }

        return new ScreenFrame
        {
            Columns = Columns,
            Rows = Rows,
            Cells = cells,
            PlainText = plain?.ToString() ?? "",
            CursorX = state.CursorViewportX,
            CursorY = state.CursorViewportY,
            CursorVisible = state.CursorVisible && state.CursorViewportHasValue,
            CursorBlinking = state.CursorBlinking,
        };
    }

    private static PaintedCell Paint(Cell cell, RenderStateColors colors)
    {
        bool tail = cell.Wide == CellWide.SpacerTail;
        string grapheme = tail || string.IsNullOrEmpty(cell.Grapheme) ? "" : cell.Grapheme;
        Style style = cell.Style;
        (byte fr, byte fg, byte fb) = Bytes(cell.FgColor, colors.Foreground);
        (byte br, byte bg, byte bb) = Bytes(cell.BgColor, colors.Background);
        if (style.Inverse)
        {
            (fr, br) = (br, fr);
            (fg, bg) = (bg, fg);
            (fb, bb) = (bb, fb);
        }

        if (style.Invisible)
        {
            grapheme = "";
        }

        return new PaintedCell(
            grapheme,
            fr, fg, fb,
            br, bg, bb,
            style.Bold,
            style.Italic,
            style.Faint,
            style.Underline != 0 || cell.HasHyperlink,
            tail);
    }

    private static PaintedCell Blank(RenderStateColors colors)
    {
        (byte fr, byte fg, byte fb) = Bytes(null, colors.Foreground);
        (byte br, byte bg, byte bb) = Bytes(null, colors.Background);
        return new PaintedCell("", fr, fg, fb, br, bg, bb, false, false, false, false, false);
    }

    private static (byte R, byte G, byte B) Bytes(ColorRgb? color, ColorRgb fallback)
    {
        ColorRgb value = color ?? fallback;
        return (value.R, value.G, value.B);
    }

    private static string Sanitize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "";
        }

        string oneLine = title.Replace('\r', ' ').Replace('\n', ' ').Replace('\0', ' ').Trim();
        return oneLine.Length <= 60 ? oneLine : oneLine[..60];
    }
}
