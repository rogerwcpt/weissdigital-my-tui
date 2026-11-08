using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>Drag either border between Sessions and the content without changing focus.</summary>
internal sealed class SessionSplitter : IDisposable
{
    internal const int DefaultWidth = 28;
    internal const int MinimumSessionsWidth = 18;
    internal const int MinimumContentWidth = 20;

    private readonly FrameView _sessions;
    private readonly View _leftBorder;
    private readonly View _rightBorder;
    private readonly Action<int> _resize;
    private View? _dragBorder;
    private int _startColumn;
    private int _startWidth;
    private bool _active;

    internal SessionSplitter(FrameView sessions, FrameView content, Action<int> resize)
    {
        _sessions = sessions;
        _resize = resize;
        _leftBorder = sessions.Border.GetOrCreateView();
        _rightBorder = content.Border.GetOrCreateView();
        foreach (View border in new[] { _leftBorder, _rightBorder })
        {
            border.MousePositionTracking = true;
            border.MouseEvent += OnMouseEvent;
        }
    }

    internal bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (!value)
            {
                EndDrag();
            }
        }
    }

    /// <summary>Keep both panels usable, sharing the space when the host is very narrow.</summary>
    internal static int ClampWidth(int preferred, int available)
    {
        if (available <= 0)
        {
            return 0;
        }

        int content = Math.Min(MinimumContentWidth, (available + 1) / 2);
        int maximum = available - content;
        int minimum = Math.Min(MinimumSessionsWidth, maximum);
        return Math.Clamp(preferred, minimum, maximum);
    }

    private void OnMouseEvent(object? sender, Mouse mouse)
    {
        if (sender is not View border || !Active)
        {
            return;
        }

        MouseFlags flags = mouse.Flags;
        if (_dragBorder is not null)
        {
            if (border != _dragBorder)
            {
                return;
            }

            if (flags.HasFlag(MouseFlags.PositionReport) || flags.HasFlag(MouseFlags.LeftButtonReleased))
            {
                _resize(_startWidth + mouse.ScreenPosition.X - _startColumn);
            }

            if (flags.HasFlag(MouseFlags.LeftButtonReleased))
            {
                EndDrag();
            }

            mouse.Handled = true;
            return;
        }

        if (!flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            return;
        }

        var position = border.ScreenToViewport(mouse.ScreenPosition);
        int column = border == _leftBorder ? border.Viewport.Width - 1 : 0;
        // Leave the captions and bottom corners alone.
        if (position.X != column || position.Y < 1 || position.Y >= border.Viewport.Height - 1)
        {
            return;
        }

        _startColumn = mouse.ScreenPosition.X;
        _startWidth = _sessions.Frame.Width;
        _dragBorder = border;
        border.App?.Mouse.GrabMouse(border);
        mouse.Handled = true;
    }

    private void EndDrag()
    {
        if (_dragBorder?.App is { } app && app.Mouse.IsGrabbed(_dragBorder))
        {
            app.Mouse.UngrabMouse();
        }

        _dragBorder = null;
    }

    public void Dispose()
    {
        EndDrag();
        _leftBorder.MouseEvent -= OnMouseEvent;
        _rightBorder.MouseEvent -= OnMouseEvent;
    }
}
