using System.Collections.ObjectModel;
using System.Text;
using MyTui.Persistence;
using MyTui.Sessions;
using MyTui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace MyTui.UI;

/// <summary>
/// Session list plus Terminal, Files, and Preview. One grid is drawn. Other processes keep their PTY.
/// </summary>
internal sealed class SessionScreen : IDisposable
{
    private readonly IApplication _app;
    private readonly ObservableCollection<string> _labels = [];
    private readonly SessionList _list;
    private readonly CellGridView _grid;
    private readonly FileTree _tree;
    private readonly View _terminal;
    private readonly View _files;
    private readonly View _preview;
    private readonly View[] _panes;
    private readonly Tabs _tabs;
    private readonly Markdown _markdown;
#pragma warning disable CS0618 // Plain text only. The replacement is an editor package, which this phase does not take.
    private readonly TextView _text;
#pragma warning restore CS0618
    private readonly Label _note;
    private readonly Label _hint;
    private readonly Label _status;
    private SessionManager? _manager;
    private string _root = "";
    private object? _timeout;
    private int _blinkPhase = -1;
    private bool _updating;
    private bool _modal;
    private bool _disposed;

    internal SessionScreen(IApplication app)
    {
        _app = app;
        _grid = new CellGridView(HandleChord, StartFocused, CloseSelected);
        _hint = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Text = "Ctrl+N creates a session.",
        };
        _tree = new FileTree(this, new DelegateTreeBuilder<FileNode>(
            node => Workspace.Children(_root, node.Path),
            node => node.Directory))
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true,
        };
        _tree.SelectionChanged += (_, args) =>
        {
            if (_updating || _manager?.Focused is not Session session)
            {
                return;
            }

            session.SelectedPath = args.NewValue?.Path;
            ShowPreview(session);
        };
        _markdown = new Markdown
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            CanFocus = true,
            HotKeySpecifier = new Rune('\uE000'),
        };
#pragma warning disable CS0618
        _text = new TextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
            WordWrap = true,
            Visible = false,
            CanFocus = true,
        };
#pragma warning restore CS0618
        _note = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
            Visible = true,
            Text = "Select a file.",
        };
        _terminal = new View { Title = "Terminal", Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        _files = new View { Title = "Files", Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        _preview = new View { Title = "Preview", Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
        _terminal.Add(_grid, _hint);
        _files.Add(_tree);
        _preview.Add(_markdown, _text, _note);
        Wire(_markdown);
        Wire(_text);
        Wire(_preview);
        _panes = [_terminal, _files, _preview];
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        _tabs.Add(_terminal, _files, _preview);
        _status = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Text = "Ctrl+N creates a session.",
        };
        _list = new SessionList(this)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _list.SetSource(_labels);
        _list.ValueChanged += (_, args) =>
        {
            if (_updating || args.NewValue is not int index)
            {
                return;
            }

            ShowSession(index);
        };

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
            Title = "Session",
            X = Pos.Right(Sessions),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Content.Add(_tabs, _status);
    }

    internal FrameView Sessions { get; }

    internal FrameView Content { get; }

    internal void Start()
    {
        if (_timeout is not null)
        {
            return;
        }

        _manager = SessionManager.Load(StateStore.DefaultPath());
        RefreshLabels();
        if (_manager.Sessions.Count > 0)
        {
            ShowSession(_manager.FocusedIndex);
        }
        else
        {
            _hint.Visible = true;
            _grid.Visible = false;
            UpdateStatus();
        }

        _list.SetFocus();
        _timeout = _app.AddTimeout(TimeSpan.FromMilliseconds(30), Tick);
    }

    internal bool HandleChord(Key key)
    {
        if (key == Key.N.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            NewSession();
            return true;
        }

        if (key == Key.O.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            ToggleFocus();
            return true;
        }

        if (key == Key.T.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            CycleTab();
            return true;
        }

        if (key == Key.E.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            EditSession();
            return true;
        }

        if (key == Key.R.WithCtrl && !key.IsAlt && !key.IsShift)
        {
            RefreshFiles();
            return true;
        }

        return false;
    }

    internal void NewSession()
    {
        if (_manager is null || _modal)
        {
            return;
        }

        string shell;
        try
        {
            shell = ShellLocator.Resolve();
        }
        catch (InvalidOperationException ex)
        {
            _status.Text = ex.Message;
            return;
        }

        var initial = new SessionDraft(SuggestName(Path.GetFileName(shell)), Directory.GetCurrentDirectory(), shell, "");
        SessionDraft? draft = Ask("New session", initial, "Start", mustResolve: true);
        if (draft is null)
        {
            return;
        }

        var session = _manager.Create(
            draft.Value.Name,
            Path.GetFullPath(draft.Value.Cwd),
            draft.Value.Command,
            CommandLine.Split(draft.Value.Args));
        try
        {
            session.Start(Columns(), Rows());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            session.StartError = ex.Message;
        }

        RefreshLabels();
        ShowSession(_manager.FocusedIndex);
        if (session.IsRunning)
        {
            FocusPane();
        }
    }

    internal void EditSession()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        var initial = new SessionDraft(session.Name, session.Cwd, session.Command, string.Join(' ', session.Args));
        SessionDraft? draft = Ask("Edit session", initial, "Save", mustResolve: false);
        if (draft is null)
        {
            return;
        }

        _manager.Update(
            session,
            draft.Value.Name,
            Path.GetFullPath(draft.Value.Cwd),
            draft.Value.Command,
            CommandLine.Split(draft.Value.Args));
        session.StartError = null;
        Content.Title = session.Name;
        if (session.Tab == SessionTab.Files)
        {
            RefreshFiles();
        }
        else
        {
            ShowPreview(session);
        }

        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
    }

    internal void CloseSelected()
    {
        if (_manager?.Focused is not Session session)
        {
            return;
        }

        if (_grid.Session == session.Terminal)
        {
            _grid.Session = null;
        }

        _manager.Close(session);
        RefreshLabels();
        if (_manager.Sessions.Count == 0)
        {
            _grid.Visible = false;
            _hint.Visible = true;
            _grid.Live = false;
            Content.Title = "Session";
            ClearTree();
            ShowPreview(null);
            UpdateStatus();
            _list.SetFocus();
            return;
        }

        ShowSession(_manager.FocusedIndex);
    }

    internal void MoveFocused(int delta)
    {
        if (_manager is null || !_manager.MoveFocused(delta))
        {
            return;
        }

        RefreshLabels();
    }

    internal void ToggleFocus()
    {
        if (_list.HasFocus)
        {
            FocusPane();
        }
        else
        {
            _list.SetFocus();
        }
    }

    internal void RefreshFiles()
    {
        Session? session = _manager?.Focused;
        _root = session?.Cwd ?? "";
        string? keep = session?.SelectedPath;
        _updating = true;
        _tree.ClearObjects();
        if (session is not null && Directory.Exists(session.Cwd))
        {
            _tree.AddObjects(Workspace.Children(session.Cwd, session.Cwd));
        }

        _updating = false;
        if (keep is not null)
        {
            FileNode? match = Workspace.Children(_root, _root)
                .FirstOrDefault(node => string.Equals(node.Path, keep, StringComparison.Ordinal));
            if (match is not null)
            {
                _tree.GoTo(match);
            }
        }

        ShowPreview(session);
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

        if (_manager is null)
        {
            return;
        }

        foreach (Session session in _manager.Sessions)
        {
            session.Stop();
        }
    }

    private SessionDraft? Ask(string title, SessionDraft initial, string accept, bool mustResolve)
    {
        _modal = true;
        try
        {
            return SessionDialog.Ask(_app, title, initial, accept, mustResolve);
        }
        finally
        {
            _modal = false;
        }
    }

    private void StartFocused()
    {
        if (_manager?.Focused is not Session session || session.IsRunning)
        {
            return;
        }

        try
        {
            session.Start(Columns(), Rows());
            _grid.Session = session.Terminal;
            _grid.Live = session.Tab == SessionTab.Terminal;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or PlatformNotSupportedException)
        {
            session.StartError = ex.Message;
        }

        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
        ResizeActive();
    }

    private void CycleTab()
    {
        if (_manager?.Focused is not Session session)
        {
            return;
        }

        SessionTab next = session.Tab switch
        {
            SessionTab.Terminal => SessionTab.Files,
            SessionTab.Files => SessionTab.Preview,
            _ => SessionTab.Terminal,
        };
        _manager.SetTab(session, next);
        SelectTab(next);
        _grid.Live = next == SessionTab.Terminal;
        if (next == SessionTab.Files)
        {
            RefreshFiles();
        }

        FocusPane();
        ResizeActive();
    }

    private void ShowSession(int index)
    {
        if (_manager is null || index < 0 || index >= _manager.Sessions.Count)
        {
            _grid.Session = null;
            _grid.Live = false;
            _grid.Visible = false;
            _hint.Visible = true;
            UpdateStatus();
            return;
        }

        bool listHadFocus = _list.HasFocus;
        _manager.Focus(index);
        Session session = _manager.Sessions[index];
        _hint.Visible = false;
        _grid.Visible = true;
        _grid.Session = session.Terminal;
        _grid.Live = session.Tab == SessionTab.Terminal;
        Content.Title = session.Name;
        SelectTab(session.Tab);
        if (session.Tab == SessionTab.Files)
        {
            RefreshFiles();
        }
        else
        {
            ShowPreview(session);
        }

        ResizeActive();
        UpdateStatus();
        SyncLabel(index);
        if (listHadFocus)
        {
            _list.SetFocus();
        }
    }

    private void SelectTab(SessionTab tab)
    {
        _updating = true;
        _tabs.Value = _panes[(int)tab];
        _updating = false;
    }

    private void FocusPane()
    {
        switch (_manager?.Focused?.Tab)
        {
            case SessionTab.Files:
                _tree.SetFocus();
                break;
            case SessionTab.Preview:
                if (_markdown.Visible)
                {
                    _markdown.SetFocus();
                }
                else if (_text.Visible)
                {
                    _text.SetFocus();
                }
                else
                {
                    _preview.SetFocus();
                }

                break;
            default:
                if (_grid.Visible)
                {
                    _grid.SetFocus();
                }

                break;
        }
    }

    private void ShowPreview(Session? session)
    {
        if (session is null)
        {
            ApplyPreview(new PreviewText.Content(PreviewText.Kind.None, "Select a file."));
            return;
        }

        if (session.SelectedPath is not null && !Workspace.IsInside(session.Cwd, session.SelectedPath))
        {
            ApplyPreview(new PreviewText.Content(PreviewText.Kind.None, "Outside the session directory."));
            return;
        }

        ApplyPreview(PreviewText.Read(session.SelectedPath));
    }

    private void ApplyPreview(PreviewText.Content content)
    {
        _markdown.Visible = content.Kind == PreviewText.Kind.Markdown;
        _text.Visible = content.Kind == PreviewText.Kind.Text;
        _note.Visible = !_markdown.Visible && !_text.Visible;
        if (_markdown.Visible)
        {
            _markdown.Text = content.Text;
        }

        if (_text.Visible)
        {
            _text.Text = content.Text;
        }

        if (_note.Visible)
        {
            _note.Text = content.Text;
        }
    }

    private void ClearTree()
    {
        _updating = true;
        _root = "";
        _tree.ClearObjects();
        _updating = false;
    }

    private void RefreshLabels()
    {
        if (_manager is null)
        {
            return;
        }

        _updating = true;
        while (_labels.Count > 0)
        {
            _labels.RemoveAt(_labels.Count - 1);
        }

        foreach (Session session in _manager.Sessions)
        {
            _labels.Add(session.ListLabel);
        }

        if (_manager.Sessions.Count > 0)
        {
            _list.SelectedItem = _manager.FocusedIndex;
        }

        _updating = false;
    }

    private void SyncLabel(int index)
    {
        if (_manager is null || index < 0 || index >= _labels.Count)
        {
            return;
        }

        string label = _manager.Sessions[index].ListLabel;
        if (_labels[index] != label)
        {
            _labels[index] = label;
        }
    }

    private void UpdateStatus()
    {
        if (_manager?.Focused is Session session)
        {
            _status.Text = session.StatusLine;
            return;
        }

        _status.Text = "Ctrl+N creates a session.";
    }

    private void ResizeActive()
    {
        if (_grid.Live && _grid.Session is TerminalSession session && _grid.Viewport.Width > 1 && _grid.Viewport.Height > 1)
        {
            session.Resize(_grid.Viewport.Width, _grid.Viewport.Height);
        }
    }

    private int Columns() => _grid.Viewport.Width > 1 ? _grid.Viewport.Width : 80;

    private int Rows() => _grid.Viewport.Height > 1 ? _grid.Viewport.Height : 24;

    private string SuggestName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            name = "session";
        }

        var taken = _manager!.Sessions.Select(session => session.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
        {
            return name;
        }

        for (int i = 2; ; i++)
        {
            string candidate = name + " " + i;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private bool Tick()
    {
        if (_disposed)
        {
            return false;
        }

        if (_modal || _manager is null)
        {
            return true;
        }

        for (int i = 0; i < _manager.Sessions.Count && i < _labels.Count; i++)
        {
            SyncLabel(i);
        }

        UpdateStatus();
        ResizeActive();
        TerminalSession? active = _grid.Session;
        if (active is null)
        {
            return true;
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

        foreach (Session session in _manager.Sessions)
        {
            if (session.Terminal is TerminalSession other && !ReferenceEquals(other, active))
            {
                other.ConsumeDirty();
            }
        }

        return true;
    }

    private void Wire(View view)
    {
        view.KeyDown += (_, key) =>
        {
            if (HandleChord(key))
            {
                key.Handled = true;
                return;
            }

            if (key == Key.Delete && !key.IsCtrl && !key.IsAlt && !key.IsShift)
            {
                CloseSelected();
                key.Handled = true;
            }
        };
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

            if (_screen.HandleChord(key))
            {
                return true;
            }

            if (key == Key.Delete && !key.IsCtrl && !key.IsAlt && !key.IsShift)
            {
                _screen.CloseSelected();
                return true;
            }

            if (key == Key.Enter)
            {
                _screen.FocusPane();
                return true;
            }

            if (key == Key.CursorUp.WithAlt && !key.IsCtrl && !key.IsShift)
            {
                _screen.MoveFocused(-1);
                return true;
            }

            if (key == Key.CursorDown.WithAlt && !key.IsCtrl && !key.IsShift)
            {
                _screen.MoveFocused(1);
                return true;
            }

            return base.OnKeyDown(key);
        }
    }

    private sealed class FileTree : TreeView<FileNode>
    {
        private readonly SessionScreen _screen;

        internal FileTree(SessionScreen screen, ITreeBuilder<FileNode> builder)
            : base(builder) => _screen = screen;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
            {
                return false;
            }

            if (_screen.HandleChord(key))
            {
                return true;
            }

            if (key == Key.Delete && !key.IsCtrl && !key.IsAlt && !key.IsShift)
            {
                _screen.CloseSelected();
                return true;
            }

            return base.OnKeyDown(key);
        }
    }
}
