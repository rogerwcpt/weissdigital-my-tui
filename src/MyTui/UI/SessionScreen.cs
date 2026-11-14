using System.Collections.ObjectModel;
using System.Text;
using MyTui.Persistence;
using MyTui.Sessions;
using MyTui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
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
    private readonly SessionSplitter _sessionSplitter;
    private int _sessionsWidth = SessionSplitter.DefaultWidth;
    private readonly Tabs _tabs;
    private readonly PreviewMarkdown _markdown;
    private readonly PreviewTextView _text;
    private readonly Label _note;
    private readonly Label _hint;
    private readonly Label _commands;
    private readonly FramePacer _pacer = new();
    private long _fastUntil;
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
    private Window? _window;
    private string _statusNote = "";
    private long _statusUntil;
    private ScriptRun? _shownScript;
    private int _scriptVersion;

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

    /// <summary>Session list text, with a 1-based number in front of the name.</summary>
    internal static string Numbered(string label, int index) =>
        (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "  " + label;

    /// <summary>The session name after its list number, so type-ahead can match the name.</summary>
    internal static string SearchText(string label)
    {
        int i = 0;
        while (i < label.Length && label[i] is >= '0' and <= '9')
        {
            i++;
        }

        if (i == 0 || i >= label.Length || label[i] != ' ')
        {
            return label;
        }

        while (i < label.Length && label[i] == ' ')
        {
            i++;
        }

        return label[i..];
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
        // Cmd+K is handled here, before a focused text field can treat it as the letter k.
        _app.Keyboard.KeyDown += (_, key) => OnCommandChord(key);
        _app.Keyboard.KeyUp += (_, key) =>
        {
            if (!key.Handled && AppKeys.IsCommandChord(key))
            {
                key.Handled = true;
            }
        };
        _grid = new CellGridView(HandleChord, StartFocused, CopyText, OpenLink);
        _hint = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            CanFocus = false,
            Text = "F9 opens commands, including New session.",
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
            if (!string.Equals(previous, session.SelectedPath, StringComparison.Ordinal))
            {
                session.ShowingScriptOutput = false;
            }

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
        _markdown = new PreviewMarkdown
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
        _markdown.Selected = CopyText;
        _text = new PreviewTextView { Visible = false, Selected = CopyText };
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
        // The row starts with its session number. Search still matches the name after that number.
        if (_list.KeystrokeNavigator is { } navigator)
        {
            navigator.Matcher = new SessionNameMatcher();
        }
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
            Width = SessionSplitter.DefaultWidth,
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
        _sessionSplitter = new SessionSplitter(Sessions, Content, ResizeSessions, CommitSessionsWidth);
        ApplyLayout();
    }

    internal void AttachTitle(Window window)
    {
        _window = window;
        TitleBar.Attach(window, ShowPalette);
        window.ClearingViewport += (_, _) => _grid.Repaint();
        window.KeyDown += (_, key) =>
        {
            // Terminal.Gui's quit key would stop the app without asking. Use F10 or the palette.
            if (!key.Handled && key == Key.Q.WithCtrl && !key.IsAlt && !key.IsShift)
            {
                key.Handled = true;
            }
        };
    }

    internal FrameView Sessions { get; }

    internal FrameView Content { get; }

    internal void Start(string? statePath = null)
    {
        if (_timeout is not null)
        {
            return;
        }

        _manager = SessionManager.Load(statePath ?? StateStore.DefaultPath());
        bool customWidth = false;
        if (_manager.SessionsWidth is int width)
        {
            _sessionsWidth = width;
            customWidth = true;
        }

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
        if (customWidth)
        {
            ApplyLayout();
        }

        _timeout = _app.AddTimeout(TimeSpan.FromMilliseconds(30), Tick);
        _app.Iteration += OnIteration;
    }

    internal bool HandleChord(Key key) => HandleChord(key, inTerminal: false);

    internal bool HandleChord(Key key, bool inTerminal)
    {
        if (_modal)
        {
            return false;
        }

        if (AppKeys.TrySession(key, out int sessionIndex))
        {
            SwitchSession(sessionIndex);
            UpdateStatus();
            return true;
        }

        if (!AppKeys.TryRoute(key, out AppCommand command))
        {
            return false;
        }

        switch (command)
        {
            case AppCommand.Menu:
                ShowPalette();
                break;
            case AppCommand.Center:
                ToggleContent();
                break;
            case AppCommand.Sessions:
                ToggleSessions();
                break;
            case AppCommand.Preview:
                TogglePreview();
                break;
            case AppCommand.CreateFolder:
                // F7 makes a folder only while Files has focus, as in Norton Commander.
                if (inTerminal || !FilesActive || !_files.HasFocus)
                {
                    return false;
                }

                CreateSelectedFolder();
                break;
            case AppCommand.Rename:
            case AppCommand.Delete:
                // Do not steal these keys from terminal programs or act on a panel behind Preview.
                if (inTerminal)
                {
                    return false;
                }

                if (_list.HasFocus)
                {
                    if (command == AppCommand.Rename)
                    {
                        RenameSession();
                    }
                    else
                    {
                        CloseSelected();
                    }
                }
                else if (FilesActive && _files.HasFocus)
                {
                    if (command == AppCommand.Rename)
                    {
                        RenameSelectedFile();
                    }
                    else
                    {
                        DeleteSelectedFile();
                    }
                }
                else
                {
                    return false;
                }

                break;
            case AppCommand.Quit:
                RequestQuit();
                break;
            case AppCommand.Copy:
                CopyVisible();
                break;
            case AppCommand.Paste:
                PasteClipboard();
                break;
        }

        UpdateStatus();
        return true;
    }

    private void RequestQuit()
    {
        if (_modal || _manager is null)
        {
            return;
        }

        int running = _manager.Sessions.Count(session => session.IsRunning || session.Script?.IsRunning == true);
        if (running > 0)
        {
            string detail = CloseDialog.BusyQuitText(
                _manager.Sessions.Where(session => session.IsBusy).Select(session => session.Name).ToList());
            HoldModal();
            bool quit;
            try
            {
                quit = CloseDialog.ConfirmQuit(_app, running, detail);
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

    internal void DuplicateSession()
    {
        if (_modal || _manager?.Focused is not Session source)
        {
            return;
        }

        _manager.Duplicate(source);
        RefreshLabels();
        ShowSession(_manager.FocusedIndex);
        OpenFocused();
    }

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
        RefreshWindowTitle();
        RefreshFiles();
        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
    }

    internal void RenameSession()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        HoldModal();
        string? name;
        try
        {
            name = SessionDialog.AskName(_app, session.Name);
        }
        finally
        {
            ReleaseModal();
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        _manager.Rename(session, name);
        RefreshWindowTitle();
        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
    }

    internal void CloseSelected()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if ((session.IsRunning || session.Script?.IsRunning == true) && !ConfirmClose(session.Name))
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
            RefreshWindowTitle();
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

    private void SortSessions()
    {
        if (_modal || _manager is null)
        {
            return;
        }

        _manager.SortByName();
        RefreshLabels();
    }

    private void ForceStopTerminal()
    {
        if (_modal || _manager?.Focused is not Session session || !session.IsRunning)
        {
            return;
        }

        HoldModal();
        bool stop;
        try
        {
            stop = CloseDialog.ConfirmStop(_app, session.Name);
        }
        finally
        {
            ReleaseModal();
        }

        if (!stop)
        {
            return;
        }

        _grid.Session = null;
        session.Stop(force: true);
        Forget(session);
        SyncLabel(_manager.FocusedIndex);
        UpdateStatus();
        _grid.Repaint();
        Note("Terminal stopped. Enter starts it again.");
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
            RestoreFileSelection(keep);
        }

        ShowPreview(session);
    }

    private void RestoreFileSelection(string path)
    {
        if (!Workspace.IsInside(_root, path))
        {
            return;
        }

        string directory = _root;
        string[] parts = Path.GetRelativePath(_root, path).Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < parts.Length; i++)
        {
            FileNode? node = Workspace.Children(_root, directory)
                .FirstOrDefault(child => child.Name == parts[i]);
            if (node is null)
            {
                return;
            }

            if (i == parts.Length - 1)
            {
                _tree.GoTo(node);
            }
            else
            {
                _tree.Expand(node);
                directory = node.Path;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionSplitter.Dispose();
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
            session.StopScript();
        }
    }

    private void ChooseTheme()
    {
        if (_modal)
        {
            return;
        }

        string previous = HostTheme.Current;
        HoldModal();
        string? chosen;
        try
        {
            chosen = ThemeDialog.Ask(_app, previous, PreviewTheme);
        }
        finally
        {
            ReleaseModal();
        }

        if (chosen is null)
        {
            if (HostTheme.Current != previous)
            {
                HostTheme.Apply(previous);
                Restyle();
            }

            return;
        }

        if (HostTheme.Current != chosen)
        {
            HostTheme.Apply(chosen);
        }

        if (HostTheme.Current == previous)
        {
            return;
        }

        HostTheme.Save();
        Restyle();
    }

    private void PreviewTheme(string name)
    {
        HostTheme.Apply(name);
        Restyle();
    }

    private void Restyle()
    {
        if (_window is not null)
        {
            ApplyScheme(_window, HostTheme.Scheme());
        }

        ApplyScheme(_markdown, HostTheme.Preview());
        ApplyScheme(_text, HostTheme.Preview());
        _window?.SetNeedsDraw();
        _grid.Repaint();
    }

    /// <summary>
    /// Paints <paramref name="scheme"/> onto <paramref name="view"/> and the views already in its tree.
    /// A border is included only when it already exists. Creating one here never finishes: the new
    /// border is a view, and its own border creates another, until the stack overflows and the
    /// terminal is left in mouse-tracking mode.
    /// </summary>
    internal static void ApplyScheme(View view, Scheme scheme)
    {
        var seen = new HashSet<View>();
        var pending = new Stack<View>();
        pending.Push(view);
        while (pending.Count > 0)
        {
            View current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            current.SetScheme(scheme);
            foreach (View child in current.SubViews.ToArray())
            {
                pending.Push(child);
            }

            Push(current.Margin.View);
            Push(current.Border.View);
            Push(current.Padding.View);
        }

        void Push(View? existing)
        {
            if (existing is not null)
            {
                pending.Push(existing);
            }
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

    private void OnCommandChord(Key key)
    {
        if (key.Handled || !AppKeys.IsCommandChord(key))
        {
            return;
        }

        key.Handled = true;
        if (key.EventType == KeyEventType.Press)
        {
            ShowPalette();
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
            id = CommandPalette.Ask(_app, FilesActive);
        }
        finally
        {
            ReleaseModal();
        }

        ExecuteCommand(id);
    }

    internal void ExecuteCommand(string? id)
    {
        switch (id)
        {
            case "new":
                NewSession();
                break;
            case "duplicate":
                DuplicateSession();
                break;
            case "start":
                OpenFocused();
                break;
            case "open":
                OpenFolder();
                break;
            case "open-file":
                OpenSelectedFile();
                break;
            case "rename-file":
                RenameSelectedFile();
                break;
            case "create-folder":
                CreateSelectedFolder();
                break;
            case "delete-file":
                DeleteSelectedFile();
                break;
            case "run-script":
                RunSelectedScript();
                break;
            case "script-output":
                ShowScriptOutput();
                break;
            case "stop-script":
                StopScript();
                break;
            case "reveal":
                RevealInFileManager();
                break;
            case "sort":
                SortSessions();
                break;
            case "stop":
                ForceStopTerminal();
                break;
            case "center":
                ToggleContent();
                break;
            case "restore":
                RestoreLayout();
                break;
            case "terminal":
                ShowTab(SessionTab.Terminal);
                break;
            case "files":
                ShowTab(SessionTab.Files);
                break;
            case "hide":
                ToggleSessions();
                break;
            case "maximize":
                ToggleMaximize(PanelUnderFocus());
                break;
            case "preview":
                TogglePreview();
                break;
            case "edit":
                EditSession();
                break;
            case "rename":
                RenameSession();
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
            case "theme":
                ChooseTheme();
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
        if (PreviewHasFocus())
        {
            if (_text.Visible)
            {
                CopyText(string.IsNullOrEmpty(_text.SelectedText) ? _text.Content : _text.SelectedText);
            }
            else if (_markdown.Visible)
            {
                CopyText(string.IsNullOrEmpty(_markdown.SelectedText) ? _markdown.Text : _markdown.SelectedText);
            }

            return;
        }

        string? text = _grid.SelectionText();
        if (string.IsNullOrEmpty(text))
        {
            text = _grid.Session?.Capture().PlainText;
        }

        CopyText(text ?? "");
    }

    private void CopyText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (_app.Clipboard?.TrySetClipboardData(text) == true)
        {
            Note("Copied");
        }
        else
        {
            Note("Could not access the clipboard");
        }
    }

    private bool FilesActive =>
        _manager?.Focused?.Tab == SessionTab.Files && Content.Visible && _tabs.Visible;

    private void OpenSelectedFile()
    {
        if (!FilesActive || _modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if (_tree.SelectedObject is not { Directory: false } selected)
        {
            Note("Select a file to open");
            return;
        }

        if (!Workspace.IsInside(session.Cwd, selected.Path) || !LinkOpen.TryOpenFile(selected.Path))
        {
            Note("Could not open the file");
            return;
        }

        Note("Opened " + selected.Name);
    }

    private void SwitchSession(int index)
    {
        if (_manager is null || index < 0 || index >= _manager.Sessions.Count)
        {
            Note("No session " + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        bool listHadFocus = _list.HasFocus;
        if (_list.SelectedItem != index)
        {
            _list.SelectedItem = index;
        }

        // The list keeps focus when the chord is pressed there. From a terminal or Files,
        // land in that session's own pane, including when its tab is Files.
        if (!listHadFocus)
        {
            FocusPane();
        }
    }

    private void CreateSelectedFolder()
    {
        if (!FilesActive || _modal || _manager?.Focused is not Session session)
        {
            return;
        }

        string parent = session.Cwd;
        if (_tree.SelectedObject is FileNode selected)
        {
            parent = selected.Directory
                ? selected.Path
                : Path.GetDirectoryName(selected.Path) ?? session.Cwd;
        }

        HoldModal();
        string? name;
        try
        {
            name = SessionDialog.AskName(
                _app, "", "Create folder", WorkspaceFiles.NameProblem, "Create");
        }
        finally
        {
            ReleaseModal();
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        try
        {
            session.SelectedPath = WorkspaceFiles.CreateDirectory(session.Cwd, parent, name);
            session.ShowingScriptOutput = false;
            RefreshFiles();
            FocusPane();
            Note("Created " + name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Note("Could not create folder: " + ex.Message);
        }
    }

    private void RenameSelectedFile()
    {
        if (!FilesActive || _modal || _manager?.Focused is not Session session
            || _tree.SelectedObject is not FileNode selected)
        {
            return;
        }

        HoldModal();
        string? name;
        try
        {
            name = SessionDialog.AskName(_app, selected.Name,
                selected.Directory ? "Rename folder" : "Rename file", WorkspaceFiles.NameProblem);
        }
        finally
        {
            ReleaseModal();
        }

        if (name is null)
        {
            return;
        }

        try
        {
            session.SelectedPath = WorkspaceFiles.Rename(session.Cwd, selected.Path, name);
            session.ShowingScriptOutput = false;
            RefreshFiles();
            FocusPane();
            Note("Renamed to " + name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Note("Could not rename: " + ex.Message);
        }
    }

    private void DeleteSelectedFile()
    {
        if (!FilesActive || _modal || _manager?.Focused is not Session session
            || _tree.SelectedObject is not FileNode selected)
        {
            return;
        }

        HoldModal();
        bool delete;
        try
        {
            delete = CloseDialog.ConfirmDelete(_app, selected.Name, selected.Directory);
        }
        finally
        {
            ReleaseModal();
        }

        if (!delete)
        {
            return;
        }

        try
        {
            WorkspaceFiles.Delete(session.Cwd, selected.Path);
            session.SelectedPath = null;
            session.ShowingScriptOutput = false;
            RefreshFiles();
            FocusPane();
            Note("Deleted " + selected.Name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Note("Could not delete: " + ex.Message);
        }
    }

    private void RunSelectedScript()
    {
        if (!FilesActive || _modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if (_tree.SelectedObject is not { Directory: false } selected
            || !selected.Path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            Note("Select a .sh file to run");
            return;
        }

        try
        {
            session.RunScript(selected.Path);
            ShowScriptOutput();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            Note("Could not run script: " + ex.Message);
        }
    }

    private void ShowScriptOutput()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if (session.Script is null)
        {
            Note("No script output yet. Select a .sh file and choose Run selected script.");
            return;
        }

        session.ShowingScriptOutput = true;
        _previewOpen = true;
        _contentHidden = false;
        if (_maximized is MaximizedPanel.Content or MaximizedPanel.Sessions)
        {
            _maximized = MaximizedPanel.None;
        }

        ApplyLayout();
        ShowPreview(session);
        FocusPreview();
    }

    private void StopScript()
    {
        if (_modal || _manager?.Focused is not Session session || session.Script?.IsRunning != true)
        {
            return;
        }

        session.StopScript();
        ShowScriptOutput();
    }

    private void RevealInFileManager()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        FileNode? selected = FilesActive ? _tree.SelectedObject : null;
        string path = selected?.Path ?? session.Cwd;
        if (!Workspace.IsInside(session.Cwd, path) || !LinkOpen.TryReveal(path, select: selected is not null))
        {
            Note("Could not reveal the path");
            return;
        }

        Note("Revealed " + (selected?.Name ?? session.Name));
    }

    private void OpenLink(string url)
    {
        if (LinkOpen.TryOpen(url))
        {
            Note("Opened link");
            return;
        }

        Note("Could not open the link");
    }

    private void Note(string text)
    {
        _statusNote = text;
        _statusUntil = Environment.TickCount64 + 2500;
        _commands.Text = text;
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

    /// <summary>
    /// Enter, a double-click in the session list, or the palette. A stopped session starts.
    /// A running one only takes focus, on whichever tab it is already showing.
    /// </summary>
    private void OpenFocused()
    {
        if (_modal || _manager?.Focused is not Session session)
        {
            return;
        }

        if (!session.IsRunning)
        {
            // The new process is sized from the terminal pane. Reveal it before starting.
            if (_contentHidden || _maximized is MaximizedPanel.Sessions or MaximizedPanel.Preview || session.Tab != SessionTab.Terminal)
            {
                _contentHidden = false;
                if (_maximized is MaximizedPanel.Sessions or MaximizedPanel.Preview)
                {
                    _maximized = MaximizedPanel.None;
                }

                _manager.SetTab(session, SessionTab.Terminal);
                _hint.Visible = false;
                _grid.Visible = true;
                SelectTab(SessionTab.Terminal);
                ApplyLayout();
            }

            StartFocused();
        }

        FocusPane();
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
        RefreshWindowTitle();
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

    private void RestoreLayout()
    {
        _sessionsHidden = false;
        _contentHidden = false;
        _previewOpen = false;
        _maximized = MaximizedPanel.None;
        _sessionsWidth = SessionSplitter.DefaultWidth;
        _manager?.SetSessionsWidth(null);
        // Hiding Preview can transfer focus into Files and select its first row.
        // Do not let that implicit selection reopen the panel we just closed.
        bool updating = _updating;
        _updating = true;
        try
        {
            ApplyLayout();
            FocusPane();
        }
        finally
        {
            _updating = updating;
        }
    }

    private void ToggleContent()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(_sessionsHidden, _contentHidden, _previewOpen, _maximized);
        if (layout.TabsVisible)
        {
            MinimizeContent();
            return;
        }

        _contentHidden = false;
        _maximized = MaximizedPanel.None;
        ApplyLayout();
        FocusPane();
    }

    private void ShowTab(SessionTab tab)
    {
        if (_maximized is MaximizedPanel.Sessions or MaximizedPanel.Preview)
        {
            RestoreLayout();
        }

        _contentHidden = false;
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
        // Focusing Files may select its first row. That is not a user request to
        // open Preview, especially just after restoring the default layout.
        _updating = true;
        try
        {
            FocusPane();
        }
        finally
        {
            _updating = false;
        }

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

    private void ResizeSessions(int width)
    {
        if (_modal || _disposed)
        {
            return;
        }

        _sessionsWidth = SessionSplitter.ClampWidth(width, Sessions.SuperView?.Viewport.Width ?? 80);
        ApplyLayout();
        _grid.Repaint();
    }

    private void CommitSessionsWidth()
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            int? width = _sessionsWidth == SessionSplitter.DefaultWidth ? null : _sessionsWidth;
            _manager.SetSessionsWidth(width);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void ApplyLayout()
    {
        WorkspaceLayout layout = WorkspaceLayout.Compute(_sessionsHidden, _contentHidden, _previewOpen, _maximized);
        _sessionSplitter.Active = layout.SessionsVisible && layout.ContentVisible;
        Sessions.Visible = layout.SessionsVisible;
        Sessions.X = 0;
        Sessions.Y = 0;
        Sessions.Width = layout.SessionsFill ? Dim.Fill() : layout.SessionsVisible
            ? Dim.Func(_ => SessionSplitter.ClampWidth(_sessionsWidth, Sessions.SuperView?.Viewport.Width ?? 80))
            : 0;
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
        _shownScript = null;
        if (session?.ShowingScriptOutput == true && session.Script is { } script)
        {
            _shownScript = script;
            UpdateScriptPreview(script, reset: true);
            return;
        }

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

    private void UpdateScriptPreview(ScriptRun script, bool reset = false)
    {
        if (!reset && _scriptVersion == script.Version)
        {
            return;
        }

        ScriptRun.Snapshot snapshot = script.Capture();
        _scriptVersion = snapshot.Version;
        _preview.Title = $"Output  {Path.GetFileName(script.Path)} · {snapshot.Status}";
        _markdown.Visible = false;
        _note.Visible = false;
        _text.Visible = true;
        _text.UpdateOutput(snapshot.Text, reset);
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
        string label = Numbered(session.ListLabel, index);
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

    private void RefreshWindowTitle()
    {
        string title = _manager?.Focused is Session session ? session.WindowTitle : "Session";
        if (Content.Title != title)
        {
            Content.Title = title;
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

        if (_statusUntil != 0 && Environment.TickCount64 >= _statusUntil)
        {
            _statusUntil = 0;
            if (_commands.Text == _statusNote)
            {
                _commands.Text = CommandPalette.Footer;
            }
        }

        for (int i = 0; i < _manager.Sessions.Count && i < _labels.Count; i++)
        {
            SyncLabel(i);
        }

        RefreshWindowTitle();

        if (_manager.Focused is { ShowingScriptOutput: true, Script: { } script }
            && ReferenceEquals(script, _shownScript) && _preview.Visible)
        {
            UpdateScriptPreview(script);
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
    /// Runs before Terminal.Gui reads <see cref="Application.MaximumIterationsPerSecond"/> and
    /// before it draws. The cap chosen here is the sleep after this iteration. A queued key or
    /// fresh output on the visible terminal selects the fast cap, so the echo is not left behind
    /// an idle sleep. A hidden or covered terminal does not keep the loop awake.
    /// </summary>
    private void OnIteration(object? sender, EventArgs<IApplication?> args)
    {
        if (_disposed)
        {
            return;
        }

        long now = Environment.TickCount64;
        bool showOutput = !_modal
            && !_contentHidden
            && _grid.Visible
            && _grid.Live
            && _maximized != MaximizedPanel.Sessions
            && _maximized != MaximizedPanel.Preview;
        TerminalSession? active = showOutput ? _grid.Session : null;
        bool busy = active?.IsDirty == true || PendingInput.Any(_app);
        WakeDecision pace = WakePace.Choose(now, busy, _fastUntil);
        _fastUntil = pace.FastUntil;
        if (Application.MaximumIterationsPerSecond != pace.PerSecond)
        {
            Application.MaximumIterationsPerSecond = pace.PerSecond;
        }

        if (active is null || !active.IsDirty)
        {
            return;
        }

        if (_pacer.ShouldPaint(now, active.InSynchronizedUpdate))
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
        };
    }

    /// <summary>Matches a typed name, or the leading session number, against a numbered row.</summary>
    private sealed class SessionNameMatcher : ICollectionNavigatorMatcher
    {
        public bool IsCompatibleKey(Key key)
        {
            Rune rune = key.AsRune;
            return rune != default && !Rune.IsControl(rune) && !key.IsAlt && !key.IsCtrl;
        }

        public bool IsMatch(string search, object value)
        {
            string full = value?.ToString() ?? "";
            return full.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                || SearchText(full).StartsWith(search, StringComparison.OrdinalIgnoreCase);
        }
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

            if (key == Key.Enter)
            {
                _screen.OpenFocused();
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

            bool handled = base.OnMouseEvent(mouse);
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonDoubleClicked)
                && mouse.Position is { } click
                && (Viewport.Width <= 1 || (int)click.X < Viewport.Width - 1))
            {
                int row = Viewport.Y + (int)click.Y;
                if (SelectedItem == row)
                {
                    _screen.OpenFocused();
                    return true;
                }
            }

            return handled;
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

            return base.OnKeyDown(key);
        }
    }
}
