using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using MyTui.Terminal;

namespace MyTui.UI;

/// <summary>
/// Left list and one live shell. The other rows are placeholders, not a session model.
/// </summary>
internal sealed class SessionScreen : IDisposable
{
    private static readonly string[] Names = ["Shell", "Claude Code", "PI", "Codex"];

    private readonly IApplication _app;
    private readonly PtySession _session;
    private readonly RawTerminalView _terminal;
    private readonly Label _note;
    private readonly ListView _list;
    private readonly ConcurrentQueue<string> _chunks = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private object? _timeout;
    private int _started;
    private bool _disposed;

    internal SessionScreen(IApplication app)
    {
        _app = app;
        string shell = ShellLocator.Resolve();
        _session = PtySession.Start(shell, [shell], 80, 24);
        _terminal = new RawTerminalView(_session)
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _note = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            CanFocus = false,
            Text = "Placeholder. Phase 0 runs one shell.",
        };
        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };
        _list.SetSource(new ObservableCollection<string>(Names));
        _list.SelectedItem = 0;
        _list.ValueChanged += (_, args) => Show(args.NewValue);

        Sessions = new FrameView
        {
            Title = "Sessions",
            X = 0,
            Y = 0,
            Width = 20,
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
        Content.Add(_terminal, _note);
    }

    internal FrameView Sessions { get; }

    internal FrameView Content { get; }

    internal void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _session.Output += OnOutput;
        _session.StartReading();
        _timeout = _app.AddTimeout(TimeSpan.FromMilliseconds(30), Drain);
        _terminal.SetFocus();
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

        _session.Dispose();
    }

    private void OnOutput(byte[] bytes)
    {
        var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int written = _decoder.GetChars(bytes, chars, flush: false);
        if (written > 0)
        {
            _chunks.Enqueue(new string(chars, 0, written));
        }
    }

    private bool Drain()
    {
        if (_disposed)
        {
            return false;
        }

        var text = new StringBuilder();
        while (_chunks.TryDequeue(out string? chunk))
        {
            text.Append(chunk);
        }

        if (text.Length > 0)
        {
            _terminal.AppendRaw(text.ToString());
        }

        return true;
    }

    private void Show(int? index)
    {
        int row = index ?? -1;
        bool live = row == 0;
        _terminal.Visible = live;
        _note.Visible = !live;
        if (!live && row > 0 && row < Names.Length)
        {
            _note.Text = $"{Names[row]} is a placeholder. Phase 0 runs one shell.";
        }

        if (live)
        {
            _terminal.SetFocus();
        }
    }
}
