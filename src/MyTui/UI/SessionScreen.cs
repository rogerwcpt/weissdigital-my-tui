using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MyTui.Terminal;

namespace MyTui.UI;

/// <summary>
/// In-memory session list. One grid is drawn. The others keep their process and VT state.
/// </summary>
internal sealed class SessionScreen : IDisposable
{
    private readonly IApplication _app;
    private readonly List<TerminalSession> _sessions = [];
    private readonly ObservableCollection<string> _labels = [];
    private readonly SessionList _list;
    private readonly CellGridView _grid;
    private readonly Label _empty;
    private object? _timeout;
    private int _blinkPhase = -1;
    private bool _disposed;

    internal SessionScreen(IApplication app)
    {
        _app = app;
        _grid = new CellGridView(NewShell, ToggleFocus);
        _empty = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Visible = false,
            Text = "Ctrl+N starts a shell.",
        };
        _list = new SessionList(this)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _list.SetSource(_labels);
        _list.ValueChanged += (_, args) => Show(args.NewValue);

        Sessions = new FrameView
        {
            Title = "Sessions",
            X = 0,
            Y = 0,
            Width = 28,
            Height = Dim.Fill(),
        };
        Sessions.Add(_list);

        Content = new FrameView
        {
            Title = "Terminal",
            X = Pos.Right(Sessions),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Content.Add(_grid, _empty);
    }

    internal FrameView Sessions { get; }

    internal FrameView Content { get; }

    internal void Start()
    {
        if (_timeout is not null)
        {
            return;
        }

        NewShell();
        _grid.SetFocus();
        _timeout = _app.AddTimeout(TimeSpan.FromMilliseconds(30), Tick);
    }

    internal void NewShell()
    {
        string shell = ShellLocator.Resolve();
        int columns = _grid.Viewport.Width > 1 ? _grid.Viewport.Width : 80;
        int rows = _grid.Viewport.Height > 1 ? _grid.Viewport.Height : 24;
        var session = TerminalSession.Start(shell, [shell], columns, rows, UniqueName(Path.GetFileName(shell)));
        _sessions.Add(session);
        _labels.Add(session.Label);
        _list.SelectedItem = _sessions.Count - 1;
        Show(_list.SelectedItem);
        _grid.SetFocus();
    }

    internal void CloseSelected()
    {
        int index = _list.SelectedItem ?? -1;
        if (index < 0 || index >= _sessions.Count)
        {
            return;
        }

        TerminalSession session = _sessions[index];
        _sessions.RemoveAt(index);
        _labels.RemoveAt(index);
        if (_grid.Session == session)
        {
            _grid.Session = null;
        }

        session.Dispose();
        if (_sessions.Count == 0)
        {
            _empty.Visible = true;
            _list.SetFocus();
            return;
        }

        int next = Math.Min(index, _sessions.Count - 1);
        _list.SelectedItem = next;
        Show(next);
    }

    internal void ToggleFocus()
    {
        if (_grid.HasFocus)
        {
            _list.SetFocus();
        }
        else if (_grid.Session is not null)
        {
            _grid.SetFocus();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_timeout is not null)
        {
            _app.RemoveTimeout(_timeout);
        }

        foreach (TerminalSession session in _sessions)
        {
            session.Dispose();
        }

        _sessions.Clear();
    }

    private bool Tick()
    {
        if (_disposed)
        {
            return false;
        }

        for (int i = 0; i < _sessions.Count; i++)
        {
            string label = _sessions[i].Label;
            if (_labels[i] != label)
            {
                _labels[i] = label;
            }
        }

        TerminalSession? active = _grid.Session;
        if (active is null)
        {
            return true;
        }

        int columns = _grid.Viewport.Width;
        int rows = _grid.Viewport.Height;
        if (columns > 1 && rows > 1 && (active.Columns != columns || active.Rows != rows))
        {
            active.Resize(columns, rows);
        }

        int phase = (int)(Environment.TickCount64 / 500);
        bool blink = _grid.HasFocus && _grid.CursorBlinking && phase != _blinkPhase;
        if (phase != _blinkPhase)
        {
            _blinkPhase = phase;
            _grid.Blink(phase % 2 == 0);
        }

        if (active.ConsumeDirty() || blink)
        {
            _grid.SetNeedsDraw();
        }

        return true;
    }

    private void Show(int? index)
    {
        int row = index ?? -1;
        if (row < 0 || row >= _sessions.Count)
        {
            _grid.Session = null;
            _empty.Visible = true;
            return;
        }

        _empty.Visible = false;
        TerminalSession session = _sessions[row];
        if (_grid.Viewport.Width > 1 && _grid.Viewport.Height > 1)
        {
            session.Resize(_grid.Viewport.Width, _grid.Viewport.Height);
        }

        _grid.Session = session;
    }

    private sealed class SessionList : ListView
    {
        private readonly SessionScreen _screen;

        internal SessionList(SessionScreen screen) => _screen = screen;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
            {
                return false;
            }

            if (key == Key.N.WithCtrl && !key.IsAlt && !key.IsShift)
            {
                _screen.NewShell();
                return true;
            }

            if (key == Key.O.WithCtrl && !key.IsAlt && !key.IsShift)
            {
                _screen.ToggleFocus();
                return true;
            }

            if (key == Key.Delete)
            {
                _screen.CloseSelected();
                return true;
            }

            if (key == Key.Enter)
            {
                _screen._grid.SetFocus();
                return true;
            }

            return base.OnKeyDown(key);
        }
    }

    private string UniqueName(string name)
    {
        if (_sessions.All(session => session.Label != name && !session.Label.StartsWith(name + " ", StringComparison.Ordinal)))
        {
            return name;
        }

        for (int i = 2; ; i++)
        {
            string candidate = name + " " + i;
            if (_sessions.All(session => session.Label != candidate))
            {
                return candidate;
            }
        }
    }
}
