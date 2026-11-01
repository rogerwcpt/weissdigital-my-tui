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
/// Session list, Terminal and Files tabs, and a preview panel on the right. One grid is drawn.
/// Other processes keep their PTY.
/// </summary>
internal sealed class SessionScreen : IDisposable
{
    private readonly IApplication _app;
    private readonly ObservableCollection<string> _labels = [];
    private readonly SessionList _list;
    private readonly CellGridView _grid;
    private readonly FileTree _tree;
    private readonly Label _filesNote;
    private readonly View _terminal;
    private readonly View _files;
    private readonly FrameView _preview;
    private readonly View[] _panes;
    private readonly PanelCaption _sessionsCaption;
    private readonly PanelCaption _contentCaption;
    private readonly PanelCaption _previewCaption;
    private readonly Tabs _tabs;
    private readonly Markdown _markdown;
    private readonly PreviewTextView _text;
    private readonly Label _note;
    private readonly Label _hint;
    private readonly Label _commands;
    private readonly FramePacer _pacer = new();
    private readonly LeaderState _leader = new();
    private readonly HashSet<Session> _unseen = [];
    private readonly HashSet<Session> _rang = [];
    private readonly Dictionary<Session, long> _quietUntil = [];
    private SessionManager? _manager;
    private string _root = "";
    private object? _timeout;
    private int _blinkPhase = -1;
    private bool _previewOpen;
    private bool _sessionsHidden;
    private bool _contentHidden;
    private MaximizedPanel _maximized;
    private bool _updating;
    private bool _modal;
    private bool _disposed;

    /// <summary>
    /// Mac Delete is Backspace. Forward delete is Delete or DeleteChar. Any of them closes from outside a live terminal.
    /// </summary>
    internal static bool IsCloseKey(Key key) =>
        !key.IsCtrl && !key.IsAlt && !key.IsShift
        && (key == Key.Backspace || key == Key.Delete || key == Key.DeleteChar);

    /// <summary>Selected session row, with a close mark in the last column.</summary>
    internal static string ActiveRow(string label, int width)
    {
        if (width < 2)
        {
            return label;
        }

        var runes = label.EnumerateRunes().ToList();
        if (runes.Count >= width)
        {
            runes = runes.Take(width - 1).ToList();
        }

        int spaces = width - 1 - runes.Count;
        return string.Concat(runes) + new string(' ', spaces) + "\u00d7";
    }

    internal static string ActivityLabel(string label, bool unseen, bool bell)
    {
        if (bell)
        {
            return label + " !";
        }

        return unseen ? label + " ●" : label;
    }

    internal static string StatusRow(string details, string hint, int width)
    {
        if (width < 1 || string.IsNullOrEmpty(hint) || width < hint.Length + 12)
        {
            return FitWidth(details, Math.Max(width, 0));
        }

        int room = width - hint.Length;
        string left = details.Length < room ? details : FitWidth(details, room - 1);
        return left.PadRight(room) + hint;
    }

    private static string FitWidth(string text, int width)
    {
        if (width <= 0)
        {
            return "";
        }

        if (text.Length <= width)
        {
            return text;
        }

        return width == 1 ? "…" : text[..(width - 1)] + "…";
    }

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
            Text = "Ctrl+N creates a session. Ctrl+Shift+P opens commands.",
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

            string? previous = session.SelectedPath;
            session.SelectedPath = args.NewValue?.Path;
            if (args.NewValue is { Directory: false }
                && !string.Equals(previous, session.SelectedPath, StringComparison.Ordinal))
            {
                _previewOpen = true;
                if (_maximized == MaximizedPanel.Content)
                {
                    _maximized = MaximizedPanel.None;
                }

                ApplyLayout();
            }

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
        _markdown.SetScheme(HostTheme.Preview());
        _text = new PreviewTextView { Visible = false };
        _text.SetScheme(HostTheme.Preview());
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
        _filesNote = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = false,
            Visible = false,
            Text = "Empty directory.",
        };
        _terminal = new View { Title = "Terminal", CanFocus = true };
        _files = new View { Title = "Files", CanFocus = true };
        _preview = new FrameView
        {
            Title = "Preview",
            X = 0,
            Y = 0,
            Width = 0,
            Height = Dim.Fill(),
            Visible = false,
            CanFocus = true,
        };
        _terminal.Add(_grid, _hint);
        // A click redraws a frame and clears the area under it. The grid would then skip
        // cells it still has cached. A parent clear has to schedule a full paint first.
        _terminal.ClearingViewport += (_, _) => _grid.Repaint();
        _files.Add(_tree, _filesNote);
        _preview.Add(_markdown, _text, _note);
        Wire(_markdown);
        Wire(_text);
        Wire(_preview);
        Wire(_terminal);
        Wire(_files);
        _panes = [_terminal, _files];
        _tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _tabs.Add(_terminal, _files);
        _tabs.ValueChanged += (_, args) => OnTabChanged(args.NewValue);
        Wire(_tabs);
        _list = new SessionList(this)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
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
        _commands = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Text = CommandPalette.Footer,
        };
        Sessions.Add(_list, new PaletteRule(), _commands);
        Content = new FrameView
        {
            Title = "Session",
            X = Pos.Right(Sessions),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        Content.Add(_tabs, _preview);
        Content.ClearingViewport += (_, _) => _grid.Repaint();
        _sessionsCaption = PanelCaption.Attach(Sessions, () => ToggleMaximize(MaximizedPanel.Sessions), ToggleSessions);
        BorderGlyph.Place(Sessions, '+', atEnd: true, NewSession, boxed: true);
        _contentCaption = PanelCaption.Attach(Content, () => ToggleMaximize(MaximizedPanel.Content), MinimizeContent);
        _previewCaption = PanelCaption.Attach(_preview, () => ToggleMaximize(MaximizedPanel.Preview), MinimizePreview);
        ApplyLayout();
    }

    internal void AttachTitle(Window window)
    {
        TitleBar.Attach(window, ShowPalette);
        window.ClearingViewport += (_, _) => _grid.Repaint();
        window.KeyDown += (_, key) =>
        {
            // A live terminal consumes Ctrl+Q and may still bubble the same event.
            if (key.Handled || _modal || key != Key.Q.WithCtrl || key.IsAlt || key.IsShift)
            {
                return;
            }

            RequestQuit();
            key.Handled = true;
        };
    }

    internal FrameView Sessions { get; }

    internal FrameView Content { get; }

    internal void Start()
    {
        if (_timeout is not null)
        {
            return;
        }

        AppKeys.Load();
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
        _app.Iteration += OnIteration;
    }

    internal bool HandleChord(Key key) => HandleChord(key, inTerminal: false);

    internal bool HandleChord(Key key, bool inTerminal)
    {
        KeyRoute route = AppKeys.Route(key, inTerminal, _leader, Environment.TickCount64, out AppCommand command);
        if (route == KeyRoute.Forward)
        {
            return false;
        }

        if (route == KeyRoute.Run)
        {
            Run(command);
        }

        UpdateStatus();
        return true;
    }

    private void Run(AppCommand command)
    {
        switch (command)
        {
            case AppCommand.Menu:
                ShowPalette();
                break;
            case AppCommand.NewSession:
                NewSession();
                break;
            case AppCommand.CycleFocus:
                ToggleFocus();
                break;
            case AppCommand.NextPanel:
                CyclePanels(forward: true);
                break;
            case AppCommand.PreviousPanel:
                CyclePanels(forward: false);
                break;
            case AppCommand.HideSessions:
                ToggleSessions();
                break;
            case AppCommand.Maximize:
                ToggleMaximize(PanelUnderFocus());
                break;
            case AppCommand.CycleTab:
                CycleTab();
                break;
            case AppCommand.Preview:
                TogglePreview();
                break;
            case AppCommand.Edit:
                EditSession();
                break;
            case AppCommand.Refresh:
                RefreshFiles();
                break;
            case AppCommand.Templates:
                Templates();
                break;
            case AppCommand.Close:
                CloseSelected();
                break;
            case AppCommand.Copy:
                CopyVisible();
                break;
            case AppCommand.Paste:
                PasteClipboard();
                break;
            case AppCommand.Help:
                ShowPalette();
                break;
            case AppCommand.Quit:
                RequestQuit();
                break;
        }
    }

    private void RequestQuit()
    {
        if (_modal || _manager is null)
        {
            return;
        }

        int running = _manager.Sessions.Count(session => session.IsRunning);
        if (running > 0)
        {
            HoldModal();
            bool quit;
            try
            {
                quit = CloseDialog.ConfirmQuit(_app, running);
            }
            finally
            {
                ReleaseModal();
            }

            if (!quit)
            {
                return;
            }
        }

        _app.RequestStop();
    }

    internal void NewSession() => BeginNew(null);

    internal void OpenFolder()
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
            _commands.Text = ex.Message;
            return;
        }

        HoldModal();
        string? folder;
        try
        {
            folder = FolderDialog.Ask(_app, _manager.Focused?.Cwd);
        }
        finally
        {
            ReleaseModal();
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        string leaf = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var session = _manager.Create(
            SuggestName(string.IsNullOrEmpty(leaf) ? "folder" : leaf),
            Path.GetFullPath(folder),
            shell,
            [],
            null);
        try
        {
            session.Start(Columns(), Rows());
            NoteStarted(session);
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

    private void BeginNew(SessionTemplate? template)
    {
        if (_manager is null || _modal)
        {
            return;
        }

        SessionDraft initial;
        IReadOnlyDictionary<string, string>? env = null;
        if (template is null)
        {
            string shell;
            try
            {
                shell = ShellLocator.Resolve();
            }
            catch (InvalidOperationException ex)
            {
                _commands.Text = ex.Message;
                return;
            }

            initial = new SessionDraft(SuggestName(Path.GetFileName(shell)), Directory.GetCurrentDirectory(), shell, "");
        }
        else
        {
            string cwd = string.IsNullOrWhiteSpace(template.Cwd) ? Directory.GetCurrentDirectory() : template.Cwd;
            initial = new SessionDraft(
                SuggestName(template.Name),
                cwd,
                template.Command,
                string.Join(' ', template.Args));
            env = template.Env;
        }

        SessionDraft? draft = Ask("New session", initial, "Start", mustResolve: true);
        if (draft is null)
        {
            return;
        }

        var session = _manager.Create(
            draft.Value.Name,
            Path.GetFullPath(draft.Value.Cwd),
            draft.Value.Command,
            CommandLine.Split(draft.Value.Args),
            env);
        try
        {
            session.Start(Columns(), Rows());
            NoteStarted(session);
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
        RefreshFiles();
        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
    }

    internal void CloseSelected()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if (session.IsRunning && !ConfirmClose(session.Name))
        {
            return;
        }

        if (_grid.Session == session.Terminal)
        {
            _grid.Session = null;
        }

        _manager.Close(session);
        Forget(session);
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
            return;
        }

        if (_previewOpen && !PreviewHasFocus())
        {
            FocusPreview();
            return;
        }

        _list.SetFocus();
    }

    internal void RefreshFiles()
    {
        Session? session = _manager?.Focused;
        _root = session?.Cwd ?? "";
        string? keep = session?.SelectedPath;
        _updating = true;
        _tree.ClearObjects();
        IReadOnlyList<FileNode> children = [];
        if (session is not null && Directory.Exists(session.Cwd))
        {
            children = Workspace.Children(session.Cwd, session.Cwd);
            _tree.AddObjects(children);
        }

        _tree.Visible = children.Count > 0;
        _filesNote.Visible = !_tree.Visible;
        _filesNote.Text = session is not null && Directory.Exists(session.Cwd)
            ? "Empty directory."
            : "Directory not found.";
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
        _app.Iteration -= OnIteration;
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

    private void ShowAbout()
    {
        if (_modal)
        {
            return;
        }

        HoldModal();
        try
        {
            AboutDialog.Show(_app);
        }
        finally
        {
            ReleaseModal();
        }
    }

    private void ShowPalette()
    {
        if (_modal)
        {
            return;
        }

        _commands.Text = CommandPalette.Footer;
        HoldModal();
        string? id;
        try
        {
            id = CommandPalette.Ask(_app);
        }
        finally
        {
            ReleaseModal();
        }

        switch (id)
        {
            case "new":
                NewSession();
                break;
            case "open":
                OpenFolder();
                break;
            case "focus":
                ToggleFocus();
                break;
            case "next":
                CyclePanels(forward: true);
                break;
            case "previous":
                CyclePanels(forward: false);
                break;
            case "hide":
                ToggleSessions();
                break;
            case "maximize":
                ToggleMaximize(PanelUnderFocus());
                break;
            case "tab":
                CycleTab();
                break;
            case "preview":
                TogglePreview();
                break;
            case "edit":
                EditSession();
                break;
            case "refresh":
                RefreshFiles();
                break;
            case "templates":
                Templates();
                break;
            case "close":
                CloseSelected();
                break;
            case "copy":
                CopyVisible();
                break;
            case "paste":
                PasteClipboard();
                break;
            case "about":
                ShowAbout();
                break;
            case "quit":
                RequestQuit();
                break;
        }
    }

    private void Templates()
    {
        if (_manager is null || _modal)
        {
            return;
        }

        var store = new TemplateStore(TemplateStore.DefaultPath());
        while (true)
        {
            IReadOnlyList<SessionTemplate> templates = store.Load();
            HoldModal();
            TemplateAction action;
            int index;
            try
            {
                action = TemplateDialog.Ask(_app, templates, out index);
            }
            finally
            {
                ReleaseModal();
            }

            if (action == TemplateAction.Cancel)
            {
                return;
            }

            if (action == TemplateAction.Save)
            {
                if (_manager.Focused is not Session session)
                {
                    _commands.Text = "No session to save.";
                    return;
                }

                store.Upsert(new SessionTemplate
                {
                    Name = session.Name,
                    Cwd = session.Cwd,
                    Command = session.Command,
                    Args = session.Args,
                    Env = new Dictionary<string, string>(session.Env),
                });
                continue;
            }

            if (action == TemplateAction.Remove)
            {
                store.RemoveAt(index);
                continue;
            }

            if (action == TemplateAction.Apply && index >= 0 && index < templates.Count)
            {
                BeginNew(templates[index]);
            }

            return;
        }
    }

    private bool ConfirmClose(string name)
    {
        HoldModal();
        try
        {
            return CloseDialog.Confirm(_app, name);
        }
        finally
        {
            ReleaseModal();
        }
    }

    private void CopyVisible()
    {
        string? text = _grid.Session?.Capture().PlainText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _app.Clipboard?.TrySetClipboardData(text);
    }

    private void PasteClipboard()
    {
        if (_grid.Session is not TerminalSession session || session.HasExited || !session.IsRunning)
        {
            return;
        }

        if (_app.Clipboard?.TryGetClipboardData(out string text) == true && !string.IsNullOrEmpty(text))
        {
            session.Paste(text);
        }
    }

    private SessionDraft? Ask(string title, SessionDraft initial, string accept, bool mustResolve)
    {
        HoldModal();
        try
        {
            return SessionDialog.Ask(_app, title, initial, accept, mustResolve);
        }
        finally
        {
            ReleaseModal();
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
            NoteStarted(session);
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

        if (_maximized is MaximizedPanel.Sessions or MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        _contentHidden = false;
        SessionTab next = session.Tab == SessionTab.Files ? SessionTab.Terminal : SessionTab.Files;
        _manager.SetTab(session, next);
        ApplyLayout();
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
        _unseen.Remove(session);
        _rang.Remove(session);
        if (session.Tab == SessionTab.Preview)
        {
            _previewOpen = true;
            ApplyLayout();
            _manager.SetTab(session, SessionTab.Terminal);
        }

        _hint.Visible = false;
        _grid.Visible = true;
        _grid.Session = session.Terminal;
        _grid.Live = session.Tab == SessionTab.Terminal;
        Content.Title = session.Name;
        SelectTab(session.Tab);
        RefreshFiles();

        ResizeActive();
        UpdateStatus();
        for (int i = 0; i < _labels.Count; i++)
        {
            SyncLabel(i);
        }

        if (listHadFocus)
        {
            _list.SetFocus();
        }
    }

    private void SelectTab(SessionTab tab)
    {
        if (tab == SessionTab.Preview)
        {
            tab = SessionTab.Terminal;
        }

        _updating = true;
        _tabs.Value = _panes[(int)tab];
        _updating = false;
        if (tab == SessionTab.Terminal)
        {
            _grid.Repaint();
        }
    }

    private void OnTabChanged(View? view)
    {
        if (_updating || _manager?.Focused is not Session session || view is null)
        {
            return;
        }

        int index = Array.IndexOf(_panes, view);
        if (index < 0)
        {
            return;
        }

        var tab = (SessionTab)index;
        _manager.SetTab(session, tab);
        _grid.Live = tab == SessionTab.Terminal;
        if (tab == SessionTab.Files)
        {
            RefreshFiles();
        }
        else
        {
            _grid.Repaint();
        }

        FocusPane();
        ResizeActive();
    }

    private void TogglePreview()
    {
        bool previewFocused = PreviewHasFocus();
        _previewOpen = !_previewOpen;
        if (!_previewOpen && _maximized == MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        if (_previewOpen && _maximized != MaximizedPanel.None)
        {
            _maximized = MaximizedPanel.None;
        }

        _contentHidden = false;
        ApplyLayout();
        if (_previewOpen)
        {
            FocusPreview();
        }
        else if (previewFocused)
        {
            FocusPane();
        }

        ResizeActive();
    }

    private void CyclePanels(bool forward)
    {
        PanelStop[] order = [PanelStop.Sessions, PanelStop.Terminal, PanelStop.Files];
        int index = Array.IndexOf(order, CurrentStop());
        if (index < 0)
        {
            index = 0;
        }

        int next = (index + (forward ? 1 : order.Length - 1)) % order.Length;
        FocusStop(order[next]);
    }

    private PanelStop CurrentStop()
    {
        if (_list.HasFocus)
        {
            return PanelStop.Sessions;
        }

        if (_tree.HasFocus || _files.HasFocus)
        {
            return PanelStop.Files;
        }

        if (_grid.HasFocus || _terminal.HasFocus)
        {
            return PanelStop.Terminal;
        }

        if (PreviewHasFocus() || _manager?.Focused?.Tab == SessionTab.Files)
        {
            return _manager?.Focused?.Tab == SessionTab.Files ? PanelStop.Files : PanelStop.Terminal;
        }

        return PanelStop.Terminal;
    }

    private void FocusStop(PanelStop stop)
    {
        switch (stop)
        {
            case PanelStop.Sessions:
                _sessionsHidden = false;
                if (_maximized is MaximizedPanel.Content or MaximizedPanel.Preview)
                {
                    _maximized = MaximizedPanel.None;
                }

                ApplyLayout();
                _list.SetFocus();
                break;
            case PanelStop.Terminal:
                ShowTab(SessionTab.Terminal);
                break;
            case PanelStop.Files:
                ShowTab(SessionTab.Files);
                break;
        }
    }

    private void ShowTab(SessionTab tab)
    {
        _contentHidden = false;
        if (_maximized is MaximizedPanel.Sessions or MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        ApplyLayout();
        if (_manager?.Focused is Session session)
        {
            _manager.SetTab(session, tab);
            _grid.Live = tab == SessionTab.Terminal;
            if (tab == SessionTab.Files)
            {
                RefreshFiles();
            }
        }

        SelectTab(tab);
        FocusPane();
        ResizeActive();
    }

    private void ToggleSessions()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(_sessionsHidden, _contentHidden, _previewOpen, _maximized);
        if (layout.SessionsVisible)
        {
            _sessionsHidden = true;
            _contentHidden = false;
            if (_maximized == MaximizedPanel.Sessions)
            {
                _maximized = MaximizedPanel.None;
            }

            ApplyLayout();
            if (_list.HasFocus)
            {
                FocusPane();
            }

            return;
        }

        _sessionsHidden = false;
        if (_maximized is MaximizedPanel.Content or MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        ApplyLayout();
        _list.SetFocus();
    }

    private void MinimizeContent()
    {
        _contentHidden = true;
        _sessionsHidden = false;
        if (_maximized is MaximizedPanel.Content or MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        ApplyLayout();
        _list.SetFocus();
    }

    private void MinimizePreview()
    {
        bool previewFocused = PreviewHasFocus();
        _previewOpen = false;
        if (_maximized == MaximizedPanel.Preview)
        {
            _maximized = MaximizedPanel.None;
        }

        ApplyLayout();
        if (previewFocused)
        {
            FocusPane();
        }
    }

    private void ToggleMaximize(MaximizedPanel panel)
    {
        if (_maximized == panel)
        {
            _maximized = MaximizedPanel.None;
        }
        else
        {
            _maximized = panel;
            switch (panel)
            {
                case MaximizedPanel.Sessions:
                    _sessionsHidden = false;
                    break;
                case MaximizedPanel.Content:
                    _contentHidden = false;
                    break;
                case MaximizedPanel.Preview:
                    _previewOpen = true;
                    _contentHidden = false;
                    break;
            }
        }

        ApplyLayout();
        switch (panel)
        {
            case MaximizedPanel.Sessions:
                _list.SetFocus();
                break;
            case MaximizedPanel.Preview:
                if (_preview.Visible)
                {
                    FocusPreview();
                }
                else
                {
                    FocusPane();
                }

                break;
            default:
                FocusPane();
                break;
        }
    }

    private MaximizedPanel PanelUnderFocus()
    {
        if (_list.HasFocus)
        {
            return MaximizedPanel.Sessions;
        }

        if (PreviewHasFocus())
        {
            return MaximizedPanel.Preview;
        }

        return MaximizedPanel.Content;
    }

    private void ApplyLayout()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(_sessionsHidden, _contentHidden, _previewOpen, _maximized);
        Sessions.Visible = layout.SessionsVisible;
        Sessions.X = 0;
        Sessions.Y = 0;
        Sessions.Width = layout.SessionsFill ? Dim.Fill() : layout.SessionsVisible ? 28 : 0;
        Sessions.Height = Dim.Fill();

        Content.Visible = layout.ContentVisible;
        Content.X = layout.SessionsVisible && !layout.SessionsFill ? Pos.Right(Sessions) : 0;
        Content.Y = 0;
        Content.Width = layout.ContentVisible ? Dim.Fill() : 0;
        Content.Height = Dim.Fill();

        _tabs.Visible = layout.TabsVisible;
        _tabs.X = 0;
        _tabs.Y = 0;
        _tabs.Height = Dim.Fill();

        if (layout.PreviewFill)
        {
            _preview.Visible = true;
            _preview.X = 0;
            _preview.Y = 0;
            _preview.Width = Dim.Fill();
            _preview.Height = Dim.Fill();
        }
        else if (layout.PreviewVisible)
        {
            _tabs.Width = Dim.Percent(60);
            _preview.Visible = true;
            _preview.X = Pos.Right(_tabs);
            _preview.Y = 0;
            _preview.Width = Dim.Fill();
            _preview.Height = Dim.Fill();
        }
        else
        {
            _preview.Visible = false;
            _preview.Width = 0;
            _tabs.Width = Dim.Fill();
        }

        _sessionsCaption.Maximized = _maximized == MaximizedPanel.Sessions;
        _contentCaption.Maximized = _maximized == MaximizedPanel.Content;
        _previewCaption.Maximized = _maximized == MaximizedPanel.Preview;

        if (Sessions.SuperView is { } parent)
        {
            parent.SetNeedsLayout();
            parent.Layout();
        }

        ResizeActive();
    }

    private enum PanelStop
    {
        Sessions,
        Terminal,
        Files,
    }

    private bool PreviewHasFocus() =>
        _preview.HasFocus || _markdown.HasFocus || _text.HasFocus;

    private void FocusPreview()
    {
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
    }

    private void FocusPane()
    {
        switch (_manager?.Focused?.Tab)
        {
            case SessionTab.Files:
                if (_tree.Visible)
                {
                    _tree.SetFocus();
                }
                else
                {
                    _files.SetFocus();
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
            _preview.Title = "Preview";
            ApplyPreview(new PreviewText.Content(PreviewText.Kind.None, "Select a file."));
            return;
        }

        _preview.Title = session.SelectedPath is { Length: > 0 } path
            ? "Preview  " + Path.GetFileName(path)
            : "Preview";

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
            _text.Content = content.Text;
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
        _tree.Visible = true;
        _filesNote.Visible = false;
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

        for (int i = 0; i < _manager.Sessions.Count; i++)
        {
            _labels.Add(RowText(_manager.Sessions[i], i));
        }

        if (_manager.Sessions.Count > 0)
        {
            _list.SelectedItem = _manager.FocusedIndex;
        }

        _updating = false;
    }

    private string RowText(Session session, int index)
    {
        string label = session.ListLabel;
        if (_manager is null || index != _manager.FocusedIndex)
        {
            return ActivityLabel(label, _unseen.Contains(session), _rang.Contains(session));
        }

        return ActiveRow(label, _list.Viewport.Width);
    }

    private void SyncLabel(int index)
    {
        if (_manager is null || index < 0 || index >= _labels.Count)
        {
            return;
        }

        string label = RowText(_manager.Sessions[index], index);
        if (_labels[index] != label)
        {
            _labels[index] = label;
        }
    }

    private void UpdateStatus()
    {
    }

    private void HoldModal()
    {
        _modal = true;
        _grid.Covered = true;
        _grid.Repaint();
    }

    private void ReleaseModal()
    {
        _modal = false;
        _grid.Covered = false;
        _grid.Repaint();
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
        NoteActivity(active);
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

        if (blink)
        {
            _grid.SetNeedsDraw();
        }

        return true;
    }

    private void NoteStarted(Session session)
    {
        if (session.Terminal is TerminalSession terminal)
        {
            terminal.ConsumeDirty();
            terminal.ConsumeBell();
        }

        _unseen.Remove(session);
        _rang.Remove(session);
        _quietUntil[session] = Environment.TickCount64 + 1000;
    }

    private void Forget(Session session)
    {
        _unseen.Remove(session);
        _rang.Remove(session);
        _quietUntil.Remove(session);
    }

    private void NoteActivity(TerminalSession? active)
    {
        if (_manager is null)
        {
            return;
        }

        long now = Environment.TickCount64;
        foreach (Session session in _manager.Sessions)
        {
            if (session.Terminal is not TerminalSession terminal)
            {
                continue;
            }

            bool bell = terminal.ConsumeBell();
            if (active is not null && ReferenceEquals(terminal, active))
            {
                continue;
            }

            bool quiet = _quietUntil.TryGetValue(session, out long until) && now < until;
            if (quiet)
            {
                terminal.ConsumeDirty();
                continue;
            }

            if (bell)
            {
                _rang.Add(session);
            }

            if (terminal.IsDirty)
            {
                _unseen.Add(session);
                terminal.ConsumeDirty();
            }
        }
    }

    /// <summary>
    /// Runs at the top of every main-loop iteration, before Terminal.Gui lays out and draws,
    /// so PTY output that arrived since the last frame is painted in this one.
    /// </summary>
    private void OnIteration(object? sender, EventArgs<IApplication?> args)
    {
        if (_disposed || _modal || _grid.Session is not TerminalSession active || !active.IsDirty)
        {
            return;
        }

        if (_pacer.ShouldPaint(Environment.TickCount64, active.InSynchronizedUpdate))
        {
            active.ConsumeDirty();
            _grid.SetNeedsDraw();
        }
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

            if (IsCloseKey(key))
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
            if (_screen.HandleChord(key))
            {
                key.Handled = true;
                return true;
            }

            if (IsCloseKey(key))
            {
                _screen.CloseSelected();
                key.Handled = true;
                return true;
            }

            if (key == Key.Enter)
            {
                _screen.FocusPane();
                key.Handled = true;
                return true;
            }

            if (key == Key.CursorUp.WithAlt && !key.IsCtrl && !key.IsShift)
            {
                _screen.MoveFocused(-1);
                key.Handled = true;
                return true;
            }

            if (key == Key.CursorDown.WithAlt && !key.IsCtrl && !key.IsShift)
            {
                _screen.MoveFocused(1);
                key.Handled = true;
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
                && mouse.Position is { } position
                && Viewport.Width > 1
                && (int)position.X >= Viewport.Width - 1)
            {
                int row = Viewport.Y + (int)position.Y;
                if (SelectedItem == row)
                {
                    _screen.CloseSelected();
                    return true;
                }
            }

            return base.OnMouseEvent(mouse);
        }
    }

    private sealed class FileTree : TreeView<FileNode>
    {
        private readonly SessionScreen _screen;

        internal FileTree(SessionScreen screen, ITreeBuilder<FileNode> builder)
            : base(builder) => _screen = screen;

        protected override bool OnKeyDown(Key key)
        {
            if (_screen.HandleChord(key))
            {
                key.Handled = true;
                return true;
            }

            if (IsCloseKey(key))
            {
                _screen.CloseSelected();
                key.Handled = true;
                return true;
            }

            return base.OnKeyDown(key);
        }
    }
}
