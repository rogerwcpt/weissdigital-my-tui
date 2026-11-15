using Ghostty.Vt;
using MyTui.Sessions;
using MyTui.Terminal;
using MyTui.UI;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using VtTerminal = Ghostty.Vt.Terminal;

namespace MyTui.Tests;

/// <summary>
/// Ctrl+C has to reach the foreground program as the interrupt byte. The host may deliver
/// that press as a raw 0x03 or as a kitty sequence.
/// </summary>
[Collection("HostTheme")]
public class CtrlCDeliveryTests
{
    [Theory]
    [InlineData("\u0003")]
    [InlineData("\u001b[99;5u")]
    [InlineData("\u001b[99;5:1u")]
    public void HostCtrlC_ReachesTheAppAsTheInterruptKey(string sequence)
    {
        IApplication app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        try
        {
            Assert.True(CommandChord.Install(app));
            var processor = (AnsiInputProcessor)app.Driver!.GetInputProcessor();
            var presses = new List<Key>();
            app.Keyboard.KeyDown += (_, key) => presses.Add(key);
            foreach (char ch in sequence)
            {
                processor.InputQueue.Enqueue(ch);
            }

            processor.ProcessQueue();
            Key key = Assert.Single(presses);
            Assert.Equal(KeyEventType.Press, key.EventType);
            Assert.True(key.IsCtrl);
            Assert.False(key.IsShift);
            Assert.False(key.IsAlt);
            using var terminal = new VtTerminal(80, 24);
            using var encoder = new KeyEncoder();
            Assert.Equal(new byte[] { 0x03 }, KeyInput.Encode(terminal, encoder, key));
        }
        finally
        {
            app.Dispose();
        }
    }

    [Fact]
    public void HostCtrlCRelease_IsNotASecondInterrupt()
    {
        IApplication app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        try
        {
            Assert.True(CommandChord.Install(app));
            var processor = (AnsiInputProcessor)app.Driver!.GetInputProcessor();
            int presses = 0;
            int releases = 0;
            app.Keyboard.KeyDown += (_, _) => presses++;
            app.Keyboard.KeyUp += (_, key) =>
            {
                releases++;
                Assert.Equal(KeyEventType.Release, key.EventType);
                Assert.True(key.IsCtrl);
                Assert.False(key.IsShift);
            };
            foreach (char ch in "\u001b[99;5:3u")
            {
                processor.InputQueue.Enqueue(ch);
            }

            processor.ProcessQueue();
            Assert.Equal(0, presses);
            Assert.Equal(1, releases);
        }
        finally
        {
            app.Dispose();
        }
    }

    [UnixFact(Timeout = 20000)]
    public async Task FocusedTerminal_CtrlCAbortsTheLineAndStopsTheJob()
    {
        using var workspace = new ShellWorkspace();
        workspace.Open();
        TerminalSession session = workspace.WaitForPrompt();

        byte[]? encoded = session.Encode(Key.C.WithCtrl);
        Assert.Equal(new byte[] { 0x03 }, encoded);
        Press(workspace, "echo LEFT");
        Assert.True(workspace.App.Keyboard.RaiseKeyDownEvent(Key.C.WithCtrl));
        Thread.Sleep(300);
        Press(workspace, "echo AFTER\n");
        string aborted = workspace.WaitFor(text => text.Contains("AFTER"));
        Assert.DoesNotContain("\nLEFT", aborted.Replace("\r\n", "\n"));

        Press(workspace, "sleep 30\n");
        Assert.True(WaitUntil(() => session.IsBusy, TimeSpan.FromSeconds(3)));
        Thread.Sleep(250);
        Assert.True(workspace.App.Keyboard.RaiseKeyDownEvent(Key.C.WithCtrl));
        Assert.True(WaitUntil(() => !session.IsBusy, TimeSpan.FromSeconds(3)), workspace.ScreenText());
        await Task.CompletedTask;
    }

    [UnixFact(Timeout = 20000)]
    public async Task HostBytes_CtrlCAbortsTheLine()
    {
        using var workspace = new ShellWorkspace();
        workspace.Open();
        workspace.WaitForPrompt();
        Press(workspace, "echo LEFT");
        workspace.Feed("\u001b[99;5u");
        Thread.Sleep(300);
        Press(workspace, "echo AFTER\n");
        string text = workspace.WaitFor(value => value.Contains("AFTER"));
        Assert.DoesNotContain("\nLEFT", text.Replace("\r\n", "\n"));
        await Task.CompletedTask;
    }

    private static void Press(ShellWorkspace workspace, string text)
    {
        foreach (char ch in text)
        {
            Key key = ch switch
            {
                '\n' => Key.Enter,
                ' ' => Key.Space,
                >= 'A' and <= 'Z' => new Key(ch).WithShift,
                >= 'a' and <= 'z' => new Key(ch),
                >= '0' and <= '9' => new Key(ch),
                _ => throw new InvalidOperationException($"No key for {ch}."),
            };
            Assert.True(workspace.App.Keyboard.RaiseKeyDownEvent(key), workspace.ScreenText());
        }
    }

    private static bool WaitUntil(Func<bool> ready, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (ready())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return ready();
    }

    private sealed class ShellWorkspace : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("my-tui-ctrlc").FullName;
        private readonly Window _window;
        private readonly SessionToken _token;

        internal ShellWorkspace()
        {
            var env = new Dictionary<string, string>
            {
                ["PATH"] = "/bin:/usr/bin",
                ["HOME"] = "/tmp",
                ["ZDOTDIR"] = "/tmp",
                ["TERM"] = "xterm-256color",
            };
            SessionManager.Load(Path.Combine(_directory, "state.json"))
                .Create("shell", _directory, "/bin/zsh", ["-f", "-i"], env);
            App = Application.Create();
            App.Init(DriverRegistry.Names.ANSI);
            CommandChord.Install(App);
            App.Driver!.SetScreenSize(100, 30);
            Screen = new SessionScreen(App);
            _window = new Window();
            Screen.AttachTitle(_window);
            _window.Add(Screen.Sessions, Screen.Content);
            _token = App.Begin(_window)!;
            Screen.Start(Path.Combine(_directory, "state.json"));
            App.LayoutAndDraw();
        }

        internal IApplication App { get; }

        internal SessionScreen Screen { get; }

        internal CellGridView Grid =>
            Screen.Content.SubViews.OfType<Tabs>().Single()
                .SubViews.SelectMany(view => view.SubViews).OfType<CellGridView>().Single();

        internal void Open()
        {
            ListView list = Screen.Sessions.SubViews.OfType<ListView>().Single();
            Assert.True(list.NewKeyDownEvent(Key.Enter));
            Assert.True(Grid.HasFocus);
        }

        internal TerminalSession WaitForPrompt()
        {
            Assert.True(WaitUntil(() => Grid.Session is { IsRunning: true }, TimeSpan.FromSeconds(3)));
            TerminalSession session = Grid.Session!;
            WaitFor(text => text.Contains('#') || text.Contains('$') || text.Contains('%'));
            return session;
        }

        internal string WaitFor(Func<string, bool> ready)
        {
            string text = "";
            bool ok = WaitUntil(() =>
            {
                text = ScreenText();
                return ready(text);
            }, TimeSpan.FromSeconds(4));
            Assert.True(ok, text);
            return text;
        }

        internal string ScreenText() => Grid.Session?.Capture().PlainText ?? "";

        internal void Feed(string sequence)
        {
            var processor = (AnsiInputProcessor)App.Driver!.GetInputProcessor();
            foreach (char ch in sequence)
            {
                processor.InputQueue.Enqueue(ch);
            }

            processor.ProcessQueue();
        }

        public void Dispose()
        {
            Screen.Dispose();
            App.End(_token);
            _window.Dispose();
            App.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            {
                Skip = "The PTY host is macOS and Linux only.";
            }
            else if (!File.Exists("/bin/zsh"))
            {
                Skip = "Requires /bin/zsh.";
            }
        }
    }
}
